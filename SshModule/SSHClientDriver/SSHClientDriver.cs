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

namespace SSHClientDriver
{
    public delegate void InitializedDataHandler(ushort state);

    public delegate void ConnectionStateHandler(ushort state);

    public delegate void ReceivedDataHandler(SimplSharpString data);

    public class SSHClientDevice
    {
        private bool initialized = false;
        private SshClient client;
        private ShellStream stream;
        private string username, hostname, password;
        private int port;
        public InitializedDataHandler InitializedData { get; set; }
        public ConnectionStateHandler ConnectionState { get; set; }
        public ReceivedDataHandler ReceivedData { get; set; }
        private string debugName;
        public ushort debugEnable = 0;

        public void Debug(string message)
        {
            if (debugEnable >= 1)
            {
                CrestronConsole.PrintLine(" [" + debugName + "] " + message);
            }
        }

        public void Initialize(string hostname, int port, string username, string password, string debugName)
        {
            this.hostname = hostname;
            this.port = port;
            this.username = username;
            this.password = password;
            this.debugName = debugName;

            Debug($"Initializing SSH client: {hostname}:{port}:{username}:{password}");
            initialized = true;
            InitializedData(Convert.ToUInt16(1));
        }

        public void Connect()
        {
            if (!initialized)
            {
                Debug("Connecting SSH client...");
                return;
            }

            var authMethod = new KeyboardInteractiveAuthenticationMethod(username);
            authMethod.AuthenticationPrompt +=
                new EventHandler<AuthenticationPromptEventArgs>(AuthenticationPromptHandler);
            client = new SshClient(hostname, port, username, password);
            client.ErrorOccurred += new EventHandler<ExceptionEventArgs>(ClientErrorHandler);
            client.HostKeyReceived += new EventHandler<HostKeyEventArgs>(HostKeyReceivedHandler);
            Debug("Attempting connection to: " + hostname + ":" + port);
            try
            {
                client.Connect();
            }
            catch (SshConnectionException e)
            {
                Debug("Connection error: " + e.Message + ", Reason: " + e.DisconnectReason);
                Disconnect();
                return;
            }

            stream = client.CreateShellStream("terminal", 80, 24, 800, 600, 1024);
            stream.DataReceived += new EventHandler<ShellDataEventArgs>(StreamDataReceivedHandler);
            stream.ErrorOccurred += new EventHandler<ExceptionEventArgs>(StreamErrorOccurredHandler);
            if (client.IsConnected)
            {
                Debug("Connected");
                ConnectionState(Convert.ToUInt16(1));
            }
            else
            {
                Debug("Could not complete connection");
            }
        }

        public void Disconnect()
        {
            Debug("Disconnect() called.");
            ConnectionState(Convert.ToUInt16(0));
            try
            {
                if (stream != null)
                    stream.Dispose();
            }
            catch (Exception e)
            {
                Debug("Disconnect() exception occured freeing strea: " + e.Message);
            }

            try
            {
                if (client != null && client.IsConnected)
                    client.Disconnect();
                client.Dispose();
            }
            catch (Exception e)
            {
                Debug("Disconnect() exception occured: " + e.Message);
            }
        }

        public void SendCommand(string Command)
        {
            if (client == null || client.IsConnected == false)
            {
                Debug("SendCommand() called, but client is not connected");
                Disconnect();
                return;
            }

            if (stream != null && stream.CanWrite)
                stream.WriteLine(Command);
        }

        private void StreamDataReceivedHandler(object sender, ShellDataEventArgs e)
        {
            var stream = (ShellStream)sender;
            var dataReceived = "";
            while (stream.DataAvailable)
            {
                dataReceived += stream.Read();
            }
            if(dataReceived != ""){
                if (dataReceived.Length > 250)
                {
                    var dataReceivedArray = SplitDataReceived(dataReceived, 250);
                    foreach (var str in dataReceivedArray)
                    {
                        ReceivedData(str);
                    }
                }
                else ReceivedData(dataReceived);
            }
        }

        private void StreamErrorOccurredHandler(object sender, System.EventArgs e)
        {
            Debug("$SSH Shellstream error " + e.ToString());
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
    }
}
