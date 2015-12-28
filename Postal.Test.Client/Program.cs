using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text.RegularExpressions;

namespace Postal.Test.Client
{
    class Program
    {
        const string Usage =
@"You can type any of the following commands into the prompt below.
get <name>[,<name>,<name>...]⏎ will get one or more values stored against that name
set <name>:<value>[,<name>:<value>,<name>:<value>,...]⏎ will store or overwrite one or more stored values
exit will shut down the client and server";

        static readonly Regex _commandRegex = new Regex(@"(?<command>get|set|exit)(\s+((?<names>[^:^,]+)\s*((\:\s*(?<values>[^:^,]+))?(,\s*)?)+)+)?", RegexOptions.Compiled | RegexOptions.Singleline);

        // All lowercase so we can parse strings to this enum
        enum Command
        {
            get,
            set,
            exit
        }

        static void Main(string[] args)
        {
            var exit = false;
            int sequence = 0;

            using (var clientPipe = new NamedPipeClientStream(Messages.PipeName))
            {
                try
                {
                    clientPipe.Connect();
                }
                catch (Exception)
                {
                    Console.WriteLine("Could not connect to server pipe, please start the server and then try again");
                    return;
                }

                while (!exit)
                {
                    Console.WriteLine(Usage);
                    Console.Write(">");
                    var input = Console.ReadLine();
                    var match = _commandRegex.Match(input);
                    
                    if (!match.Success)
                        continue;
                    
                    var command = (Command)Enum.Parse(typeof(Command), match.Groups["command"].Value);
                    var names = (from Capture capture in match.Groups["names"].Captures select capture.Value).ToArray();
                    
                    switch (command)
                    {
                        case Command.get:
                            {
                                // using (var getStringsRequest = new FileStream("GetStrings.Request", FileMode.Create))
                                //     getStringsRequest.MessagesGetStrings(names);

                                var response = clientPipe.MessagesGetStrings(names);
                                if (response.Result == Messages.Result.Success)
                                    Console.WriteLine("Values for keys: {0} are: {1}", string.Join(", ", names), string.Join(", ", response.Values));
                                else
                                    Console.WriteLine("There was an error fetching values for one or more keys: {0}", response.Message);
                            }
                            break;

                        case Command.set:
                            {
                                var kvps = Enumerable.Zip(names,
                                    from Capture capture in match.Groups["values"].Captures select capture.Value,
                                    (k, v) => new Messages.KeyValuePair
                                    {
                                        Key = k,
                                        Value = v
                                    });

                                // using (var setStringsRequest = new FileStream("SetStrings.Request", FileMode.Create))
                                //     setStringsRequest.MessagesSetStrings(kvps.ToArray());

                                var response = clientPipe.MessagesSetStrings(kvps.ToArray());
                                if (response.Result == Messages.Result.Success)
                                    Console.WriteLine("Successfully set values for keys: {0}", string.Join(", ", names));
                                else
                                    Console.WriteLine("There was an error setting values for keys: {0}, error was: {1}",
                                        string.Join(", ", names),
                                        response.Message);
                            }
                            break;
                        case Command.exit:
                            Console.WriteLine("Asked to exit, shutting down server");
                            clientPipe.MessagesExit();
                            Console.WriteLine("Asked to exit, shutting down client");
                            exit = true;
                            break;
                    }
                }
            }
        }
    }
}