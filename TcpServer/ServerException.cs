using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace TcpConnector
{
    [Serializable]
    public class ServerException : Exception
    {
        public ServerException(string message) : base(message) { }

        // Ensure Exception is Serializable
        protected ServerException(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt) { }
    }
}
