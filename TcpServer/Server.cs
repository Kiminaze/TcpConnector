using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace TcpConnector
{
    /// <summary>
    /// Provides extension methods for byte arrays.
    /// </summary>
    public static class Extensions
    {
        /// <summary>
        /// Returns just specified part of a byte array.
        /// </summary>
        /// <param name="data">The original byte array.</param>
        /// <param name="index">The index to start at.</param>
        /// <param name="length">How many elements should be copied into the sub array.</param>
        /// <returns>The specified part of the array.</returns>
        public static byte[] SubArray(this byte[] data, int index, int length) {
            byte[] result = new byte[length];
            Array.Copy(data, index, result, 0, length);
            return result;
        }
    }

    /// <summary>
    /// Provides Tcp connection functionalities for a server.
    /// </summary>
    public class Server
    {
        private class ClientData
        {
            public uint ID { get; internal set; }

            public TcpClient Client { get; private set; }

            public Thread Thread { get; private set; }

            public byte[] LastPacket { get; set; }

            public ClientData(uint id, TcpClient client, Thread thread) {
                ID = id;
                Client = client;
                Thread = thread;
                LastPacket = new byte[0];
            }
        }

        private class ClientList
        {
            public List<ClientData> list = new List<ClientData>();

            public int Count {
                get {
                    return list.Count;
                }
            }

            public ClientData this[uint id] {
                get {
                    for (int i = 0; i < list.Count; i++) {
                        if (list[i].ID == id)
                            return list[i];
                    }

                    throw new ArgumentOutOfRangeException("id", "User ID does not exist!");
                }
            }

            public IEnumerator<ClientData> GetEnumerator() {
                foreach (ClientData data in list) {
                    yield return data;
                }
            }

            public void Add(uint id, TcpClient client, Thread thread) {
                ClientData clientData = new ClientData(id, client, thread);
                list.Add(clientData);
            }

            public void Remove(uint id) {
                for (int i = 0; i < list.Count; i++) {
                    if (list[i].ID == id) {
                        list.RemoveAt(i);
                        return;
                    }
                }

                throw new ArgumentOutOfRangeException("id", "User ID does not exist!");
            }

            public void Clear() {
                list.Clear();
            }
        }

        private enum PacketType : byte
        {
            ConnectionEnd,
            KeepAlive,
            Data
        }

        /// <summary>
        /// Returns, if the server is currently running.
        /// </summary>
        public bool IsRunning { get; private set; } = false;

        /// <summary>
        /// Returns the port of the server.
        /// </summary>
        public int Port { get; private set; }

        private TcpListener tcpListener;

        private Thread listenThread;

        private ClientList clients = new ClientList();
        private uint currentClientID;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="clientID">The id of the client that connected.</param>
        public delegate void ClientConnected(uint clientID);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="clientID">The id of the client that disconnected.</param>
        public delegate void ClientDisconnected(uint clientID);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="clientID">The id of the client that sent the data.</param>
        /// <param name="data">The data received.</param>
        public delegate void DataReceived(uint clientID, byte[] data);

        /// <summary>
        /// Gets invoked when a client connects.
        /// </summary>
        public event ClientConnected OnClientConnect;
        /// <summary>
        /// Gets invoked when a client disconnects.
        /// </summary>
        public event ClientDisconnected OnClientDisconnect;
        /// <summary>
        /// Gets invoked when the server receives any data.
        /// </summary>
        public event DataReceived OnDataReceived;

        /// <summary>
        /// Initializes a new instance of class Server using a port.
        /// </summary>
        /// <param name="port">The port of the server.</param>
        public Server(int port) {
            this.Port = port;
        }

        /// <summary>
        /// Starts the server.
        /// </summary>
        public void Start() {
            if (IsRunning) {
                throw new ServerException("Server is already running!");
            }

            IsRunning = true;

            clients = new ClientList();
            currentClientID = 0;

            tcpListener = new TcpListener(IPAddress.Any, Port);
            listenThread = new Thread(ListenForClientConnection);
            listenThread.Start();
        }

        /// <summary>
        /// Stops the server and all communication with all connected clients.
        /// </summary>
        public void Stop() {
            if (!IsRunning) {
                throw new ServerException("Server is already stopped!");
            }

            // end connection with clients
            byte[] connectionEndByte = { (byte)PacketType.ConnectionEnd };
            foreach (ClientData clientData in clients) {
                NetworkStream stream = clientData.Client.GetStream();
                stream.Write(connectionEndByte, 0, connectionEndByte.Length);
                stream.Flush();

                if (clientData.Thread != null)
                    clientData.Thread.Interrupt();
            }

            if (tcpListener != null)
                tcpListener.Stop();

            if (listenThread != null)
                listenThread.Interrupt();
        }

        /// <summary>
        /// Sends data to all connected clients.
        /// </summary>
        /// <param name="data">The data to be send.</param>
        public void SendData(byte[] data) {
            byte[] packetType = { (byte)PacketType.Data };
            byte[] dataLength = BitConverter.GetBytes(data.Length);

            byte[] outStream = new byte[packetType.Length + dataLength.Length + data.Length];
            Buffer.BlockCopy(packetType, 0, outStream, 0, packetType.Length);
            Buffer.BlockCopy(dataLength, 0, outStream, packetType.Length, dataLength.Length);
            Buffer.BlockCopy(data, 0, outStream, packetType.Length + dataLength.Length, data.Length);

            foreach (ClientData clientData in clients) {
                NetworkStream stream = clientData.Client.GetStream();
                stream.Write(outStream, 0, outStream.Length);
                stream.Flush();
            }
        }

        /// <summary>
        /// Sends data to a specified connected client.
        /// </summary>
        /// <param name="clientID">The id of the client.</param>
        /// <param name="data">The data to be send.</param>
        public void SendData(uint clientID, byte[] data) {
            byte[] packetType = { (byte)PacketType.Data };
            byte[] dataLength = BitConverter.GetBytes(data.Length);

            byte[] outStream = new byte[packetType.Length + dataLength.Length + data.Length];
            Buffer.BlockCopy(packetType, 0, outStream, 0, packetType.Length);
            Buffer.BlockCopy(dataLength, 0, outStream, packetType.Length, dataLength.Length);
            Buffer.BlockCopy(data, 0, outStream, packetType.Length + dataLength.Length, data.Length);

            NetworkStream stream = clients[clientID].Client.GetStream();
            stream.Write(outStream, 0, outStream.Length);
            stream.Flush();
        }

        private void ListenForClientConnection() {
            tcpListener.Start();

            TcpClient client = new TcpClient();

            while (true) {
                try {
                    client = tcpListener.AcceptTcpClient();

                    Thread clientThread = new Thread(new ParameterizedThreadStart(HandleClientConnection));
                    clientThread.Start(client);

                    clients.Add(currentClientID, client, clientThread);
                } catch (SocketException) {
                    // socket connection broken
                    break;
                } catch (Exception ex) {
                    // any other error
                    Console.WriteLine("[ERROR] " + ex.ToString());
                    break;
                }
            }

            tcpListener.Stop();
        }

        private void HandleClientConnection(Object cl) {
            TcpClient client = (TcpClient)cl;

            uint clientID = currentClientID;
            currentClientID++;

            this.OnClientConnect?.Invoke(clientID);

            NetworkStream stream = client.GetStream();

            int nextDataLength = 0;

            bool terminateConnection = false;
            while (!terminateConnection) {
                try {
                    // read data from stream in 1kb chunks
                    byte[] readBuffer = new byte[1024];
                    int bytesRead = 0;
                    List<byte> byteStreamList = new List<byte>();
                    do {
                        bytesRead = stream.Read(readBuffer, 0, readBuffer.Length);
                        byteStreamList.AddRange(readBuffer.SubArray(0, bytesRead));
                    } while (stream.DataAvailable);
                    stream.Flush();
                    byte[] byteStream = byteStreamList.ToArray();

                    if (byteStream.Length <= 0)
                        break;

                    while (byteStream.Length > 0 && !terminateConnection) {
                        if (nextDataLength == 0) {
                            PacketType type = (PacketType)byteStream[0];

                            switch (type) {
                                case PacketType.ConnectionEnd:
                                    // client wants to terminate the connection
                                    terminateConnection = true;
                                    break;

                                case PacketType.KeepAlive:
                                    // if received a keepAlive packet, send one back
                                    byte[] keepAliveByte = { (byte)PacketType.KeepAlive };
                                    stream.Write(keepAliveByte, 0, keepAliveByte.Length);
                                    stream.Flush();

                                    byteStream = byteStream.SubArray(1, byteStream.Length - 1);
                                    break;

                                case PacketType.Data:
                                    // data packet received
                                    int length = BitConverter.ToInt32(byteStream.SubArray(1, 4), 0);
                                    if (5 + length <= byteStream.Length) {
                                        byte[] data = byteStream.SubArray(5, length);
                                        clients[clientID].LastPacket = data;

                                        this.OnDataReceived?.Invoke(clientID, data);

                                        byteStream = byteStream.SubArray(5 + length, byteStream.Length - (5 + length));
                                    } else {
                                        int partLength = byteStream.Length - 5;
                                        byte[] data = byteStream.SubArray(5, partLength);
                                        clients[clientID].LastPacket = data;

                                        byteStream = new byte[0];

                                        nextDataLength = length - partLength;
                                    }
                                    break;

                                default:
                                    break;
                            }
                        } else {
                            if (byteStream.Length >= nextDataLength) {
                                clients[clientID].LastPacket = clients[clientID].LastPacket.Concat(byteStream.SubArray(0, nextDataLength)).ToArray();

                                this.OnDataReceived?.Invoke(clientID, clients[clientID].LastPacket);

                                byteStream = byteStream.SubArray(0, nextDataLength);

                                nextDataLength = 0;
                            } else {
                                clients[clientID].LastPacket = clients[clientID].LastPacket.Concat(byteStream).ToArray();

                                byteStream = new byte[0];

                                nextDataLength -= byteStream.Length;
                            }
                        }
                    }
                } catch (SocketException) {
                    // connection broken
                    break;
                } catch (IOException) {
                    // connection broken while reading data
                    break;
                }
            }

            clients.Remove(clientID);
            this.OnClientDisconnect?.Invoke(clientID);
        }
    }
}
