using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace NET5
{
    public sealed class NetworkReader
    {
        NetworkStream stream;
        StringBuilder builder;

        public NetworkReader(NetworkStream stream)
        {
            this.stream = stream;

            builder = new StringBuilder();
        }

        public string ReadLine()
        {
            builder = builder.Clear();

            bool lastR = false;

            char c;

            while(true)
            {
                c = (char)stream.ReadByte();

                builder.Append(c);

                if (lastR && c == '\n')
                    break;

                lastR = c == '\r';
            }

            builder.Remove(builder.Length - 2, 2);

            return builder.ToString();
        }
    }
}
