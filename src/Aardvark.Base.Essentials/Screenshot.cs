using Aardvark.Base;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Aardvark.Rendering
{

    public class HttpServer2Route
    {
        private enum Mode
        {
            Literal,
            Variable,
            Mixed,
        }
        private Http.Verb m_verb;
        private string m_route;
        private Action<HttpServerRequestInfo> m_handler;

        private string[] m_tokens;
        private Mode[] m_modes;

        public Action<HttpServerRequestInfo> Handler { get { return m_handler; } }
        public string Route { get { return m_route; } }
        public Http.Verb Verb { get { return m_verb; } }
        public bool Matches(string s, Dictionary<string, string> variables)
        {
            var matchedVariables = new List<(string, string)>();
            var tokens = s.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length != m_tokens.Length) return false;
            for (int i = 0; i < tokens.Length; i++)
            {
                switch (m_modes[i])
                {
                    case Mode.Literal:
                        if (!m_tokens[i].Equals(tokens[i])) return false;
                        break;
                    case Mode.Variable:
                        matchedVariables.Add((m_tokens[i], tokens[i]));
                        break;
                    case Mode.Mixed:
                        {
                            int start = m_tokens[i].IndexOf('[');
                            int end = m_tokens[i].IndexOf(']');
                            if (end != m_tokens[i].Length - 1) return false;
                            var pre = m_tokens[i].Left(start);
                            if (!tokens[i].StartsWith(pre)) return false;
                            matchedVariables.Add(
                                (
                                    m_tokens[i].Substring(start + 1, end - start - 1),
                                    tokens[i].Substring(pre.Length)
                                ));
                        }
                        break;
                    default:
                        throw new NotImplementedException();
                }
            }

            foreach (var mv in matchedVariables) variables[mv.Item1] = mv.Item2;
            return true;
        }

        public HttpServer2Route(string route, Http.Verb verb, Action<HttpServerRequestInfo> handler)
        {
            m_verb = verb;
            m_route = route;
            m_handler = handler;

            m_tokens = route.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            m_modes = new Mode[m_tokens.Length];
            for (int i = 0; i < m_tokens.Length; i++)
            {
                var token = m_tokens[i];
                var length = token.Length;
                if (token.Contains("["))
                {
                    if (token[0] == '[' && token[length - 1] == ']')
                    {
                        m_modes[i] = Mode.Variable;
                        m_tokens[i] = token.Substring(1, length - 2);
                    }
                    else
                    {
                        m_modes[i] = Mode.Mixed;
                    }
                }
                else
                {
                    m_modes[i] = Mode.Literal;
                }
            }
        }
    }

    public class HttpServer2Dispatcher
    {
        private List<HttpServer2Route> m_routes = new List<HttpServer2Route>();

        public void AddRoutes(params HttpServer2Route[] routes)
        {
            AddRoutes((IEnumerable<HttpServer2Route>)routes);
        }
        public void AddRoutes(IEnumerable<HttpServer2Route> routes)
        {
            m_routes.AddRange(routes);
        }
        public void AddRoute(HttpServer2Route route)
        {
            m_routes.Add(route);
        }

        public HttpServer2Dispatcher() { }
        public HttpServer2Dispatcher(params HttpServer2Route[] routes)
            : this((IEnumerable<HttpServer2Route>)routes)
        {
        }
        public HttpServer2Dispatcher(IEnumerable<HttpServer2Route> routes)
        {
            m_routes.AddRange(routes);
        }

        public HttpServer2Route Dispatch(string uri, Http.Verb verb, Dictionary<string, string> variables)
        {
            return m_routes
                .Where(r => r.Verb == verb)
                .Where(r => r.Matches(uri, variables))
                .FirstOrDefault();
        }
    }

    public class HttpServerRequestInfo
    {
        private HttpListenerContext m_context;

        private string m_head;
        private string m_route;
        private Http.Verb m_verb;
        private Dictionary<string, string> m_query = new Dictionary<string, string>();

        public string Head
        {
            get { return m_head; }
            internal set { m_head = value; }
        }
        public string Route
        {
            get { return m_route; }
            internal set { m_route = value; }
        }
        public HttpListenerContext Context
        {
            get { return m_context; }
            internal set { m_context = value; }
        }
        public Http.Verb Verb
        {
            get { return m_verb; }
            internal set { m_verb = value; }
        }
        public Dictionary<string, string> Query
        {
            get { return m_query; }
        }
        public string Get(string paramName, string defaultValue)
        {
            return m_query.ContainsKey(paramName) ? m_query[paramName] : defaultValue;
        }
        public int Get(string paramName, int defaultValue)
        {
            try
            {
                return m_query.ContainsKey(paramName) ? m_query[paramName].ToInt() : defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }
        public double Get(string paramName, double defaultValue)
        {
            try
            {
                return m_query.ContainsKey(paramName) ? m_query[paramName].ToDouble() : defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }
    }

    public class HttpServerDispatcher
    {
        private Dictionary<string, List<Func<HttpServerRequestInfo, bool>>> m_handlers =
            new Dictionary<string, List<Func<HttpServerRequestInfo, bool>>>();
        private Dictionary<string, List<HttpServerDispatcher>> m_dispatchers =
            new Dictionary<string, List<HttpServerDispatcher>>();

        public void Register(Func<HttpServerRequestInfo, bool> handler)
        {
            Register("", handler);
        }
        public void Register(string prefix, Func<HttpServerRequestInfo, bool> handler)
        {
            prefix = prefix ?? "";
            if (!m_handlers.ContainsKey(prefix))
            {
                m_handlers[prefix] = new List<Func<HttpServerRequestInfo, bool>>();
            }
            m_handlers[prefix].Add(handler);
        }

        public void Register(Http.Verb verb, Func<HttpServerRequestInfo, bool> handler)
        {
            Register("", verb, handler);
        }
        public void Register(string prefix, Http.Verb verb, Func<HttpServerRequestInfo, bool> handler)
        {
            prefix = prefix ?? "";
            prefix += "." + verb.ToString();
            Register(prefix, handler);
        }

        public void Register(HttpServerDispatcher dispatcher)
        {
            Register("", dispatcher);
        }
        public void Register(string prefix, HttpServerDispatcher dispatcher)
        {
            prefix = prefix ?? "";
            if (!m_dispatchers.ContainsKey(prefix))
            {
                m_dispatchers[prefix] = new List<HttpServerDispatcher>();
            }
            m_dispatchers[prefix].Add(dispatcher);
        }

        public IEnumerable<string> Prefixes
        {
            get
            {
                return
                    m_handlers.Keys.Select(x => x.ToString()).Concat(
                    m_dispatchers.Keys.Select(x => x.ToString())
                    );
            }
        }

        /// <summary>
        /// Returns true if route has been handled.
        /// </summary>
        internal bool Dispatch(HttpServerRequestInfo info)
        {
            string route = info.Route;
            bool routeHasBeenHandled = false;

            // 0. preconditions
            // (e.g. "/foo/bar/woohoo" -> "foo/bar/woohoo")
            if (route != null && route.Length > 0 && route[0] == '/') route = route.Substring(1);

            // 1. split route into head and tail
            string head, tail;
            int separatorIndex = route.IndexOf('/');
            if (separatorIndex < 0)
            {
                head = route;
                tail = "";
            }
            else
            {
                head = route.Substring(0, separatorIndex);
                tail = route.Substring(separatorIndex + 1);
            }

            // 2. call handlers & dispatchers
            List<Func<HttpServerRequestInfo, bool>> handlers;
            if (m_handlers.TryGetValue("", out handlers))
            {
                foreach (var handler in handlers)
                {
                    routeHasBeenHandled |= handler(info);
                }
            }

            var storeRoute = info.Route;
            var storeHead = info.Head;
            info.Route = tail;
            info.Head = head;

            if (m_handlers.TryGetValue(head, out handlers))
            {
                foreach (var handler in handlers)
                {
                    routeHasBeenHandled |= handler(info);
                }
            }
            if (m_handlers.TryGetValue(head + "." + info.Verb.ToString(), out handlers))
            {
                foreach (var handler in handlers)
                {
                    routeHasBeenHandled |= handler(info);
                }
            }

            List<HttpServerDispatcher> dispatchers;
            if (m_dispatchers.TryGetValue(head, out dispatchers))
            {
                foreach (var dispatcher in dispatchers)
                {
                    routeHasBeenHandled |= dispatcher.Dispatch(info);
                }
            }

            info.Head = storeHead;
            info.Route = storeRoute;
            return routeHasBeenHandled;
        }

        public HttpServerDispatcher()
        {
            Register("help", DefaultHelpHandler);
        }

        private bool DefaultHelpHandler(HttpServerRequestInfo info)
        {
            var items = new List<string>();
            foreach (var p in Prefixes)
            {
                if (p == null || p == "" || p == "help") continue;
                if (p.EndsWith(".GET")) { items.Add(p.Sub(0, -4)); continue; }
                if (p.EndsWith(".POST")) { items.Add(p.Sub(0, -5)); continue; }
                if (p.EndsWith(".PUT")) { items.Add(p.Sub(0, -4)); continue; }
                if (p.EndsWith(".DELETE")) { items.Add(p.Sub(0, -7)); continue; }
                items.Add(p);
            }
            info.Context.SendReply(items.Distinct().Join(", "));
            return true;
        }

    }

    public static class Http
    {
        public enum Verb
        {
            GET,
            POST,
            PUT,
            DELETE,
        }

        public static WebResponse Get(string uri, Dictionary<string, object> query)
        {
            return Send("GET", uri, query, null, null);
        }

        public static WebResponse Get(string uri)
        {
            return Send("GET", uri, null, null, null);
        }

        public static WebResponse Post(
            string uri, Dictionary<string, object> query,
            string contentType, byte[] data)
        {
            return Send("POST", uri, query, data, contentType);
        }

        public static WebResponse Post(
            string uri, Dictionary<string, object> query,
            string contentType, string data)
        {
            return Send("POST", uri, query, new UTF8Encoding().GetBytes(data), contentType);
        }

        public static WebResponse Post(
            string uri, string contentType, byte[] data)
        {
            return Send("POST", uri, null, data, contentType);
        }

        public static WebResponse Post(
            string uri, string contentType, string data)
        {
            return Send("POST", uri, null, new UTF8Encoding().GetBytes(data), contentType);
        }

        public static WebResponse Delete(string uri)
        {
            return Send("DELETE", uri, null, null, null);
        }

        public static WebResponse Put(string uri)
        {
            return Send("PUT", uri, null, null, null);
        }

        //public static WebResponse Put(
        //    string uri, Dictionary<string, object> query,
        //    string contentType, byte[] data)
        //{
        //    return Send("PUT", uri, query, data, contentType);
        //}

        //public static WebResponse Delete(
        //    string uri, Dictionary<string, object> query)
        //{
        //    return Send("DELETE", uri, query, null, null);
        //}

        public static WebResponse Send(
            string verb, string uri,
            Dictionary<string, object> query,
            byte[] postData,
            string contentType)
        {
            if (uri.Right(1) == "/") uri = uri.Substring(0, uri.Length - 1);
            if (query != null)
            {
                uri += "?" + query
                    .Select(x => string.Format("{0}={1}", x.Key, x.Value.ToString()))
                    .Join("&");
            }
            var finalUri = new Uri(uri);
            var httpRequest = (HttpWebRequest)WebRequest.Create(finalUri);
            httpRequest.Method = verb;
            if (postData != null)
            {
                httpRequest.ContentType = contentType;
                httpRequest.ContentLength = postData.Length;
                using (var s = httpRequest.GetRequestStream())
                {
                    s.Write(postData, 0, postData.Length);
                }
            }
            return httpRequest.GetResponse();
        }
    }

    public class HttpServer
    {
        private static string s_defaultPrefix = null;
        private HttpListener m_listener = new HttpListener();
        private HttpServerDispatcher m_dispatcher = new HttpServerDispatcher();
        private HttpServer2Dispatcher m_dispatcher2 = null;

        private volatile bool m_isRunning = false;
        private volatile bool m_stop = false;
        private object m_lock = new object();

        public void SwitchToNewStyle()
        {
            m_dispatcher2 = new HttpServer2Dispatcher();
        }

        /// <summary>
        /// Old-style dispatcher.
        /// </summary>
        public HttpServerDispatcher Dispatcher
        {
            get { return m_dispatcher; }
        }
        /// <summary>
        /// New-style dispatcher.
        /// </summary>
        public HttpServer2Dispatcher Dispatcher2
        {
            get { return m_dispatcher2; }
        }

        public bool IsRunning
        {
            get { return m_isRunning; }
        }

        /// <summary>
        /// Gets underlying HttpListener for fine-grained control.
        /// </summary>
        public HttpListener HttpListener
        {
            get { return m_listener; }
        }

        public void Stop()
        {
            if (!m_isRunning) return;
            m_stop = true;
            m_listener.Close();
            while (m_isRunning) ;// Report.Warn("stopping");
        }

        public void Start()
        {
            lock (m_lock)
            {
                if (m_isRunning)
                {
                    Report.Line(5, "WARNING: HttpServer already running - doing nothing");
                    return;
                }
                m_isRunning = true;
            }

            // listen on default uri if no user-specified prefix is given
            // default is: http:\\<first local ip address>:4242/
            if (m_listener.Prefixes.Count == 0)
            {
                if (s_defaultPrefix == null)
                {
                    var localIP =
                        Dns.GetHostAddresses(Dns.GetHostName())
                        .Where(x => x.AddressFamily == AddressFamily.InterNetwork)
                        .FirstOrDefault()
                        ;
                    if (localIP != null)
                    {
                        s_defaultPrefix = string.Format("http://{0}:4242/", localIP.ToString());
                    }
                    else
                    {
                        s_defaultPrefix = "http://localhost:4242/";
                    }
                }

                m_listener.Prefixes.Add(s_defaultPrefix);
            }

            try
            {
                Report.Line(4, "[starting] kernel http server");
                m_listener.Start();
                Report.Line(4, "[started ] kernel http server");
                Report.Line(4, "           listening on {0}",
                    m_listener.Prefixes.Select(x => x).Join(", ")
                    );
            }
            catch (Exception e)
            {
                // catch all - if listening fails for any reason
                // then report exception and continue without kernel http server
                Report.Line("[info] failed to listen on {0}", s_defaultPrefix);
                Report.Line("       {0}", e.ToString());

                lock (m_lock) m_isRunning = false;
                return; // do not start server loop
            }

            // http server loop
            Task.Factory.StartNew(delegate
            {
                while (!m_stop)
                {
                    try
                    {
                        var context = m_listener.GetContext();
                        if (m_stop) break;

                        #region Init HttpServerRequestInfo

                        var info = new HttpServerRequestInfo() { Context = context };

                        // init route
                        var route = context.Request.RawUrl;
                        var queryStartIndex = route.IndexOf('?');
                        if (queryStartIndex >= 0)
                        {  // cut off query part of uri
                            route = route.Substring(0, queryStartIndex);
                        }
                        if (route.Length > 0 && route.Right(1) == "/")
                        {   // cut off trailing '/'
                            route = route.Substring(0, route.Length - 1);
                        }
                        info.Route = route;

                        // init verb
                        switch (context.Request.HttpMethod)
                        {
                            case "GET": info.Verb = Http.Verb.GET; break;
                            case "POST": info.Verb = Http.Verb.POST; break;
                            case "PUT": info.Verb = Http.Verb.PUT; break;
                            case "DELETE": info.Verb = Http.Verb.DELETE; break;
                        }

                        // init query
                        var query = context.Request.QueryString;
                        for (int i = 0; i < query.Count; i++)
                        {
                            info.Query[query.GetKey(i)] = query.GetValues(i).Join(";");
                        }

                        #endregion

                        if (m_dispatcher2 == null)
                        {
                            // dispatch (old-style)
                            Task.Factory.StartNew(delegate
                            {
                                try
                                {
                                    bool handled = m_dispatcher.Dispatch(info);

                                    // if no handler matches reply "not found"
                                    if (!handled)
                                    {
                                        context.SendReply(s_notFoundReply, HttpStatusCode.NotFound);
                                    }
                                }
                                catch (Exception e)
                                {
                                    Report.Warn("HttpServer: {0}", e.ToString());
                                }
                            });
                        }
                        else
                        {
                            // dispatch new-style
                            var match = m_dispatcher2.Dispatch(info.Route, info.Verb, info.Query);
                            if (match == null)
                            {
                                Task.Factory.StartNew(delegate
                                {
                                    context.SendReply(s_notFoundReply, HttpStatusCode.NotFound);
                                });
                            }
                            else
                            {
                                Task.Factory.StartNew(delegate
                                {
                                    match.Handler(info);
                                });
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        // catch all - the server loop must not end
                        // [ISSUE 20080715 sm> handle case when the thread itself dies
                        // -> has to be resurrected by the kernel
                        Report.Line(4, "HttpServer: {0}", e.ToString());
                        Thread.Sleep(100);
                    }
                }

                m_stop = false;
                lock (m_lock) m_isRunning = false;
            });
        }

        private static string s_notFoundReply =
            new XElement("html",
                new XElement("body",
                    new XElement("div", "Not found!"),
                    new XElement("br"),
                    new XElement("div", "Copyright (c) 2008-2017. Aardvark Platform Team.")
                    )
                )
                .ToString();

        private void ReportRequestProperties(HttpListenerRequest request)
        {
            Report.Begin(4, "Request");

            // Display the MIME types that can be used in the response.
            string[] types = request.AcceptTypes;
            if (types != null)
            {
                Report.Begin(4, "acceptable mime types");
                foreach (string s in types)
                {
                    Report.Line(4, "{0}", s);
                }
                Report.End();
            }
            // Display the language preferences for the response.
            types = request.UserLanguages;
            if (types != null)
            {
                Report.Begin(4, "acceptable natural languages");
                foreach (string l in types)
                {
                    Report.Line(4, "{0}", l);
                }
                Report.End();
            }

            Report.Begin(4, "query");
            foreach (var x in request.QueryString.AllKeys)
            {
                Report.Line(4, "{0} = {1}", x.ToString(), request.QueryString[x].ToString());
            }
            Report.End();

            Report.Line(4, "url         : {0}", request.Url.OriginalString);
            Report.Line(4, "raw url     : {0}", request.RawUrl);

            Report.Line(4, "referred by : {0}", request.UrlReferrer);

            Report.Line(4, "http method : {0}", request.HttpMethod);

            Report.Line(4, "host name   : {0}", request.UserHostName);
            Report.Line(4, "host address: {0}", request.UserHostAddress);
            Report.Line(4, "user agent  : {0}", request.UserAgent);

            Report.End();
        }

    }

    public static class WebReponseExtensions
    {
        public static string GetResponseAsString(this WebResponse response)
        {
            return new StreamReader(response.GetResponseStream()).ReadToEnd();
        }

        public static byte[] GetResponseAsBytes(this WebResponse response)
        {
            return new BinaryReader(response.GetResponseStream()).ReadBytes(
                (int)response.ContentLength
                );
        }
    }

    public static class HttpListenerContextExtensions
    {
        public static void SendReply(this HttpListenerContext context)
        {
            SendReply(context, "", HttpStatusCode.OK);
        }

        public static void SendReply(this HttpListenerContext context, string message)
        {
            SendReply(context, message, HttpStatusCode.OK);
        }

        public static void SendReply(this HttpListenerContext context,
            string message, HttpStatusCode httpStatusCode)
        {
            context.Response.ContentLength64 = Encoding.UTF8.GetByteCount(message);
            context.Response.StatusCode = (int)httpStatusCode;
            using (var s = context.Response.OutputStream)
            {
                using (var writer = new StreamWriter(s))
                {
                    writer.Write(message);
                }
            }
        }

        public static void SendReply(this HttpListenerContext context, byte[] message)
        {
            SendReply(context, message, HttpStatusCode.OK);
        }

        public static void SendReply(this HttpListenerContext context,
            byte[] message, HttpStatusCode httpStatusCode)
        {
            context.Response.ContentLength64 = message.Length;
            context.Response.StatusCode = (int)httpStatusCode;
            using (var s = context.Response.OutputStream)
            {
                s.Write(message, 0, message.Length);
            }
        }
    }

    public static class Screenshot
    {
        public static string CreateImageDesktopFilename()
        {
            var now = DateTime.Now;
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                string.Format(
                    "screenshot_{0:0000}{1:00}{2:00}_{3:00}{4:00}{5:00}.png",
                    now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second
                    )
                );
        }

        public static string CreateScreenShotFilePath(string path)
        {
            var now = DateTime.Now;
            return Path.Combine(
                path,
                string.Format(
                    "screenshot_{0:0000}{1:00}{2:00}_{3:00}{4:00}{5:00}.png",
                    now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second
                    )
                );
        }

        /// <summary>
        /// Saves the image to a file on the desktop.
        /// </summary>
        public static void SaveToDesktop(PixImage image)
        {
            SaveToFile(image, CreateImageDesktopFilename());
        }

        public static void SaveToFileInPath(PixImage image, string path)
        {
            SaveToFile(image, CreateScreenShotFilePath(path));
        }

        /// <summary>
        /// Saves the image to a given filename.
        /// </summary>
        public static void SaveToFile(PixImage image, string fileName)
        {
            image.SaveAsImage(fileName, PixFileFormat.Png);
            Report.Line(5, "saved screenshot: {0}", fileName);
        }

        public static string UploadImageDataToServer(MemoryStream stream, string tags)
        {
            try
            {
                var data = stream.ToArray();

                var response = Http.Post(
                    "http://tracker.vrvis.lan:4242/Screenshots", "image/png", data);
                //"http://localhost:4242/Screenshots", "image/png", data);

                var key = response.GetResponseAsString();

                Report.Line("uploaded screenshot to server");
                Report.Line("tags: {0}", tags);


                Http.Post(String.Format(
                    "http://tracker.vrvis.lan:4242/ScreenshotTag?screenshotUri={0}", Path.GetFileName(key)),
                    //"http://localhost:4242/ScreenshotTag?screenshotUri={0}", Path.GetFileName(key)),
                    "text/xml",
                    tags);

                return key;
            }
            catch (Exception e)
            {
                // fail (almost) silently if upload does not work
                Report.Line("screenshot upload failed: {0}", e.ToString());
                return null;
            }
        }

        /// <summary>
        /// Starts the image upload and returns an image key which
        /// is generated by the server to identify the image.
        /// </summary>
        public static string UploadImageToServer(PixImage image, string tags = null)
        {
            try
            {
                using (var stream = new MemoryStream())
                {
                    image.SaveAsImage(stream, PixFileFormat.Png);

                    tags = tags ?? Environment.UserName + " " + Assembly.GetEntryAssembly().GetName().Name;
                    tags = "aardvark.rendering " + tags;

                    return UploadImageDataToServer(stream, tags);
                }
            }
            catch (Exception e)
            {
                // fail (almost) silently if upload does not work
                Report.Line("screenshot upload failed: {0}", e.ToString());
                return null;
            }
        }

        /// <summary>
        /// Uploads the meta information, which is stored in an XElement hierarchy
        /// to the server.
        /// </summary>
        public static void UploadMetaInfoToServer(XElement metaInfo)
        {
            try
            {
                byte[] data;
                using (var stream = new MemoryStream())
                {
                    var streamWriter = new StreamWriter(stream);
                    metaInfo.Save(streamWriter);
                    data = stream.ToArray();
                }

                Http.Post("http://tracker.vrvis.lan:4242/ScreenshotInfo", "text/xml", data);
                Report.Line("uploaded meta info to server");
            }
            catch (Exception e)
            {
                // fail (almost) silently if upload does not work
                Report.Line("metainfo upload failed: {0}", e.ToString());
            }
        }

        private static XElement BuildCompleteMetaInfo(XElement metaDataFromForm, string imageKey, PixImage image)
        {
            string[] parts = imageKey.Split('\\');
            string fileName = parts[3];

            var created = DateTime.Now;
            var thumbFileName = fileName + ".thumb.jpg";
            //var infoFileName = fileName + ".xml";
            var size = image.Size;

            var metaInfo =
                new XElement("ScreenshotInfo",
                    new XAttribute("Created", created.ToString()),
                    new XElement("FileName", fileName),
                    new XElement("ThumbFileName", thumbFileName),
                    new XElement("Size", size.ToString()),
                    new XElement("Tag", "",
                        new XAttribute("Created", created.ToString())),
                    new XElement("Tag", metaDataFromForm.Element("Tag").Value,
                        new XAttribute("Created", created.ToString())),
                    new XElement("Kategorie", "",
                        new XAttribute("Created", created.ToString())),
                    new XElement("Kategorie", metaDataFromForm.Element("Kategorie").Value,
                        new XAttribute("Created", created.ToString())),
                    new XElement("Comment", "Screenshot erstellt",
                        new XAttribute("Created", created.ToString())),
                    new XElement("Comment", metaDataFromForm.Element("Comment").Value,
                    new XAttribute("Created", created.ToString()))
                );

            return metaInfo;
        }

        public static void SaveAndUpload(PixImage image, bool uploadToServer, string tags = null)
        {
            // need to clone the image; original will be disposed afterwards
            Task.Factory.StartNew(delegate
            {
                // 1. save file locally
                SaveToDesktop(image);

                // 2. upload
                if (uploadToServer)
                {
                    UploadImageToServer(image, tags);
                }
            }
            //, Kernel.TaskManager.CpuBoundLowPriority
            );
        }

    }
}
