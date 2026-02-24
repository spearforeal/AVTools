// Simpl# SSH Client Library for Crestron 4-Series
// Supports password and key-based SSH authentication and interactive shell sessions.
// Uses Crestron.SimplSharp.Ssh (SSH.NET) for SSH functionality.

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
        
        
        

        public void Debug(string message)
        {
            if (DebugEnable >= 1)
            {
                CrestronConsole.PrintLine(" [" + _debugName + "] " + message);
            }
        }

        public void Initialize(string hostname, int port, string username, string password, string debugName)
        {
            this._hostname = hostname;
            this._port = port;
            this._username = username;
            this._password = password;
            this._debugName = debugName;

            Debug($"Initializing SSH client: {hostname}:{port}:{username}");
            _initialized = true;
            SafeInvokeInitialized(1);
            SafeInvokeConnectionState(0);
            //InitializedData(Convert.ToUInt16(1));
            
        }


        public void Connect()
        {
            if (!_initialized)
            {
                Debug("Connect() called but not initialized");
                return;
            }

            var user = (_username ?? "").Trim();
            var host = (_hostname ?? "").Trim();
            var pass = (_password ?? "").Trim();
            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(host))
            {
                Debug("Connect blocked");
                return;
            }

            if (_port <= 0)
            {
                Debug("Connect blocked: Invalid port number");
                return;
            }

            _username = user;
            _hostname = host;
            _password = pass;

            if (_client != null)
            {
                if (_client.IsConnected)
                {
                    if (_stream != null && _stream.CanWrite)
                    {
                        Debug("Connect ignored: already connected");
                        return;
                    }

                    Debug("Connect(): client connected but stream invalid; reconnecting.");
                }
                else
                {
                    Debug("Connect(): previous client existed but was not connected; cleaning up.");
                }
                Disconnect();
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
                Disconnect();
                return;
            }
            catch (SshConnectionException e)
            {
                Debug("Connection error: " + e.Message + ", Reason:  " + e.DisconnectReason);
                if(e.InnerException != null) Debug("Inner: " +  e.InnerException.Message);
                Disconnect();
                return;
            }
            catch (Exception e)
            {
                Debug("Unexpected connect error: " + e.Message);
                Disconnect();
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
                Disconnect();
                return;
            }
            if (_client.IsConnected)
            {
                Debug("Connected");
                SetConnectionState(1);
                StartMonitor();
            }
            else
            {
                Debug("Could not complete connection");
            }
        }

        public void Disconnect()
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
                Debug("SendCommand error: " + e.Message);
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
            Debug("$SSH Shellstream error " + e);
            SetConnectionState(0);
            Disconnect();

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
            Debug("SSH client error " + e.Exception.Message);
            SetConnectionState(0);
            Disconnect();
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
                        Debug("Monitor: not connected");
                        SetConnectionState(0);
                        Disconnect();
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
                            Debug("Monitor: heartbeat missed exceeded. Forcing disconnect");
                            SetConnectionState(0);
                            Disconnect();
                            return;
                        }

                    }

                    SetConnectionState(1);
                }
                catch (Exception ex)
                {
                    Debug("Monitor exception: " + ex.Message);
                    SetConnectionState(0);
                    Disconnect();
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
