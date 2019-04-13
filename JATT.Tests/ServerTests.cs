using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace JATT.Tests
{
    [TestClass]
    public class ServerTests
    {
        [TestMethod]
        public void Start_test()
        {
            JATTServer server = new JATTServer();
            server.Start(7991);
            Assert.IsTrue(server.IsListening, "Server has started test");
            server.Stop();
        }

        [TestMethod]
        public void Custom_packet_type_test()
        {
            JATTServer server = new JATTServer();
            server.RegisterUserPacket(0x17, SendMessageToClient);
            server.UseAuth = false;
            server.Start(7991);

            JATTClient client = new JATTClient();
            client.Connect("127.0.0.1", 7991);
            client.SendMessageToServer(new UserPacket(0x17, null));
            client.Disconnect();
            server.Stop();
        }

        private void SendMessageToClient(JATTServer.ServerClient client, UserPacket packet)
        {
            Assert.IsNotNull(packet, "Userpacket is not null");
        }

        [TestMethod]
        public void UseAuth_test()
        {
            JATTServer server = new JATTServer();
            server.RegisterUserPacket(0x17, SendMessageToClient);
            server.UseAuth = true;
            server.Password = "123";
            server.Start(7991);

            JATTClient client1 = new JATTClient();
            JATTClient client2 = new JATTClient();
            Assert.IsFalse(client1.ConnectWithAuth("127.0.0.1", 7991, "TestClient", "321"), "Connect using wrong password");
            Assert.IsTrue(client2.ConnectWithAuth("127.0.0.1", 7991, "TestClient", "123"), "Connect using the right password");
            client1.Disconnect();
            server.Stop();
        }
    }
}
