using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace JATT
{
    internal class MulticastHelper
    {
        public class Sender
        {
            UdpClient _udpClient;
            IPAddress ip;
            IPEndPoint ipep;
            public Sender(IPAddress localIP, string multicastIP, int multicastPort)
            {
                _udpClient = new UdpClient(AddressFamily.InterNetwork);
                ip = IPAddress.Parse(multicastIP);
                _udpClient.JoinMulticastGroup(ip);
                //_socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(ip));
                //_socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 2);
                ipep = new IPEndPoint(ip, multicastPort);
                //_socket.Connect(ipep);
            }

            public void Send(byte[] data)
            {
                _udpClient.Send(data, data.Length, ipep);
            }

            public void Close()
            {
                _udpClient.Close();
            }
        }

        public class Receiver
        {
            UdpClient _udpClient;
            IPAddress ip;
            public event ReceiveHandler OnMessageReceived;

            private byte[] buffer = new byte[1024];
            public Receiver(IPAddress localIP, string multicastIP, int multicastPort)
            {
                try
                {
                    _udpClient = new UdpClient(7995);
                    ip = IPAddress.Parse(multicastIP);
                    _udpClient.JoinMulticastGroup(ip, localIP);
                    //IPEndPoint ipep = new IPEndPoint(localIP, multicastPort);
                    //_socket.Bind(ipep);
                    //IPAddress ip = IPAddress.Parse(multicastIP);
                    //_socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(ip, localIP));
                    _udpClient.BeginReceive(MulticastReceiveCallback, null);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Error in MulticastHelper: " + e.ToString());
                }
            }

            private void MulticastReceiveCallback(IAsyncResult ar)
            {
                try
                {
                    var ipEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    byte[] recBuf = _udpClient.EndReceive(ar, ref ipEndPoint);
                    //byte[] recBuf = new byte[received];
                    //Array.Copy(buffer, recBuf, received);
                    OnMessageReceived?.Invoke(recBuf);
                    _udpClient.BeginReceive(MulticastReceiveCallback, null);
                }
                catch { }
            }

            public void Close()
            {
                _udpClient.Close();
            }
        }

        private UdpClient _udpclient;

        private int _port = 7995;
        IPAddress _multicastIPaddress;
        IPAddress _localIPaddress;
        IPEndPoint _localEndPoint;
        IPEndPoint _remoteEndPoint;

        public delegate void ReceiveHandler(byte[] data);
        public event ReceiveHandler OnMessageReceived;

        public MulticastHelper(IPAddress localIP, string multicastIP, int multicastPort)
        {
            _multicastIPaddress = IPAddress.Parse(multicastIP);
            _localIPaddress = localIP;
            _localEndPoint = new IPEndPoint(_localIPaddress, _port);
            _remoteEndPoint = new IPEndPoint(_multicastIPaddress, _port);
            _port = multicastPort;
            _udpclient = new UdpClient();
            _udpclient.ExclusiveAddressUse = false;
            _udpclient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udpclient.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 2);
            _udpclient.ExclusiveAddressUse = false;
            _udpclient.Client.Bind(_localEndPoint);
            _udpclient.JoinMulticastGroup(_multicastIPaddress, _localIPaddress);
            _udpclient.BeginReceive(MulticastReceiveCallback, null);
        }

        private void MulticastReceiveCallback(IAsyncResult ar)
        {
            IPEndPoint sender = new IPEndPoint(0, 0);
            try
            {
                byte[] receivedBytes = _udpclient.EndReceive(ar, ref sender);
                OnMessageReceived?.Invoke(receivedBytes);
                _udpclient.BeginReceive(MulticastReceiveCallback, null);
            }
            catch { }
            
            
            

            
        }

        public void SendMessage(byte[] data)
        {
            _udpclient.Send(data, data.Length, _remoteEndPoint);
        }

        public void Close()
        {
            _udpclient.Client.Shutdown(SocketShutdown.Both);
            _udpclient.Close();
        }
    }
}
