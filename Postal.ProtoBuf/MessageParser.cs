using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using Microsoft.CSharp;

using Sprache;

namespace Postal.ProtoBuf
{
    public static class MessageParser
    {
        public class Message
        {
            public string Name;
            public Request Request;
            public Response Response;
        }

        public class Field
        {
            public string Type;
            public string Name;
            public bool Mandatory;
        }

        public class Request
        {
            public IEnumerable<Field> Fields;
        }

        public class Response
        {
            public IEnumerable<Field> Fields;
        }

        public static readonly Parser<string> _identifierParser = Parse.Regex(@"\w\w+").Text().Token();
        public static readonly Parser<string> _typeParser = Parse.Regex(@"(\w+\.?(\[\])?)+").Text().Token();
        public static readonly Parser<string> _namespaceDefParser = from namespace_reserved in Parse.String("namespace").Text().Token()
                                                                    from ns in _typeParser
                                                                    from semicolon in Parse.Char(';').Once().Text().Token()
                                                                    select ns;
        public static readonly Parser<Field> _fieldParser = from mandatory in Parse.String("mandatory").Text().Optional().Token()
                                                            from field_type in _typeParser
                                                            from field_name in _identifierParser
                                                            from semiColon in Parse.Char(';').Once().Text().Token()
                                                            select new Field
                                                            {
                                                                Name = field_name,
                                                                Type = field_type,
                                                                Mandatory = mandatory.IsDefined
                                                            };
        public static readonly Parser<Request> _requestParser = from message_request in Parse.String("request").Text().Token()
                                                                from message_start in Parse.Char('{').Once().Text().Token()
                                                                from fields in _fieldParser.Many()
                                                                from message_end in Parse.Char('}').Once().Text().Token()
                                                                select new Request { Fields = fields };
        public static readonly Parser<Response> _responseParser = from message_request in Parse.String("response").Text().Token()
                                                                  from message_start in Parse.Char('{').Once().Text().Token()
                                                                  from fields in _fieldParser.Many()
                                                                  from message_end in Parse.Char('}').Once().Text().Token()
                                                                  select new Response { Fields = fields };
        public static readonly Parser<Message> _messageParser = from message_reserved in Parse.String("message").Text().Token()
                                                               from message_name in _identifierParser
                                                               from message_start in Parse.Char('{').Once().Text().Token()
                                                               from request in _requestParser.Optional()
                                                               from response in _responseParser.Optional()
                                                               from message_end in Parse.Char('}').Once().Text().Token()
                                                               select new Message 
                                                               { 
                                                                   Name = message_name, 
                                                                   Request = request.IsDefined ? request.Get() : null,
                                                                   Response = response.IsDefined ? response.Get(): null
                                                               };
        public static Parser<Tuple<string, IEnumerable<Message>>> _messagesParser = from ns in _namespaceDefParser
                                                                                    from message in _messageParser.Many()
                                                                                    select Tuple.Create(ns, message);

        public static Tuple<string, IEnumerable<Message>> ParseText(string text)
        {
            return _messagesParser.Parse(text);
        }
    }
}
