using System;
using System.Net;
using System.Threading;
using System.Text;
using System.Windows;
using System.Threading.Tasks;
using Dynamo.ViewModels;
using System.IO;
using Newtonsoft.Json;
using Dynamo.Graph.Workspaces;
using Dynamo.Models;
using System.Windows.Threading;
using Dynamo.Core;
using Dynamo.Wpf.ViewModels.Watch3D;
using Dynamo.Graph.Nodes;
using System.Collections;
using System.Diagnostics;
using System.ComponentModel;
using System.Drawing;
using System.Collections.Generic;

namespace ServerViewExtension
{
    public class HttpServer : IDisposable
    {
        private HttpListener _httpListener = null;
        private Thread _connectionThread = null;
        private Boolean _running, _disposed;
        private DynamoViewModel _viewModel;
        public RequestHelper requestHelper;
        private Window _dynamoWindow;
        private static string outputDir = "C:/Users/Arefin/Documents/ServerViewExtension/temp/";

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

            requestHelper = new RequestHelper(listenerContext, this._viewModel, this._dynamoWindow, outputDir);
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
        public List<ContextItem> Alts { get; set; }

        public ContextData(List<ContextItem> alts)
        {
            Alts = alts;
        }
    }

    public class ContextItem
    {
        public object Graph { get; set; }
        public string Id { get; set; }

        public ContextItem(object graph, string id)
        {
            Graph = graph;
            Id = id;
        }
    }

    public class GraphData
    {
        public object Graph { get; set; }
        public byte[] Image { get; set; }
        public string Id { get; set; }
        public Dictionary<string, string> Outputs { get; set; }

        public GraphData(object graph, byte[] img, string id, Dictionary<string, string> outputs)
        {
            Graph = graph;
            Image = img;
            Id = id;
            Outputs = outputs;
        }
    }

    public class ResponseData
    {
        public List<GraphData> Alts { get; set; }

        public ResponseData(List<GraphData> alts)
        {
            Alts = alts;
        }
    }

    public class RequestHelper: NotificationObject
    {
        private HttpListenerContext _context;
        private DynamoViewModel _dynamoViewModel;
        //public TaskCompletionSource<Object> graphEvaluationCompletionObject;
        public TaskCompletionSource<byte[]> snapshotRetrievalCompletionObject;
        private Window _dynamoWindow;
        private  string _outputDir;
        private HelixWatch3DViewModel _backgroundPreviewViewModel;
        private ContextItem currentRun;
        public RequestHelper(HttpListenerContext context, DynamoViewModel dynamoViewModel, Window dynamoWindow, string outputDir)
        {
            _context = context;
            _dynamoViewModel = dynamoViewModel;
            _dynamoWindow = dynamoWindow;
            _outputDir = outputDir;
        }

        void SceneItems_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "SceneItems")
            {
                var a = ((HelixWatch3DViewModel)_dynamoViewModel.BackgroundPreviewViewModel).SceneItems as ICollection;
                if (a.Count == 4)
                {
                    Save3dImage();
                }
            }
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
            _backgroundPreviewViewModel = _dynamoViewModel.BackgroundPreviewViewModel as HelixWatch3DViewModel;
            _backgroundPreviewViewModel.PropertyChanged += SceneItems_PropertyChanged;

            HttpListenerRequest request = _context.Request;
            // 1. Get the array of requested runs from the request body
            Stream body = request.InputStream;
            ContextData data = DeserializeFromStream(body);
            List<ContextItem> requestedRuns = data.Alts;

            List<GraphData> evaluatedRuns = new List<GraphData>();

