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
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Makaretu.Dns;
using Newtonsoft.Json;
using QRCoder;
using FoveatedStreamingSample;

namespace FoveatedStreaming.WindowsSample
{
    /// <summary>
    /// Orchestrates the three connections required for FoveatedStreaming:
    /// <list type="bullet">
    ///   <item><see cref="BonjourConnection"/> - Advertises this PC on the local network via mDNS</item>
    ///   <item><see cref="SessionManagementConnection"/> - Handles the TCP session management protocol with Vision Pro</item>
    ///   <item><see cref="CloudXRConnection"/> - Manages the NVIDIA CloudXR streaming service</item>
    /// </list>
    /// This is the main entry point for starting and stopping streaming sessions.
    /// </summary>
    internal class ConnectionManager : SessionManagementConnectionObserver, IDisposable
    {
        public readonly string BundleID;
        public readonly IPEndPoint Endpoint;

        public delegate void SessionDisconnectRequestedEvent();
        public event SessionDisconnectRequestedEvent SessionDisconnectRequestedByClient;

        private SessionManagementConnection _sessionManagement;
        private CloudXRConnection _cloudXR;
        private BonjourConnection _bonjour;

        private MainViewModel _viewModel;
        private readonly LogBuffer _smcLogBuffer;
        private readonly LogBuffer _cloudXRLogBuffer;

        public class InvalidConfigurationException : Exception
        {
            public new string Message;

            public InvalidConfigurationException(string message)
            {
                this.Message = message;
            }
        }

        public ConnectionManager(MainViewModel viewModel, LogBuffer smcLogBuffer, LogBuffer cloudXRLogBuffer)
        {
            this.BundleID = viewModel.BundleID.Trim();
            this._viewModel = viewModel;
            this._smcLogBuffer = smcLogBuffer;
            this._cloudXRLogBuffer = cloudXRLogBuffer;

            if (BundleID.Length == 0)
            {
                throw new InvalidConfigurationException("Bundle ID is empty.");
            }

            ushort port;
            try
            {
                port = ushort.Parse(viewModel.Port.Trim());
            } catch
            {
                throw new InvalidConfigurationException("Unable to parse Port number.");
            }

            IPAddress address = IPAddress.Parse(viewModel.SelectedIPAddress);
            this.Endpoint = new IPEndPoint(address, port);

            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _viewModel.BonjourStatus = ConnectionStatus.Running;
                _viewModel.BonjourStatusText = $"Advertising at {Endpoint} for BundleID {BundleID}";

                _viewModel.CloudXRStatus = ConnectionStatus.Stopped;
                _viewModel.CloudXRStatusText = "NVStreamManager Launched.";

                _viewModel.SessionManagementStatus = ConnectionStatus.Stopped;
                _viewModel.SessionManagementStatusText = "Listening for connections.";
            });

            _sessionManagement = new SessionManagementConnection(this, Endpoint, viewModel.ForceQRCode, smcLogBuffer);
            _cloudXR = new CloudXRConnection(cloudXRLogBuffer);
            _bonjour = new BonjourConnection(BundleID, Endpoint);

