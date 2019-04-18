using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace JATT
{

    public class ServerDetails
    {
        public System.Net.EndPoint EndPoint { get; internal set; }
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
        #endregion

        #region Properties

        public string Identifier { get; set; } = "Client1";
        public string Password { get; set; } = "";

        /// <summary>
        /// Gets the state of the server. Used only with <see cref="ConnectWithAuth"/>.
        /// </summary>
        public ClientState State { get; internal set; }
        /// <summary>
        /// Gets a <see cref="ServerDetails"/> value that holds the details of the server to which the client is connected.
        /// </summary>
        public ServerDetails ServerDetails { get; private set; }

        public bool UseEcho { get; set; } = true;
        public bool IsListening { get; set; } = false;

        public bool EchoReceived { get { return _lastMessage == null; } }

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
        /// <summary>
        /// Occurs when the client receives the <see cref="ServerDetails"/>
        /// </summary>
        public event WelcomeMessageReceivedHandler OnWelcomeMsgReceived;
        /// <summary>
        /// TODO
        /// </summary>
        public event TransmissionEndHandler OnTransmissionEnd;

        #endregion

        #region Private variables
        private Socket _socket;
        bool _isActive = false;
        private List<byte> _message = new List<byte>();
        private byte[] _lastMessage = null;
        private static ManualResetEvent _timeoutObject = new ManualResetEvent(false);
        #endregion

        #region Public Methods


        public bool ConnectAndAuth(string ip, int port, int timeoutMS = 5000)
        {
            _timeoutObject.Reset();
            IPEndPoint endPoint = new IPEndPoint(IPAddress.Parse(ip), port);
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            var result = _socket.BeginConnect(endPoint, ConnectCallback, null);

            if(_timeoutObject.WaitOne(timeoutMS, false))
            {
                if(_isActive)
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
            //await Task.Factory.FromAsync(result, (r) => _socket.EndConnect(r));
            
            //OnConnected?.Invoke();
            ////if (_listenerThread != null) { return true; }
            ////_listenerThread = new Thread(ListenerLoop);
            ////_listenerThread.IsBackground = true;
           
            //
        }

        private void ConnectCallback(IAsyncResult ar)
        {
            try
            {
                _socket.EndConnect(ar);
                OnConnected?.Invoke();
                IsListening = true;
                _isActive = true;
                ThreadPool.QueueUserWorkItem(ListenerLoop);
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

        public void SendMessage(string message)
        {
            SendMessage(new Message(message));
        }

        public void SendMessage(Message message)
        {
            SendMessage((byte[])message);
        }

        public void SendMessage(byte[] data)
        {
            lock (_socket)
            {
                if (_lastMessage == null)
                {
                    _lastMessage = new byte[data.Length-1];
                    Array.Copy(data, _lastMessage, data.Length-1);
                }
                _socket.Send(data, 0, data.Length, SocketFlags.None);
            }
        }
        public void SendMessageAsync(string message)
        {
            SendMessageAsync(new Message(message));
        }

        public void SendMessageAsync(Message message)
        {
            SendMessageAsync(message);
        }

        public void SendMessageAsync(byte[] data)
        {
            lock (_socket)
            {
                if (_lastMessage == null)
                {
                    _lastMessage = new byte[data.Length-1];
                    Array.Copy(data, _lastMessage, data.Length - 1);
                }
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
                        Console.WriteLine("Socket Exception: " + se.ErrorCode + " Message: " + se.Message);
                    }

                    if (!completedAsync)
                    {
                        SendCallback(this, completeArgs);
                    }
                }
            }
        }


        private static void SendCallback(object sender, SocketAsyncEventArgs e)
        {
            if (e.SocketError == SocketError.Success)
            {

            }
            else
            {
                Console.WriteLine("Socket error: {0}", e.SocketError);
            }
        }

        ///// <summary>
        ///// Sends the specified <see cref="Packet"/> to the clients on the server. (Excluding the server)
        ///// Uses <see cref="BroadcastPacket"/>.
        ///// </summary>
        ///// <param name="packet">Any type of <see cref="Packet"/></param>
        //public void BroadcastMessage(Packet packet)
        //{
        //    if (packet == null || !_client.Connected)
        //        return;
        //    SendMessageToServer(new BroadcastPacket(packet));
        //}

        /// <summary>
        /// Sends the specified <see cref="Packet"/> to the clients on the server. (Excluding the server)
        /// Uses <see cref="BroadcastPacket"/> and <see cref="TextPacket"/>.
        /// </summary>
        /// <param name="message">The message to be sent.</param>
        //public void BroadcastMessage(string message)
        //{
        //    if (!_client.Connected || message == null || message == "")
        //        return;
        //    SendMessageToServer(new BroadcastPacket(new TextPacket(message)));
        //}

        /// <summary>
        /// TODO
        /// </summary>
        /// <returns></returns>
        public bool IsConnected()
        {
            //https://social.msdn.microsoft.com/Forums/en-US/c857cad5-2eb6-4b6c-b0b5-7f4ce320c5cd/c-how-to-determine-if-a-tcpclient-has-been-disconnected?forum=netfxnetcom
            if (IsSocketConnected(_socket))
            {
                if ((_socket.Poll(0, SelectMode.SelectWrite)) && (!_socket.Poll(0, SelectMode.SelectError)))
                {
                    byte[] buffer = new byte[1];
                    if (_socket.Receive(buffer, SocketFlags.Peek) == 0)
                    {
                        return false;
                    }
                    else
                    {
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

        /// <summary>
        /// Disconnects the client.
        /// </summary>
        public void Disconnect()
        {
            IsListening = false;
            while (_isActive)
                Thread.Sleep(100);
        }

        #endregion

        #region Private Methods
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
            OnDisconnected?.Invoke();
            if (_socket != null)
            {
                _socket.Close();
                _socket = null;
            }
            _isActive = false;
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
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


            if (!IsConnected())
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
            bool skipTransEvent = false;
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
                    if (UseEcho)
                    {
                        if(_lastMessage == null)
                        {
                            _lastMessage = message;
                            SendMessage(message);
                            _lastMessage = null;
                        }
                        else if (_lastMessage != null && Enumerable.SequenceEqual(msg, _lastMessage))
                        {
                            _lastMessage = null;
                            skipTransEvent = true;
                            continue;
                        }
                            
                    }
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
                                OnRegistered?.Invoke();
                                State = ClientState.Registered;
                                break;
                            case StatusCode.NeedPassword:
                                SendMessage(new Message(String.Format("{0} {1}", Command.PW, Password)));
                                break;
                            case StatusCode.IncorrectIdentifier:
                                OnRegisterFailed?.Invoke("Incorrect identifier");
                                State = ClientState.Declined;
                                break;
                            case StatusCode.IncorrectPassword:
                                OnRegisterFailed?.Invoke("Incorrect password");
                                State = ClientState.Declined;
                                break;
                            case StatusCode.ServerIsFull:
                                //  TODO:
                                State = ClientState.Declined;
                                break;
                            default:
                                break;
                        }
                        return;
                    }
                    OnMessageReceived?.Invoke(message);
                }
                else
                {
                    _message.AddRange(newByte);
                }
            }
            if (buffer.Count > 0 && !skipTransEvent)
            {
                OnTransmissionEnd?.Invoke(buffer.ToArray());
            }
        }

        private static readonly object locker = new object();
        public static IEnumerable<ServerDetails> FindServers(string multicastIp = "237.1.3.4", int port = 7995, int timeout = 5)
        {
            lock (locker)
            {
                List<ServerDetails> serverDetailList = new List<ServerDetails>();
                MulticastHelper multicastHelper = new MulticastHelper(multicastIp, port);
                multicastHelper.OnMessageReceived += (data) =>
                {
                    string str = Message.MessageEncoding.GetString(data).Trim();
                    if (str.StartsWith("201"))
                    {
                        string[] s = str.Split('\0');
                        ServerDetails serverDetails = new ServerDetails()
                        {
                            WelcomeMessage = s[0],
                            Clients = int.Parse(s[1]),
                            MaxClients = int.Parse(s[2]),
                            IsPasswordProtected = bool.Parse(s[3])
                        };
                        serverDetailList.Add(serverDetails);
                    }
                };
                multicastHelper.SendMessage(new byte[3] { 0x61, 0x73, 0x6b });
                Stopwatch sw = new Stopwatch();
                sw.Start();
                TimeSpan timeSpan = new TimeSpan(0, 0, timeout);
                while (sw.Elapsed < timeSpan)
                {
                    while(serverDetailList.Count > 0)
                    {
                        yield return serverDetailList.Last();
                        serverDetailList.RemoveAt(serverDetailList.Count - 1);
                    }
                   
                }
                multicastHelper.Close();
                //return serverDetailList;
            }
        }

        #endregion
    }
}
