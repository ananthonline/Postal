using NUnit.Framework;

using System;

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

message SetStrings
{
	request
	{
		mandatory string[] Names;
		mandatory string[] Values;
	}
	response
	{
		mandatory Result ErrCode;
		string Message;
	}
}

message GetStrings
{
	request
	{
		mandatory string[] Names;
	}
	response
	{
		mandatory Result ErrCode;
		string Message;
		mandatory string[] Values;
	}
}
";
        
        [TestCase]
        public void TestEnumParser()
        {
            var def = MessageParser.ParseText(EnumDef);
        }
    }
}
