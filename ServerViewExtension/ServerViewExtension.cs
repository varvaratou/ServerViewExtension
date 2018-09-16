﻿using System;
using System.Collections.Generic; 
using System.Windows.Controls;
using Dynamo.Wpf.Extensions;
using Dynamo.ViewModels;
using Dynamo.Graph.Nodes; 
using System.Windows;


namespace ServerViewExtension
{
    /// <summary>
    /// The View Extension framework for Dynamo allows you to extend
    /// the Dynamo UI by registering custom MenuItems. A ViewExtension has 
    /// two components, an assembly containing a class that implements 
    /// IViewExtension, and an ViewExtensionDefintion xml file used to 
    /// instruct Dynamo where to find the class containing the
    /// IViewExtension implementation. The ViewExtensionDefinition xml file must
    /// be located in your [dynamo]\viewExtensions folder.
    /// 
    /// This sample demonstrates an IViewExtension implementation which 
    /// shows a modeless window when its MenuItem is clicked. 
    /// The Window created tracks the number of nodes in the current workspace, 
    /// by handling the workspace's NodeAdded and NodeRemoved events.
    /// </summary>
    public class ServerViewExtension : IViewExtension
    {
        private MenuItem sampleMenuItem;
        private HttpServer server;
        private Window dynamoWindow;
        private DynamoViewModel dynamoViewModel;
        private IEnumerable<NodeModel> nodes; 
 

        public void Dispose()
        {
        }

        public void Startup(ViewStartupParams p)
        {
        }

        public void Loaded(ViewLoadedParams p)
        {
            // Save a reference to your loaded parameters.
            // You'll need these later when you want to use
            // the supplied workspaces
            this.dynamoViewModel = p.DynamoWindow.DataContext as DynamoViewModel;
            this.dynamoWindow = p.DynamoWindow;
            this.nodes = p.CurrentWorkspaceModel.Nodes; 

            sampleMenuItem = new MenuItem { Header = "Show Server View Extension Sample Window" };
            sampleMenuItem.Click += (sender, args) =>
            {
                var viewModel = new MainWindowViewModel(p);
                this.server = new HttpServer(p, this.dynamoViewModel, this.dynamoWindow);
                this.server.Start();

                var window = new MainWindow
                {
                    // Set the data context for the main grid in the window.
                    MainGrid = { DataContext = viewModel },

                    // Set the owner of the window to the Dynamo window.
                    Owner = p.DynamoWindow
                };

                window.Left = window.Owner.Left + 400;
                window.Top = window.Owner.Top + 200;

                // Show a modeless window.
                window.Show();
            };
            p.AddMenuItem(MenuBarType.View, sampleMenuItem);

        }

        public void Shutdown()
        {
        }

        public string UniqueId
        {
            get
            {
                return Guid.NewGuid().ToString();
            }
        }

        public string Name
        {
            get
            {
                return "Sample View Extension";
            }
        }

    }
}
