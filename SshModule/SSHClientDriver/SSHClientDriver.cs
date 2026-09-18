
using System;
using System.Collections.Generic;
using Crestron.SimplSharp;
using Crestron.SimplSharp.Ssh;
using Crestron.SimplSharp.Ssh.Common;

namespace SSHClientDriver
{
    public delegate void InitializedDataHandler(ushort state);

    public delegate void ConnectionStateHandler(ushort state);

    public delegate void ReceivedDataHandler(SimplSharpString data);

    public class SshClientDevice
    {
        private bool _initialized;
        private SshClient _client;
        private ShellStream _stream;
        private string _username, _hostname, _password;
        private int _port;
        public InitializedDataHandler InitializedData { get; set; }
        public ConnectionStateHandler ConnectionState { get; set; }
        public ReceivedDataHandler ReceivedData { get; set; }
        private string _debugName;
        public ushort DebugEnable = 0;
        private CTimer _monitorTimer;
        private const long MonitorIntervalMs = 2000;
        private const long IdleTimeoutMs = 60000;
        private long _lastRxTicks;
        private ushort _currentConnState;
        private readonly object _stateLock = new object();
        private readonly object _ioLock = new object();
        private readonly object _healthLock = new object();
        private int _missedKeepalives;
        private const int MaxMisses = 2;
        private const string HeartbeatToken = "__sshhb__";
        private readonly object _disconnectLock = new object();
        private bool _disconnecting;
        private readonly object _connectionLock = new object();
        private CTimer _reconnectTimer;
        private bool _connectionRequested;
        private bool _connecting;
        private const long InitialReconnectDelayMs = 5000;
        private const long MaximumReconnectDelayMs = 30000;
        private long _nextReconnectDelayMs = InitialReconnectDelayMs;
        
        
        

        public SshClientDevice()
        {
            _reconnectTimer = new CTimer(ReconnectTimerCallback, null, -1L);
        }

        public void Debug(string message)
        {
            if (DebugEnable >= 1)
            {
                CrestronConsole.PrintLine(" [" + _debugName + "] " + message);
            }
        }

        public void Initialize(string hostname, int port, string username, string password, string debugName)
        {
            // Reinitialization replaces the active session. Tear it down first so
            // connection feedback and the client/stream state cannot disagree,
            // and the next Connect() uses the newly supplied settings.
            Disconnect();

            this._hostname = hostname;
            this._port = port;
            this._username = username;
            this._password = password;
            this._debugName = debugName;

            Debug($"Initializing SSH client: {hostname}:{port}:{username}");
            _initialized = true;
            SafeInvokeInitialized(1);
            SafeInvokeConnectionState(0);
        }
        public void Connect()
        {
            lock (_connectionLock)
            {
                _connectionRequested = true;
            }

            AttemptConnection();
        }