            for (int i = 0; i < requestedRuns.Count; i++)
            {
                this.currentRun = requestedRuns[i];
                object graph = this.currentRun.Graph;
                string id = this.currentRun.Id;

                TaskCompletionSource<Object> graphEvaluationCompletionObject = new TaskCompletionSource<Object>();
                this.snapshotRetrievalCompletionObject = new TaskCompletionSource<byte[]>();

                object evaluatedGraphJson = EvaluateGraph(graph);
                graphEvaluationCompletionObject.SetResult(evaluatedGraphJson);

                // Prepare response
                var outputsData = graphEvaluationCompletionObject.Task.Result;
                if (outputsData != null)
                {
                    WorkspaceModel currentWorkspaceModel = _dynamoViewModel.CurrentSpace;
                    Dictionary<string, string> graphOutputs = new Dictionary<string, string>();
                    foreach (NodeModel node in currentWorkspaceModel.Nodes)
                    {
                        if (node.IsSetAsOutput)
                        {
                            graphOutputs[node.Name] = node.GetValue(0, _dynamoViewModel.Model.EngineController).StringData;
                        }
                    }

                    var snapshotData = snapshotRetrievalCompletionObject.Task.Result;
                    if (snapshotData != null)
                    {
                        GraphData graphData = new GraphData(outputsData, snapshotData, id, graphOutputs);
                        evaluatedRuns.Add(graphData);
                    }
                }
            }
            HttpListenerResponse response = this._context.Response;
            response.StatusCode = (int)HttpStatusCode.OK;
            byte[] messageBytes = Encoding.Default.GetBytes(JsonConvert.SerializeObject(new ResponseData(evaluatedRuns)));
            response.OutputStream.Write(messageBytes, 0, messageBytes.Length);
            // Send the HTTP response to the client
            response.Close();
            this.UnregisterListener();
        }

        public void Save3dImage()
        {
            _dynamoWindow.Dispatcher.Invoke(
                  DispatcherPriority.ApplicationIdle,
                  new Action(() => {
                      string path = Path.Combine(_outputDir, "output_" + this.currentRun.Id + ".bmp");
                      _dynamoViewModel.OnRequestSave3DImage(this, new ImageSaveEventArgs(path));
                      Debug.WriteLine("Geometry preview 3d image has been saved to file.");

                      Bitmap image  = new Bitmap(path);
                      ImageConverter converter = new ImageConverter();
                      var byteArray = (byte[])converter.ConvertTo(image, typeof(byte[]));

                      snapshotRetrievalCompletionObject.SetResult(byteArray);
                  }));
        }

        public object EvaluateGraph(object graph)
        {
            // 2. Save graph to json file be able to load on Dynamo
            string tempJsonFilePath = Path.Combine(_outputDir, "temp.dyn");
            using (StreamWriter file = File.CreateText(tempJsonFilePath))
            {
                JsonSerializer serializer = new JsonSerializer();
                serializer.Serialize(file, graph);
            }

            // 3. Load dynamo file
            bool evalCompleted = false;
            _dynamoWindow.Dispatcher.Invoke( new Action(() =>
            {
                // Load graph from the temp json file
                _dynamoViewModel.OpenCommand.Execute(tempJsonFilePath);
                var homeWorkspace = _dynamoViewModel.Model.CurrentWorkspace as HomeWorkspaceModel;
                Console.WriteLine("loaded file");

                EventHandler<EvaluationCompletedEventArgs> checkEvaluationCompletion = (sender, e) => { evalCompleted = true; };
                _dynamoViewModel.Model.EvaluationCompleted += checkEvaluationCompletion;
                _dynamoViewModel.HomeSpace.Run();
            }));

            while (evalCompleted == false)
            {
                Thread.Sleep(250);
            }

            _dynamoWindow.Dispatcher.Invoke(new Action(() =>
            {
                _dynamoViewModel.SaveCommand.Execute(tempJsonFilePath);
            }));

            // deserialize JSON directly from a file
            using (StreamReader file = File.OpenText(tempJsonFilePath))
            {
                JsonSerializer serializer = new JsonSerializer();
                object jsonGraph = serializer.Deserialize(file, typeof(object));
                return jsonGraph;
            }
        }

        public void UnregisterListener()
        {
            _backgroundPreviewViewModel.PropertyChanged -= SceneItems_PropertyChanged;
        }
    }
}

