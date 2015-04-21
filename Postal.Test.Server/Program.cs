using System;

using Postal.Test;
using System.IO.Pipes;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;

namespace Postal.Test.Server
{
    class Program
    {
        static readonly Dictionary<string, string> _values = new Dictionary<string, string>();
        static NamedPipeServerStream _serverPipe;

        static void Main(string[] args)
        {
            using (_serverPipe = new NamedPipeServerStream(PipeDetails.Name, PipeDirection.InOut))
            {
                Messages.GetStrings.MessageReceived += getStrings_MessageReceived;
                Messages.SetStrings.MessageReceived += setStrings_MessageReceived;

                var exit = false;
                while (!exit)
                {
                    try
                    {
                        if (!_serverPipe.IsConnected)
                        {
                            Console.WriteLine("Waiting for client connection...");
                            _serverPipe.WaitForConnection();
                            Console.WriteLine("Client connected. Waiting for commands.");
                        }

                        _serverPipe.ProcessRequest();
                    }
                    catch (IOException)
                    {
                        Console.WriteLine("Server pipe error");
                        _serverPipe.Disconnect();
                    }
                    catch (Exception)
                    {
                        Console.WriteLine("Unknown error, quitting");
                        _serverPipe.Disconnect();
                    }
                }
            }
        }

        static Messages.SetStrings.Response setStrings_MessageReceived(Messages.SetStrings.Request request)
        {
            Console.WriteLine("Asked to set {0}",
                string.Join(", ", from i in Enumerable.Range(0, request.Names.Length)
                                  select string.Format("{0} = {1}", request.Names[i], request.Values[i])));
            
            var response = new Messages.SetStrings.Response();
            try
            {
                for (int i = 0; i < request.Names.Length; i++)
                    _values[request.Names[i]] = request.Values[i];
            }
            catch (Exception ex)
            {
                response.Result = false;
                response.Message = ex.Message;
                return response;
            }

            response.Result = true;
            return response;
        }

        static Messages.GetStrings.Response getStrings_MessageReceived(Messages.GetStrings.Request request)
        {
            Console.WriteLine("Asked to get values for: {0}", string.Join(", ", request.Names));
            var error = new StringBuilder();
            var response = new Messages.GetStrings.Response
            {
                Result = true,
                Values = new string[request.Names.Length]
            };
            try
            {
                for (int i = 0; i < request.Names.Length; i++)
                {
                    if (!_values.ContainsKey(request.Names[i]))
                    {
                        response.Result = false; // We failed to do something
                        error.AppendFormat("Could not find key: {0}\n", request.Names[i]);
                        Console.WriteLine("Could not find key: {0}\n", request.Names[i]);
                        continue;
                    }

                    Console.WriteLine("Found value {0} for key {1}", _values[request.Names[i]], request.Names[i]);
                    response.Values[i] = _values[request.Names[i]];
                }
            }
            catch (Exception ex)
            {
                response.Result = false;
                response.Message = ex.Message;
            }

            if (!response.Result)
                response.Message = error.ToString();

            return response;
        }
    }
}