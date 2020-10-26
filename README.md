--------------------------------------------------
---- TcpConnector ----
--------------------------------------------------

These two libraries can be used to setup a server-client system using a Tcp connection. The TcpConnector manages the connection without the need to interfere to keep it running.
An Example can be found here: [ChatSystem](https://github.com/Kiminaze/ChatSystem)

**Server Features:**
- Constructor can be used to create one or more instances with different ports
- 4 functions:
  - Start()
  - Stop()
  - SendData(byte[] data) (to single client)
  - SendData(uint clientID, byte[] data) (to all clients)
- 3 events:
  - OnClientConnect(uint clientID)
  - OnClientDisconnect(uint clientID)
  - OnDataReceived(uint clientID, byte[] data)

**Client Features:**
- Constructor can be used create one or more instances with different ip addresses and ports
- 3 functions:
  - Connect()
  - Disconnect()
  - SendData(byte[] data)
- 4 events:
  - ConnectionEstablished()
  - ConnectionFailed()
  - ConnectionTerminated()
  - DataReceived(byte[] data)
