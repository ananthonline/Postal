using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Text;

using ProtoBuf;

using Microsoft.Build.Framework;
using Microsoft.CSharp;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace Postal.ProtoBuf
{
    public sealed class Postal : ITask
    {
        public IBuildEngine BuildEngine { get; set; }
        public ITaskHost HostObject { get; set; }

        public ITaskItem[] InputFiles { get; set; }

        [Output]
        public ITaskItem[] OutputFiles { get; set; }

        public string OutputDir { get; set; }

        private readonly Dictionary<string, string> builtInTypeReplacements = new Dictionary<string, string>
        {
            { "object", "Object" },
            { "string", "String" },
            { "bool", "Boolean" },
            { "sbyte", "SByte" },
            { "byte", "Byte" },
            { "char", "Char" },
            { "decimal", "Decimal" },
            { "double", "Double" },
            { "ushort", "UInt16" },
            { "short", "Int16" },
            { "ulong", "UInt64" },
            { "long", "Int64" },
            { "float", "Single" },
            { "uint", "UInt32" },
            { "int", "Int32" }
        };

        private const string MetadataLink = "Link";
        private const string MetadataCopyToOutputDirectory = "CopyToOutputDirectory";
        private const string MetadataFullPath = "FullPath";
        private const string MetadataIdentity = "Identity";

        public bool Execute()
        {
            var projectDir = Path.GetDirectoryName(BuildEngine.ProjectFileOfTaskNode);

            for (int i = 0; i < InputFiles.Length; i++)
            {
                BuildEngine.LogMessageEvent(new BuildMessageEventArgs(string.Format("Generating message wrappers for {0}", Path.GetFileName(InputFiles[i].ItemSpec)),
                    "Postal", "Postal.ProtoBuf", MessageImportance.High));

                foreach (var mi in InputFiles[i].MetadataNames)
                    BuildEngine.LogMessageEvent(new BuildMessageEventArgs(string.Format("\tMetadata: {0}: {1}", mi.ToString(), InputFiles[i].GetMetadata(mi.ToString())), "Postal", "Postal.ProtoBuf", MessageImportance.High));

                var link = InputFiles[i].GetMetadata(MetadataLink);
                var isLink = !string.IsNullOrEmpty(link);
                var identity = InputFiles[i].GetMetadata(MetadataIdentity);
                var copy = InputFiles[i].GetMetadata(MetadataCopyToOutputDirectory);
                var copyToOutputDirectory = !string.IsNullOrEmpty(copy) || (copy == "PreserveNewest") || (copy == "Always");
                var fullPath = InputFiles[i].GetMetadata(MetadataFullPath);
                OutputDir = Path.GetDirectoryName(OutputFiles[i].ItemSpec);

                var postalFileContents = File.ReadAllText(InputFiles[i].ItemSpec);
                var parsed = MessageParser.ParseText(postalFileContents);
                var codeUnit = GenerateCodeDOM(fullPath, parsed);

                File.WriteAllText(OutputFiles[i].ItemSpec, GenerateCSharpCode(codeUnit));
                var protoPath = Path.Combine(Path.GetDirectoryName(OutputFiles[i].ItemSpec), Path.GetFileNameWithoutExtension(OutputFiles[i].ItemSpec) + ".proto");
                File.WriteAllText(protoPath, GenerateProtoFile(OutputFiles[i].ItemSpec, codeUnit));

                var headerText = GenerateHeaderFile(parsed);
                var headerPath = Path.Combine(Path.GetDirectoryName(OutputFiles[i].ItemSpec), Path.GetFileNameWithoutExtension(OutputFiles[i].ItemSpec) + ".h");
                File.WriteAllText(headerPath, headerText);

                BuildEngine.LogMessageEvent(new BuildMessageEventArgs(string.Format("OutputDir is: {0}", OutputDir), "Postal", "Postal.ProtoBuf", MessageImportance.High));
            }

            return true;
        }

        private CodeTypeDeclaration FindTypeEndingWith(CodeTypeDeclarationCollection collection, string endsWith)
        {
            for (int i = 0; i < collection.Count; i++)
                if (collection[i].Name.EndsWith(endsWith))
                    return collection[i];
            return null;
        }

        public string GenerateCSharpCode(CodeCompileUnit compileunit)
        {
            var provider = new CSharpCodeProvider();

            var containerType = FindTypeEndingWith(compileunit.Namespaces[0].Types, "_MessagesContainer");

            var sb = new StringBuilder();
            using (var sw = new StringWriter(sb))
            using (var tw = new IndentedTextWriter(sw, "    "))
                provider.GenerateCodeFromCompileUnit(compileunit, tw,
                    new CodeGeneratorOptions());

            var result = sb.ToString();

            return result;
        }

        private string HeaderFileTemplate =
@"
#pragma once

#ifdef __cplusplus
namespace {0}
{{
#endif

{1}

#ifdef __cplusplus
}}
#endif
";

        public string GenerateHeaderFile(MessageParser.PostalDefinition parsed)
        {
            var constants = from def in parsed.PostalTypes
                            where def is MessageParser.ConstantDefinition
                            let constDef = def as MessageParser.ConstantDefinition
                            select string.Format("#define {0} {1}", constDef.Name, constDef.Value);

            return string.Format(HeaderFileTemplate, parsed.Namespace, string.Join("\n", constants));
        }

        private static readonly MethodInfo GetProtoMethod = typeof(Serializer).GetMethod("GetProto");
        private static readonly Regex RemoveMessageContainer = new Regex(@"message .+_MessagesContainer[^}]+}");

        public string GenerateProtoFile(string sourceFilename, CodeCompileUnit compileUnit)
        {
            var provider = new CSharpCodeProvider();
            var sourceCode = File.ReadAllText(sourceFilename);

            var parameters = new CompilerParameters
            {
                GenerateExecutable = false,
                ReferencedAssemblies = { "System.dll", "System.Core.dll", "System.Xml.dll", Path.Combine(OutputDir.Replace("obj", "bin"), "protobuf-net.dll") },
                OutputAssembly = string.Format("{0}\\{1}.dll", OutputDir.Replace("obj", "bin"), Guid.NewGuid().ToString())
            };
            var results = provider.CompileAssemblyFromSource(parameters, sourceCode);

            if (results.Errors.Count > 0)
                for (int i = 0; i < results.Errors.Count; i++)
                    BuildEngine.LogErrorEvent(new BuildErrorEventArgs("Postal", results.Errors[i].ErrorNumber, results.Errors[i].FileName, results.Errors[i].Line, results.Errors[i].Column, results.Errors[i].Line, results.Errors[i].Column, results.Errors[i].ErrorText, "Postal", "Postal"));

            var assembly = results.CompiledAssembly; // Loads this assembly into our namespace

            var type = (from t in assembly.GetTypes()
                        where t.IsDefined(typeof(ProtoContractAttribute), false) && t.Name.EndsWith("_MessagesContainer")
                        select t).FirstOrDefault();
            if (type == null)
                return string.Empty;

            var method = GetProtoMethod.MakeGenericMethod(type);
            if (method == null)
                return "method was null";

            var proto = (string)method.Invoke(null, null);
            proto = RemoveMessageContainer.Replace(proto, "");

            return proto;
        }

        private readonly CSharpCodeProvider cs = new CSharpCodeProvider();

        private string getFormalParamName(MessageParser.FieldDefinition field)
        {
            var name = string.Format("{0}{1}", field.Name.First().ToString().ToLower(), field.Name.Substring(1, field.Name.Length - 1));
            return cs.CreateEscapedIdentifier(name);
        }

        const int MUST_BE_LESS_THAN = 100000000; // 8 decimal digits
        private static int GetStableHash(string s)
        {
            uint hash = 0;
            // if you care this can be done much faster with unsafe 
            // using fixed char* reinterpreted as a byte*
            foreach (byte b in System.Text.Encoding.Unicode.GetBytes(s))
            {   
                hash += b;
                hash += (hash << 10);
                hash ^= (hash >> 6);    
            }
            // final avalanche
            hash += (hash << 3);
            hash ^= (hash >> 11);
            hash += (hash << 15);
            // helpfully we only want positive integer < MUST_BE_LESS_THAN
            // so simple truncate cast is ok if not perfect
            return (int)(hash % MUST_BE_LESS_THAN);
        }
        
        public CodeCompileUnit GenerateCodeDOM(string filename, MessageParser.PostalDefinition parsed)
        {
            var className = Path.GetFileNameWithoutExtension(filename);

            var codeUnit = new CodeCompileUnit();
            var ns = new CodeNamespace(parsed.Namespace);
            codeUnit.Namespaces.Add(ns);

            ns.Imports.Add(new CodeNamespaceImport("System"));
            ns.Imports.Add(new CodeNamespaceImport("System.Collections.Generic"));
            ns.Imports.Add(new CodeNamespaceImport("System.Collections.ObjectModel"));
            ns.Imports.Add(new CodeNamespaceImport("System.ComponentModel"));
            ns.Imports.Add(new CodeNamespaceImport("System.IO"));
            ns.Imports.Add(new CodeNamespaceImport("System.Linq"));
            ns.Imports.Add(new CodeNamespaceImport("System.Threading.Tasks"));
            ns.Imports.Add(new CodeNamespaceImport("global::ProtoBuf"));

            ns.Imports.Add(new CodeNamespaceImport("Postal.Core"));

            var messagesType = new CodeTypeDeclaration(className)
            {
                // Hack to make this class static so we can define extension methods
                // http://stackoverflow.com/a/6308395/802203
                Attributes = MemberAttributes.Final,
                TypeAttributes = TypeAttributes.Public,
                StartDirectives = { new CodeRegionDirective(CodeRegionMode.Start, "\nstatic") },
                EndDirectives = { new CodeRegionDirective(CodeRegionMode.End, string.Empty) },
                IsPartial = true
            };
            ns.Types.Add(messagesType);

            var messagesContainerType = new CodeTypeDeclaration(string.Format("{0}_MessagesContainer", messagesType.Name))
            {
                TypeAttributes = TypeAttributes.Public,
                CustomAttributes = { new CodeAttributeDeclaration("ProtoContract") }
            };
            messagesType.Members.Add(messagesContainerType);

            messagesType.Members.Add(new CodeSnippetTypeMember("public delegate TResponse ProcessRequestDelegate<TRequest, TResponse>(TRequest request) where TRequest : IRequest where TResponse : IResponse;"));

            messagesType.Members.Add(new CodeSnippetTypeMember(string.Format("private readonly static Dictionary<int, Type> _messageRequestTypes = new Dictionary<int, Type>(){{ {0} }};",
                string.Join(",\n", from message in parsed.PostalTypes
                                   where message is MessageParser.MessageDefinition
                                   select string.Format("{{ {0}.{0}Tag, typeof({0}.Request) }}", (message as MessageParser.MessageDefinition).Name)))));
            messagesType.Members.Add(new CodeSnippetTypeMember("public readonly static ReadOnlyDictionary<int, Type> MessageRequestTypes = new ReadOnlyDictionary<int, Type>(_messageRequestTypes);"));

            messagesType.Members.Add(new CodeSnippetTypeMember(string.Format("private readonly static Dictionary<int, Type> _messageResponseTypes = new Dictionary<int, Type>(){{ {0} }};",
                string.Join(",\n", from message in parsed.PostalTypes
                                   where message is MessageParser.MessageDefinition
                                   select string.Format("{{ {0}.{0}Tag, typeof({0}.Response) }}", (message as MessageParser.MessageDefinition).Name)))));
            messagesType.Members.Add(new CodeSnippetTypeMember("public readonly static ReadOnlyDictionary<int, Type> MessageResponseTypes = new ReadOnlyDictionary<int, Type>(_messageResponseTypes);"));

            messagesType.Members.Add(new CodeSnippetTypeMember(@"
private static void Serialize<T>(Stream stream, T request) where T: IRequest
{
    Serializer.SerializeWithLengthPrefix(stream, request, PrefixStyle.Base128, request.Tag);
}"));
            messagesType.Members.Add(new CodeSnippetTypeMember(@"
private static T Deserialize<T>(Stream stream) where T : IResponse
{
    object value;
    Serializer.NonGeneric.TryDeserializeWithLengthPrefix(stream, PrefixStyle.Base128,
        tag =>
        {
            Type type;
            return _messageResponseTypes.TryGetValue(tag, out type) ? type : null;
        }, out value);
    return (T)value;
}"));
            messagesType.Members.Add(new CodeSnippetTypeMember(@"
public static void ProcessRequest(this Stream stream)
{
    object value;
    Serializer.NonGeneric.TryDeserializeWithLengthPrefix(stream, PrefixStyle.Base128,
        tag =>
        {
            Type type;
            return _messageRequestTypes.TryGetValue(tag, out type) ? type : null;
        }, out value);
    var request = (IRequest)value;
    if (request == null)
        return;
    Serializer.NonGeneric.SerializeWithLengthPrefix(stream, request.InvokeReceived(), PrefixStyle.Base128, request.Tag);
}"));

            int dummyFieldsTag = 1;
            foreach (var type in parsed.PostalTypes)
            {
                var constDef = type as MessageParser.ConstantDefinition;
                if (constDef != null)
                {
                    var qualifiedType = constDef.Type.ReplaceAll(builtInTypeReplacements);
                    var constant = new CodeMemberField(qualifiedType, constDef.Name)
                    {
                        InitExpression = new CodeSnippetExpression(string.Format("({0}){1}", qualifiedType, constDef.Value)),
                        Attributes = MemberAttributes.Const | MemberAttributes.Public
                    };
                    messagesType.Members.Add(constant);

                    continue;
                }

                var enumDef = type as MessageParser.EnumDefinition;
                if (enumDef != null)
                {
                    var enumType = new CodeTypeDeclaration(enumDef.Name)
                    {
                        IsEnum = true
                    };
                    foreach (var enumValue in enumDef.Values)
                    {
                        var field = new CodeMemberField(enumDef.Name, enumValue.Item1);
                        if (enumValue.Item2.HasValue)
                            field.InitExpression = new CodePrimitiveExpression(enumValue.Item2.Value);
                        enumType.Members.Add(field);
                    }

                    messagesType.Members.Add(enumType);

                    continue;
                }

                var structDef = type as MessageParser.StructDefinition;
                if (structDef != null)
                {
                    var structType = new CodeTypeDeclaration(structDef.Name)
                    {
                        IsStruct = true,
                        IsPartial = true,
                        CustomAttributes = { new CodeAttributeDeclaration("ProtoContract") }
                    };

                    int fieldTag = 1;
                    foreach (var structField in structDef.Fields)
                    {
                        var field = new CodeMemberField(structField.Type.ReplaceAll(builtInTypeReplacements), structField.Name)
                            {
                                CustomAttributes = { new CodeAttributeDeclaration("ProtoMember", new CodeAttributeArgument(new CodeSnippetExpression(fieldTag.ToString()))) },
                                Attributes = MemberAttributes.Public
                            };
                        if (structField.DefaultValue != null)
                            field.CustomAttributes.Add(new CodeAttributeDeclaration("DefaultValue", new CodeAttributeArgument(new CodeSnippetExpression(structField.DefaultValue))));
                        structType.Members.Add(field);
                        fieldTag++;
                    }

                    messagesType.Members.Add(structType);
                    messagesContainerType.Members.Add(new CodeSnippetTypeMember(string.Format("[ProtoMember({0})] public {1} struct{1} {{ get; set; }}", dummyFieldsTag++, structDef.Name)));
                    continue;
                }

                var messageDef = type as MessageParser.MessageDefinition;
                if (messageDef != null)
                {
                    var messageType = new CodeTypeDeclaration(messageDef.Name)
                    {
                        Attributes = MemberAttributes.Final,
                        TypeAttributes = TypeAttributes.Public
                    };
                    messagesType.Members.Add(messageType);

                    messageType.Members.Add(new CodeSnippetTypeMember(string.Format("public const int {0}Tag = {1};", messageType.Name, GetStableHash(string.Format("{0}.{1}.{2}", ns.Name, messagesType.Name, messageType.Name)))));
                    messageType.Members.Add(new CodeSnippetTypeMember(@"public static event ProcessRequestDelegate<Request, Response> MessageReceived;"));

                    messageType.Members.Add(new CodeSnippetTypeMember(string.Format(
                        messageDef.Response == null ?
@"public static Task SendAsync({1})
{{
    return Task.Factory.StartNew(() =>
    {{
        Serialize(stream, new {0}.Request
        {{
            {2}
        }});
    }});
}}" :
@"public static Task<{0}.Response> SendAsync({1})
{{
    return Task.Factory.StartNew(() =>
    {{
        Serialize(stream, new {0}.Request
        {{
            {2}
        }});
        return Deserialize<{0}.Response>(stream);
    }});
}}", messageType.Name, string.Join(", ", new[] { "Stream stream" }.Concat(from field in messageDef.Request.Fields
                                                                          let paramName = getFormalParamName(field)
                                                                          select string.Format("{0} {1}", field.Type, paramName))),
                          string.Join(", \n", from field in messageDef.Request.Fields
                                              let paramName = getFormalParamName(field)
                                              select string.Format("{0} = {1}", field.Name, paramName)))));
                    messageType.Members.Add(new CodeSnippetTypeMember(string.Format(
                        messageDef.Response == null ?
@"public static void Send({1})
{{
    Serialize(stream, new {0}.Request
    {{
        {2}
    }});
}}" :
@"public static {0}.Response Send({1})
{{
    Serialize(stream, new {0}.Request
    {{
        {2}
    }});
    return Deserialize<{0}.Response>(stream);
}}", messageType.Name, string.Join(", ", new[] { "Stream stream" }.Concat(from field in messageDef.Request.Fields
                                                                          let paramName = getFormalParamName(field)
                                                                          select string.Format("{0} {1}", field.Type, paramName))),
                      string.Join(", \n", from field in messageDef.Request.Fields
                                          let paramName = getFormalParamName(field)
                                          select string.Format("{0} = {1}", field.Name, paramName)))));

                    // Add extensions methods for true RPC
                    messagesType.Members.Add(new CodeSnippetTypeMember(string.Format(
                        messageDef.Response == null ?
@"public static void {1}(this {2})
{{
    {3}.Send({4});
}}" :
@"public static {0}.Response {1}(this {2})
{{
    return {3}.Send({4});
}}",
                        messageType.Name,
                        messagesType.Name + messageType.Name,
                        string.Join(", ", new[] { "Stream stream" }.Concat(from field in messageDef.Request.Fields
                                                                           let paramName = getFormalParamName(field)
                                                                           select string.Format("{0} {1}", field.Type, paramName))),
                        messageType.Name,
                        string.Join(", ", new[] { "stream" }.Concat(from field in messageDef.Request.Fields
                                                                    let paramName = getFormalParamName(field)
                                                                    select paramName))
                                    )));

                    messagesType.Members.Add(new CodeSnippetTypeMember(string.Format(
                        messageDef.Response == null ?
@"public static Task {1}Async(this {2})
{{
    {3}.SendAsync({4});
}}" :
@"public static Task<{0}.Response> {1}Async(this {2})
{{
    return {3}.SendAsync({4});
}}",
                        messageType.Name,
                        messagesType.Name + messageType.Name,
                        string.Join(", ", new[] { "Stream stream" }.Concat(from field in messageDef.Request.Fields
                                                                           let paramName = getFormalParamName(field)
                                                                           select string.Format("{0} {1}", field.Type, paramName))),
                        messageType.Name,
                        string.Join(", ", new[] { "stream" }.Concat(from field in messageDef.Request.Fields
                                                                    let paramName = getFormalParamName(field)
                                                                    select paramName))
                                    )));

                    CodeTypeDeclaration messageRequestType = null;
                    if (messageDef.Request != null)
                    {
                        messageRequestType = new CodeTypeDeclaration("Request")
                        {
                            Attributes = MemberAttributes.Final,
                            TypeAttributes = TypeAttributes.Public,
                            BaseTypes = { new CodeTypeReference("IRequest") },
                            CustomAttributes = { new CodeAttributeDeclaration("ProtoContract", 
                                                 new[] 
                                                 { 
                                                     new CodeAttributeArgument
                                                     {
                                                         Name = "Name",
                                                         Value = new CodeSnippetExpression(string.Format("\"{0}{1}\"", messageType.Name, "Request"))
                                                     }
                                                 }) 
                            }
                        };
                        messageRequestType.Members.Add(new CodeSnippetTypeMember(string.Format("int IRequest.Tag {{ get {{ return {0}Tag; }} }}", messageType.Name)));
                        messageRequestType.Members.Add(new CodeSnippetTypeMember(string.Format("object IRequest.InvokeReceived() {{ return {0}.MessageReceived(this); }}", messageType.Name)));
                        messageType.Members.Add(messageRequestType);

                        int fieldTag = 1;
                        foreach (var field in messageDef.Request.Fields)
                        {
                            var member = new CodeSnippetTypeMember(string.Format("[ProtoMember({2}, IsRequired = {3})] {4} public {0} {1} {{ get; set; }}",
                                field.Type, field.Name, fieldTag++, field.Mandatory.ToString().ToLowerInvariant(),
                                field.DefaultValue != null ? string.Format("[DefaultValue({0})]", field.DefaultValue) : ""));
                            messageRequestType.Members.Add(member);
                        }

                        messagesContainerType.Members.Add(new CodeSnippetTypeMember(string.Format("[ProtoMember({0})] public {1}.{2}.{3} {1}_{2}_{3} {{ get; set; }}", dummyFieldsTag++, messagesType.Name, messageType.Name, "Request")));
                    }

                    CodeTypeDeclaration messageResponseType = null;
                    if (messageDef.Response != null)
                    {
                        messageResponseType = new CodeTypeDeclaration("Response")
                        {
                            Attributes = MemberAttributes.Final,
                            TypeAttributes = TypeAttributes.Public,
                            BaseTypes = { new CodeTypeReference("IResponse") },
                            CustomAttributes = { new CodeAttributeDeclaration("ProtoContract", 
                                                 new[]
                                                 {
                                                     new CodeAttributeArgument
                                                     {
                                                         Name = "Name",
                                                         Value = new CodeSnippetExpression(string.Format("\"{0}{1}\"", messageType.Name, "Response"))
                                                     }
                                                 })
                            }
                        };
                        messageResponseType.Members.Add(new CodeSnippetTypeMember(string.Format("int IResponse.Tag {{ get {{ return {0}Tag; }} }}", messageType.Name)));
                        messageType.Members.Add(messageResponseType);

                        int fieldTag = 1;
                        foreach (var field in messageDef.Response.Fields)
                        {
                            var member = new CodeSnippetTypeMember(string.Format("[ProtoMember({2}, IsRequired = {3})] {4} public {0} {1} {{ get; set; }}",
                                field.Type, field.Name, fieldTag++, field.Mandatory.ToString().ToLowerInvariant(),
                                field.DefaultValue != null ? string.Format("[DefaultValue({0})]", field.DefaultValue) : ""));
                            messageResponseType.Members.Add(member);
                        }

                        messagesContainerType.Members.Add(new CodeSnippetTypeMember(string.Format("[ProtoMember({0})] public {1}.{2}.{3} {1}_{2}_{3} {{ get; set; }}", dummyFieldsTag++, messagesType.Name, messageType.Name, "Response")));
                    }
                }
            } // foreach type

            return codeUnit;
        }
    }
}
