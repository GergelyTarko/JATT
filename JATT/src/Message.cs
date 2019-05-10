using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace JATT
{
    /// 2xx     Success
    /// 3xx     Request
    /// 4xx     Client
    /// 5xx     Server
    /// 
    ///
    /// x0x     Connection
    /// x1x     
    /// x2x
    /// x3x     Authentication






    public enum StatusCode : int
    {
       Undefined = 100,
       ReadyForNewUser = 201,
       AccessGranted = 231,
       NeedPassword = 331,
       IncorrectIdentifier = 531,
       IncorrectPassword = 532,
       ServerIsFull = 503
    }

    public enum MessageType : int
    {
        Unknown = 0,
        Status = 1,
        Command = 2
    }


    public enum Command : int
    {
        Unknown = 0,
        USR = 4,
        PW = 5
    }

    public class Message
    {
        public static byte MessageDelimiter { get; set; } = 0x01;// 0x0a;
        public static Encoding MessageEncoding { get; set; } = Encoding.UTF8;
        public MessageType Type { get; internal set; }
        public byte[] Data { get; internal set; }
        public bool GetResponse { get; internal set; } = false;
        public string TextMessage {
            get
            {
                return MessageEncoding.GetString(Data, _messageOffset, Data.Length - _messageOffset);
            }
        }

        public int Size
        {
            get
            {
                return Data.Length+1 + (GetResponse? 1 : 0);
            }
        }

        protected int _messageOffset = 0;   //TODO

        public Message(byte[] data)
        {
            if(data.Last() == 0x07 && data.Length > 1)
            {
                Data = new byte[data.Length - 1];
                Buffer.BlockCopy(data, 0, Data, 0, data.Length - 1);
                GetResponse = true;
            }
            else
            {
                Data = new byte[data.Length];
                Buffer.BlockCopy(data, 0, Data, 0, data.Length);
                GetResponse = false;
            }
                
        }

        public Message(string text)
        {
            Data = MessageEncoding.GetBytes(text);
        }

        public static implicit operator byte[] (Message message)
        {
            byte[] data = new byte[message.Size];
            Buffer.BlockCopy(message.Data, 0, data, 0, message.Data.Length);
            data[data.Length - 1] = MessageDelimiter;
            if (message.GetResponse)
                data[data.Length - 2] = 0x07;
            return data;
        }

        private static bool IsCommand(string str)
        {
            switch (str)
            {
                case "USR":
                case "PW":
                    return true;
            }
            return false;
        }

        internal static bool ParseCommand(Message message, out Command command, out string comment)
        {
            try
            {
                string text = message.TextMessage;
                string fWord = text.Split(' ')[0];
                
                if(IsCommand(fWord))
                {
                    if (Enum.TryParse(fWord, out command))
                    {
                        comment = text.Substring(fWord.Length+1, text.Length - fWord.Length-1);
                        return true;
                    }
                }
                command = Command.Unknown;
                comment = "";
                return false;
            }
            catch 
            {
                command = Command.Unknown;
                comment = null;
                return false;
            }
        }

        internal static bool ParseStatusCode(Message message, out StatusCode statusCode, out string comment)
        {
            string text = message.TextMessage;
            int code = 100;
            string fWord = text.Split(' ')[0];
            if (int.TryParse(fWord, out code))
            {
                statusCode = (StatusCode)code;
                if (text.Length > 3)
                    comment = text.Substring(fWord.Length + 1, text.Length - fWord.Length - 1);
                else
                    comment = "";
                return true;
            }
            statusCode = (StatusCode)code;
            comment = null;
            return false;
        }
    }
}
