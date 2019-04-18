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
        private UdpClient _udpclient;

        private int _port = 7995;
        IPAddress _multicastIPaddress;
        IPAddress _localIPaddress;
        IPEndPoint _localEndPoint;
        IPEndPoint _remoteEndPoint;

        public delegate void ReceiveHandler(byte[] data);
        public event ReceiveHandler OnMessageReceived;

        public MulticastHelper(string multicastIP, int multicastPort)
        {
            _multicastIPaddress = IPAddress.Parse(multicastIP);
            _localIPaddress = IPAddress.Any;
            _localEndPoint = new IPEndPoint(_localIPaddress, _port);
            _remoteEndPoint = new IPEndPoint(_multicastIPaddress, _port);
            _port = multicastPort;
            _udpclient = new UdpClient();
            _udpclient.ExclusiveAddressUse = false;
            _udpclient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
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
