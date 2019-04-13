using System;
using System.Collections.Generic;
using System.Text;

namespace JATT
{
    public enum PacketType : byte
    {
        Undefined = 0x00,           //0
        Register = 0x01,            //1
        Response = 0x02,            //2
        WelcomeMessage = 0x07,      //7
        BroadcastMessage = 0x06,    //6
        Text = 0x03,                //3
        UserDefined = 0x05          //5
    }

    public enum ResponseCode
    {
        Undefined = 0,
        Ok = 200,
        PasswordError = 401,
        ServerIsFull = 500
    }

    public class Packet
    {
        public static Encoding MessageEncoding { get; set; } = Encoding.UTF8;
        public PacketType Type { get; private set; }
        public byte[] Data { get; internal set; }
        public static byte PacketEnding { get; private set; } = 0x04;
        public int Size
        {
            get
            {
                return Data.Length + 2;
            }
        }

        public Packet(PacketType type)
        {
            Type = type;
        }
        public Packet(byte[] stream)
        {
            try
            {
                Type = (PacketType)Enum.Parse(typeof(PacketType), stream[0].ToString());
            }
            catch
            {
                Type = PacketType.Undefined;
            }
            Data = new byte[stream.Length - 1];
            Buffer.BlockCopy(stream, 1, Data, 0, Data.Length);
        }

        public static implicit operator byte[] (Packet packet)
        {
            byte[] data = new byte[packet.Data.Length + 2];
            data[0] = (byte)packet.Type;
            Buffer.BlockCopy(packet.Data, 0, data, 1, packet.Data.Length);
            data[data.Length - 1] = PacketEnding;
            return data;
        }

        public override string ToString()
        {
            string data = "|  " + ((byte)Type) + " |";
            string dataString = MessageEncoding.GetString(Data, 0, Data.Length > 60 ? 60 : Data.Length);
            dataString = dataString.Replace(Environment.NewLine, "");
            if (dataString.Length < MessageEncoding.GetString(Data).Length)
                dataString += "...";
            data += (MessageEncoding.GetString(Data).Length < 7) ? new string(' ', 7) : dataString;
            data += "| " + PacketEnding + " |";
            string returner = "|";
            for (int i = 0; i < data.Length - 2; i++)
                returner += "-";
            returner += "|\n";
            int sc = Math.Max(dataString.Length - 3, 4);

            returner += "|Type|";
            returner += new string(' ', (sc - 1) / 2);
            returner += "Data";
            returner += new string(' ', sc / 2);
            returner += "|End|\n";
            returner += data + "\n|";
            for (int i = 0; i < data.Length - 2; i++)
                returner += "-";
            returner += "|";
            return returner;
        }
    }

    public sealed class RegisterPacket : Packet
    {
        public static void Parse(Packet packet, out string identifier, out string password)
        {
            List<byte> nb = new List<byte>();
            int i = 0;
            while (packet.Data[i] != 0x1c)
            {
                nb.Add(packet.Data[i]);
                i++;
            }
            i++;
            identifier = MessageEncoding.GetString(nb.ToArray(), 0, nb.Count);
            password = MessageEncoding.GetString(packet.Data, i, packet.Data.Length - nb.Count - 1);
        }

        public RegisterPacket(string identifier, string password) : base(PacketType.Register)
        {
            List<byte> data = new List<byte>();
            data.AddRange(MessageEncoding.GetBytes(identifier));
            data.Add(0x1c);
            data.AddRange(MessageEncoding.GetBytes(password));
            Data = data.ToArray();
        }
    }

    public sealed class ResponsePacket : Packet
    {

        public static void Parse(Packet packet, out int code)
        {
            code = BitConverter.ToInt32(packet.Data, 0);
        }

        public static void Parse(Packet packet, out ResponseCode code)
        {
            code = (ResponseCode)BitConverter.ToInt32(packet.Data, 0);
        }

        public ResponsePacket(int code) : base(PacketType.Response)
        {
            Data = BitConverter.GetBytes(code);
        }
    }

    public sealed class TextPacket : Packet
    {
        public static void Parse(Packet packet, out string message)
        {
            message = MessageEncoding.GetString(packet.Data);
        }

        public TextPacket(string message) : base(PacketType.Text)
        {
            Data = MessageEncoding.GetBytes(message);
        }

    }

    public sealed class WelcomePacket : Packet
    {
        public static void Parse(Packet packet, out string welcomeMessage, out int clients, out int maxClients, out bool passwordProtected)
        {
            clients = BitConverter.ToInt32(packet.Data, 0);
            maxClients = BitConverter.ToInt32(packet.Data, 4);
            passwordProtected = packet.Data[8] == 1;
            welcomeMessage = MessageEncoding.GetString(packet.Data, 9, packet.Data.Length - 9);
        }

        public WelcomePacket(string welcomeMessage, int clients, int maxClients, bool passwordProtected) : base(PacketType.WelcomeMessage)
        {
            List<byte> data = new List<byte>();
            data.AddRange(BitConverter.GetBytes(clients));
            data.AddRange(BitConverter.GetBytes(maxClients));
            data.Add(passwordProtected ? (byte)1 : (byte)0);
            data.AddRange(MessageEncoding.GetBytes(welcomeMessage));
            Data = data.ToArray();
        }
    }

    public class UserPacket : Packet
    {
        public byte[] UserData { get; protected set; } = null;
        public static void Parse(Packet packet, out byte type, out byte[] data)
        {
            type = packet.Data[0];
            data = new byte[packet.Data.Length - 1];
            Buffer.BlockCopy(packet.Data, 1, data, 0, data.Length);
        }

        public UserPacket(byte type, byte[] data) : base(PacketType.UserDefined)
        {
            if (data != null)
                Data = new byte[data.Length + 1];
            else
                Data = new byte[1];
            UserData = data;
            Data[0] = type;
            if(data != null)
                Buffer.BlockCopy(data, 0, Data, 1, data.Length);
        }

        protected UserPacket(byte type) : base(PacketType.UserDefined)
        {
            Data = new byte[1];
            Data[0] = type;
        }

        protected void BuildFromArray(byte[] data)
        {
            byte type = Data[0];
            Data = new byte[data.Length + 1];
            UserData = data;
            Data[0] = type;
            Buffer.BlockCopy(data, 0, Data, 1, data.Length);
        }
    }

    public class BroadcastPacket : Packet
    {
        public Packet Packet { get; } = null;

        public static void Parse(Packet packet, out Packet broadcastPacket)
        {
            broadcastPacket = new Packet(packet.Data);
        }

        public BroadcastPacket(Packet packtet) : base(PacketType.BroadcastMessage)
        {
            Data = packtet;
            Packet = Packet;
        }
    }
}
