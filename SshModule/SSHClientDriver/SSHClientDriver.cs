// Simpl# SSH Client Library for Crestron 4-Series
// Supports password and key-based SSH authentication and interactive shell sessions.
// Uses Crestron.SimplSharp.Ssh (SSH.NET) for SSH functionality.

using System;
using System.Collections.Generic;
using System.Text;
using Crestron.SimplSharp;
using Crestron.SimplSharp.Net;
using Crestron.SimplSharp.Ssh;
using Crestron.SimplSharp.Ssh.Common;
using Crestron.SimplSharp.CrestronSockets;

namespace SSHClientDriver
{
    public delegate void InitializedDataHandler(ushort state);

    public delegate void ConnectionStateHandler(ushort state);

    public delegate void ReceivedDataHandler(SimplSharpString data);

    public class SSHClientDevice
    {
        private bool _initialized = false;
        private SshClient _client;
        private ShellStream _stream;
        private string username, hostname, password;
        private int port;
        public InitializedDataHandler InitializedData { get; set; }
        public ConnectionStateHandler ConnectionState { get; set; }
        public ReceivedDataHandler ReceivedData { get; set; }
        private string _debugName;
        public ushort DebugEnable = 0;
        private CTimer _monitorTimer;
        private long _monitorIntervalMs = 5000;
        private long _idleTimeoutMs = 30000; 
        private long _lastRxTicks = 0;
        private ushort _currentConnState = 0;
        private readonly object _stateLock = new object();
        

        public void Debug(string message)
        {
            if (DebugEnable >= 1)
            {
                CrestronConsole.PrintLine(" [" + _debugName + "] " + message);
            }
        }

        public void Initialize(string hostname, int port, string username, string password, string debugName)
        {
            this.hostname = hostname;
            this.port = port;
            this.username = username;
            this.password = password;
            this._debugName = debugName;

            Debug($"Initializing SSH client: {hostname}:{port}:{username}:{password}");
            _initialized = true;
            InitializedData?.Invoke(1);
            ConnectionState?.Invoke(0);
            //InitializedData(Convert.ToUInt16(1));
            
        }


        public void Connect()
        {
            if (!_initialized)
            {
                Debug("Connecting SSH client...");
                return;
            }

            var authMethod = new KeyboardInteractiveAuthenticationMethod(username);
            authMethod.AuthenticationPrompt += AuthenticationPromptHandler;
            var pwd = new PasswordAuthenticationMethod(username, password);
            var connectInfo = new ConnectionInfo(hostname, port, username, new AuthenticationMethod[]{pwd, authMethod});
            
            _client = new SshClient(connectInfo);
            _client.KeepAliveInterval = TimeSpan.FromSeconds(30);
            _client.ErrorOccurred += new EventHandler<ExceptionEventArgs>(ClientErrorHandler);
            _client.HostKeyReceived += new EventHandler<HostKeyEventArgs>(HostKeyReceivedHandler);
            Debug("Attempting connection to: " + hostname + ":" + port);
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

            _stream = _client.CreateShellStream("terminal", 80, 24, 800, 600, 1024);
            _stream.DataReceived += new EventHandler<ShellDataEventArgs>(StreamDataReceivedHandler);
            _stream.ErrorOccurred += new EventHandler<ExceptionEventArgs>(StreamErrorOccurredHandler);
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
            Debug("Disconnect() called.");
            StopMonitor();
            SetConnectionState(0);
            try
            {
                if (_stream != null)
                {
                    try
                    {

                        _stream.DataReceived -= StreamDataReceivedHandler;
                    }
                    catch
                    {
                        // ignored
                    }

                    try
                    {
                        _stream.ErrorOccurred -= StreamErrorOccurredHandler;
                    }
                    catch
                    {
                        // ignored
                    }

                    try
                    {
                        _stream.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Debug("Stream dispose " + ex.Message);
                    }

                    _stream = null;

                }

            }
            catch (Exception e)
            {
                Debug("Disconnect() stream exception " + e.Message);
            }

            try
            {
                if (_client == null) return;
                try
                {
                    if (_client.IsConnected) _client.Disconnect();
                }
                catch (Exception ex)
                {
                    Debug("Client Disconnect: " + ex.Message);
                }
                try
                {
                    _client.Dispose();
                }
                catch (Exception ex)
                {
                    Debug("Client dispose: " + ex.Message);
                }
                _client = null;
            }
            catch (Exception e)
            {
                Debug("Disconnect() exception occured: " + e.Message);
            }
        }
       public void SendCommand(string command)
        {
            if (_client == null || !_client.IsConnected || _stream == null || !_stream.CanWrite)
            {
                Debug("SendCommand() not connected");
                return;
            }

            try
            {
                _stream.WriteLine(command);
            }
            catch (Exception e)
            {
                Debug("SendCommand error: " + e.Message);
            }
        }

