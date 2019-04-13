using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;

namespace JATT
{

    public class ServerDetails
    {
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
        public delegate void PacketReceivedHandler(Packet packet);
        public delegate void MessageReceivedHandler(string message);
        public delegate void ConnectionHandler();
        public delegate void ErrorHandler(string error);
        public delegate void UserPacketReceivedHandler(UserPacket packet);
        #endregion

        #region Properties
        /// <summary>
        /// Gets the state of the server. Used only with <see cref="ConnectWithAuth"/>.
        /// </summary>
        public ClientState State { get; internal set; }
        /// <summary>
        /// Gets a <see cref="ServerDetails"/> value that holds the details of the server to which the client is connected.
        /// </summary>
        public ServerDetails ServerDetails { get; private set; }
        /// <summary>
        /// Occurs when the client receives any kind of <see cref="Packet"/>.
        /// </summary>
        public event PacketReceivedHandler OnPacketReceived;
        /// <summary>
        /// Occurs when the client receives a text message. (<see cref="TextPacket"/>)
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
        /// Occurs when the client receives a <see cref="UserPacket"/>.
        /// </summary>
        public event UserPacketReceivedHandler OnUserPacketReceived;
        /// <summary>
        /// Occurs when the client receives the <see cref="ServerDetails"/>
        /// </summary>
        public event WelcomeMessageReceivedHandler OnWelcomeMsgReceived;

        #endregion

        #region Private variables
        private bool Listen { get; set; } = false;
        private TcpClient _client;
        private Thread _listenerThread = null;
        private byte _delimiter = 0x04;
        private List<byte> _message = new List<byte>();
        private Dictionary<byte, Action<UserPacket>> _userDefinedEvents = new Dictionary<byte, Action<UserPacket>>();
        #endregion

        #region Public Methods
        /// <summary>
        /// Connects to the specified port on the specified host using authentication.
        /// </summary>
        /// <param name="ip">The host to which you intend to connect.</param>
        /// <param name="port"></param>
        /// <param name="identifier"></param>
        /// <param name="password"></param>
        /// <returns>True if authentication succeeded.</returns>
        public bool ConnectWithAuth(string ip, int port, string identifier, string password)
        {
            State = ClientState.Pending;
            _client = new TcpClient();
            _client.Connect(ip, port);
            OnConnected?.Invoke();
            if (_listenerThread != null) { return false; }
            _listenerThread = new Thread(ListenerLoop);
            _listenerThread.IsBackground = true;
            Listen = true;
            _listenerThread.Start();

            SendMessageToServer(new RegisterPacket(identifier, password));
            ResponseCode statusCode = ResponseCode.Undefined;
            OnPacketReceived += (packet) =>
            {
                if (packet.Type == PacketType.Response)
                {
                    ResponsePacket.Parse(packet, out statusCode);
                }

            };
            Stopwatch sw = new Stopwatch();
            sw.Start();
            TimeSpan timeSpan = new TimeSpan(0, 0, 3);
            while (statusCode == ResponseCode.Undefined && sw.Elapsed < timeSpan)
            {
                System.Threading.Thread.Sleep(10);
            }
            if (statusCode == ResponseCode.Ok)
            {
                State = ClientState.Registered;
                OnRegistered?.Invoke();
                return true;
            }
            else
            {
                State = ClientState.Declined;
                OnRegisterFailed?.Invoke(String.Format("Error: {0} ", statusCode));
                Disconnect();
                return false;
            }
        }

        /// <summary>
        /// Connects to the specified port on the specified host.
        /// </summary>
        /// <param name="ip">The host to which you intend to connect.</param>
        /// <param name="port">The port number of the host to which you intend to connect.</param>
        public void Connect(string ip, int port)
        {
            _client = new TcpClient();
            _client.Connect(ip, port);
            OnConnected?.Invoke();
            if (_listenerThread != null) { return; }
            _listenerThread = new Thread(ListenerLoop);
            _listenerThread.IsBackground = true;
            Listen = true;
            _listenerThread.Start();
        }


        /// <summary>
        /// Returns the received bytes. (Receiving a packet clears the buffer)
        /// </summary>
        /// <returns>Buffer.</returns>
        public byte[] Buffer()
        {
            return _message.ToArray();
        }

        /// <summary>
        /// Sends the specified <see cref="Packet"/> to the server.
        /// </summary>
        /// <param name="packet">Any type of <see cref="Packet"/></param>
        public void SendMessageToServer(Packet packet)
        {
            if (packet == null || !_client.Connected)
                return;
            _client.GetStream().Write(packet, 0, packet.Size);
        }

