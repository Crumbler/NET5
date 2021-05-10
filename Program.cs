using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace NET5
{
    public static class Program
    {
        public static object consoleLock = new object();

        public static void Main()
        {
            Directory.SetCurrentDirectory("files");

            TcpListener listener = new(IPAddress.Any, 80);

            listener.Start(5);

            while (true)
            {
                Socket clientSock = listener.AcceptSocket();

                Client client = new(clientSock);

                Task.Run(client.Handle);
            }
        }
    }
}