            _cloudXR.StateChanged += this.OnCloudXRStateChange;
        }

        public void Dispose()
        {
            _cloudXR?.Dispose();
            _bonjour?.Dispose();
            _sessionManagement?.Dispose();

            _cloudXR = null;
            _bonjour = null;
            _sessionManagement = null;
        }

        public async Task DisposeAsync()
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _viewModel.BonjourStatus = _viewModel.CloudXRStatus = _viewModel.SessionManagementStatus = ConnectionStatus.Paused;
                _viewModel.BonjourStatusText = _viewModel.CloudXRStatusText = _viewModel.SessionManagementStatusText = "Disconnecting...";
                _viewModel.QRImageSource = null;
            });

            if (_cloudXR != null)
            {
                _cloudXR.StateChanged -= this.OnCloudXRStateChange;
            }

            _cloudXR?.Dispose();
            _bonjour?.Dispose();

            if (_sessionManagement != null)
            {
                await _sessionManagement.DisposeAsync();
            }

            _cloudXR = null;
            _bonjour = null;
            _sessionManagement = null;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _viewModel.BonjourStatus = _viewModel.CloudXRStatus = _viewModel.SessionManagementStatus = ConnectionStatus.Stopped;
                _viewModel.BonjourStatusText = _viewModel.CloudXRStatusText = _viewModel.SessionManagementStatusText = "Disconnected";
            });
        }

        private static BitmapImage GenerateBarcodeImage(BarcodePayload barcode)
        {
            var data = new { token = barcode.clientToken, digest = barcode.certificateFingerprint };
            string payload = JsonConvert.SerializeObject(data);

            var qrGenerator = new QRCodeGenerator();
            var qrPayload = qrGenerator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.L);

            var pngQRCode = new PngByteQRCode(qrPayload);
            var pngBytes = pngQRCode.GetGraphic(20);

            // Convert PNG bytes directly to WPF BitmapImage
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = new MemoryStream(pngBytes);
            bitmap.EndInit();

            bitmap.Freeze();

            return bitmap;
        }

        public override async Task BarcodePresentationRequested(SessionInformation session)
        {
            BitmapImage barcodeImage = GenerateBarcodeImage(session.barcode);

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _viewModel.QRImageSource = barcodeImage;
            });
        }

        public override async Task ConnectionErrorOccurred(string message)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _viewModel.SessionManagementStatus = ConnectionStatus.Stopped;
                _viewModel.SessionManagementStatusText = $"ERROR: {message}";
            });
        }

        public override BarcodePayload GenerateBarcode(string clientID)
        {
            return _cloudXR.GenerateBarcode(clientID);
        }

        public override async Task SessionStatusDidChange(SessionInformation session, string sessionStatus, CancellationToken cancellationToken)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                // The QR code image can always be dismissed when session status is updated.
                _viewModel.QRImageSource = null;

                // Update text at bottom of the screen to current status.
                _viewModel.SessionManagementStatusText = sessionStatus;

                // Update Red/Yellow/Green indicator.
                switch (sessionStatus) {
                    case SessionStatus.Waiting:
                    case SessionStatus.Connecting:
                    case SessionStatus.Paused:
                        _viewModel.SessionManagementStatus = ConnectionStatus.Paused;
                        break;
                    case SessionStatus.Connected:
                        _viewModel.SessionManagementStatus = ConnectionStatus.Running;
                        break;
                    case SessionStatus.Disconnected:
                        _viewModel.SessionManagementStatus = ConnectionStatus.Stopped;
                        _viewModel.SessionManagementStatusText = "Listening for connections.";
                        break;
                }
            });

            if (sessionStatus == SessionStatus.Waiting)
            {
                // If the session status is WAITING, this means the Vision Pro is waiting for the game to start.
                // So, start the CloudXR session and then send the MediaStreamIsReady message.
                await _cloudXR.UpdateCloudXRPresentation(true);

                await _sessionManagement.MediaStreamIsReady(cancellationToken);
            } else if (sessionStatus == SessionStatus.Disconnected)
            {
                SessionDisconnectRequestedByClient?.Invoke();
            }
        }

        private async Task OnCloudXRStateChange(CloudXRConnection.State newState)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (newState.GameIsConnected && newState.CloudXRClientIsConnected && newState.OpenXRRuntimeIsRunning)
                {
                    _viewModel.CloudXRStatus = ConnectionStatus.Running;
                    _viewModel.CloudXRStatusText = "OpenXR Game is connected and streaming to Vision Pro.";
                }
                else
                {
                    string oxrGameConnected = newState.GameIsConnected ? "Connected" : "Disconnected";
                    string oxrRuntimeRunning = newState.OpenXRRuntimeIsRunning ? "Running" : "Stopped";
                    string cxrClientConnected = newState.CloudXRClientIsConnected ? "Connected" : "Disconnected";

                    _viewModel.CloudXRStatusText = $"OpenXR Game: {oxrGameConnected}; OpenXR Runtime: {oxrRuntimeRunning}; CloudXR Client: {cxrClientConnected}";
                    if (newState.GameIsConnected || newState.CloudXRClientIsConnected || newState.OpenXRRuntimeIsRunning)
                    {
                        _viewModel.CloudXRStatus = ConnectionStatus.Paused;
                    }
                    else
                    {
                        _viewModel.CloudXRStatus = ConnectionStatus.Stopped;
                    }
                }
            });
        }
    }
}