        private void AttemptConnection()
        {
            lock (_connectionLock)
            {
                if (!_connectionRequested || _connecting)
                {
                    return;
                }

                _connecting = true;
            }

            bool scheduleReconnect = false;
            try
            {
                if (!_initialized)
                {
                    Debug("Connect() called but not initialized");
                    StopConnectionRequests();
                    return;
                }

                var user = (_username ?? "").Trim();
                var host = (_hostname ?? "").Trim();
                var pass = _password ?? "";
                if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(host))
                {
                    Debug("Connect blocked: username or hostname is empty");
                    StopConnectionRequests();
                    return;
                }
                if (_port <= 0 || _port > 65535)
                {
                    Debug("Connect blocked: Invalid port number");
                    StopConnectionRequests();
                    return;
                }
                _username = user;
                _hostname = host;
                _password = pass;
                if (_client != null)
                {
                    if (_client.IsConnected && _stream != null && _stream.CanWrite)
                    {
                        Debug("Connect ignored: already connected");
                        return;
                    }

                    Debug("Connect(): cleaning up previous client.");
                    CleanupConnection();
                }

                var authMethod = new KeyboardInteractiveAuthenticationMethod(user);
                authMethod.AuthenticationPrompt += AuthenticationPromptHandler;
                var pwd = new PasswordAuthenticationMethod(user, pass);
                var connectInfo = new ConnectionInfo(host, _port, user, new AuthenticationMethod[]{pwd, authMethod});
                _client = new SshClient(connectInfo);
                _client.KeepAliveInterval = TimeSpan.Zero;
                _client.ErrorOccurred += ClientErrorHandler;
                _client.HostKeyReceived += HostKeyReceivedHandler;
                Debug("Attempting connection to: " + host + ":" + _port);
                try
                {
                    _client.Connect();
                }
                catch (SshAuthenticationException e)
                {
                    Debug("Authentication failed: " + e.Message);
                    StopConnectionRequests();
                    CleanupConnection();
                    return;
                }
                catch (SshConnectionException e)
                {
                    Debug("Connection error: " + e.Message + ", Reason:  " + e.DisconnectReason);
                    if(e.InnerException != null) Debug("Inner: " +  e.InnerException.Message);
                    CleanupConnection();
                    scheduleReconnect = true;
                    return;
                }
                catch (Exception e)
                {
                    Debug("Unexpected connect error: " + e.Message);
                    CleanupConnection();
                    scheduleReconnect = true;
                    return;
                }

                try
                {
                    _stream = _client.CreateShellStream("terminal", 80, 24, 800, 600, 1024);
                    _stream.DataReceived += StreamDataReceivedHandler;
                    _stream.ErrorOccurred += StreamErrorOccurredHandler;
                }
                catch (Exception e)
                {
                    Debug("Shell stream setup failed: " + e.Message);
                    CleanupConnection();
                    scheduleReconnect = true;
                    return;
                }
                if (_client.IsConnected)
                {
                    Debug("Connected");
                    CancelReconnect();
                    lock (_connectionLock)
                    {
                        _nextReconnectDelayMs = InitialReconnectDelayMs;
                    }
                    SetConnectionState(1);
                    StartMonitor();
                }
                else
                {
                    Debug("Could not complete connection");
                    CleanupConnection();
                    scheduleReconnect = true;
                }
            }
            catch (Exception e)
            {
                Debug("Unexpected connection state error: " + e.Message);
                CleanupConnection();
                scheduleReconnect = true;
            }
            finally
            {
                lock (_connectionLock)
                {
                    _connecting = false;
                }

                if (scheduleReconnect)
                {
                    ScheduleReconnect();
                }
            }
        }

        public void Disconnect()
        {
            StopConnectionRequests();
            CleanupConnection();
        }

        private void CleanupConnection()
        {
            lock (_disconnectLock)
            {
                if (_disconnecting) return;
                _disconnecting = true;
            }

            try
            {
                StopMonitor();
                SetConnectionState(0);
                ShellStream stream;
                SshClient client;
                lock (_ioLock)
                {
                    stream = _stream;
                    _stream = null;
                    client = _client;
                    _client = null;

                }

                if (stream != null)
                {
                    try
                    {
                        stream.DataReceived -= StreamDataReceivedHandler;
                    }
                    catch
                    {
                        // ignored
                    }

                    try
                    {
                        stream.ErrorOccurred -= StreamErrorOccurredHandler;
                    }
                    catch
                    {
                        // ignored
                    }

                    try
                    {
                        stream.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Debug("Stream dispose: " + ex.Message);
                    }
                }

                if (client == null) return;
                {
                    try
                    {
                        client.ErrorOccurred -= ClientErrorHandler;
                    }
                    catch
                    {
                        // ignored
                    }

                    try
                    {
                        client.HostKeyReceived -= HostKeyReceivedHandler;
                    }
                    catch
                    {
                        // ignored
                    }

                    try
                    {
                        if (client.IsConnected)
                            client.Disconnect();
                    }
                    catch (Exception ex)
                    {
                        Debug("Client disconnect: " + ex.Message);
                    }

                    try
                    {
                        client.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Debug("Client dispose: " + ex.Message);
                    }
                }
            }
            finally
            {
                lock (_disconnectLock)
                {
                    _disconnecting = false;
                }
            }
        }

