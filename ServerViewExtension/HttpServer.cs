using System;
using System.Net;
using System.Threading;
using System.Text;
using System.Windows;
using System.Threading.Tasks;
using Dynamo.ViewModels;
using System.IO;
using Dynamo.Controls;
using Newtonsoft.Json;

namespace ServerViewExtension
{
    public class HttpServer : IDisposable
    {
        private HttpListener _httpListener = null;
        private Thread _connectionThread = null;
        private Boolean _running, _disposed;
        private DynamoViewModel _viewModel;
        public RequestHelper requestHelper;
        public static int stepCounter = 0;
        public static int episodeCounter = 0;
        private Window _dynamoWindow;

        public HttpServer(DynamoViewModel viewModel, Window dynamoWindow)
        {
            this._viewModel = viewModel;
            this._dynamoWindow = dynamoWindow;

            if (!HttpListener.IsSupported)
            {
                // Requires at least a Windows XP with Service Pack 2
                throw new NotSupportedException("The Http Server cannot run on this operating system.");
            }
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add("http://localhost:8080/");
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

            requestHelper = new RequestHelper(listenerContext, this._viewModel, this._dynamoWindow);
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

    public class ContextData
    {
        public object Graph { get; set; }

        public ContextData(object graph)
        {
            Graph = graph;
        }
    }

    public class RequestHelper
    {
        private HttpListenerContext _context;
        private DynamoViewModel _dynamoViewModel;
        public TaskCompletionSource<Object> completionObject;
        private Window _dynamoWindow;
        public const string BackgroundPreviewName = "BackgroundPreview";
        internal Watch3DView BackgroundPreview { get; private set; }

        public RequestHelper(HttpListenerContext context, DynamoViewModel dynamoViewModel, Window dynamoWindow)
        {
            _context = context;
            _dynamoViewModel = dynamoViewModel;
            _dynamoWindow = dynamoWindow;
        }

        public static ContextData DeserializeFromStream(Stream stream)
        {
            var serializer = new JsonSerializer();

            using (var sr = new StreamReader(stream))
            using (var jsonTextReader = new JsonTextReader(sr))
            {
                return (ContextData)serializer.Deserialize(jsonTextReader, typeof(ContextData));
            }
        }

        public void ExecuteAndSendResponse()
        {
            completionObject = new TaskCompletionSource<Object>();
            HttpListenerRequest request = _context.Request;
            
            executeAction(request);

            // Prepare response
            var data = completionObject.Task.Result;
            if (data != null)
            {
                HttpListenerResponse response = this._context.Response;
                response.StatusCode = (int)HttpStatusCode.OK;

                string message = "saved";
                byte[] messageBytes = Encoding.Default.GetBytes(message);
                response.OutputStream.Write(messageBytes, 0, messageBytes.Length);

                // Send the HTTP response to the client
                response.Close();
            }
        }

        public void executeAction(HttpListenerRequest request)
        {
            var evalComplete = false;

            // Get graph json out of response body
            Stream body = request.InputStream;
            ContextData jsonResult = DeserializeFromStream(body);
            object graph = jsonResult.Graph;

            // TODO: 
            // Set the inputs according to requests
            // updatedGraph = self.set_workspace_inputs(graph, inputs)

            // Save graph to json file be able to load on Dynamo
            string tempJsonFilePath = "C:/Users/toulkev/dev/temp.json";
            using (StreamWriter file = File.CreateText(tempJsonFilePath))
            {
                JsonSerializer serializer = new JsonSerializer();
                serializer.Serialize(file, graph);
            }

            // Load graph on Dynamo
            _dynamoViewModel.Model.OpenFileFromPath(tempJsonFilePath, true);
            Console.WriteLine("loaded file");
            _dynamoViewModel.Model.EvaluationCompleted += (o, args) => { evalComplete = true; };
            while (evalComplete == false)
            {
                Thread.Sleep(250);
            }

            // Example code for grabbing a snaphsot
            _dynamoWindow.Dispatcher.BeginInvoke(new System.Action(() =>
            {
                string path = Path.Combine("C:/Users/toulkev/dev", "output.png");
                _dynamoViewModel.OnRequestSave3DImage(this, new ImageSaveEventArgs(path));
                completionObject.SetResult(true);
            }));
        }
    }
}