        private void StreamDataReceivedHandler(object sender, ShellDataEventArgs e)
        {
            _lastRxTicks = DateTime.UtcNow.Ticks;
            var stream = (ShellStream)sender;
            var dataReceived = "";
            while (stream.DataAvailable)
            {
                dataReceived += stream.Read();
            }

            if (string.IsNullOrEmpty(dataReceived)) return;
            if (dataReceived.Length > 250)
            {
                foreach (var chunk in SplitDataReceived(dataReceived, 250))
                {
                    ReceivedData?.Invoke(chunk);
                }

            }
            else ReceivedData?.Invoke(dataReceived);
        }

        private void StreamErrorOccurredHandler(object sender, System.EventArgs e)
        {
            Debug("$SSH Shellstream error " + e.ToString());
            SetConnectionState(0);
            Disconnect();

        }
        private void AuthenticationPromptHandler(object sender, AuthenticationPromptEventArgs e)
        {
            Debug("Sending password");
            foreach (AuthenticationPrompt prompt in e.Prompts)
            {
                prompt.Response = password;

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

        private IEnumerable<string> SplitDataReceived(string str, int maxChuckSize)
        {
            for (var i = 0; i < str.Length; i += maxChuckSize)
            {
                yield return str.Substring(i, Math.Min(maxChuckSize, str.Length - i));
            }
        }

        private List<string> SplitDataReceived(string str, int maxChuckSize, int i)
        {
            var stringLength = str.Length;
            var strArray = new List<string>();
            for (i = 0; i < stringLength; i += maxChuckSize)
            {
                if (i + maxChuckSize > stringLength)
                {
                    maxChuckSize = stringLength - i;

                }

                strArray.Add(str.Substring(i, maxChuckSize));
            }
            return strArray;
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
            ConnectionState?.Invoke(state);
            Debug($"ConnectionState -> {state}");
        }

        private void StartMonitor()
        {
            StopMonitor();
            _lastRxTicks = DateTime.UtcNow.Ticks;
            _monitorTimer = new CTimer(_ =>
            {
                try
                {
                    if (_client == null || !_client.IsConnected|| _stream == null)
                    {
                        Debug("Monitor: IsConnected == false");
                        SetConnectionState(0);
                        Disconnect();
                        return;
                    }
                    var nowTicks = DateTime.UtcNow.Ticks;
                    var last = (_lastRxTicks == 0) ? nowTicks : _lastRxTicks;
                    var idleMs = (nowTicks - last) / TimeSpan.TicksPerMillisecond;


                    if (idleMs > _idleTimeoutMs)
                    {
                        Debug(
                            $"Monitor: idle {idleMs / 1000.0:0.0}s > {_idleTimeoutMs / 1000.0:0.0}s, sending keepalive");
                        var ok = TryKeepAlive();
                        if (!ok)
                        {
                            Debug("Monitor: keepalive failed");
                            SetConnectionState(0);
                            Disconnect();
                            return;
                        }

                        _lastRxTicks = DateTime.UtcNow.Ticks;
                    }

                    SetConnectionState(1);
                }
                catch (Exception ex)
                {
                    Debug("Monitor exception: " + ex.Message);
                    SetConnectionState(0);
                    Disconnect();
                }
            }, null, _monitorIntervalMs,  _monitorIntervalMs);
        }

        private void StopMonitor()
        {
            try
            {
                if (_monitorTimer != null)
                {
                    _monitorTimer.Stop();
                    _monitorTimer.Dispose();
                    _monitorTimer = null;
                }
            }
            catch (Exception e)
            {
                // ignored
            }
        }

        private bool TryKeepAlive()
        {
            try
            {
                if (_client != null && _client.IsConnected)
                {
                    _client.SendKeepAlive();
                    return true;
                }

                return false;
            }
            catch
            {
                try
                {
                    if (_stream != null && _stream.CanWrite)
                    {
                        _stream.Write("\n");
                        _stream.Flush();
                        return true;
                    }
                }
                catch
                {
                    // ignored
                }

                return false;
            }
        }
    }
}
