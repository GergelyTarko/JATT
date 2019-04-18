using System;
using System.Threading.Tasks;
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
        public async Task UseAuth_test()
        {
            JATTServer server = new JATTServer();
            server.UseAuth = true;
            server.Password = "123";
            server.Start(7991);

            JATTClient client1 = new JATTClient();
            JATTClient client2 = new JATTClient();
            var result = client1.ConnectWithAuthAsync("127.0.0.1", 7991, "TestClient", "321");
            await result;
            Assert.IsFalse(result.Result, "Connect using wrong password");
            result = client1.ConnectWithAuthAsync("127.0.0.1", 7991, "TestClient", "123");
            Assert.IsTrue(result.Result, "Connect using the right password");
            client1.Disconnect();
            server.Stop();
        }
    }
}
