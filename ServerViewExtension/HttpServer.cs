using System;
using System.Net;
using System.Threading;
using System.Text;
using Newtonsoft.Json;
using System.Windows;
using System.Threading.Tasks;
using System.Diagnostics;

namespace ServerViewExtension
{
    public class HttpServer : IDisposable
    {
        private HttpListener _httpListener = null;
        private Thread _connectionThread = null;
        private Boolean _running, _disposed;
        private Window _dynamoWindow;
        public RequestHelper requestHelper;
        public static int stepCounter = 0;
        public static int episodeCounter = 0;

        public HttpServer(string prefix, Window dynamoWindow)
        {
            this._dynamoWindow = dynamoWindow;

            if (!HttpListener.IsSupported)
            {
                // Requires at least a Windows XP with Service Pack 2
                throw new NotSupportedException("The Http Server cannot run on this operating system.");
            }
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add(prefix);
        }

        public void Start()
        {
            if (!_httpListener.IsListening)
            {
                _httpListener.Start();
                _running = true;
                _connectionThread = new Thread(new ThreadStart(this.ConnectionThreadStart));
                _connectionThread.Start();
            }
        }

        public void Stop()
        {
            if (_httpListener.IsListening)
            {
                _running = false;
                _httpListener.Stop();
            }
        }

        private void ConnectionThreadStart()
        {
            try
            {
                while (_running)
                {
                    HttpListenerContext context = _httpListener.GetContext();
                    HandleContext(context);
                }
            }
            catch (HttpListenerException)
            {
                Console.WriteLine("HTTP server was shut down.");
            }
        }

        public void HandleContext(HttpListenerContext listenerContext)
        {
            HttpListenerRequest request = listenerContext.Request;
            string requestHandlerName = request.Url.AbsolutePath;

            requestHelper = new RequestHelper(listenerContext, this._dynamoWindow);
            requestHelper.ExecuteAndSendResponse();
        }

        public virtual void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (this._disposed)
            {
                return;
            }
            if (disposing)
            {
                if (this._running)
                {
                    this.Stop();
                }
                if (this._connectionThread != null)
                {
                    this._connectionThread.Abort();
                    this._connectionThread = null;
                }
            }
            this._disposed = true;
        }
    }

    public class RequestHelper
    {
        private HttpListenerContext _context;
        private Window _dynamoWindow;
        public TaskCompletionSource<Object> completionObject;

        public RequestHelper(HttpListenerContext context, Window dynamoWindow)
        {
            _context = context;
            _dynamoWindow = dynamoWindow;
        }

        public void ExecuteAndSendResponse()
        {
            completionObject = new TaskCompletionSource<Object>();

            HttpListenerRequest request = _context.Request;
            executeAction(request);

            var data = completionObject.Task.Result;
            if (data != null)
            {
                HttpListenerResponse response = this._context.Response;
                response.StatusCode = (int)HttpStatusCode.OK;
                string message = JsonConvert.SerializeObject(data);
                byte[] messageBytes = Encoding.Default.GetBytes(message);
                response.OutputStream.Write(messageBytes, 0, messageBytes.Length);

                // Send the HTTP response to the client
                response.Close();
            }
        }

        public void executeAction(HttpListenerRequest request)
        {

        }
    }
}

