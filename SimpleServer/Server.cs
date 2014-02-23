
namespace PollRobots.SimpleServer
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Net;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web;

    /// <summary>Implements a simple http server serving files under a
    /// particular directory.</summary>
    public sealed class Server : IDisposable
    {
        /// <summary>Used to stop the server.</summary>
        private CancellationTokenSource tokenSource;

        /// <summary>Initializes a new instance of the <see cref="Server"/>
        /// class.</summary>
        /// <param name="port">The port number to listen on.</param>
        /// <param name="root">The root from which files will be served.</param>
        /// <param name="directoryListings">Indicates whether directory listings
        /// will be generated.</param>
        public Server(int port, string root, bool directoryListings)
        {
            if (port < 0 || port >= 0x10000)
            {
                throw new ArgumentOutOfRangeException("port");
            }
            else if (string.IsNullOrEmpty(root))
            {
                throw new ArgumentNullException("root");
            }
            else if (!Directory.Exists(root))
            {
                throw new ArgumentException("Cannot find directory", "root");
            }

            this.Port = port;
            this.Root = root;
            this.DirectoryListings = directoryListings;

            this.tokenSource = new CancellationTokenSource();
        }

        /// <summary>Gets the port number to listen on.</summary>
        public int Port { get; private set; }

        /// <summary>Gets the root directory from which files are served.</summary>
        public string Root { get; private set; }

        /// <summary>Gets a value indicating whether directory listings will be
        /// generated.</summary>
        public bool DirectoryListings { get; private set; }

        /// <summary>Gets a value indicating whether the server has been started.</summary>
        public bool IsStarted { get; private set; }

        /// <summary>Starts listening.</summary>
        /// <returns>The task representing the running server.</returns>
        public Task Start()
        {
            if (this.IsStarted)
            {
                throw new InvalidOperationException("Server is already started.");
            }

            this.IsStarted = true;
            return Task.Run(async () => await this.RunListener(this.tokenSource.Token));
        }

        /// <summary>Stops the server.</summary>
        public void Stop()
        {
            if (!this.IsStarted)
            {
                throw new InvalidOperationException("Server has not been started.");
            }
            else if (this.tokenSource == null || this.tokenSource.IsCancellationRequested)
            {
                throw new InvalidOperationException("Server is already stopped.");
            }

            this.tokenSource.Cancel();
        }

        /// <summary>Disposes this server instance.</summary>
        public void Dispose()
        {
            this.tokenSource.Dispose();
            this.tokenSource = null;
        }

        /// <summary>Runs the <see cref="HttpListener"/> waiting for requests.</summary>
        /// <param name="cancel">The cancellation token used to signal stopping the server.</param>
        /// <returns>The task objecct.</returns>
        private async Task RunListener(CancellationToken cancel)
        {
            var prefix = string.Format(
                CultureInfo.InvariantCulture,
                "http://localhost:{0}/",
                this.Port);
            var listener = new HttpListener();
            listener.Prefixes.Add(prefix);
            listener.Start();

            while (true)
            {
                try
                {
                    var task = listener.GetContextAsync();
                    var context = await task.ContinueWith(tc => tc.Result, cancel);

                    var t = ServeRequest(context).ContinueWith(_ => { }, cancel);
                }
                catch (Exception e)
                {
                    if (cancel.IsCancellationRequested)
                    {
                        Trace.TraceInformation("Cancelled");
                    }
                    else
                    {
                        Trace.TraceError(
                            "Exception in http listener context: {0}",
                            e.Message);
                    }
                    break;
                }
            }
        }

        /// <summary>Serves an individual file request.</summary>
        /// <param name="context">The context for the request.</param>
        /// <returns>An async task.</returns>
        private async Task ServeRequest(HttpListenerContext context)
        {
            var path = context.Request.Url.AbsolutePath;
            if (!string.IsNullOrEmpty(path))
            {
                path = path.Substring(1);
            }

            path = Path.Combine(this.Root, path.Replace('/', '\\'));
            if (!path.StartsWith(this.Root))
            {
                Trace.TraceWarning(
                    "Forbidden to load '{0}'",
                    context.Request.Url.AbsolutePath);
                context.Response.StatusCode = 403;
            }
            else if (context.Request.HttpMethod != "GET")
            {
                Trace.TraceWarning(
                    "Forbidden to use method {0} '{1}'",
                    context.Request.HttpMethod,
                    context.Request.Url.AbsolutePath);
                context.Response.StatusCode = 403;
            }
            else if (File.Exists(path))
            {
                var contentType = MimeMapping.GetMimeMapping(path);
                Trace.TraceInformation("Serving '{0}' - {1}", path, contentType);
                try
                {
                    using (var file = File.OpenRead(path))
                    {
                        context.Response.ContentType = contentType;
                        context.Response.ContentLength64 = file.Length;
                        await file.CopyToAsync(context.Response.OutputStream);

                        Trace.TraceInformation("Served '{0}', {1} bytes", path, file.Length);
                    }
                }
                catch (Exception e)
                {
                    Trace.TraceError("Exception serving '{0}': {1}", path, e.Message);
                    context.Response.StatusCode = 500;
                }
            }
            else if (this.DirectoryListings && Directory.Exists(path))
            {
                Trace.TraceInformation("Directory listing '{0}'", path);

                try
                {
                    var builder = new StringBuilder();
                    builder.Append("<!DOCTYPE html><html><head>");

                    var dirInfo = new DirectoryInfo(path);
                    var dirName = dirInfo.FullName.Substring(this.Root.Length).Replace('\\', '/');
                    if (!dirName.EndsWith("/"))
                    {
                        dirName += "/";
                    }

                    builder.AppendFormat(@"<title>{0}</title>", dirName);
                    builder.Append(@"</head><body>");
                    builder.AppendFormat(@"<h2>{0}</h2>", dirName); 

                    builder.Append(@"<pre>");
                    if (path.Length > this.Root.Length)
                    {
                        var entry = dirInfo.Parent;
                        var dpath = entry.FullName.Substring(this.Root.Length).Replace('\\', '/');
                        builder.AppendFormat(
                            @"{0:yyyy-MM-dd HH:mm:ss}   &lt;DIR&gt;            <a href=""{1}/"">..</a>",
                            entry.LastWriteTime,
                            dpath).AppendLine();
                    }

                    foreach (var entry in dirInfo.EnumerateFileSystemInfos())
                    {
                        var dpath = entry.FullName.Substring(this.Root.Length).Replace('\\', '/');

                        string size;
                        string spaces;
                        var file = entry as FileInfo;
                        if (file != null)
                        {
                            size = file.Length.ToString("N0", CultureInfo.InvariantCulture);
                            spaces = "                   ".Substring(size.Length);
                        }
                        else
                        {
                            size = "";
                            spaces = "   &lt;DIR&gt;           ";
                        }

                        builder.AppendFormat(
                            @"{0:yyyy-MM-dd HH:mm:ss}{1}{2} <a href=""{3}"">{4}</a>",
                            entry.LastWriteTime,
                            spaces, size,
                            dpath,
                            entry.Name).AppendLine();
                    }
                    builder.Append(@"</pre></body></html>");
                    var output = Encoding.UTF8.GetBytes(builder.ToString());

                    context.Response.ContentLength64 = output.Length;
                    context.Response.ContentType = System.Net.Mime.MediaTypeNames.Text.Html;
                    await context.Response.OutputStream.WriteAsync(output, 0, output.Length);
                    Trace.TraceInformation("Directory '{0}', {1} bytes", path, output.Length);
                }
                catch (Exception e)
                {
                    Trace.TraceError("Exception serving directory '{0}': {1}", path, e.Message);
                    context.Response.StatusCode = 500;
                }
            }
            else
            {
                Trace.TraceWarning(
                    "Cannot find '{0}'",
                    context.Request.Url.AbsolutePath);
                context.Response.StatusCode = 404;
            }

            context.Response.Close();
        }
    }
}