        /// <summary>
        /// Sends a text message to the server using a <see cref="TextPacket"/>.
        /// </summary>
        /// <param name="message">The message to be sent.</param>
        public void SendMessageToServer(string message)
        {
            if (!_client.Connected || message == null || message == "")
                return;
            SendMessageToServer(new TextPacket(message));
        }

        /// <summary>
        /// Sends the specified <see cref="Packet"/> to the clients on the server. (Excluding the server)
        /// Uses <see cref="BroadcastPacket"/>.
        /// </summary>
        /// <param name="packet">Any type of <see cref="Packet"/></param>
        public void BroadcastMessage(Packet packet)
        {
            if (packet == null || !_client.Connected)
                return;
            SendMessageToServer(new BroadcastPacket(packet));
        }

        /// <summary>
        /// Sends the specified <see cref="Packet"/> to the clients on the server. (Excluding the server)
        /// Uses <see cref="BroadcastPacket"/> and <see cref="TextPacket"/>.
        /// </summary>
        /// <param name="message">The message to be sent.</param>
        public void BroadcastMessage(string message)
        {
            if (!_client.Connected || message == null || message == "")
                return;
            SendMessageToServer(new BroadcastPacket(new TextPacket(message)));
        }

        /// <summary>
        /// Disconnects the client.
        /// </summary>
        public void Disconnect()
        {
            if (_client == null) return;
            _client.Close();
            _client = null;
        }

        #endregion

        #region Private Methods
        private void ListenerLoop(object obj)
        {
            while (Listen)
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

            _listenerThread = null;
        }

        bool IsConnected()
        {
            //https://social.msdn.microsoft.com/Forums/en-US/c857cad5-2eb6-4b6c-b0b5-7f4ce320c5cd/c-how-to-determine-if-a-tcpclient-has-been-disconnected?forum=netfxnetcom
            if (_client.Connected)
            {
                if ((_client.Client.Poll(0, SelectMode.SelectWrite)) && (!_client.Client.Poll(0, SelectMode.SelectError)))
                {
                    byte[] buffer = new byte[1];
                    if (_client.Client.Receive(buffer, SocketFlags.Peek) == 0)
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

        private void Process()
        {
            if (_client == null)
                return;
            if (_client.Connected == false)
            {
                Listen = false;
                OnDisconnected?.Invoke();
                return;
            }


            if (!IsConnected())
            {
                Listen = false;
                OnDisconnected?.Invoke();
                return;
            }

            int bytesAvailable = _client.Available;
            if (bytesAvailable == 0)
            {
                Thread.Sleep(10);
                return;
            }

            List<byte> buffer = new List<byte>();

            while (_client.Available > 0 && _client.Connected)
            {
                byte[] newByte = new byte[1];
                _client.Client.Receive(newByte, 0, 1, SocketFlags.None);
                buffer.AddRange(newByte);
                if (newByte[0] == _delimiter)
                {
                    byte[] msg = _message.ToArray();
                    _message.Clear();
                    Packet packet = new Packet(msg);
                    OnPacketReceived?.Invoke(packet);
                    switch (packet.Type)
                    {
                        case PacketType.WelcomeMessage:
                            int clients;
                            int maxClients;
                            bool passwordProtected;
                            string welcomeMessage;
                            WelcomePacket.Parse(packet, out welcomeMessage, out clients, out maxClients, out passwordProtected);
                            ServerDetails = new ServerDetails
                            {
                                WelcomeMessage = welcomeMessage,
                                Clients = clients,
                                MaxClients = maxClients,
                                IsPasswordProtected = passwordProtected
                            };
                            OnWelcomeMsgReceived?.Invoke(ServerDetails);
                            break;

                        case PacketType.Text:
                            string message;
                            TextPacket.Parse(packet, out message);
                            OnMessageReceived?.Invoke(message);
                            break;

                        case PacketType.Response:
                            // Responses are handled different way      
                            // TODO: Implement better response handling
                            break;

                        case PacketType.UserDefined:
                            byte type;
                            byte[] data;
                            UserPacket.Parse(packet, out type, out data);
                            OnUserPacketReceived?.Invoke(new UserPacket(type, data));
                            _userDefinedEvents[type].DynamicInvoke(new UserPacket(type, data));
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

        #endregion
    }
}
