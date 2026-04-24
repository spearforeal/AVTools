using Crestron.SimplSharp;
using Crestron.SimplSharp.CrestronSockets;
using System;
using System.Text;
namespace TcpClientGenericSPlus
{
    public class TcpClientGeneric
    {
        public const ushort Connected = 2;
        public const ushort Disconnected = 0;
        private TCPClient _client;
        public string Address;
        public int Port;
        public string Username = string.Empty;
        public string Password = string.Empty;
        public string LoginPrompt = string.Empty;
        public string PasswordPrompt = string.Empty;
        public string LoginSuccess = string.Empty;
        public string LoginFailure = string.Empty;
        public string DisconnectCommand = string.Empty;
        private bool _loggedIn;
        public int RxBufferSize;
        public string DebugName;
        public bool Debug;
        public bool RawBytes;
        public bool RemoveNullCharacters;
        private string _splitOnCharacter;
        public bool SendKeepALive;
        public bool ReconnectAutomatically;
        private CTimer _reconnectTimer;
        private CTimer _connectionTimer;
        private bool _enableAutoReconnect;
        private bool _reconnectIsActive;
        public bool IsConnected;
        public long ConnectionTimeout = 5000;
        public long ReconnectTimeout = 2000;
        public long RecheckForValidAddressTime = 6000;
        private int _connectionTimerCount;
        private bool _systemNotShuttingDown;
        public event SocketChangeEvent OnSocketChangeEvent;
        public event SocketChangeSPlusEvent OnSocketChangeSPlusEvent;
        public event DataRxEventSPlus OnDataRxEventSPlus;
        

        public TcpClientGeneric()
        {
            SendKeepALive = false;
            ReconnectAutomatically = false;
            RemoveNullCharacters = false;
            RawBytes = false;
            DebugName = nameof(TcpClientGeneric);
            RxBufferSize = 4096;
            _splitOnCharacter = string.Empty;
            _systemNotShuttingDown = true;
            _reconnectTimer = new CTimer(reconnect_TimerCallback, null, -1L);
            _connectionTimer = new CTimer(connection_TimerCallback, null, -1L);
            CrestronEnvironment.ProgramStatusEventHandler += CrestronEnvironment_ProgramStatusEventHandler;
        }

        public void SendString(string str)
        {
            try
            {
                if (_client != null)
                {
                    var buffer = RawBytes ? GetRawBytes(str) : Encoding.ASCII.GetBytes(str);
                    var num = (int)_client.SendData(buffer, buffer.Length);
                }

                if (!Debug || str.Length <= 0)
                    return;
                ConsolePrintAsciiHexStrings.Print(str, "[#] " + DebugName + ". TX: ");
            }
            catch (Exception ex)
            {
                if (_client != null && IsConnected)
                {
                    var num = (int)_client.DisconnectFromServer();
                }
                else
                    ErrorLog.Error("[#] {0}. SendString unhandled exception: {1}", DebugName, ex.ToString());
            }
        }

        public void KeepAlive()
        {
            if (_client == null)
                return;
            try
            {
                if (_client.ClientStatus != SocketStatus.SOCKET_STATUS_CONNECTED || !SendKeepALive)
                    return;
                SendString(string.Empty);
            }
            catch (Exception ex)
            {
                if (!Debug)
                    return;
                CrestronConsole.PrintLine("[#] {0}. KeepAlive. exception sending KeepAlive; disconnecting. exception: {1}", DebugName, ex.Message);
            }
        }

