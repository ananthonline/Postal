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
            using (_serverPipe = new NamedPipeServerStream(Messages.PipeName, PipeDirection.InOut))
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

                        _serverPipe.ProcessRequest((stream, request) => 
                        {
                            Console.WriteLine("Request deserialized!");
                            return true;
                        });
                    }
                    catch (IOException)
                    {
                        Console.WriteLine("Server pipe error");
                        _serverPipe.Disconnect();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Unknown error: {0}, quitting\nStacktrace: {1}", ex.Message, ex.StackTrace);
                        _serverPipe.Disconnect();
                        return;
                    }
                }
            }
        }

        static Messages.SetStrings.Response setStrings_MessageReceived(Messages.SetStrings.Request request)
        {
            Console.WriteLine("Asked to set {0}",
                string.Join(", ", from i in Enumerable.Range(0, request.KeyValuePairs.Length)
                                  select string.Format("{0} = {1}", request.KeyValuePairs[i].Key, request.KeyValuePairs[i].Value)));
            
            var response = new Messages.SetStrings.Response();
            try
            {
                for (int i = 0; i < request.KeyValuePairs.Length; i++)
                    _values[request.KeyValuePairs[i].Key] = request.KeyValuePairs[i].Value;
            }
            catch (Exception ex)
            {
                response.Result = Messages.Result.Exception;
                response.Message = ex.Message;
                return response;
            }

            response.Result = Messages.Result.Success;
            return response;
        }

        static Messages.GetStrings.Response getStrings_MessageReceived(Messages.GetStrings.Request request)
        {
            Console.WriteLine("Asked to get values for: {0}", string.Join(", ", request.Names));
            var error = new StringBuilder();
            var response = new Messages.GetStrings.Response
            {
                Result = Messages.Result.Success,
                Values = new string[request.Names.Length]
            };
            try
            {
                for (int i = 0; i < request.Names.Length; i++)
                {
                    if (!_values.ContainsKey(request.Names[i]))
                    {
                        response.Result = Messages.Result.CouldNotFindKey; // We failed to do something
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
                response.Result = Messages.Result.Exception;
                response.Message = ex.Message;
            }

            if (response.Result != Messages.Result.Success)
                response.Message = error.ToString();

            return response;
        }
    }
}