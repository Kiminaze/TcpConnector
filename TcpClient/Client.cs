using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Remoting.Messaging;
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
    /// Provides Tcp connection functionalities for a client.
    /// </summary>
    public class Client
    {
        private enum PacketType : byte
        {
            ConnectionEnd,
            KeepAlive,
            Data
        }

        /// <summary>
        /// The IPAddress of the Client.
        /// </summary>
        public IPAddress Address { get; private set; }

        /// <summary>
        /// The Port of the Client.
        /// </summary>
        public int Port { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the client is connected.
        /// </summary>
        public bool Connected {
            get {
                return client.Connected;
            }
        }

        /// <summary>
        /// Indicates if the client is currently trying to connect.
        /// </summary>
        public bool IsConnecting { get; private set; }

        private TcpClient client;
        private NetworkStream stream;

        private Thread listenThread;
        private Thread keepAliveThread;

        /// <summary>
        /// 
        /// </summary>
        public delegate void ConnectionEstablished();
        /// <summary>
        /// 
        /// </summary>
        public delegate void ConnectionFailed();
        /// <summary>
        /// 
        /// </summary>
        public delegate void ConnectionTerminated();
        /// <summary>
        /// 
        /// </summary>
        /// <param name="data">The data received.</param>
        public delegate void DataReceived(byte[] data);

        /// <summary>
        /// Gets invoked when the initial connection is successful.
        /// </summary>
        public event ConnectionEstablished OnConnectionEstablished;
        /// <summary>
        /// Gets invoked when the initial connection failed.
        /// </summary>
        public event ConnectionFailed OnConnectionFailed;
        /// <summary>
        /// Gets invoked when the connection gets interrupted.
        /// </summary>
        public event ConnectionTerminated OnConnectionTerminated;
        /// <summary>
        /// Gets invoked when the client receives any data.
        /// </summary>
        public event DataReceived OnDataReceived;

        /// <summary>
        /// Initializes a new instance of the Client class using an ip address and port.
        /// </summary>
        /// <param name="ipAddress">The ip address of the Client.</param>
        /// <param name="port">The port of the Client.</param>
        public Client(IPAddress ipAddress, int port) {
            // set address and port
            Address = ipAddress;
            Port = port;

            // get a new TcpClient
            client = new TcpClient();
        }

        /// <summary>
        /// Connects the client to its ip address and port.
        /// </summary>
        /// <returns>Wether the connection was successful or not.</returns>
        public void Connect() {
            // if client is currently connecting, return
            if (IsConnecting || client.Connected)
                return;

            // connect the client
            IsConnecting = true;
            client.BeginConnect(Address, Port, new AsyncCallback(ConnectCallback), null);
        }

        private void ConnectCallback(IAsyncResult ar) {
            try {
                client.EndConnect(ar);
                if (client.Connected) {
                    // start a thread to listen for any data
                    listenThread = new Thread(ListenForServer);
                    listenThread.Start();

                    // start a thread to keep the connection alive
                    keepAliveThread = new Thread(KeepAlive);
                    keepAliveThread.Start();

                    this.OnConnectionEstablished?.Invoke();
                } else {
                    // connection not possible
                    this.OnConnectionFailed?.Invoke();
                }
            } catch (SocketException) {
                // connection not possible
                this.OnConnectionFailed?.Invoke();
            } finally {
                IsConnecting = false;
            }
        }

        /// <summary>
        /// Disconnect the client from its current connection.
        /// </summary>
        public void Disconnect() {
            // if client is already disconnected, return
            if (!client.Connected)
                return;

            // send a disconnect signal
            stream = client.GetStream();
            byte[] outStream = { (byte)PacketType.ConnectionEnd };
            stream.Write(outStream, 0, outStream.Length);
            stream.Flush();

            // stop the listen thread
            if (listenThread != null)
                listenThread.Interrupt();

            // close the client
            client.Close();

            // close the keep alive thread
            if (keepAliveThread != null)
                keepAliveThread.Interrupt();
        }

        /// <summary>
        /// Sends data over the connection.
        /// </summary>
        /// <param name="data">The data to be send.</param>
        public void SendData(byte[] data) {
            // if data is null, throw exception
            if (data == null)
                throw new ArgumentNullException("data");

            try {
                // get stream from the connection
                stream = client.GetStream();

                byte[] packetType = { (byte)PacketType.Data };

                // pack all the data into a byte array
                byte[] outStream = new byte[1 + 4 + data.Length];
                Buffer.BlockCopy(packetType, 0, outStream, 0, packetType.Length);
                Buffer.BlockCopy(BitConverter.GetBytes(data.Length), 0, outStream, 1, 4);
                Buffer.BlockCopy(data, 0, outStream, 5, data.Length);

                // write data to the stream
                stream.Write(outStream, 0, outStream.Length);
                stream.Flush();
            } catch (IOException) {
                // failure while writing to stream -> do nothing
            } catch (Exception) {
                // any other error -> do nothing
            }
        }

        private void ListenForServer() {
            // get stream from client
            stream = client.GetStream();

            int nextDataLength = 0;
            byte[] lastPacket = new byte[0];

            // while server does not terminate the connection, read from stream
            bool terminateConnection = false;
            while (!terminateConnection) {
                try {
                    // read data from stream in 1kb chunks
                    // and write it into a list
                    byte[] readBuffer = new byte[1024];
                    int bytesRead = 0;
                    List<byte> byteStreamList = new List<byte>();
                    do {
                        bytesRead = stream.Read(readBuffer, 0, readBuffer.Length);
                        byteStreamList.AddRange(readBuffer.SubArray(0, bytesRead));
                    } while (stream.DataAvailable);
                    stream.Flush();
                    byte[] byteStream = byteStreamList.ToArray();

                    // if the length of the stream is 0, server closed connection
                    if (byteStream.Length <= 0)
                        break;

                    // check for multiple packets in one package
                    while (byteStream.Length > 0 && !terminateConnection) {
                        if (nextDataLength == 0) {
                            PacketType type = (PacketType)byteStream[0];

                            switch (type) {
                                case PacketType.ConnectionEnd:
                                    // on manual server shutdown
                                    terminateConnection = true;
                                    break;

                                case PacketType.KeepAlive:
                                    // do nothing on keep alive, server is still reachable
                                    break;

                                case PacketType.Data:
                                    // data packet received

                                    // get data packet length
                                    int length = BitConverter.ToInt32(byteStream.SubArray(1, 4), 0);
                                    if (5 + length <= byteStream.Length) {
                                        // single part data packet received
                                        byte[] data = byteStream.SubArray(5, length);
                                        lastPacket = data;

                                        // invoke data received event
                                        this.OnDataReceived?.Invoke(data);

                                        // cut byteStream by already used data
                                        byteStream = byteStream.SubArray(5 + length, byteStream.Length - (5 + length));
                                    } else {
                                        // multiple part data packet received
                                        int partLength = byteStream.Length - 5;
                                        byte[] data = byteStream.SubArray(5, partLength);
                                        lastPacket = data;

                                        byteStream = new byte[0];

                                        nextDataLength = length - partLength;
                                    }
                                    break;

                                default:
                                    break;
                            }
                        } else {
                            if (byteStream.Length >= nextDataLength) {
                                // all parts assembled into one packet
                                lastPacket = lastPacket.Concat(byteStream.SubArray(0, nextDataLength)).ToArray();

                                // invoke data received event
                                this.OnDataReceived?.Invoke(lastPacket);

                                byteStream = byteStream.SubArray(0, nextDataLength);

                                nextDataLength = 0;
                            } else {
                                // still missing a part
                                lastPacket = lastPacket.Concat(byteStream).ToArray();

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

            // close client
            client.Close();

            // stop keepAliveThread
            if (keepAliveThread != null)
                keepAliveThread.Interrupt();

            // invoke connection terminated event
            this.OnConnectionTerminated?.Invoke();
        }

        private void KeepAlive() {
            while (true) {
                try {
                    // wait 10s
                    Thread.Sleep(10000);

                    // get stream from and to server
                    stream = client.GetStream();

                    // send a keepAlive to the server
                    byte[] outStream = { (byte)PacketType.KeepAlive };
                    stream.Write(outStream, 0, outStream.Length);
                    stream.Flush();
                } catch (IOException) {
                    // failure while writing to stream -> do nothing
                } catch (ThreadInterruptedException) {
                    // thread interrupted -> break out of loop
                    break;
                }
            }
        }
    }
}