        public void Connect()
        {
            try
            {
                _enableAutoReconnect = ReconnectAutomatically;
                if (Debug)
                    CrestronConsole.PrintLine("[#] {0}. Trying to connect to: {1}:{2}", DebugName, Address, Port);
                if (Address != null)
                {
                    if (Address.Length > 0)
                    {
                        if (_client == null)
                        {
                            _client = new TCPClient(Address, Port, RxBufferSize);
                            _client.HandleLinkLoss();
                            _client.HandleLinkUp();
                            _client.SocketStatusChange +=
                                new TCPClientSocketStatusChangeEventHandler(client_SocketStatusChangeEventHandler);
                        }
                        else
                        {
                            if (Debug)
                                CrestronConsole.PrintLine(
                                    "[#] {0}. client.Address/Internal: {1}/{2}, client.Port/Internal: {3}/{4}.",
                                    DebugName, _client.AddressClientConnectedTo, Address, _client.PortNumber, Port);
                            if (_client.AddressClientConnectedTo != Address)
                                _client.AddressClientConnectedTo = Address;
                            if (_client.PortNumber != Port)
                                _client.PortNumber = Port;
                        }

                        var serverAsync = (int)_client.ConnectToServerAsync(_tcpClientConnectCallback);
                        if (Debug)
                            CrestronConsole.PrintLine("[#] {0}. Starting connectionTimer.", DebugName);
                        _connectionTimer.Reset(100L);
                    }
                    else
                    {
                        _reconnectTimer.Reset(RecheckForValidAddressTime);
                        _reconnectIsActive = ReconnectAutomatically;
                        if (!Debug)
                            return;
                        CrestronConsole.PrintLine("[#] {0}. Address is empty. Retrying in {1}s.", DebugName,
                            (RecheckForValidAddressTime / 1000L));
                    }
                }
                else
                {
                    _reconnectTimer.Reset(RecheckForValidAddressTime);
                    _reconnectIsActive = ReconnectAutomatically;
                    if (!Debug)
                        return;
                    CrestronConsole.PrintLine("[#] {0}. Address is null. Retrying in {1}s.", DebugName,
                        (RecheckForValidAddressTime / 1000L));
                }
            }
            catch (SocketException ex)
            {
                DisconnectAndDispose();
                _reconnectTimer.Reset(ReconnectTimeout);
                _reconnectIsActive = ReconnectAutomatically;
                if(Debug)
                    CrestronConsole.PrintLine("[#] {0}. Could not connect to {1}. Retrying in {2}s. SocketException: {3}.", Debug, Address, (ReconnectTimeout / 1000L), ex.Message);
                ErrorLog.Error("[#] {0}. Could not connect to {1}; will try to reconnect. SocketException: {2}.", DebugName, Address, ex.Message);
            }
        }

        public void EnableKeepAlive() => SendKeepALive = true;
        public void DisableKeepAlive() => SendKeepALive = false;

        public string GetValidUserPass(string pass)
        {
            var validUserPass = string.Empty;
            if (!string.IsNullOrEmpty(pass))
                validUserPass = pass;
            return validUserPass;
        }

        public void Disconnect()
        {
            try
            {
                if (_reconnectIsActive)
                {
                    _reconnectIsActive = false;
                    _reconnectTimer.Stop();
                }

                _enableAutoReconnect = false;
                _connectionTimer.Stop();
                DisconnectAndDispose();
            }
            catch (Exception ex)
            {
                ErrorLog.Error("[#] {0}. Disconnect() exception: {1}", DebugName, ex.ToString());
            }
        }

