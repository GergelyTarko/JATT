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
    public enum ClientState
    {
        Pending,
        Registered,
        Declined
    }

    public class JATTServer
    {
        public class Connection
        {
            public string Identifier { get; internal set; }
            public ClientState State { get; internal set; }
            public Socket Socket { get; internal set; }

            internal JATTServer _server = null;
            //internal byte[] _lastMessage = null;

            public Connection()
            {

            }

            public void SendMessage(string message)
            {
                _server.SendMessage(this, message);
            }
            public void SendMessage(byte[] data)
            {
                _server.SendMessage(this, data);
            }
            public void SendMessageAsync(string message)
            {
                _server.SendMessageAsync(this, message);
            }
            public void SendMessageAsync(Message message)
            {
                _server.SendMessageAsync(this, message);
            }

            /// <summary>
            /// Gets a value indicating whether the server received the echo of last sent data.
            /// </summary>
            //public bool EchoReceived { get { return _lastMessage == null; } }

            public void Disconnect()
            {
                Socket.Shutdown(SocketShutdown.Both);
                Socket.Close();
                Socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            }

            public bool IsConnected
            {
                get { return IsSocketConnected(Socket); }
            }

        }

        #region Delegates

        public delegate void ClientEventHandler(Connection client);
        public delegate void MessageReceivedHandler(Connection client, Message message);
        public delegate void TransmissionEndHandler(Connection client, byte[] data);
        public delegate void LogWriteHandler(int level, string str);

        #endregion

        #region Properties
        /// <summary>
        /// Occurs when a client connects.
        /// </summary>
        public event ClientEventHandler OnClientConnected;
        /// <summary>
        /// Occurs when a client gets registered.
        /// </summary>
        public event ClientEventHandler OnClientRegistered;
        /// <summary>
        /// Occurs when a client disconnects from the server
        /// </summary>
        public event ClientEventHandler OnClientDisconnected;
        /// <summary>
        /// Occurs when the server recives a <see cref="Message"/>.
        /// </summary>
        public event MessageReceivedHandler OnMessageReceived;
        /// <summary>
        /// TODO
        /// </summary>
        public event TransmissionEndHandler OnTransmissionEnd;
        /// <summary>
        /// TODO
        /// </summary>
        public event LogWriteHandler OnLogWrite;
        /// <summary>
        /// Gets or sets the maximum number of clients.
        /// </summary>
        public int MaxConnections { get; set; } = 32;
        /// <summary>
        /// Gets or sets the port you intend to host on.
        /// </summary>
        public int Port { get; set; } = 7991;
        public int MulticastPort { get; set; } = 7995;
        public string MulticastIP { get; set; } = "237.1.3.4";
        /// <summary>
        /// Gets or sets the value used as welcome message.
        /// </summary>
        public string WelcomeMessage
        {
            get { return _welcomeMessage; }
            set
            {
                if (string.IsNullOrEmpty(value))
                    _welcomeMessage = "null";
                else
                    _welcomeMessage = value;
            }
        }
        [Obsolete]
        /// <summary>
        /// Gets or sets the value indicating whether the server uses echo.
        /// </summary>
        public bool UseEcho { get; set; } = false;
        /// <summary>
        /// Gets or sets the value indicating whether the server uses authentication.
        /// </summary>
        public bool UseAuth { get; set; } = true;
        /// <summary>
        /// Gets or sets the password of the server.
        /// </summary>
        public string Password { get; set; } = "";
        /// <summary>
        /// Gets or sets the value indicating whether the server should listen for incoming data.
        /// </summary>
        public bool IsListening { get; set; } = false;
        /// <summary>
        /// Gets or sets the value indicating whether the server should be discoverable by the clients.
        /// </summary>
        public bool EnableDiscovery { get; set; } = true;
        /// <summary>
        /// Gets the list of the connected clients.
        /// </summary>
        public List<Connection> Connections { get; private set; } = new List<Connection>();

        #endregion

        #region Private variables
        private string _welcomeMessage = "Welcome to the JATTServer";
        IPAddress _ip;
        Socket _socket;
        MulticastHelper.Sender _multicastHelper;
        [Obsolete]
        MulticastHelper _mcHelper;
        bool _isActive = false;
        List<Connection> _disconnectionList = new List<Connection>();
        List<byte> _message = new List<byte>();
        bool _isWaitingForResponse = false;
        

        #endregion

        public void Start()
        {
            foreach (var ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    _ip =  ip;
                    break;
                }
            }
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _socket.Bind(new IPEndPoint(_ip, Port));
            _socket.Listen(MaxConnections);
            OnLogWrite?.Invoke(0, string.Format("Hosting on {0}", _ip.ToString()));


            _multicastHelper = new MulticastHelper.Sender(_ip, MulticastIP, MulticastPort);

            IsListening = true;
            _isActive = true;
            ThreadPool.QueueUserWorkItem(ListenerLoop);
        }

        public void Stop()
        {
            OnLogWrite?.Invoke(0, "Closing server.");
            IsListening = false;
            while (_isActive)
                Thread.Sleep(100);
            Connections.Clear();
            OnLogWrite?.Invoke(0, "The server has been closed.");
        }

        public void SMAndWaitForResponse(Connection client, string message, TimeSpan timeout)
        {
            Message msg = new Message(message);
            msg.GetResponse = true;
            SendMessage(client, msg);
            _isWaitingForResponse = true;
            Stopwatch sw = new Stopwatch();
            sw.Start();

            while (_isWaitingForResponse && sw.Elapsed < timeout)
            {
                System.Threading.Thread.Sleep(10);
            }
        }

        public void SendMessage(Connection client, string message)
        {
            SendMessage(client, new Message(message));
        }

        public void SendMessage(Connection client, Message message)
        {
            SendMessage(client, (byte[])message);
        }

        public void SendMessage(Connection client, byte[] data)
        {
            if (client == null || !client.IsConnected || data == null)
                return;
            lock (client.Socket)
            {
                //if (client._lastMessage == null)
                //{
                //    client._lastMessage = new byte[data.Length - 1];
                //    Array.Copy(data, client._lastMessage, data.Length - 1);
                //}
                //else
                //{
                //    client._lastMessage = null;
                //}
                client.Socket.Send(data, 0, data.Length, SocketFlags.None);
            }
        }

        public void SendMessageAsync(Connection client, string message)
        {
            SendMessageAsync(client, new Message(message));
        }

        public void SendMessageAsync(Connection client, Message message)
        {
            SendMessageAsync(client, message);
        }

        public void SendMessageAsync(Connection client, byte[] data)
        {
            if (client == null || !client.IsConnected || data == null)
                return;
            lock (client.Socket)
            {
                //if (client._lastMessage == null)
                //{
                //    client._lastMessage = new byte[data.Length - 1];
                //    Array.Copy(data, client._lastMessage, data.Length - 1);
                //}
                bool completedAsync = false;
                using (SocketAsyncEventArgs completeArgs = new SocketAsyncEventArgs())
                {
                    completeArgs.UserToken = client.Socket;
                    completeArgs.SetBuffer(data, 0, data.Length);
                    completeArgs.Completed += SendCallback;
                    completeArgs.RemoteEndPoint = client.Socket.RemoteEndPoint;
                    try
                    {
                        completedAsync = client.Socket.SendAsync(completeArgs);
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

        //public bool WaitForEcho(int timeout)
        //{
        //    Stopwatch sw = new Stopwatch();
        //    sw.Start();
        //    TimeSpan timeSpan = new TimeSpan(0, 0, timeout);
        //    while (_lastMessage != null && sw.Elapsed < timeSpan)
        //    {
        //        System.Threading.Thread.Sleep(10);
        //    }
        //    return _lastMessage == null;
        //}

        private void ListenerLoop(object state)
        {
            Stopwatch stopwatch = new Stopwatch();
            TimeSpan timeSpan = new TimeSpan(0, 0, 1);
            stopwatch.Start();
            while (IsListening)
            {
                try
                {
                    Process();
                }
                catch
                {

                }
                if(EnableDiscovery)
                {
                    if (stopwatch.Elapsed > timeSpan)
                    {
                        stopwatch.Restart();
                        Message multicastMsg = new Message(String.Format("{0} {1}:{2}\0{3}\0{4}\0{5}\0{6}", (int)StatusCode.ReadyForNewUser, _ip.ToString(), Port, WelcomeMessage, Connections.Count, 32, Password != ""));
                        _multicastHelper.Send(multicastMsg);
                    }
                }
                

                System.Threading.Thread.Sleep(10);
            }
            foreach (var item in Connections)
            {
                item.Disconnect();
            }
            if(_multicastHelper != null)
            {
                _multicastHelper.Close();
                _multicastHelper = null;
            }
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
            if (_disconnectionList.Count > 0)
            {
                var disconnectedClients = _disconnectionList.ToArray();
                _disconnectionList.Clear();

                foreach (var c in disconnectedClients)
                {
                    Connections.Remove(c);
                    OnLogWrite?.Invoke(0, "A client has disconnected.");
                    OnClientDisconnected?.Invoke(c);
                }
            }

            if (_socket.Poll(0, SelectMode.SelectRead))
            {
                _socket.BeginAccept(AcceptClientCallback, null);
            }

            

            foreach (var c in Connections)
            {

                if (!c.IsConnected)
                {
                    _disconnectionList.Add(c);
                }

                if (c.Socket.Available == 0)
                {
                    continue;
                }
                
                

                List<byte> buffer = new List<byte>();
                while (c.Socket.Available > 0 && c.Socket.Connected)
                {
                    byte[] newByte = new byte[1];
                    c.Socket.Receive(newByte, 0, 1, SocketFlags.None);
                    buffer.AddRange(newByte);
                    if (newByte[0] == Message.MessageDelimiter)
                    {
                        byte[] msg = _message.ToArray();
                        _message.Clear();
                        Message message = new Message(msg);
                        //if (UseEcho)
                        //{
                        //    if (c._lastMessage == null)
                        //    {
                        //        c._lastMessage = message;
                        //        SendMessage(c, message);
                        //        //c._lastMessage = null;
                        //    }
                        //    else if (c._lastMessage != null && Enumerable.SequenceEqual(msg, c._lastMessage))
                        //    {
                        //        Console.WriteLine("Echo received: " + Message.MessageEncoding.GetString(c._lastMessage));
                        //        c._lastMessage = null;
                        //        buffer.Clear();
                        //        continue;
                        //    }
                        //    else
                        //    {
                        //        Console.WriteLine("------------------------ERROR START-----------------------------------");
                        //        Console.WriteLine("false Echo received: " + Message.MessageEncoding.GetString(msg));
                        //        Console.WriteLine("instead of  " + Message.MessageEncoding.GetString(c._lastMessage));
                        //        Console.WriteLine("------------------------ERROR END-----------------------------------");
                        //    }

                        //}
                        if(message.Data[0] == 0x07)
                        {
                            _isWaitingForResponse = false;
                            continue;
                        }
                        if (buffer.Count > 0)
                        {
                            Command cmd = Command.Unknown;
                            string comment = "";
                            if (Message.ParseCommand(message, out cmd, out comment))
                            {
                                switch (cmd)
                                {
                                    case Command.Unknown:
                                        break;
                                    case Command.USR:

                                        if (comment == "")
                                        {
                                            c.SendMessage(new Message(String.Format("{0}", (int)StatusCode.IncorrectIdentifier)));
                                            c.Disconnect();
                                            continue;//return
                                        }
                                        c.Identifier = comment;
                                        if (UseAuth)
                                        {
                                            if (!string.IsNullOrEmpty(Password))
                                            {
                                                c.SendMessage(new Message(String.Format("{0}", (int)StatusCode.NeedPassword)));
                                            }
                                            else
                                            {
                                                c.SendMessage(new Message(String.Format("{0}", (int)StatusCode.AccessGranted)));
                                                OnLogWrite?.Invoke(0, string.Format("{0} has registered.", c.Identifier));
                                                OnClientRegistered?.Invoke(c);
                                            }
                                        }
                                        else
                                        {
                                            c.SendMessage(new Message(String.Format("{0}", (int)StatusCode.AccessGranted)));
                                        }
                                        break;
                                    case Command.PW:
                                        if (UseAuth && string.IsNullOrEmpty(c.Identifier))
                                        {
                                            c.SendMessage(new Message(String.Format("{0}", (int)StatusCode.IncorrectIdentifier)));
                                            c.Disconnect();
                                            continue;//return
                                        }
                                        if (Password != comment)
                                        {
                                            c.SendMessage(new Message(String.Format("{0}", (int)StatusCode.IncorrectPassword)));
                                            c.Disconnect();
                                            continue;//return
                                        }
                                        c.SendMessage(new Message(String.Format("{0}", (int)StatusCode.AccessGranted)));
                                        c.State = ClientState.Registered;
                                        OnLogWrite?.Invoke(0, string.Format("{0} has registered.", c.Identifier));
                                        OnClientRegistered?.Invoke(c);
                                        break;
                                    default:
                                        break;
                                }
                                //continue; //return
                            }
                            else
                            {
                                OnMessageReceived?.Invoke(c, message);
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
                    OnTransmissionEnd?.Invoke(c, buffer.ToArray());
                }
            }
        }

        private void AcceptClientCallback(IAsyncResult ar)
        {
            Socket newSocket = _socket.EndAccept(ar);
            Connection client = new Connection()
            {
                _server = this,
                Socket = newSocket
            };
            OnLogWrite?.Invoke(0, "A client has connected.");
            Connections.Add(client);
            OnClientConnected?.Invoke(client);
            if (Connections.Count-1 < MaxConnections)
            {
                client.SendMessage(new Message(String.Format("{0} {1}\0{2}\0{3}\0{4}", (int)StatusCode.ReadyForNewUser, WelcomeMessage, Connections.Count, 32, !string.IsNullOrEmpty(Password))));               
            }
            else
            {
                client.SendMessage(new Message(String.Format("{0} {1}\0{2}\0{3}\0{4}", (int)StatusCode.ServerIsFull, WelcomeMessage, Connections.Count, 32, !string.IsNullOrEmpty(Password))));
                client.Disconnect();
            }
        }

        private static bool IsSocketConnected(Socket s)
        {
            // https://stackoverflow.com/questions/2661764/how-to-check-if-a-socket-is-connected-disconnected-in-c
            bool part1 = s.Poll(1000, SelectMode.SelectRead);
            bool part2 = (s.Available == 0);
            if ((part1 && part2) || !s.Connected)
                return false;
            else
                return true;
        }

    }
}