        private void StopConnectionRequests()
        {
            lock (_connectionLock)
            {
                _connectionRequested = false;
                _nextReconnectDelayMs = InitialReconnectDelayMs;
            }

            CancelReconnect();
        }

        private void ScheduleReconnect()
        {
            long delay;
            lock (_connectionLock)
            {
                if (!_connectionRequested)
                {
                    return;
                }

                delay = _nextReconnectDelayMs;
                _nextReconnectDelayMs = Math.Min(
                    _nextReconnectDelayMs * 2,
                    MaximumReconnectDelayMs);
            }

            Debug("Scheduling reconnect in " + (delay / 1000) + " seconds");
            _reconnectTimer.Reset(delay);
        }

        private void CancelReconnect()
        {
            try
            {
                if (_reconnectTimer != null)
                {
                    _reconnectTimer.Stop();
                }
            }
            catch (Exception ex)
            {
                Debug("Reconnect timer stop: " + ex.Message);
            }
        }

        private void ReconnectTimerCallback(object state)
        {
            lock (_connectionLock)
            {
                if (!_connectionRequested)
                {
                    return;
                }
            }

            Debug("Reconnect timer expired");
            AttemptConnection();
        }

        private void HandleConnectionLoss(string reason)
        {
            Debug(reason);
            CleanupConnection();
            ScheduleReconnect();
        }



        public void SendCommand(string command)
        {
            try
            {
                lock (_ioLock)
                {
                    var stream = _stream;
                    var client = _client;
                    if (client == null || !client.IsConnected || stream == null || !stream.CanWrite)
                    {
                        Debug("SendCommand() not connected");
                        return;
                    }

                    stream.WriteLine(command);
                    stream.Flush();
                }
            }
            catch (Exception e)
            {
                HandleConnectionLoss("SendCommand error: " + e.Message);
            }
        }

        private void StreamDataReceivedHandler(object sender, ShellDataEventArgs e)
        {
            ShellStream stream;
            lock (_ioLock)
            {
                stream = _stream;
            }

            if (stream == null) return;
            if (!object.ReferenceEquals(sender, stream)) return;
            var dataReceived = "";
            try
            {
                var sb = new System.Text.StringBuilder();
                while (stream.DataAvailable)
                {
                    sb.Append(stream.Read());
                }

                dataReceived = sb.ToString();
            }
            catch(Exception ex)
            {
                Debug("Stream read exception (likely disposing): " + ex.Message);
                return;
            }
            if (string.IsNullOrEmpty(dataReceived)) return;
            lock (_healthLock)
            {
                _lastRxTicks = DateTime.UtcNow.Ticks;
                _missedKeepalives = 0;
            }
            if(dataReceived.IndexOf(HeartbeatToken, StringComparison.Ordinal) >= 0)
                dataReceived = dataReceived.Replace(HeartbeatToken, string.Empty);
            if (dataReceived.Length == 0) return;
            if (dataReceived.Length > 250)
            {
                foreach (var chunk in SplitDataReceived(dataReceived, 250))
                {
                    SafeInvokeReceivedData(chunk);
                }

            }
            else SafeInvokeReceivedData(dataReceived);
        }

        private void SendHeartBeat()
        {
            try
            {
                lock (_ioLock)
                {
                    var stream = _stream;
                    if (stream == null || !stream.CanWrite) return;

                    stream.WriteLine("echo " + HeartbeatToken);
                    stream.Flush();
                }
            }
            catch
            {
                // ignored
            }
        }

