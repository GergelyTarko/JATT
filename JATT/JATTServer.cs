using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

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
        public class ServerClient
        {
            /// <summary>
            /// Gets the identifier.
            /// </summary>
            public string Identifier { get; internal set; }
            /// <summary>
            /// Gets the underlying <see cref="TcpClient"/>.
            /// </summary>
            public TcpClient Client { get; internal set; }
            /// <summary>
            /// Gets the state of the client.
            /// </summary>
            public ClientState State { get; internal set; }

            /// <summary>
            /// Sends the specified <see cref="Packet"/> to the client.
            /// </summary>
            /// <param name="packet">Any type of <see cref="Packet"/></param>
            public void SendMessage(Packet packet)
            {
                Client.GetStream().Write(packet, 0, packet.Size);
            }

            /// <summary>
            /// Sends a text message to the client using a <see cref="TextPacket"/>.
            /// </summary>
            /// <param name="message">The message to be sent.</param>
            public void SendMessage(string message)
            {
                SendMessage(new TextPacket(message));
            }
        }

        #region Delegates
        public delegate void ClientEventHandler(ServerClient client);
        public delegate void PacketReceivedHandler(ServerClient client, Packet packet);
        public delegate void MessageReceivedHandler(ServerClient client, string message);
        public delegate void UserPacketReceivedHandler(ServerClient client, UserPacket packet);
        #endregion

        #region Properties
        /// <summary>
        /// Gets a value indicating whether the server is running.
        /// </summary>
        public bool IsListening { get; private set; } = false;
        /// <summary>
        /// Gets the list of the connected clients.
        /// </summary>
        public List<ServerClient> Clients { get; internal set; } = new List<ServerClient>();
        /// <summary>
        /// Gets or sets the maximum number of clients accepted by the server.
        /// </summary>
        public int MaxClients { get; set; } = 32;
        /// <summary>
        /// Gets or sets the password.
        /// </summary>
        public string Password { get; set; } = "";
        /// <summary>
        /// Gets or sets the welcome message.
        /// </summary>
        public string WelcomeMessage { get; set; } = "Minerva Server";
        /// <summary>
        /// Gets or sets the value indicating whether the server uses authentication.
        /// </summary>
        public bool UseAuth { get; set; } = true;

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
        /// Occurs when the server recives any kind of <see cref="Packet"/>.
        /// </summary>
        public event PacketReceivedHandler OnPacketReceived;
        /// <summary>
        /// Occurs when the server recives a text message. (<see cref="TextPacket"/>)
        /// </summary>
        public event MessageReceivedHandler OnMessageReceived;
        /// <summary>
        /// Occurs when the server receives a <see cref="UserPacket"/>.
        /// </summary>
        public event UserPacketReceivedHandler OnUserPacketReceived;

        #endregion

        #region Private variables
        private TcpListenerEx _listener;
        private List<ServerClient> _disconnectedClients = new List<ServerClient>();
        private List<byte> _message = new List<byte>();
        private Dictionary<byte, Action<ServerClient, UserPacket>> _userDefinedEvents = new Dictionary<byte, Action<ServerClient, UserPacket>>();
        #endregion

        #region Public Methods
        /// <summary>
        /// Starts the server on the specified port.
        /// </summary>
        /// <param name="port">The port to listen on.</param>
        /// <exception cref="SocketException"></exception>
        public void Start(int port)
        {
            _listener = new TcpListenerEx(new IPEndPoint(IPAddress.Any, port));
            _listener.Start();
            IsListening = true;
            ThreadPool.QueueUserWorkItem(ListenerLoop);
        }

        /// <summary>
        /// Sends the specified <see cref="Packet"/> to each connected client.
        /// </summary>
        /// <param name="packet">Any type of <see cref="Packet"/>.</param>
        public void BroadcastMessage(Packet packet)
        {
            if (!_listener.Active || packet == null)
                return;
            foreach (var client in Clients)
            {
                if (client.State != ClientState.Pending)
                    client.Client.GetStream().Write(packet, 0, packet.Size);
            }
        }

        /// <summary>
        /// Sends a text message to each connected client using a <see cref="TextPacket"/>.
        /// </summary>
        /// <param name="message">The message to be sent.</param>
        public void BroadcastMessage(string message)
        {
            if (!_listener.Active || message == null || message == "")
                return;
            BroadcastMessage(new TextPacket(message));
        }

        /// <summary>
        /// Sends a <see cref="Packet"/> to the specified <see cref="ServerClient"/>.
        /// </summary>
        /// <param name="client">The receiver of the packet.</param>
        /// <param name="packet">Any type of <see cref="Packet"/>.</param>
        public void SendMessage(ServerClient client, Packet packet)
        {
            if (!_listener.Active || packet == null)
                return;
            if (client.State != ClientState.Pending)
                client.Client.GetStream().Write(packet, 0, packet.Size);
        }

        /// <summary>
        /// Sends a text message to the specified <see cref="ServerClient"/>.
        /// </summary>
        /// <param name="client">The receiver of the message.</param>
        /// <param name="message">The message to be sent.</param>
        public void SendMessage(ServerClient client, string message)
        {
            if (message == null || message == "")
                return;
            SendMessage(client, new TextPacket(message));
        }

        /// <summary>
        /// Registers an <see cref="Action"/> to be invoked when the server receives the specified type of <see cref="UserPacket"/>.
        /// </summary>
        /// <param name="type">The type of the packet.</param>
        /// <param name="method">The action to be invoked.</param>
        public void RegisterUserPacket(byte type, Action<ServerClient, UserPacket> method)
        {
            var result = _userDefinedEvents.Where(x => x.Key == type).FirstOrDefault();
            _userDefinedEvents[type] = new Action<ServerClient, UserPacket>(method);
        }

        /// <summary>
        /// Stops the server.
        /// </summary>
        public void Stop()
        {
            IsListening = false;
            while (_listener.Active)
                Thread.Sleep(100);
            Clients.Clear();
        }

        /// <summary>
        /// Returns the received bytes. (Receiving a packet clears the buffer)
        /// </summary>
        /// <returns>Buffer.</returns>
        public byte[] Buffer()
        {
            return _message.ToArray();
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

                System.Threading.Thread.Sleep(10);
            }
            _listener.Stop();
        }

        bool IsSocketConnected(Socket s)
        {
            // https://stackoverflow.com/questions/2661764/how-to-check-if-a-socket-is-connected-disconnected-in-c
            bool part1 = s.Poll(1000, SelectMode.SelectRead);
            bool part2 = (s.Available == 0);
            if ((part1 && part2) || !s.Connected)
                return false;
            else
                return true;
        }

        private void Process()
        {
            if (_disconnectedClients.Count > 0)
            {
                var disconnectedClients = _disconnectedClients.ToArray();
                _disconnectedClients.Clear();

                foreach (var c in disconnectedClients)
                {
                    Clients.Remove(c);
                    OnClientDisconnected?.Invoke(c);
                }
            }
            if (_listener.Pending())
            {
                ServerClient newClient = new ServerClient
                {
                    Client = _listener.AcceptTcpClient(),
                    State = UseAuth ? ClientState.Pending : ClientState.Registered
                };
                newClient.SendMessage(new WelcomePacket(WelcomeMessage, Clients.Count, 32, Password != ""));
                if (Clients.Count >= MaxClients)
                {
                    newClient.State = ClientState.Declined;
                    newClient.SendMessage(new ResponsePacket((int)ResponseCode.ServerIsFull));
                }
                else
                {
                    Clients.Add(newClient);
                    OnClientConnected?.Invoke(newClient);
                }

            }



            foreach (var c in Clients)
            {

                if (IsSocketConnected(c.Client.Client) == false)
                {
                    _disconnectedClients.Add(c);
                }

                if (c.Client.Available == 0)
                {
                    continue;
                }

                List<byte> buffer = new List<byte>();

                while (c.Client.Available > 0 && c.Client.Connected)
                {
                    byte[] newByte = new byte[1];
                    c.Client.Client.Receive(newByte, 0, 1, SocketFlags.None);
                    buffer.AddRange(newByte);

                    if (newByte[0] == Packet.PacketEnding)
                    {
                        byte[] msg = _message.ToArray();
                        _message.Clear();
                        Packet packet = new Packet(msg);
                        OnPacketReceived?.Invoke(c, packet);
                        switch (packet.Type)
                        {
                            case PacketType.Register:
                                {
                                    if (c.State == ClientState.Pending)
                                    {
                                        string identifier = "";
                                        string password = "";
                                        RegisterPacket.Parse(packet, out identifier, out password);
                                        c.Identifier = identifier;
                                        if (UseAuth && Password != "")
                                        {
                                            if (Password != password)
                                            {
                                                c.SendMessage(new ResponsePacket((int)ResponseCode.PasswordError));
                                                return;
                                            }
                                        }
                                        c.State = ClientState.Registered;
                                        c.SendMessage(new ResponsePacket((int)ResponseCode.Ok));
                                        OnClientRegistered?.Invoke(c);
                                    }
                                }
                                break;

                            case PacketType.Text:
                                string message;
                                TextPacket.Parse(packet, out message);
                                OnMessageReceived?.Invoke(c, message);
                                break;

                            case PacketType.UserDefined:
                                byte type;
                                byte[] data;
                                UserPacket.Parse(packet, out type, out data);
                                OnUserPacketReceived?.Invoke(c, new UserPacket(type, data));
                                _userDefinedEvents[type].DynamicInvoke(c, new UserPacket(type, data));
                                break;

                            case PacketType.BroadcastMessage:
                                Packet forwardingPacket;
                                BroadcastPacket.Parse(packet, out forwardingPacket);
                                foreach (var client in Clients)
                                {
                                    if (client.State != ClientState.Pending && client != c)
                                        client.Client.GetStream().Write(forwardingPacket, 0, forwardingPacket.Size);
                                }
                                break;

                            default:
                                Console.WriteLine(Packet.MessageEncoding.GetString(msg));
                                break;
                        }
                    }
                    else
                    {
                        _message.AddRange(newByte);
                    }
                }
            }
        }

        #endregion
    }
}
