using NUnit.Framework;

using System;
using System.Linq;

using Postal.ProtoBuf;
using System.IO;

namespace Postal.ProtoBuf.UnitTests
{
    [TestFixture]
    public class ParserTests
    {
        private const string EnumDef = @"
namespace Postal.Test;

enum Result
{
	UnknownError;
	Success;
}
";

        private const string StructDef = @"
namespace Postal.Test;

struct Test
{
    int OneValue = 0xFFFF;
    float ThirdValue = 3.14159265359;
    string AnotherValue = ""This is a string value"";
    byte[] ArrayOfValues;
}
";

        private const string Test = @"
namespace Postal.Test;

const string RegistrationChannelName = ""ChannelRegistrar"";
const float PI = 3.14159;

enum Result
{
	UnknownError = 0;
	Exception;
	CouldNotFindKey;
	Success;
}

struct MyStruct
{
    int OneValue = 0xFFFF;
    float ThirdValue = 3.14159265359;
    string AnotherValue = ""This is a string value"";
    byte[] ArrayOfValues;
    uint test;
}

message SetStrings
{
	request
	{
		mandatory string[] Names;
		mandatory string[] Values;
	}
	response
	{
		mandatory Result Result;
		string Message;
	}
}

message GetStrings
{
	request
	{
		mandatory string[] Names;
        MyStruct struc;
	}
	response
	{
		mandatory Result Result = Result.Success;
		string Message;
		mandatory string[] Values;
	}
}";

        [TestCase]
        public void TestEnumParser()
        {
            var def = MessageParser.ParseText(EnumDef);
            var enumType = (from type in def.PostalTypes
                            let e = type as MessageParser.EnumDefinition
                            where e != null
                            select e).FirstOrDefault();
            Assert.IsNotNull(enumType);
            Assert.AreEqual(enumType.Name, "Result");
            Assert.AreEqual(enumType.Values.Count(), 2);
            Assert.AreEqual(enumType.Values.First(), "UnknownError");
            Assert.AreEqual(enumType.Values.Last(), "Success");
        }

        [TestCase]
        public void TestStructParser()
        {
            var def = MessageParser.ParseText(StructDef);
            var structType = (from type in def.PostalTypes
                              let s = type as MessageParser.StructDefinition
                              where s != null
                              select s).FirstOrDefault();
            Assert.IsNotNull(structType);
            Assert.AreEqual(structType.Name, "Test");
            Assert.AreEqual(structType.Fields.Count(), 2);

            Assert.AreEqual(structType.Fields.First().Name, "OneValue");
            Assert.AreEqual(structType.Fields.First().Type, "int");

            Assert.AreEqual(structType.Fields.Last().Name, "ArrayOfValues");
            Assert.AreEqual(structType.Fields.Last().Type, "byte[]");
        }

        [TestCase]
        public void Test2()
        {
            var def = MessageParser.ParseText(Test);
            var postal = new Postal();
            postal.OutputDir = AppDomain.CurrentDomain.BaseDirectory;
            var codeDOM = postal.GenerateCodeDOM("Test.postal", def);
            var code = postal.GenerateCSharpCode(codeDOM);
            var header = postal.GenerateHeaderFile(def);
            File.WriteAllText("Test.cs", code);
            var proto = postal.GenerateProtoFile("Test.cs", codeDOM);
        }
    }
}