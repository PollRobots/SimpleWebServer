using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;

using PollRobots.SimpleServer;

class Program
{
    private static void Main(string[] args)
    {
        var levels = SourceLevels.Error;
        var directoryListings = true;

        var argList = new List<string>(args);
        if (argList.Remove("-v"))
        {
            levels = SourceLevels.Warning;
        }

        if (argList.Remove("-vv"))
        {
            levels = SourceLevels.Information;
        }

        if (argList.Remove("-nodirs"))
        {
            directoryListings = false;
        }

        if (argList.Remove("-h") || argList.Remove("-help") || argList.Remove("-?"))
        {
            Usage();
            return;
        }

        args = argList.ToArray();

        int port;
        if (args.Length < 1 ||
            !int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out port) ||
            port <= 0 || port >= 0x10000)
        {
            Console.Error.WriteLine("The first argument must be a valid port number.");
            Usage();
            return;
        }
        else if (args.Length < 2 || !Directory.Exists(args[1])) 
        {
            Console.Error.WriteLine("The second argument must be a valid directory.");
            Usage();
            return;
        }

        var listener = new ConsoleTraceListener();
        listener.Filter = new EventTypeFilter(levels);
        Trace.Listeners.Add(listener);

        var dir = Path.GetFullPath(args[1]);

        Trace.TraceInformation("Listening on port {0}", port);
        Trace.TraceInformation("Serving all files under {0}", dir);

        var server = new Server(port, dir, directoryListings);
        var task = server.Start();

        Console.CancelKeyPress += (s, e) =>
            {
                Trace.TraceInformation("Cancellation key pressed: {0}", e.SpecialKey);
                e.Cancel = true;
                server.Stop();
            };

        task.Wait();
    }

    static void Usage()
    {
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine();
        Console.WriteLine("  SimpleServer <port> <root> [options]");
        Console.WriteLine();
        Console.WriteLine("Arguments:");
        Console.WriteLine();
        Console.WriteLine("  <port>         The port number the server will listen on.");
        Console.WriteLine("  <root>         The root directory files will be served from.");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine();
        Console.WriteLine("  -nodirs        Do not generate directory listings");
        Console.WriteLine("  -v             write verbose output to the console");
        Console.WriteLine("  -vv            write very verbose output to the console");
        Console.WriteLine("  -h -help -?    Display this message");
    }
}
