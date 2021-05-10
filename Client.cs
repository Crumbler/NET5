using System;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace NET5
{
    public sealed class Client
    {
        Socket sock;
        NetworkStream networkStream;
        NetworkReader reader;
        StreamWriter writer;

        ~Client()
        {
            sock.Close();
        }

        public Client(Socket sock)
        {
            this.sock = sock;

            networkStream = new NetworkStream(this.sock);
            writer = new StreamWriter(networkStream, Encoding.ASCII);

            reader = new NetworkReader(networkStream);

            writer.AutoFlush = false;
            
            writer.NewLine = "\r\n";
        }

        private static string GetJSONResponse(string path)
        {
            static string GetObject(string path)
            {
                bool isDirectory = Directory.Exists(path);

                return "{\"path\":\"" +
                       Path.GetFileName(path) +
                       "\",\"type\":\"" +
                       (isDirectory ? "directory" : "file") +
                       "\"}";
            }

            string[] paths = Directory.GetFileSystemEntries(path);

            StringBuilder response = new();

            response.Append('[');

            for (int i = 0; i < paths.Length; ++i)
            {
                response.Append(GetObject(paths[i]));

                // If not last path
                if (i != paths.Length - 1)
                    response.Append(',');
            }

            response.Append(']');

            return response.ToString();
        }

        private void SendDirectoryInfo(string path)
        {
            lock (Program.consoleLock)
                Console.WriteLine($"GET directory {path} info");

            string jsonResponse = GetJSONResponse(path);

            int messageSize = Encoding.ASCII.GetByteCount(jsonResponse);

            writer.WriteLine("HTTP/1.1 200 OK");
            writer.WriteLine("Content-Type: application/json");
            writer.WriteLine($"Content-Length: {messageSize}");
            writer.WriteLine();

            writer.Write(jsonResponse);

            writer.Flush();
        }

        private void SendFile(string path)
        {
            lock (Program.consoleLock)
                Console.WriteLine($"GET file {path}");

            using FileStream stream = new(path, FileMode.Open, FileAccess.Read);

            DateTime mTime = File.GetLastWriteTime(path).ToUniversalTime();

            var mTimeString = mTime.ToString("r");

            writer.WriteLine("HTTP/1.1 200 OK");
            writer.WriteLine($"Last-Modified: {mTimeString}");
            writer.WriteLine("Content-Type: application/octet-stream");
            writer.WriteLine($"Content-Length: {stream.Length}");
            writer.WriteLine();

            writer.Flush();

            stream.CopyTo(networkStream);
        }

        private void SendResponse(HTTPResponse responseType)
        {
            writer.Write("HTTP/1.1 " + (int)responseType + ' ');

            switch(responseType)
            {
                case HTTPResponse.OK:
                    writer.WriteLine("OK");
                    break;

                case HTTPResponse.Created:
                    writer.WriteLine("Created");
                    break;

                case HTTPResponse.NotFound:
                    writer.WriteLine("Not Found");
                    break;

                case HTTPResponse.MethodNotAllowed:
                    writer.WriteLine("Method Not Allowed");
                    break;

                case HTTPResponse.NotAcceptable:
                    writer.WriteLine("Not Acceptable");
                    break;
            }

            writer.WriteLine("Content-Length: 0");
            writer.WriteLine();

            writer.Flush();
        }

        private void ProcessCopy(string path, string source)
        {
            path = path.Remove(0, 1);
            source = source.Remove(0, 1);
            
            bool resourceExisted = Directory.Exists(path) || File.Exists(path);

            if (Directory.Exists(path) || Directory.Exists(source))
            {
                lock (Program.consoleLock)
                    Console.WriteLine($"Either {path} or {source} is a directory");

                SendResponse(HTTPResponse.NotAcceptable);

                return;
            }

            if (!File.Exists(source))
            {
                lock (Program.consoleLock)
                    Console.WriteLine($"File {source} not found");

                SendResponse(HTTPResponse.NotFound);

                return;
            }

            lock (Program.consoleLock)
                Console.WriteLine($"Copying file {source} to {path}");

            File.Copy(source, path, true);

            SendResponse(resourceExisted ? HTTPResponse.OK : HTTPResponse.Created);
        }

        private void ProcessPut(string path, int contentLength)
        {
            path = path.Remove(0, 1);

            bool resourceExisted = Directory.Exists(path) || File.Exists(path);

            if (Directory.Exists(path))
            {
                lock (Program.consoleLock)
                    Console.WriteLine($"Cannot PUT {path}. It's a directory");

                SendResponse(HTTPResponse.NotAcceptable);

                return;
            }

            lock (Program.consoleLock)
                Console.WriteLine($"PUT file {path}");

            var buf = new byte[1024 * 4];

            int totalReceived = 0;

            using FileStream stream = new(path, FileMode.Create, FileAccess.Write);

            while(totalReceived < contentLength)
            {
                int receivedBytes = networkStream.Read(buf);

                if (receivedBytes == 0)
                    return;

                totalReceived += receivedBytes;

                stream.Write(buf[..receivedBytes]);
            }

            SendResponse(resourceExisted ? HTTPResponse.OK : HTTPResponse.Created);
        }

        private void ProcessDelete(string path)
        {
            path = path.Remove(0, 1);

            if (!Directory.Exists(path) && !File.Exists(path))
            {
                lock (Program.consoleLock)
                    Console.WriteLine($"File {path} not found");

                SendResponse(HTTPResponse.NotFound);

                return;
            }

            lock (Program.consoleLock)
                Console.WriteLine($"DELETE {path}");

            if (Directory.Exists(path))
                Directory.Delete(path, true);
            else
                File.Delete(path);

            SendResponse(HTTPResponse.OK);
        }

        private void ProcessGet(string path)
        {
            if (path == "/")
                path = ".";
            else
                path = path.Remove(0, 1);

            if (!Directory.Exists(path) && !File.Exists(path))
            {
                lock (Program.consoleLock)
                    Console.WriteLine($"File {path} not found");

                SendResponse(HTTPResponse.NotFound);

                return;
            }

            if (Directory.Exists(path))
                SendDirectoryInfo(path);
            else
                SendFile(path);
        }

        private void SendFileInfo(string path)
        {
            FileInfo fileInfo = new(path);

            DateTime mTime = File.GetLastWriteTime(path).ToUniversalTime();

            var mTimeString = mTime.ToString("r");

            writer.WriteLine("HTTP/1.1 200 OK");
            writer.WriteLine($"Last-Modified: {mTimeString}");
            writer.WriteLine("Content-Type: application/octet-stream");
            writer.WriteLine($"Content-Length: {fileInfo.Length}");
            writer.WriteLine();

            writer.Flush();
        }

        private void ProcessHead(string path)
        {
            path = path.Remove(0, 1);

            if (!File.Exists(path))
            {
                lock (Program.consoleLock)
                    Console.WriteLine($"File {path} not found");

                SendResponse(HTTPResponse.NotFound);

                return;
            }

            lock (Program.consoleLock)
                Console.WriteLine($"HEAD {path}");

            SendFileInfo(path);
        }

        public void Handle()
        {
            while (true)
            {
                int contentLength = 0;

                string copySource = null;

                string s = reader.ReadLine();

                string[] requestSplits = s.Split(' ');

                // Read until \r\n
                while(true)
                {
                    s = reader.ReadLine();

                    if (s.Length == 0 || s == string.Empty)
                        break;

                    string[] headerSplits = s.Split(": ");

                    string header = headerSplits[0];

                    switch(header)
                    {
                        case "Content-Length":
                            contentLength = int.Parse(headerSplits[1]);
                            break;

                        case "X-Source":
                            copySource = headerSplits[1];
                            break;
                    }
                }

                string verb = requestSplits[0],
                       path = requestSplits[1];

                switch(verb)
                {
                    case "GET":
                        ProcessGet(path);
                        break;

                    case "PUT":
                        if (copySource == null)
                            ProcessPut(path, contentLength);
                        else
                            ProcessCopy(path, copySource);
                        break;

                    case "HEAD":
                        ProcessHead(path);
                        break;

                    case "DELETE":
                        ProcessDelete(path);
                        break;

                    default:
                        lock (Program.consoleLock)
                            Console.WriteLine($"Method {verb} not allowed");

                        SendResponse(HTTPResponse.MethodNotAllowed);
                        break;
                }
            }
        }
    }
}
