using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace JATT
{

    public class ServerDetails
    {
        /// <summary>
        /// Gets the <see cref="IPEndPoint"/> of the server.
        /// </summary>
        public IPEndPoint EndPoint { get; internal set; }
        /// <summary>
        /// Gets the welcome message of the server.
        /// </summary>
        public string WelcomeMessage { get; internal set; }
        /// <summary>
        /// Gets the number of clients on the server.
        /// </summary>
        public int Clients { get; internal set; }
        /// <summary>
        /// Gets the maximum number of clients of the server.
        /// </summary>
        public int MaxClients { get; internal set; }
        /// <summary>
        /// Gets a <see cref="bool"/> value that represents whether the server is password protected.
        /// </summary>
        public bool IsPasswordProtected { get; internal set; }
        /// <summary>
        /// 
        /// </summary>
        public override string ToString()
        {
            return String.Format("{0} {1}/{2} {3}", WelcomeMessage, Clients, MaxClients, IsPasswordProtected ? "[Protected]" : "");
        }
    }

    public class JATTClient
    {
        #region Delegates
        public delegate void WelcomeMessageReceivedHandler(ServerDetails serverDetails);
        public delegate void MessageReceivedHandler(Message message);
        public delegate void ConnectionHandler();
        public delegate void ErrorHandler(string error);
        public delegate void TransmissionEndHandler(byte[] data);
        public delegate void ServerFoundHandler(ServerDetails serverDetails);
        public delegate void LogWriteHandler(int level, string str);
        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets a <see cref="string"/> value used for identification by the server.
        /// </summary>
        public string Identifier { get; set; } = "Client1";
        /// <summary>
        /// Gets or sets a <see cref="string"/> value used by the authorization of the client.
        /// </summary>
        public string Password { get; set; } = "";
        /// <summary>
        /// Gets the state of the server. Used only with <see cref="ConnectWithAuth"/>.
        /// </summary>
        public ClientState State { get; internal set; }
        /// <summary>
        /// Gets a <see cref="ServerDetails"/> value that holds the details of the server to which the client is connected.
        /// </summary>
        public ServerDetails ServerDetails { get; private set; }
        [Obsolete]
        /// <summary>
        /// Gets or sets the value indicating whether the client uses echo.
        /// </summary>
        public bool UseEcho { get; set; } = false;
        /// <summary>
        /// Gets or sets the value indicating whether the client should listen for incoming data.
        /// </summary>
        public bool IsListening { get; set; } = false;
        /// <summary>
        /// Gets a value indicating whether the client received the echo of last sent data.
        /// </summary>
        //public bool EchoReceived { get { return _lastMessage == null; } }
        /// <summary>
        /// Gets a value indicating whether the client is connected to the server.
        /// </summary>
        public bool IsConnected { get { return IsClientConnected(); } }
        /// <summary>
        /// Occurs when the client receives any kind of <see cref="Message"/>.
        /// </summary>
        public event MessageReceivedHandler OnMessageReceived;
        /// <summary>
        /// Occurs when the client connects to the server.
        /// </summary>
        public event ConnectionHandler OnConnected;
        /// <summary>
        /// Occurs when the client gets registered by the server.
        /// </summary>
        public event ConnectionHandler OnRegistered;
        /// <summary>
        /// Occurs when the client disconnects from the server.
        /// </summary>
        public event ConnectionHandler OnDisconnected;
        /// <summary>
        /// Occurs when the server denies the registration of the client.
        /// </summary>
        public event ErrorHandler OnRegisterFailed;
        ///// <summary>
        ///// Occurs when the client receives the <see cref="ServerDetails"/>
        ///// </summary>
        //public event WelcomeMessageReceivedHandler OnWelcomeMsgReceived;
        /// <summary>
        /// Occurs when no more readable data avaliable.
        /// </summary>
        public event TransmissionEndHandler OnTransmissionEnd;
        /// <summary>
        /// Occurs when the <see cref="FindServers"/> method has found a server.
        /// </summary>
        public static event ServerFoundHandler OnServerFound;
        /// <summary>
        /// Used for logging purposes.
        /// </summary>
        public event LogWriteHandler OnLogWrite;

        #endregion

        #region Private variables
        private Socket _socket;
        bool _isActive = false;
        private List<byte> _message = new List<byte>();
        //private byte[] _lastMessage = null;
        private static ManualResetEvent _timeoutObject = new ManualResetEvent(false);
        private Thread _listenerThread = null;
        #endregion

        #region Public Methods
        /// <summary>
        /// Connects to the specified port on the specified host and tries to authorise using the given <see cref="Identifier"/> and <see cref="Password"/>.
        /// </summary>
        /// <param name="ip">The host to which you intend to connect.</param>
        /// <param name="port">The port number of the host to which you intend to connect.</param>
        /// <param name="timeoutMS">Timeout in ms</param>
        /// <returns></returns>
        public bool ConnectAndAuth(string ip, int port, int timeoutMS = 5000)
        {
            return ConnectAndAuth(new IPEndPoint(IPAddress.Parse(ip), port), timeoutMS);
        }
        /// <summary>
        /// Connects to the specified port on the specified host and tries to authorise using the given <see cref="Identifier"/> and <see cref="Password"/>.
        /// </summary>
        /// <param name="endPoint">The host to which you intend to connect.</param>
        /// <param name="timeoutMS">Timeout in ms</param>
        /// <returns></returns>
        public bool ConnectAndAuth(IPEndPoint endPoint, int timeoutMS = 5000)
        {
            _timeoutObject.Reset();
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            var result = _socket.BeginConnect(endPoint, ConnectCallback, null);

            if (_timeoutObject.WaitOne(timeoutMS, false))
            {
                if (_isActive)
                {
                    WaitForRegister(5);
                    return State == ClientState.Registered;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Connects to the specified port on the specified host.
        /// </summary>
        /// <param name="ip">The host to which you intend to connect.</param>
        /// <param name="port">The port number of the host to which you intend to connect.</param>
        public bool Connect(string ip, int port, int timeoutMS = 5000)
        {

            IPEndPoint endPoint = new IPEndPoint(IPAddress.Parse(ip), port);
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            var result = _socket.BeginConnect(endPoint, ConnectCallback, null);

            if (_timeoutObject.WaitOne(timeoutMS, false))
            {
                if (_isActive)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }



        /// <summary>
        /// Returns the received bytes. (Receiving a <see cref="Message"/> clears the buffer)
        /// </summary>
        /// <returns>Buffer.</returns>
        public byte[] Buffer()
        {
            return _message.ToArray();
        }
        /// <summary>
        /// Sends a message to the server using the <see cref="Message"/> class.
        /// </summary>
        /// <param name="message">The string to be sent.</param>
        public void SendMessage(string message)
        {
            SendMessage(new Message(message));
        }
        /// <summary>
        /// Sends a message to the server using the <see cref="Message"/> class.
        /// </summary>
        /// <param name="message">The message to be sent.</param>
        public void SendMessage(Message message)
        {
            SendMessage((byte[])message);
        }
        /// <summary>
        /// Sends the specified byte array to the server.
        /// </summary>
        /// <param name="message">The array to be sent.</param>
        public void SendMessage(byte[] data)
        {
            lock (_socket)
            {
                if (data == null || data.Length == 0 || !IsSocketConnected(_socket))
                    return;
                _socket.Send(data, 0, data.Length, SocketFlags.None);
            }
        }
        /// <summary>
        /// Sends a message asynchronously to the server using the <see cref="Message"/> class.
        /// </summary>
        /// <param name="message">The string to be sent.</param>
        public void SendMessageAsync(string message)
        {
            SendMessageAsync(new Message(message));
        }
        /// <summary>
        /// Sends a message asynchronously to the server using the <see cref="Message"/> class.
        /// </summary>
        /// <param name="message">The message to be sent.</param>
        public void SendMessageAsync(Message message)
        {
            SendMessageAsync(message);
        }
        /// <summary>
        /// Sends the specified byte array asynchronously to the server.
        /// </summary>
        /// <param name="message">The array to be sent.</param>
        public void SendMessageAsync(byte[] data)
        {
            lock (_socket)
            {
                if (data == null || data.Length == 0 || !IsSocketConnected(_socket))
                    return;
                bool completedAsync = false;
                using (SocketAsyncEventArgs completeArgs = new SocketAsyncEventArgs())
                {
                    completeArgs.UserToken = _socket;
                    completeArgs.SetBuffer(data, 0, data.Length);
                    completeArgs.Completed += SendCallback;
                    completeArgs.RemoteEndPoint = _socket.RemoteEndPoint;
                    try
                    {
                        completedAsync = _socket.SendAsync(completeArgs);
                    }
                    catch (SocketException se)
                    {
                        OnLogWrite?.Invoke(2, string.Format("Socket Exception: {0} Message: {1}", se.ErrorCode, se.Message));
                    }

                    if (!completedAsync)
                    {
                        SendCallback(this, completeArgs);
                    }
                }
            }
        }
        /// <summary>
        /// Disconnects the client.
        /// </summary>
        public void Disconnect()
        {
            IsListening = false;
            if(_socket != null)
                _socket.Close();
            _socket = null;
        }

        #endregion

        #region Private Methods

        private bool IsClientConnected()
        {
            //https://social.msdn.microsoft.com/Forums/en-US/c857cad5-2eb6-4b6c-b0b5-7f4ce320c5cd/c-how-to-determine-if-a-tcpclient-has-been-disconnected?forum=netfxnetcom
            if (IsSocketConnected(_socket)) // TODO: just IsSocketConnected
            {
                if ((_socket.Poll(0, SelectMode.SelectWrite)) && (!_socket.Poll(0, SelectMode.SelectError)))
                {
                    byte[] buffer = new byte[1];
                    //int timeout = _socket.ReceiveTimeout;
                    //_socket.ReceiveTimeout = 500;
                    if (_socket.Receive(buffer, SocketFlags.Peek) == 0)
                    {
                        return false;
                    }
                    else
                    {
                        //_socket.ReceiveTimeout = timeout;
                        return true;
                    }


                }
                else
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }

        private void ConnectCallback(IAsyncResult ar)
        {
            try
            {
                _socket.EndConnect(ar);
                OnLogWrite?.Invoke(0, string.Format("Connected to {0}", _socket.RemoteEndPoint.ToString()));
                OnConnected?.Invoke();
                IsListening = true;
                _isActive = true;
                _listenerThread = new Thread(ListenerLoop);
                _listenerThread.IsBackground = true;
                _listenerThread.Start();
            }
            catch
            {

            }
            finally
            {
                _timeoutObject.Set();
            }
        }

        private void WaitForRegister(int timeout)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            TimeSpan timeSpan = new TimeSpan(0, 0, timeout);
            while (State == ClientState.Pending && sw.Elapsed < timeSpan)
            {
                System.Threading.Thread.Sleep(10);
            }
        }

        private void SendCallback(object sender, SocketAsyncEventArgs e)
        {
            if (e.SocketError == SocketError.Success)
            {
                // TODO: ?
            }
            else
            {
                OnLogWrite?.Invoke(2, string.Format("Socket Error: {0}", e.SocketError));
            }
        }
        private bool IsSocketConnected(Socket s)
        {
            // https://stackoverflow.com/questions/2661764/how-to-check-if-a-socket-is-connected-disconnected-in-c
            bool part1 = s.Poll(1000, SelectMode.SelectRead);
            bool part2 = (s.Available == 0);
            if ((part1 && part2) || !s.Connected)
                return false;
            else
                return true;
        }
        private void ListenerLoop(object state)
        {
            while (IsListening)
            {
                try
                {
                    Process();
                }
                catch
                {
                }
                Thread.Sleep(10);
            }
            OnLogWrite?.Invoke(0, "Disconnected.");
            OnDisconnected?.Invoke();
            if (_socket != null)
            {
                _socket.Close();
                _socket = null;
            }
            _isActive = false;
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _listenerThread = null;
        }

        private void Process()
        {
            if (_socket == null)
                return;
            if (_socket.Connected == false)
            {
                IsListening = false;
                return;
            }


            if (!IsConnected)
            {
                IsListening = false;
                return;
            }

            int bytesAvailable = _socket.Available;
            if (bytesAvailable == 0)
            {
                Thread.Sleep(10);
                return;
            }

            List<byte> buffer = new List<byte>();
            while (_socket.Available > 0 && _socket.Connected)
            {
                byte[] newByte = new byte[1];
                _socket.Receive(newByte, 0, 1, SocketFlags.None);
                buffer.AddRange(newByte);
                if (newByte[0] == Message.MessageDelimiter)
                {
                    byte[] msg = _message.ToArray();
                    _message.Clear();
                    Message message = new Message(msg);
                    if (buffer.Count > 0)
                    {
                        Command cmd = Command.Unknown;
                        StatusCode statusCode = StatusCode.Undefined;
                        string comment = "";
                        if (Message.ParseCommand(message, out cmd, out comment))
                        {
                            switch (cmd)
                            {
                                case Command.Unknown:
                                    break;
                                default:
                                    break;
                            }
                            return;
                        }
                        else if (Message.ParseStatusCode(message, out statusCode, out comment))
                        {
                            switch (statusCode)
                            {
                                case StatusCode.Undefined:
                                    break;
                                case StatusCode.ReadyForNewUser:
                                    SendMessage(new Message(String.Format("{0} {1}", Command.USR, Identifier)));
                                    break;
                                case StatusCode.AccessGranted:
                                    State = ClientState.Registered;
                                    OnRegistered?.Invoke();
                                    break;
                                case StatusCode.NeedPassword:
                                    SendMessage(new Message(String.Format("{0} {1}", Command.PW, Password)));
                                    break;
                                case StatusCode.IncorrectIdentifier:
                                    State = ClientState.Declined;
                                    OnLogWrite?.Invoke(0, "Register failed: Incorrect identifier.");
                                    OnRegisterFailed?.Invoke("Incorrect identifier");
                                    break;
                                case StatusCode.IncorrectPassword:
                                    State = ClientState.Declined;
                                    OnLogWrite?.Invoke(0, "Register failed: Incorrect password.");
                                    OnRegisterFailed?.Invoke("Incorrect password");
                                    break;
                                case StatusCode.ServerIsFull:
                                    State = ClientState.Declined;
                                    OnLogWrite?.Invoke(0, "Register failed: The server is full.");
                                    OnRegisterFailed?.Invoke("The server is full");
                                    break;
                                default:
                                    break;
                            }
                            return;
                        }
                        OnMessageReceived?.Invoke(message);
                        if(message.GetResponse)
                        {
                            SendMessage(new Message(new byte[] { 0x07 }));
                        }
                    }
                }
                else
                {
                    _message.AddRange(newByte);
                }
            }
            if (buffer.Count > 0)
            {
                OnTransmissionEnd?.Invoke(buffer.ToArray());
            }
        }

        #endregion
        public static NetworkInterface Adapter { get; set; } = null;
        private static readonly object locker = new object();
        /// <summary>
        /// Search for server on the local network.
        /// </summary>
        /// <param name="multicastIp"></param>
        /// <param name="port"></param>
        /// <param name="timeout"></param>
        public static void FindServers(string multicastIp = "237.1.3.4", int port = 7995, int timeout = 3)
        {
            lock (locker)
            {
                List<ServerDetails> serverDetailList = new List<ServerDetails>();
                IPAddress ipa = IPAddress.Any;
                if (Adapter == null)
                {
                    foreach (var ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                    {
                        Console.WriteLine(ip);
                        if (ip.AddressFamily == AddressFamily.InterNetwork)
                        {
                            ipa = ip;
                            break;
                        }
                    }
                }
                else
                {
                    foreach (UnicastIPAddressInformation ip in Adapter.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        {
                            ipa = ip.Address;
                            break;
                        }
                    }
                }
                MulticastHelper.Receiver multicastHelper = new MulticastHelper.Receiver(ipa, multicastIp, port);
                multicastHelper.OnMessageReceived += (data) =>
                {
                    string str = Message.MessageEncoding.GetString(data, 0, data.Length-1).Trim();
                    if (str.StartsWith("201"))
                    {
                        str = str.Remove(0, 4);
                        string[] s = str.Split('\0');
                        string[] ip = s[0].Split(':');
                        ServerDetails serverDetails = new ServerDetails();

                        serverDetails.EndPoint = new IPEndPoint(IPAddress.Parse(ip[0]), int.Parse(ip[1]));
                        serverDetails.WelcomeMessage = s[1];
                        serverDetails.Clients = int.Parse(s[2]);
                        serverDetails.MaxClients = int.Parse(s[3]);
                        serverDetails.IsPasswordProtected = bool.Parse(s[4]);
                        if(!serverDetailList.Any(x => x.EndPoint.Equals(serverDetails.EndPoint)))
                        {
                            serverDetailList.Add(serverDetails);
                            OnServerFound?.Invoke(serverDetailList.Last());
                        }
                            
                    }
                };
                Stopwatch sw = new Stopwatch();
                sw.Start();
                TimeSpan timeSpan = new TimeSpan(0, 0, timeout);
                while (sw.Elapsed < timeSpan)
                {
                    
                    Thread.Sleep(1000);
                }
                multicastHelper.Close();
            }
        }

        
    }
}
