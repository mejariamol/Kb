using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace kb
{
    class StateObject
    {
        public Socket workSocket = null;
        public int BufferSize;
        public byte[] buffer;

        public int startIndex = 0;

        public StateObject (int bufferSize)
        {
            BufferSize = bufferSize;
            buffer = new byte[bufferSize];
        }
    }
    class SocketListener
    {
        // Thread signal.
        public static ManualResetEvent allDone = new ManualResetEvent(false);

        public static void StartListening ()
        {
            IPHostEntry ipHostInfo = Dns.GetHostEntry(Dns.GetHostName());
            IPAddress ipAddress = null;
            foreach (IPAddress ip in ipHostInfo.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    ipAddress = ip;
                    break;
                }
            }
            IPEndPoint localEndPoint = new IPEndPoint(ipAddress, 44444);
            Console.WriteLine("Listening at {0}", localEndPoint.ToString());

            Socket listener = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            try
            {
                listener.Bind(localEndPoint);
                listener.Listen(10);

                Console.WriteLine("Waiting for a connection...");

                Socket handler = listener.Accept();
                Console.WriteLine("Connected. Socket: {0}", handler.LocalEndPoint);

                while (true)
                {
                    // Set the event to nonsignaled state
                    allDone.Reset();
                    
                    StateObject state = new StateObject(4);
                    state.workSocket = handler;
                    handler.BeginReceive(state.buffer, 0, state.BufferSize, 0, new AsyncCallback(ReadBufferSizeCallback), state);

                    // Wait until one data is received.
                    allDone.WaitOne();
                    if (!SocketExtensions.IsConnected(handler))
                    {
                        break;
                    }
                }

                handler.Shutdown(SocketShutdown.Both);
                handler.Close();

            }
            catch(Exception e)
            {
                Console.WriteLine(e.ToString());
            }

            Console.WriteLine("\nPress ENTER to continue...");
            Console.Read();
        }

        public static void ReadBufferSizeCallback (IAsyncResult ar)
        {
            StateObject state = (StateObject) ar.AsyncState;
            Socket handler = state.workSocket;

            int bytesRead = handler.EndReceive(ar);
            if (bytesRead > 0)
            {
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(state.buffer);

                int bufferSize = BitConverter.ToInt32(state.buffer, 0);

                StateObject _state = new StateObject(bufferSize);
                _state.workSocket = handler;
                handler.BeginReceive(_state.buffer, 0, _state.BufferSize, 0, new AsyncCallback(ReadCallback), _state);
            }
            else
            {
                allDone.Set();
            }
        }

        public static void ReadCallback (IAsyncResult ar)
        {
            StateObject state = (StateObject) ar.AsyncState;
            Socket handler = state.workSocket;

            int bytesRead = handler.EndReceive(ar);

            if (bytesRead <= 0) {
                allDone.Set();
                return;
            }
            
            if (bytesRead < state.BufferSize)
            {
                state.BufferSize -= bytesRead;
                state.startIndex += bytesRead;
                handler.BeginReceive(state.buffer, state.startIndex, state.BufferSize, 0, new AsyncCallback(ReadCallback), state);
            }
            else
            {
                // Signal the main thread to continue.
                allDone.Set();

                VirtualInput vi = VirtualInput.Parser.ParseFrom(state.buffer);
                Console.WriteLine(vi.ToString());
            }
        }
    }

    static class SocketExtensions
    {
        public static bool IsConnected(this Socket socket)
        {
            try
            {
            return !(socket.Poll(1, SelectMode.SelectRead) && socket.Available == 0);
            }
            catch (SocketException) { return false; }
        }
    }
}
