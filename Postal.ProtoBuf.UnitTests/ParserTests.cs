using NUnit.Framework;

using System;
using System.Linq;

using Postal.ProtoBuf;

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
    int OneValue;
    byte[] ArrayOfValues;
}
";

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
            var postal = new Postal();
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
    }
}
