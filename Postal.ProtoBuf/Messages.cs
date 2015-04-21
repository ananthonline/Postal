using System;
using System.Collections.Generic;
using System.IO;

using ProtoBuf;

namespace Postal.ProtoBuf
{
    public abstract class Messages
    {
        public interface IRequest
        {
            int Tag { get; }
            void InvokeReceived();
        }

        public interface IResponse
        {
        }

        public delegate TResponse ProcessRequestDelegate<TRequest, TResponse>(TRequest request)
            where TRequest : IRequest
            where TResponse : IResponse;

        private readonly static Dictionary<int, Type> _messageTypes = new Dictionary<int, Type>();

        private static void Serialize<T>(Stream stream, T request) where T : IRequest
        {
            Serializer.SerializeWithLengthPrefix(stream, request, PrefixStyle.Base128, request.Tag);
            if (!_messageTypes.ContainsKey(request.Tag))
                _messageTypes.Add(request.Tag, typeof(T));
        }

        private static T Deserialize<T>(Stream stream) where T : IResponse
        {
            object value;
            Serializer.NonGeneric.TryDeserializeWithLengthPrefix(stream, PrefixStyle.Base128,
                tag =>
                {
                    Type type;
                    return _messageTypes.TryGetValue(tag, out type) ? type : null;
                }, out value);
            return (T)value;
        }

        public static void ProcessRequest(this Stream stream)
        {
            object value;
            Serializer.NonGeneric.TryDeserializeWithLengthPrefix(stream, PrefixStyle.Base128,
                tag =>
                {
                    Type type;
                    return _messageTypes.TryGetValue(tag, out type) ? type : null;
                }, out value);
            var request = (IRequest)value;
            if (request == null)
                return;
            request.InvokeReceived();
        }
    }
}