        public void DisconnectAndDispose()
        {
            try
            {
                if (_client != null)
                {
                    if (_client.ClientStatus == SocketStatus.SOCKET_STATUS_CONNECTED)
                    {
                        if (DisconnectCommand.Length > 0)
                        {
                            SendString(DisconnectCommand + "\r");
                            if (!_systemNotShuttingDown)
                                CrestronConsole.PrintLine(
                                    "[#] {0}. Detected program stopping while connected. Send disconnect command to client.",
                                    DebugName);
                        }

                        var num = (int)_client.DisconnectFromServer();
                        if (Debug)
                            CrestronConsole.PrintLine("[#] {0}. Disconnecting from {1} and disposing client.",
                                DebugName, Address);
                    }
                    else if (Debug)
                        CrestronConsole.PrintLine("[#] {0}. Cannot disconnect, not connected. Disposing", DebugName);

                    _client.Dispose();
                }
                else
                {
                    if (!Debug)
                        CrestronConsole.PrintLine("[#] {0}. Cannot disconnect, client not instantiated", DebugName);
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Error("[#] {0}. DisconnectAndDispose() exception: {1}", DebugName, ex.ToString());
            }
        }

        public void SetSplitCharacter(string splitOn)
        {
            if(splitOn.Length == 1)
                _splitOnCharacter = splitOn;
            else if (splitOn.Length > 1)
            {
                _splitOnCharacter = splitOn.Substring(splitOn.Length - 1, 1);
                ErrorLog.Error(
                    "[#] {0}. Attempting to set a split string ({1}) longer than 1 character. The last character will be used: {2}",
                    DebugName, splitOn, splitOn.Substring(splitOn.Length - 1, 1));
            }
            else
                _splitOnCharacter = string.Empty;
        }

        public void EnableDebug() => Debug = true;
        public void DisableDebug() => Debug = false;
        public void EnableRawBytes() => RawBytes = true;
        public void DisableRawBytes() => RawBytes = false;
        public void EnableRemoveNullCharacters() => RemoveNullCharacters = true;
        public void DisableRemoveNullCharacters() => RemoveNullCharacters = false;

        private static byte[] GetRawBytes(string str)
        {
            if (string.IsNullOrEmpty(str))
                return new byte[0];

            var bytes = new byte[str.Length];
            for (var i = 0; i < str.Length; i++)
                bytes[i] = (byte)(str[i] & 0xFF);

            return bytes;
        }

        private static string GetRawString(byte[] buffer, int length)
        {
            if (buffer == null || length <= 0)
                return string.Empty;

            var chars = new char[length];
            for (var i = 0; i < length; i++)
                chars[i] = (char)buffer[i];

            return new string(chars);
        }

        public void SendResponseToSPlus(string response)
        {
            var dataRxEventSPlus = OnDataRxEventSPlus;
            if (dataRxEventSPlus == null)
                return;
            dataRxEventSPlus(this, new SerialTransmitEventArgs() { Message = response });
        }

        private void RaiseSPlusSocketChangeEvent()
        {
            if (!IsConnected)
                _loggedIn = false;
            var changeSPlusEvent = OnSocketChangeSPlusEvent;
            if (changeSPlusEvent != null)
                changeSPlusEvent(this, new AnalogTransmitEventArgs()
                {
                    Message = IsConnected ? (ushort)2 : (ushort)0
                });
            if (!Debug)
                return;
            CrestronConsole.PrintLine("[#] {0}. RaiseSPlusSocketChangeEvent(). IsConnected: {1}", DebugName, IsConnected);
        }

        private void CheckLoginInfo(string str)
        {
            if (str.ToLower().Contains(LoginPrompt.ToLower()))
            {
                if (Username.Length <= 0)
                    return;
                SendString(Username + "\r");
                if (LoginSuccess.Length != 0 || Password.Length != 0)
                    return;
                _loggedIn = true;
            }
            else if (str.ToLower().Contains(PasswordPrompt.ToLower()))
            {
                if (Password.Length <= 0) return;
                SendString(Password + "\r");
                if (LoginSuccess.Length != 0)
                    return;
                _loggedIn = true;
            }
            else if (str.ToLower().Contains(LoginSuccess.ToLower()))
            {
                _loggedIn = true;
            }
            else
            {
                if (!str.ToLower().Contains(LoginFailure.ToLower()))
                    return;
                _loggedIn = false;
            }
        }

        private void client_SocketStatusChangeEventHandler(TCPClient rxClient, SocketStatus clientSocketStatus)
        {
            bool flag = false;
            switch (clientSocketStatus)
            {
                case SocketStatus.SOCKET_STATUS_NO_CONNECT:
                case SocketStatus.SOCKET_STATUS_WAITING:
                case SocketStatus.SOCKET_STATUS_CONNECT_FAILED:
                case SocketStatus.SOCKET_STATUS_BROKEN_REMOTELY:
                case SocketStatus.SOCKET_STATUS_BROKEN_LOCALLY:
                case SocketStatus.SOCKET_STATUS_DNS_FAILED:
                case SocketStatus.SOCKET_STATUS_LINK_LOST:
                case SocketStatus.SOCKET_STATUS_SOCKET_NOT_EXIST:
                    if(IsConnected)
                        flag = true;
                    IsConnected = false;
                    if (_enableAutoReconnect && _reconnectIsActive)
                    {
                        ErrorLog.Error("[#] {0}.  client_SocketStatusChangeEventHandler.  Not connected to {1}: {2}.  Attempting reconnect in {3}s.", DebugName, Address, clientSocketStatus, ReconnectTimeout / 1000L);
                        _reconnectIsActive = true;
                        _reconnectTimer.Reset(ReconnectTimeout); 
                    }else
                        ErrorLog.Error("[#] {0}.  client_SocketStatusChangeEventHandler.  Not connected to {1}: {2}.  Not attempting reconnect.", DebugName, Address, clientSocketStatus);
                    if (Debug)
                    {
                        CrestronConsole.PrintLine("[#] {0}.  client_SocketStatusChangeEventHandler.  Not connected to {1}.  ClientStatus: {2}", DebugName, Address, clientSocketStatus);
                    }
                    break;
                case SocketStatus.SOCKET_STATUS_CONNECTED:
                    if (!IsConnected)
                        flag = true;
                    IsConnected = true;
                    _reconnectIsActive = false;
                    _reconnectTimer.Stop();
                    if (Debug)
                    {
                        CrestronConsole.PrintLine("[#] {0}.  client_SocketStatusChangeEventHandler.  Client connected to {1}.", DebugName, Address);
                    }
                    break;
                case SocketStatus.SOCKET_STATUS_DNS_LOOKUP:
                case SocketStatus.SOCKET_STATUS_DNS_RESOLVED:
                    if (Debug)
                    {
                        CrestronConsole.PrintLine("[#] {0}.  client_SocketStatusChangeEventHandler.  Pending Connection to {1} ClientStatus: {2}", DebugName, Address, clientSocketStatus);
                    }
                    break;
            }
            if (!flag || !_systemNotShuttingDown)
                return;
            RaiseSPlusSocketChangeEvent();
        }
        private void _tcpClientConnectCallback(TCPClient rxClient)
        {
            if (!IsConnected)
                return;
            if (_systemNotShuttingDown)
            {
                var dataAsync = (int) rxClient.ReceiveDataAsync(this._tcpClientReceiveCallback);
            }
            if (!Debug)
                return;
            CrestronConsole.PrintLine("[#] {0}.  tcpClientConnectCallback.  Client connected to {1}.  Waiting for async data.", DebugName, Address);
        }
        private void _tcpClientReceiveCallback(TCPClient rxClient, int bytesReceived)
        {
            try
            {
                if (IsConnected && bytesReceived > 0)
                {
                    var incomingDataBuffer = rxClient.IncomingDataBuffer;
                    var str1 = RawBytes
                        ? GetRawString(incomingDataBuffer, bytesReceived)
                        : Encoding.UTF8.GetString(incomingDataBuffer, 0, bytesReceived);
                    var empty = string.Empty;
                    var str2 = !RemoveNullCharacters ? str1 : str1.Replace("\0", string.Empty);
                    if (_splitOnCharacter.Length > 0)
                    {
                        var strArray = str2.Split(_splitOnCharacter[_splitOnCharacter.Length - 1]);
                        foreach (var str3 in strArray)
                        {
                            if (!_loggedIn)
                                CheckLoginInfo(str3);
                            if (strArray.Length > 1)
                                SendResponseToSPlus(str3 + _splitOnCharacter[_splitOnCharacter.Length - 1]);
                            else
                                SendResponseToSPlus(str3);
                        }
                    }
                    else
                        SendResponseToSPlus(str2);
                    if (Debug)
                        ConsolePrintAsciiHexStrings.Print(str2, "[#] " + DebugName + ". RX: ");
                }
                if (!_systemNotShuttingDown)
                    return;
                var dataAsync = (int) rxClient.ReceiveDataAsync(_tcpClientReceiveCallback);
            }
            catch (Exception ex)
            {
                ErrorLog.Error("[#] {0}.  clientStream_DataReceived exception: {1}", DebugName, ex.ToString());
            }
        }
        private void CrestronEnvironment_ProgramStatusEventHandler(
            eProgramStatusEventType programEventType)
        {
            try
            {
                if (programEventType != eProgramStatusEventType.Stopping || _client == null)
                    return;
                _systemNotShuttingDown = false;
                if (_client.ClientStatus == SocketStatus.SOCKET_STATUS_CONNECTED)
                {
                    Disconnect();
                    CrestronConsole.PrintLine("[#] {0}.  Detected program stopping.  Disconnecting client.", DebugName);
                }
                else
                    CrestronConsole.PrintLine("[#] {0}.  Detected program stopping.  Client not connected, nothing to disconnect.", DebugName);
            }
            catch (Exception ex)
            {
                ErrorLog.Error("[#] {0}.  Exception thrown during program stop: {1}", DebugName, ex.Message);
            }
        }
        private void reconnect_TimerCallback(object sender)
        {
            try
            {
                if (IsConnected)
                    return;
                Connect();
                _reconnectTimer.Reset(ReconnectTimeout);
                if (!Debug)
                    return;
                CrestronConsole.PrintLine("[#] {0}.  reconnect_TimerCallback triggered Connect.  Will attempt to reconnect in {1}s if this attempt fails.", DebugName, ReconnectTimeout / 1000L);
            }
            catch (Exception ex)
            {
                CrestronConsole.PrintLine("[#] {0}.  reconnect_TimerCallback exception: {1}", DebugName, ex.ToString());
            }
        } 
        private void connection_TimerCallback(object sender)
    {
      try
      {
        if (!_systemNotShuttingDown)
          return;
        if (IsConnected != (_client.ClientStatus == SocketStatus.SOCKET_STATUS_CONNECTED))
        {
          IsConnected = _client.ClientStatus == SocketStatus.SOCKET_STATUS_CONNECTED;
          if (Debug)
            CrestronConsole.PrintLine("[#] {0}.  connection_TimerCallback.  raising SPlusSocketChangeEvent.  Connected: {1}", DebugName, IsConnected);
          RaiseSPlusSocketChangeEvent();
          SocketChangeEvent socketChangeEvent = OnSocketChangeEvent;
          if (socketChangeEvent != null)
            socketChangeEvent(this, _client.ClientStatus == SocketStatus.SOCKET_STATUS_CONNECTED);
        }
        if (_enableAutoReconnect && !_reconnectIsActive && !IsConnected)
        {
          _reconnectTimer.Reset(ReconnectTimeout);
          _reconnectIsActive = true;
          if (Debug)
            CrestronConsole.PrintLine("[#] {0}.  connection_TimerCallback.  Resetting the reconnect timer; reconnect in {0}s.", DebugName, ReconnectTimeout / 1000L);
        }
        _connectionTimer.Reset(ConnectionTimeout);
        if (!Debug)
          return;
        ++_connectionTimerCount;
        if (_connectionTimerCount % Math.Floor((double) (10000L / ConnectionTimeout)) != 0.0)
          return;
        if (_client != null)
        {
          CrestronConsole.PrintLine("[#] {0}.  connection_TimerCallback.  client.IsConnected: {1}", DebugName, IsConnected);
          _connectionTimerCount = 0;
        }
        else
        {
          CrestronConsole.PrintLine("[#] {0}.  connection_TimerCallback.  client is null.", DebugName);
          _connectionTimerCount = 0;
        }
      }
      catch (Exception ex)
      {
        ErrorLog.Error("[#] {0}.  connection_TimerCallback exception: {1}", DebugName, ex.Message);
      }
    }
        public delegate void SocketChangeEvent(object sender, bool connected);

        public delegate void SocketChangeSPlusEvent(object sender, AnalogTransmitEventArgs connectedState);
        public delegate void DataRxEventSPlus(object sender, SerialTransmitEventArgs args);

    }
}