        private void StreamErrorOccurredHandler(object sender, EventArgs e)
        {
            ShellStream stream;
            lock (_ioLock)
            {
                stream = _stream;
            }

            if (!object.ReferenceEquals(sender, stream)) return;
            HandleConnectionLoss("SSH shell stream error: " + e);
        }
        private void AuthenticationPromptHandler(object sender, AuthenticationPromptEventArgs e)
        {
            Debug("Sending password");
            foreach (var prompt in e.Prompts)
            {
                prompt.Response = _password;

            }
        }
        private void HostKeyReceivedHandler(object sender, HostKeyEventArgs e)
        {
            Debug("Host key received");
            e.CanTrust = true;
        }
        private void ClientErrorHandler(object sender, ExceptionEventArgs e)
        {
            SshClient client;
            lock (_ioLock)
            {
                client = _client;
            }

            if (!object.ReferenceEquals(sender, client)) return;
            HandleConnectionLoss("SSH client error: " + e.Exception.Message);
        }

        private IEnumerable<string> SplitDataReceived(string str, int maxChunkSize)
        {
            for (var i = 0; i < str.Length; i += maxChunkSize)
            {
                yield return str.Substring(i, Math.Min(maxChunkSize, str.Length - i));
            }
        }

        private void SetConnectionState(ushort state)
        {
            bool changed;
            lock (_stateLock)
            {
                changed = _currentConnState != state;
                _currentConnState = state;
            }
            if (!changed) return;
            SafeInvokeConnectionState(state);
            Debug($"ConnectionState -> {state}");
        }

        private void SafeInvokeInitialized(ushort state)
        {
            try
            {
                InitializedData?.Invoke(state);
            }
            catch (Exception ex)
            {
                Debug("InitializedData callback error: " + ex.Message);
            }
        }

        private void SafeInvokeConnectionState(ushort state)
        {
            try
            {
                ConnectionState?.Invoke(state);
            }
            catch (Exception ex)
            {
                Debug("ConnectionState callback error: " + ex.Message);
            }
        }

        private void SafeInvokeReceivedData(SimplSharpString data)
        {
            try
            {
                ReceivedData?.Invoke(data);
            }
            catch (Exception ex)
            {
                Debug("ReceivedData callback error: " + ex.Message);
            }
        }

        private void StartMonitor()
        {
            StopMonitor();
            lock (_healthLock)
            {
                _lastRxTicks = DateTime.UtcNow.Ticks;
                _missedKeepalives = 0;
            }
            _monitorTimer = new CTimer(_ =>
            {
                try
                {
                    if (_client == null || !_client.IsConnected|| _stream == null)
                    {
                        HandleConnectionLoss("Monitor: not connected");
                        return;
                    }
                    var nowTicks = DateTime.UtcNow.Ticks;
                    long last;
                    lock (_healthLock)
                    {
                        last = (_lastRxTicks == 0) ? nowTicks : _lastRxTicks;
                    }
                    var idleMs = (nowTicks - last) / TimeSpan.TicksPerMillisecond;


                    if (idleMs > IdleTimeoutMs)
                    {
                        int currentMisses;
                        lock (_healthLock)
                        {
                            _missedKeepalives++;
                            currentMisses = _missedKeepalives;
                        }
                        Debug(
                            $"Monitor: idle {idleMs / 1000.0:0.0}s (> {IdleTimeoutMs / 1000.0:0.0}s) miss #{currentMisses}; sending heartbeat");
                        SendHeartBeat();
                        if (currentMisses >= MaxMisses)
                        {
                            HandleConnectionLoss("Monitor: heartbeat misses exceeded");
                            return;
                        }

                    }

                    SetConnectionState(1);
                }
                catch (Exception ex)
                {
                    HandleConnectionLoss("Monitor exception: " + ex.Message);
                }
            }, null, MonitorIntervalMs,  MonitorIntervalMs);
        }

        private void StopMonitor()
        {
            try
            {
                if (_monitorTimer == null) return;
                _monitorTimer.Stop();
                _monitorTimer.Dispose();
                _monitorTimer = null;
            }
            catch (Exception)
            {
                // ignored
            }
        }
    }
}
