//===----------------------------------------------------------------------===//
// Copyright © 2026 Apple Inc.
//
// Licensed under the MIT license (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// https://github.com/apple/StreamingSession/LICENSE
//
//===----------------------------------------------------------------------===//

using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Makaretu.Dns;
using System.Collections.ObjectModel;
using Newtonsoft.Json;
using QRCoder;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using FoveatedStreamingSample;

namespace FoveatedStreaming.WindowsSample
{
    public partial class MainWindow : Window
    {
        private ConnectionManager _ConnectionManager;
        private LogWindow _smcLogWindow;
        private LogWindow _cloudXRLogWindow;

        // Persistent log buffers that survive disconnect/reconnect cycles
        private readonly LogBuffer _smcLogBuffer = new LogBuffer();
        private readonly LogBuffer _cloudXRLogBuffer = new LogBuffer();

        private MainViewModel _MainViewModel
        {
            get
            {
                return DataContext as MainViewModel;
            }
        }

        public MainWindow()
        {
            InitializeComponent();

            // Register event handlers for when the window loads and closes
            this.Loaded += MainWindow_Loaded;
            this.Closed += MainWindow_Closed;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Initialize the local IP selection list.
            var localIPAddresses = MulticastService.GetIPAddresses();
            var localIPAddressStrings = localIPAddresses.Select(x => x.ToString());
            _MainViewModel.LocalIPAddresses = new ObservableCollection<string>(localIPAddressStrings);

            // Prefer an IPv4 address.  If none exist, select the first one.
            var ipv4Address = localIPAddresses.First((ipString) => ipString.AddressFamily == AddressFamily.InterNetwork);
            _MainViewModel.SelectedIPAddress = ipv4Address?.ToString() ?? localIPAddresses.First()?.ToString() ?? "";
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            _smcLogWindow?.Close();
            _cloudXRLogWindow?.Close();
            _ConnectionManager?.Dispose();
            _ConnectionManager = null;
        }

        private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (_ConnectionManager == null)
            {
                return;
            }

            _MainViewModel.DisplayDisconnectButton = false;
            _MainViewModel.DisplayStartPanel = false;

            await _ConnectionManager.DisposeAsync();
            _ConnectionManager = null;

            _MainViewModel.DisplayDisconnectButton = false;
            _MainViewModel.DisplayStartPanel = true;
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (_ConnectionManager != null)
            {
                MessageBox.Show("Tried to start a connection when a connection already exists.",
                                "Connect Error",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                return;
            }

            _MainViewModel.DisplayDisconnectButton = false;
            _MainViewModel.DisplayStartPanel = false;

            try
            {
                _ConnectionManager = new ConnectionManager(_MainViewModel, _smcLogBuffer, _cloudXRLogBuffer);
                _ConnectionManager.SessionDisconnectRequestedByClient += () =>
                {
                    Application.Current.Dispatcher.InvokeAsync(async () =>
                    {
                        if (_ConnectionManager != null)
                        {
                            _MainViewModel.DisplayDisconnectButton = false;
                            _MainViewModel.DisplayStartPanel = false;

                            await _ConnectionManager.DisposeAsync();
                            _ConnectionManager = null;

                            _MainViewModel.DisplayDisconnectButton = false;
                            _MainViewModel.DisplayStartPanel = true;
                        }
                        ConnectButton_Click(null, null);
                     });
                };
            } catch (ConnectionManager.InvalidConfigurationException ex)
            {
                MessageBox.Show("Invalid Configuration: " + ex.Message,
                                "Connect Error",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                _ConnectionManager = null;
                _MainViewModel.DisplayStartPanel = true;
                return;
            } catch (Exception ex)
            {
                MessageBox.Show("Unhandled Exception: " + ex.Message + "\nStack Trace:\n" + ex.StackTrace,
                                "Connect Error",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                _ConnectionManager = null;
                _MainViewModel.DisplayStartPanel = true;
                return;
            }

            _MainViewModel.DisplayDisconnectButton = true;
            _MainViewModel.DisplayStartPanel = false;
        }

        private void SMCLogButton_Click(object sender, RoutedEventArgs e)
        {
            if (_smcLogWindow == null || !_smcLogWindow.IsLoaded)
            {
                _smcLogWindow = new LogWindow("SMC Log", _smcLogBuffer);
            }
            _smcLogWindow.Show();
            _smcLogWindow.Activate();
        }

        private void CloudXRLogButton_Click(object sender, RoutedEventArgs e)
        {
            if (_cloudXRLogWindow == null || !_cloudXRLogWindow.IsLoaded)
            {
                _cloudXRLogWindow = new LogWindow("CloudXR Log", _cloudXRLogBuffer);
            }
            _cloudXRLogWindow.Show();
            _cloudXRLogWindow.Activate();
        }
    }
}
