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


            internal void Disconnect()
            {
                Socket.Shutdown(SocketShutdown.Both);
                Socket.Close();
                Socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            }

            public bool IsConnected
            {
                get { return IsSocketConnected(Socket); }
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

        }

        public delegate void ClientEventHandler(Connection client);
        public delegate void MessageReceivedHandler(Connection client, Message message);
        public delegate void TransmissionEndHandler(Connection client, byte[] data);

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

        
        public int MaxConnections { get; set; } = 32;
        public int Port { get; set; } = 7991;
        public int MulticastPort { get; set; } = 7995;
        public string MulticastIP { get; set; } = "237.1.3.4";
        public string WelcomeMessage { get; set; } = "Welcome to the JATTServer";
        public bool UseEcho { get; set; } = true;
        public bool UseAuth { get; set; } = true;
        public string Password { get; set; } = "";
        public bool IsListening { get; set; } = false;
        public bool EnableDiscovery { get; set; } = true;

        public bool EchoReceived { get { return _lastMessage == null; } }
        public List<Connection> Connections { get; private set; } = new List<Connection>();

        Socket _socket;
        Socket _multicastSocket;
        MulticastHelper _multicastHelper;
        bool _isActive = false;
        List<Connection> _disconnectionList = new List<Connection>();
        List<byte> _message = new List<byte>();
        private byte[] _lastMessage = null;

        public void Start()
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _socket.Bind(new IPEndPoint(IPAddress.Any, Port));
            _socket.Listen(MaxConnections);

            if(EnableDiscovery)
            {
                _multicastHelper = new MulticastHelper(MulticastIP, MulticastPort);
                _multicastHelper.OnMessageReceived += _multicastHelper_OnMessageReceived;
            }

            IsListening = true;
            _isActive = true;
            ThreadPool.QueueUserWorkItem(ListenerLoop);
        }

        private void _multicastHelper_OnMessageReceived(byte[] data)
        {
            string str = System.Text.Encoding.ASCII.GetString(data);
            Console.WriteLine(str);
            if (str == "ask")
            {
                Message msg = new Message(String.Format("{0} {1}\0{2}\0{3}\0{4}", (int)StatusCode.ReadyForNewUser, WelcomeMessage, Connections.Count, 32, Password != ""));
                _multicastHelper.SendMessage(msg);
            }
        }

        public void Stop()
        {
            IsListening = false;
            while (_isActive)
                Thread.Sleep(100);
            Connections.Clear();
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
                if (_lastMessage == null)
                {
                    _lastMessage = new byte[data.Length - 1];
                    Array.Copy(data, _lastMessage, data.Length - 1);
                }
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
                if (_lastMessage == null)
                {
                    _lastMessage = new byte[data.Length - 1];
                    Array.Copy(data, _lastMessage, data.Length - 1);
                }
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
            while (IsListening)
            {
                try
                {
                    Process();
                }
                catch
                {

                }

                System.Threading.Thread.Sleep(10);
            }
            if(_multicastSocket != null)
            {
                _multicastSocket.Close();
                _multicastSocket = null;
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
                bool skipTransEvent = false;
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
                        if (UseEcho)
                        {
                            if (_lastMessage == null)
                            {
                                _lastMessage = message;
                                SendMessage(c, message);
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
                        string comment = "";
                        if (Message.ParseCommand(message, out cmd, out comment))
                        {
                            switch (cmd)
                            {
                                case Command.Unknown:
                                    break;
                                case Command.USR:
                                    c.Identifier = comment;
                                    if (UseAuth && Password != "")
                                    {
                                        c.SendMessage(new Message(String.Format("{0}", (int)StatusCode.NeedPassword)));
                                    }
                                    break;
                                case Command.PW:
                                    if (UseAuth && c.Identifier == null || c.Identifier == "")
                                    {
                                        c.SendMessage(new Message(String.Format("{0}", (int)StatusCode.IncorrectIdentifier)));
                                        c.Disconnect();
                                        return;
                                    }
                                    if (Password != comment)
                                    {
                                        c.SendMessage(new Message(String.Format("{0}", (int)StatusCode.IncorrectPassword)));
                                        c.Disconnect();
                                        return;
                                    }
                                    c.SendMessage(new Message(String.Format("{0}", (int)StatusCode.AccessGranted)));
                                    c.State = ClientState.Registered;
                                    OnClientRegistered?.Invoke(c);
                                    break;
                                default:
                                    break;
                            }
                            return;
                        }
                        OnMessageReceived?.Invoke(c, message);
                    }
                    else
                    {
                        _message.AddRange(newByte);
                    }
                }
                if (buffer.Count > 0 && !skipTransEvent)
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
            if (WelcomeMessage != null && WelcomeMessage != "")
                client.SendMessage(new Message(String.Format("{0} {1}\0{2}\0{3}\0{4}", (int)StatusCode.ReadyForNewUser, WelcomeMessage, Connections.Count, 32, Password != "")));

            Connections.Add(client);
            OnClientConnected?.Invoke(client);

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
