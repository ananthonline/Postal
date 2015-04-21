using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Text;

using Microsoft.Build.Framework;
using Microsoft.CSharp;

namespace Postal.ProtoBuf
{
    public sealed class Postal: ITask
    {
        public IBuildEngine BuildEngine { get; set; }
        public ITaskHost HostObject { get; set; }

        public ITaskItem[] InputFiles { get; set; }

        [Output]
        public ITaskItem[] OutputFiles { get; set; }

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

                var codeUnit = GenerateCodeDOM(fullPath, File.ReadAllText(InputFiles[i].ItemSpec));

                File.WriteAllText(OutputFiles[i].ItemSpec, GenerateCSharpCode(codeUnit));
            }

            return true;
        }

        public static string GenerateCSharpCode(CodeCompileUnit compileunit)
        {
            var provider = new CSharpCodeProvider();

            var sb = new StringBuilder();
            using (var sw = new StringWriter(sb))
            using (var tw = new IndentedTextWriter(sw, "    "))
                provider.GenerateCodeFromCompileUnit(compileunit, tw,
                    new CodeGeneratorOptions());

            return sb.ToString();
        }
        public static CodeCompileUnit GenerateCodeDOM(string filename, string messageFileContents)
        {

            var parsed = MessageParser.ParseText(messageFileContents);
            var className = Path.GetFileNameWithoutExtension(filename);

            var codeUnit = new CodeCompileUnit();
            var ns = new CodeNamespace(parsed.Item1);
            codeUnit.Namespaces.Add(ns);

            ns.Imports.Add(new CodeNamespaceImport("System"));
            ns.Imports.Add(new CodeNamespaceImport("System.Collections.Generic"));
            ns.Imports.Add(new CodeNamespaceImport("System.IO"));
            ns.Imports.Add(new CodeNamespaceImport("System.Linq"));
            ns.Imports.Add(new CodeNamespaceImport("System.Threading.Tasks"));
            ns.Imports.Add(new CodeNamespaceImport("global::ProtoBuf"));

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

            messagesType.Members.Add(new CodeSnippetTypeMember("public delegate TResponse ProcessRequestDelegate<TRequest, TResponse>(TRequest request) where TRequest : IRequest where TResponse : IResponse;"));

            messagesType.Members.Add(new CodeSnippetTypeMember(string.Format("private readonly static Dictionary<int, Type> _messageRequestTypes = new Dictionary<int, Type>(){{ {0} }};",
                string.Join(",\n", from message in parsed.Item2
                                   select string.Format("{{ {0}.{0}Tag, typeof({0}.Request) }}", message.Name)))));

            messagesType.Members.Add(new CodeSnippetTypeMember(string.Format("private readonly static Dictionary<int, Type> _messageResponseTypes = new Dictionary<int, Type>(){{ {0} }};",
                string.Join(",\n", from message in parsed.Item2
                                   select string.Format("{{ {0}.{0}Tag, typeof({0}.Response) }}", message.Name)))));

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

            // Add IRequest and IResponse dummy interfaces for extension method
            var requestInterfaceType = new CodeTypeDeclaration("IRequest")
            {
                IsInterface = true,
                TypeAttributes = TypeAttributes.Public | TypeAttributes.Interface,
                Members =
                {
                    new CodeMemberProperty { Name = "Tag", HasGet = true, Type = new CodeTypeReference(typeof(int)) },
                    new CodeMemberMethod { Name = "InvokeReceived", ReturnType = new CodeTypeReference(typeof(object)) }
                }
            };
            messagesType.Members.Add(requestInterfaceType);
            var responseInterfaceType = new CodeTypeDeclaration("IResponse")
            {
                IsInterface = true,
                TypeAttributes = TypeAttributes.Public | TypeAttributes.Interface
            };
            messagesType.Members.Add(responseInterfaceType);

            int messageTag = 1001;

            foreach (var message in parsed.Item2)
            {
                var messageType = new CodeTypeDeclaration(message.Name)
                {
                    Attributes = MemberAttributes.Final,
                    TypeAttributes = TypeAttributes.Public
                };
                messagesType.Members.Add(messageType);

                messageType.Members.Add(new CodeSnippetTypeMember(string.Format("public const int {0}Tag = {1};", messageType.Name, messageTag++)));
                messageType.Members.Add(new CodeSnippetTypeMember(@"public static event ProcessRequestDelegate<Request, Response> MessageReceived;"));

                messageType.Members.Add(new CodeSnippetTypeMember(string.Format(@"
public static Task<{0}.Response> SendAsync({1})
{{
    return Task.Factory.StartNew(() =>
    {{
        Serialize(stream, new {0}.Request
        {{
            {2}
        }});
        return Deserialize<{0}.Response>(stream);
    }});
}}", messageType.Name, string.Join(", ", new[] { "Stream stream" }.Concat(from field in message.Request.Fields
                                         let paramName = string.Format("{0}{1}", field.Name.First().ToString().ToLower(), field.Name.Substring(1, field.Name.Length - 1))
                                        select string.Format("{0} {1}", field.Type, paramName))),
                      string.Join(", \n", from field in message.Request.Fields
                                          let paramName = string.Format("{0}{1}", field.Name.First().ToString().ToLower(), field.Name.Substring(1, field.Name.Length - 1))
                                          select string.Format("{0} = {1}", field.Name, paramName)))));

                messageType.Members.Add(new CodeSnippetTypeMember(string.Format(@"
public static {0}.Response Send({1})
{{
    Serialize(stream, new {0}.Request
    {{
        {2}
    }});
    return Deserialize<{0}.Response>(stream);
}}", messageType.Name, string.Join(", ", new[] { "Stream stream" }.Concat(from field in message.Request.Fields
                                                                          let paramName = string.Format("{0}{1}", field.Name.First().ToString().ToLower(), field.Name.Substring(1, field.Name.Length - 1))
                                                                          select string.Format("{0} {1}", field.Type, paramName))),
                      string.Join(", \n", from field in message.Request.Fields
                                          let paramName = string.Format("{0}{1}", field.Name.First().ToString().ToLower(), field.Name.Substring(1, field.Name.Length - 1))
                                          select string.Format("{0} = {1}", field.Name, paramName)))));

                if (message.Request != null)
                {
                    var messageRequestType = new CodeTypeDeclaration("Request")
                    {
                        Attributes = MemberAttributes.Final,
                        TypeAttributes = TypeAttributes.Public,
                        BaseTypes = { new CodeTypeReference("IRequest") },
                        CustomAttributes = { new CodeAttributeDeclaration("ProtoContract") }
                    };
                    messageType.Members.Add(messageRequestType);
                    messageRequestType.Members.Add(new CodeSnippetTypeMember(string.Format("int IRequest.Tag {{ get {{ return {0}Tag; }} }}", messageType.Name)));
                    messageRequestType.Members.Add(new CodeSnippetTypeMember(string.Format("object IRequest.InvokeReceived() {{ return {0}.MessageReceived(this); }}", messageType.Name)));

                    int fieldTag = 1;
                    foreach (var field in message.Request.Fields)
                    {
                        var member = new CodeSnippetTypeMember(string.Format("[ProtoMember({2}, IsRequired = {3})] public {0} {1} {{ get; set; }}", field.Type, field.Name, fieldTag++, field.Mandatory.ToString().ToLowerInvariant()));
                        messageRequestType.Members.Add(member);
                    }
                }
                if (message.Response != null)
                {
                    var messageResponseType = new CodeTypeDeclaration("Response")
                    {
                        Attributes = MemberAttributes.Final,
                        TypeAttributes = TypeAttributes.Public,
                        BaseTypes = { new CodeTypeReference("IResponse") },
                        CustomAttributes = { new CodeAttributeDeclaration("ProtoContract") }
                    };
                    messageType.Members.Add(messageResponseType);

                    int fieldTag = 1;
                    foreach (var field in message.Response.Fields)
                    {
                        var member = new CodeSnippetTypeMember(string.Format("[ProtoMember({2}, IsRequired = {3})] public {0} {1} {{ get; set; }}", field.Type, field.Name, fieldTag++, field.Mandatory.ToString().ToLowerInvariant()));
                        messageResponseType.Members.Add(member);
                    }
                }
            }

            return codeUnit;
        }
    }
}
