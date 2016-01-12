using Sprache;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Postal.ProtoBuf
{
    public static class MessageParser
    {
        public class PostalDefinition
        {
            public string Namespace;
            public IEnumerable<PostalTypeDefinition> PostalTypes;
        }

        public class PostalTypeDefinition
        {
        }
        
        public class MessageDefinition: PostalTypeDefinition
        {
            public string Name;
            public RequestDefinition Request;
            public ResponseDefinition Response;
        }

        public class FieldDefinition
        {
            public string Type;
            public string Name;
            public bool Mandatory;
            public string DefaultValue;
        }

        public class RequestDefinition
        {
            public IEnumerable<FieldDefinition> Fields;
        }

        public class ResponseDefinition
        {
            public IEnumerable<FieldDefinition> Fields;
        }

        public class EnumDefinition: PostalTypeDefinition
        {
            public string Name;
            public IEnumerable<Tuple<string, int?>> Values;
        }

        public class ImportDefinition: PostalTypeDefinition
        {
        }

        public class StructDefinition: PostalTypeDefinition
        {
            public string Name;
            public IEnumerable<FieldDefinition> Fields;
        }

        public class ConstantDefinition: PostalTypeDefinition
        {
            public string Name;
            public string Type;
            public string Value;
        }

        public class CommentDefinition: PostalTypeDefinition
        {
            public string Comment;
        }

        public static readonly Parser<string> _singlelineCommentParser = from value in Parse.EndOfLineComment("//")
                                                                         select value.Trim();

        public static readonly Parser<string> _identifierParser = Parse.Regex(@"\w[\w\.]+").Text().Token();

        public static readonly Parser<string> _typeParser = Parse.Regex(@"(\w+\.?(\[\])?)+").Text().Token();

        public static readonly Parser<string> _namespaceDefParser = from namespace_reserved in Parse.String("namespace").Text().Token()
                                                                    from ns in _typeParser
                                                                    from semicolon in Parse.Char(';').Once().Text().Token()
                                                                    select ns;

        public static readonly Parser<string> _constantAssignmentParser = from value_assign in Parse.Char('=').Once().Token()
                                                                          from value in _stringValueParser
                                                                                        .Or(_hexValueParser)
                                                                                        .Or(_intFloatParser)
                                                                                        .Or(_identifierParser)
                                                                                        .Text().Token()
                                                                          select value;

        public static readonly Parser<FieldDefinition> _fieldParser = from mandatory in Parse.String("mandatory").Text().Optional().Token()
                                                            from field_type in _typeParser
                                                            from field_name in _identifierParser
                                                            from defaultValue in _constantAssignmentParser.Optional().Token()
                                                            from semiColon in Parse.Char(';').Once().Text().Token()
                                                            select new FieldDefinition
                                                            {
                                                                Name = field_name,
                                                                Type = field_type,
                                                                Mandatory = mandatory.IsDefined,
                                                                DefaultValue = defaultValue.GetOrDefault()
                                                            };

        public static readonly Parser<string> _intFloatParser = from value in Parse.Regex(@"-?\d*(\.\d+)?").Text().Token()
                                                                select value;

        public static readonly Parser<string> _intValueParser = from value in Parse.Regex(@"-?\\d+").Text().Token()
                                                                select value;

        public static readonly Parser<string> _hexValueParser = from value in Parse.Regex(@"0x[A-Fa-f0-9]+").Text().Token()
                                                                select value;

        public static readonly Parser<string> _stringValueParser = from value in Parse.Regex(@"""[^""\\]*(?:\\.[^""\\]*)*""")
                                                                   select value;

        // Parse.Regex(@"(0[xX])?[\da-fA-F]+").Text().Token()
        public static readonly Parser<int> _enumValueParser = from value_assign in Parse.Char('=').Once().Text().Token()
                                                              from value in Parse.Regex(@"(0[xX])?[\da-fA-F]+").Text().Token()
                                                              let isHex = value.ToUpperInvariant().StartsWith("0X")
                                                              select isHex ? int.Parse(value.TrimStart('0', 'X', 'x'), NumberStyles.AllowHexSpecifier) : int.Parse(value);

        public static readonly Parser<Tuple<string, int?>> _enumNamesParser = from name in _identifierParser
                                                                              from value in _enumValueParser.Optional()
                                                                              from semicolon in Parse.Char(';').Once().Text().Token()
                                                                              select Tuple.Create(name, value.IsDefined ? (int?)value.Get() : null);

        public static readonly Parser<PostalTypeDefinition> _enumParser = from enum_reserved in Parse.String("enum").Text().Token()
                                                                    from enum_name in _identifierParser
                                                                    from enum_start in Parse.Char('{').Once().Text().Token()
                                                                    from enum_values in _enumNamesParser.Many()
                                                                    from enum_end in Parse.Char('}').Once().Text().Token()
                                                                    select new EnumDefinition
                                                                    { 
                                                                        Name = enum_name,
                                                                        Values = enum_values
                                                                    };

        private static readonly Parser<FieldDefinition> _structFieldParser = from field_type in _typeParser
                                                                             from field_name in _identifierParser
                                                                             from field_value in _constantAssignmentParser.Optional().Token()
                                                                             from semiColon in Parse.Char(';').Once().Text().Token()
                                                                             select new FieldDefinition
                                                                             {
                                                                                 Name = field_name,
                                                                                 Type = field_type,
                                                                                 DefaultValue = field_value.GetOrDefault()
                                                                             };

        private static readonly Parser<ConstantDefinition> _constantParser = from const_reserved in Parse.String("const").Text().Token()
                                                                             from const_type in _typeParser
                                                                             from const_name in _identifierParser
                                                                             from const_value in _constantAssignmentParser
                                                                             from semiColon in Parse.Char(';').Once().Text().Token()
                                                                             select new ConstantDefinition
                                                                             {
                                                                                 Name = const_name,
                                                                                 Type = const_type,
                                                                                 Value = const_value
                                                                             };

        private static readonly Parser<PostalTypeDefinition> _structParser = from struct_reserved in Parse.String("struct").Text().Token()
                                                                             from struct_name in _identifierParser
                                                                             from struct_start in Parse.Char('{').Once().Text().Token()
                                                                             from fields in _structFieldParser.Many()
                                                                             from struct_end in Parse.Char('}').Once().Text().Token()
                                                                             select new StructDefinition
                                                                             {
                                                                                 Name = struct_name,
                                                                                 Fields = fields
                                                                             };

        public static readonly Parser<RequestDefinition> _requestParser = from message_request in Parse.String("request").Text().Token()
                                                                from message_start in Parse.Char('{').Once().Text().Token()
                                                                from fields in _fieldParser.Many()
                                                                from message_end in Parse.Char('}').Once().Text().Token()
                                                                select new RequestDefinition { Fields = fields };

        public static readonly Parser<ResponseDefinition> _responseParser = from message_request in Parse.String("response").Text().Token()
                                                                  from message_start in Parse.Char('{').Once().Text().Token()
                                                                  from fields in _fieldParser.Many()
                                                                  from message_end in Parse.Char('}').Once().Text().Token()
                                                                  select new ResponseDefinition { Fields = fields };

        public static readonly Parser<PostalTypeDefinition> _messageParser = from message_reserved in Parse.String("message").Text().Token()
                                                               from message_name in _identifierParser
                                                               from message_start in Parse.Char('{').Once().Text().Token()
                                                               from request in _requestParser.Optional()
                                                               from response in _responseParser.Optional()
                                                               from message_end in Parse.Char('}').Once().Text().Token()
                                                               select new MessageDefinition
                                                               { 
                                                                   Name = message_name, 
                                                                   Request = request.IsDefined ? request.Get() : null,
                                                                   Response = response.IsDefined ? response.Get(): null
                                                               };
        public static readonly Parser<CommentDefinition> _commentParser = from comment in _singlelineCommentParser
                                                                          select new CommentDefinition
                                                                          {
                                                                              Comment = comment
                                                                          };

        public static Parser<PostalDefinition> _postalParser = from ns in _namespaceDefParser
                                                               from types in _messageParser
                                                                            .Or(_constantParser)
                                                                            .Or(_enumParser)
                                                                            .Or(_structParser)
                                                                            .Or(_commentParser)
                                                                            .Many()
                                                               select new PostalDefinition
                                                               {
                                                                   Namespace = ns,
                                                                   PostalTypes = types
                                                               };

        public static PostalDefinition ParseText(string text)
        {
            return _postalParser.Parse(text);
        }
    }
}