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
using System.Collections.Generic;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup.Localizer;
using Microsoft.Win32;
using Newtonsoft.Json;
using FoveatedStreamingSample;

namespace FoveatedStreaming.WindowsSample
{
    /// <summary>
    /// Session status values per the FoveatedStreaming protocol.
    /// These are sent by the Vision Pro to indicate the current session state.
    /// </summary>
    public static class SessionStatus
    {
        /// <summary>Ready to connect, awaiting MediaStreamIsReady message from PC.</summary>
        public const string Waiting = "WAITING";
        /// <summary>Streaming provider is connecting to the remote endpoint.</summary>
        public const string Connecting = "CONNECTING";
        /// <summary>Streaming provider is connected and actively streaming.</summary>
        public const string Connected = "CONNECTED";
        /// <summary>Device doffed - connection suspended but recoverable without re-pairing.</summary>
        public const string Paused = "PAUSED";
        /// <summary>Session fully disconnected - game should quit or reset.</summary>
        public const string Disconnected = "DISCONNECTED";
    }

    /// <summary>
    /// Protocol constants for the FoveatedStreaming session management protocol.
    /// </summary>
    public static class ProtocolConstants
    {
        /// <summary>Current protocol version supported by this implementation.</summary>
        public const string SupportedProtocolVersion = "1";
        /// <summary>Size in bytes of the message length prefix.</summary>
        public const int MessageLengthPrefixBytes = 4;
        /// <summary>TXT record key for the app bundle identifier in Bonjour advertisements.</summary>
        public const string BundleIdKey = "Application-Identifier";
    }

    /// <summary>
    /// Contains information about the current streaming session.
    /// </summary>
    internal struct SessionInformation
    {
        /// <summary>Unique identifier for this streaming session.</summary>
        public string sessionID;
        /// <summary>Unique identifier for the Vision Pro device (persists across sessions).</summary>
        public string clientID;
        /// <summary>QR code payload for pairing (contains client token and certificate fingerprint).</summary>
        public BarcodePayload barcode;
    }

    /// <summary>
    /// Observer interface for receiving session management events.
    /// Implement this to respond to connection state changes and pairing requests.
    /// </summary>
    internal abstract class SessionManagementConnectionObserver
    {
        protected SessionManagementConnectionObserver() { }

        /// <summary>Generate a QR code payload for pairing with the given client.</summary>
        public abstract BarcodePayload GenerateBarcode(string clientID);

        /// <summary>Called when the session status changes (WAITING, CONNECTING, CONNECTED, PAUSED, DISCONNECTED).</summary>
        public abstract Task SessionStatusDidChange(SessionInformation session, string sessionStatus, CancellationToken cancellationToken);

        /// <summary>Called when the Vision Pro requests a QR code to be displayed for pairing.</summary>
        public abstract Task BarcodePresentationRequested(SessionInformation session);

        /// <summary>Called when a connection error occurs.</summary>
        public abstract Task ConnectionErrorOccurred(string message);
    }

    /// <summary>
    /// TCP server implementing Apple's session management protocol for FoveatedStreaming.
    ///
    /// This connection is established before the CloudXR streaming provider and handles:
    /// <list type="bullet">
    ///   <item>Initial handshake and protocol version negotiation</item>
    ///   <item>Device pairing via QR code (exchanging client tokens and certificate fingerprints)</item>
    ///   <item>Session state tracking (WAITING, CONNECTING, CONNECTED, PAUSED, DISCONNECTED)</item>
    /// </list>
    ///
    /// Messages are length-prefixed JSON over TCP (4-byte little-endian length + UTF-8 JSON payload).
    /// See the protocol specification for full details.
    /// </summary>
    internal class SessionManagementConnection: IDisposable
    {
        private readonly IPEndPoint _endpoint;
        private readonly bool _forceBarcode;
        private readonly Task listeningTask;
        private readonly CancellationTokenSource _listeningTaskCancellationTokenSource;

        private NetworkStream _currentStream;
        private TcpListener _currentListener;
        private TcpClient _currentClient;

        private SessionInformation? _currentSessionStorage;
        private bool _DidDispose;
        private readonly object _currentSessionLock = new object();

        /// <summary>Log buffer for diagnostic logging of SMC events.</summary>
        public LogBuffer LogBuffer { get; }

        private SessionInformation? currentSession
        {
            get {
                lock (_currentSessionLock) {
                    return _currentSessionStorage;
                }
            }
            set
            {
                lock (_currentSessionLock)
                {
                    _currentSessionStorage = value;
                }
            }
        }

        private readonly SessionManagementConnectionObserver _observer;

        public SessionManagementConnection(SessionManagementConnectionObserver observer, IPEndPoint endpoint, bool forceBarcode, LogBuffer logBuffer)
        {
            this._endpoint = endpoint;
            this._observer = observer;
            this._forceBarcode = forceBarcode;
            this.LogBuffer = logBuffer;

            _listeningTaskCancellationTokenSource = new CancellationTokenSource();
            this.listeningTask = Task.Run(async () => await startListening(_listeningTaskCancellationTokenSource.Token));
        }

        public async Task DisposeAsync()
        {
            if (!_DidDispose)
            {
                if (!_listeningTaskCancellationTokenSource.IsCancellationRequested)
                {
                    // If we can't disconnect in 3 seconds, cancel.
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(TimeSpan.FromSeconds(3));

                        if (!_listeningTaskCancellationTokenSource.IsCancellationRequested)
                        {
                            _listeningTaskCancellationTokenSource.Cancel();
                        }
                    });

                    await RequestDisconnect(_listeningTaskCancellationTokenSource.Token);
                    Dispose();
                    await listeningTask;
                }
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_DidDispose)
            {
                _currentClient?.Close();

                if (disposing)
                {
                    _currentStream?.Dispose();
                    _currentClient?.Dispose();
                }

                if (!_listeningTaskCancellationTokenSource.IsCancellationRequested)
                {
                    _listeningTaskCancellationTokenSource.Cancel();
                }

                _currentListener?.Stop();

                _DidDispose = true;
            }
        }

        /// <summary>
        /// Checks if a specified TCP port is currently in use.
        /// </summary>
        /// <param name="port">The port number to check.</param>
        /// <returns>True if the port is in use, false otherwise.</returns>
        private static bool IsTcpPortInUse(int port)
        {
            IPGlobalProperties ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
            IPEndPoint[] tcpListeners = ipGlobalProperties.GetActiveTcpListeners();

            // Check active listeners
            if (tcpListeners.Any(endPoint => endPoint.Port == port))
            {
                return true;
            }

            // Check active connections (for ports that might be used as local endpoints but not "listening" in the server sense)
            TcpConnectionInformation[] tcpConnections = ipGlobalProperties.GetActiveTcpConnections();
            if (tcpConnections.Any(conn => conn.LocalEndPoint.Port == port))
            {
                return true;
            }

            return false;
        }

        private async Task startListening(CancellationToken cancellationToken)
        {
            if (IsTcpPortInUse(_endpoint.Port))
            {
                Console.WriteLine($"[SessionManagementConnection] WARNING: Port {_endpoint.Port} is already used on this machine.  ");
            }

            _currentListener = new TcpListener(_endpoint);
            _currentListener.Server.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
            _currentListener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _currentListener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.DontLinger, true);
            _currentListener.Start();

            // Call listener.Stop() when the task is cancelled.
            var stopOnCancel = cancellationToken.Register(() => _currentListener.Stop());

            Console.WriteLine($"[TCP] Listening endpoint {_endpoint}...");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    using (TcpClient client = await _currentListener.AcceptTcpClientAsync())
                    using (NetworkStream stream = client.GetStream())
                    {
                        Console.WriteLine($"[TCP] Client connected: {client.Client.RemoteEndPoint}");
                        LogBuffer.Add($"[CONNECT] {client.Client.RemoteEndPoint}");

                        this._currentStream = stream;
                        this._currentClient = client;

                        while (!cancellationToken.IsCancellationRequested && _currentClient.Connected)
                        {
                            bool success = await RespondToMessage(client, cancellationToken);
                            if (!success)
                            {
                                break;
                            }
                        }

                        this._currentClient.Close();
                        LogBuffer.Add("[DISCONNECT]");
                    }
                }
                catch (Exception ex)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        Console.WriteLine($"[TCP Error] {ex.Message}");
                        LogBuffer.Add($"[ERROR] {ex.Message}");
                    }
                }
                finally
                {
                    this._currentStream = null;
                    this._currentClient = null;
                }
            }

            _currentListener.Stop();
            stopOnCancel.Dispose();
        }

        private async Task<bool> RespondToMessage(TcpClient client, CancellationToken cancellationToken)
        {
            if (this._currentStream == null)
            {
                return false;
            }

            string message;
            try
            {
                message = await ReadJsonAsync(_currentStream, cancellationToken);
            }
            catch (SocketException)
            {
                // The message failed to read due to a closed connection (this is considered normal - don't spawn a popup).
                return false;
            }
            catch (Exception e)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    // The message failed to read due to a cancellation request.
                    return false;
                } else
                {
                    // The message failed to read due to a dropped connection.
                    // This is an unexpected event - perhaps, the Vision Pro ran out of battery or there was a network failure.
                    await _observer.ConnectionErrorOccurred($"Connection Failure.  Error: {e.Message}");
                    MessageBox.Show("TCP Response Failure: " + e.Message + "\nType: " + e.GetType().Name + "\nStack Trace:\n" + e.StackTrace,
                                $"TCP Response Error (Connected: {client.Connected})",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                    return false;
                }
            }

            var dynamicJson = JsonConvert.DeserializeObject<dynamic>(message);
            string eventType = dynamicJson?.Event?.ToString();
            string sessionID = dynamicJson?.SessionID;

            if (eventType == null || sessionID == null)
            {
                // Malformed event.  Ignore.
                return true;
            }

            if (this.currentSession?.sessionID != sessionID && eventType != "RequestConnection")
            {
                // Only accept session events with the active session ID.
                await SendDisconnectMessage(sessionID, cancellationToken);

                // Don't disconnect the connection in this case.
                return true;
            }

            switch(eventType)
            {
                case "SessionStatusDidChange":
                    var statusMsg = JsonConvert.DeserializeObject<SessionStatusDidChangeMessage>(message);
                    await _observer.SessionStatusDidChange(this.currentSession.Value, statusMsg.Status, cancellationToken);
                    break;
                case "RequestBarcodePresentation":
                    await _observer.BarcodePresentationRequested(this.currentSession.Value);

                    // Send AcknowledgeBarcodePresentation
                    // Upon receiving this, AVP will present the QR code scanning UI.
                    var ackBarcode = new AcknowledgeBarcodePresentationMessage
                    {
                        SessionID = this.currentSession.Value.sessionID
                    };
                    await SendJsonAsync(_currentStream, JsonConvert.SerializeObject(ackBarcode), cancellationToken);
                    break;
                case "RequestConnection":
                    var requestConnectionMessage = JsonConvert.DeserializeObject<RequestConnectionMessage>(message);

                    // Only accept connections when we don't already have a session.
                    if (this.currentSession != null)
                    {
                        await SendDisconnectMessage(sessionID, cancellationToken);
                        return true;
                    }

                    // Check that the protocol version matches.
                    // This sample code only supports version 1.
                    if (requestConnectionMessage.ProtocolVersion != ProtocolConstants.SupportedProtocolVersion)
                    {
                        await SendDisconnectMessage(sessionID, cancellationToken);
                        await _observer.ConnectionErrorOccurred("Received requestConnectionMessage with incompatible protocol version.");
                        return false;
                    }

                    // Generate the contents of a QR code, in case we need to display it later.
                    //
                    // The QR code contents include:
                    // - The Client Token, which is derived from the ClientID which was sent by the AVP.
                    // - The Certificate Fingerprint, which is a hash of the certificate used in CloudXR's streaming connection.
                    //
                    // Both the client token and certificate fingerprint are generated by CloudXR's streaming manager, using the ClientID as input.
                    BarcodePayload barcode = _observer.GenerateBarcode(requestConnectionMessage.ClientID);

                    currentSession = new SessionInformation
                    {
                        clientID = requestConnectionMessage.ClientID,
                        sessionID = requestConnectionMessage.SessionID,
                        barcode = barcode
                    };

                    // Send the Server ID, Session ID, and Certificate fingerprint back.
                    //
                    // The AVP will consult these values when it decides if the devices are paired or not.
                    // If the certificate fingerprint stored in the AVP's keychain matches this one, then we are paired.
                    // Otherwise, we are not paired and the AVP will send the "RequestBarcodePresentation" message.
                    // 
                    // We set the value of CertificateFingerprint to `null` when we want to ask the AVP to always scan barcodes (useful for debugging).
                    var ackConn = new AcknowledgeConnectionMessage
                    {
                        SessionID = requestConnectionMessage.SessionID,
                        ServerID = getServerID(),
                        CertificateFingerprint = _forceBarcode ? null : barcode.certificateFingerprint
                    };

                    // Specifies that when CertificateFingerprint is `null`, we should omit the key "CertificateFingerprint".
                    // (as opposed to setting the value to the JSON literal `null`)
                    var settings = new JsonSerializerSettings
                    {
                        NullValueHandling = NullValueHandling.Ignore
                    };

                    string ackConnMessage = JsonConvert.SerializeObject(ackConn, settings);
                    await SendJsonAsync(_currentStream, ackConnMessage, cancellationToken);
                    break;
            }

            return true;
        }

        private async Task SendDisconnectMessage(string sessionID, CancellationToken cancellationToken)
        {
            // If we are sending a disconnect message about the current session, we should clear the session.
            lock (_currentSessionLock)
            {
                if (sessionID == currentSession?.sessionID)
                {
                    currentSession = null;
                }
            }
            
            if (_currentStream == null)
            {
                return;
            }

            var disconnectMsg = new RequestSessionDisconnectMessage
            {
                SessionID = sessionID
            };

            string jsonMessage = JsonConvert.SerializeObject(disconnectMsg);
            await SendJsonAsync(_currentStream, jsonMessage, cancellationToken);
        }

        private async Task ReadBytesAsync(NetworkStream stream, byte[] buffer, int offset, int total, CancellationToken cancellationToken)
        {
            for (int bytesRead = 0; bytesRead < total; /*empty*/)
            {
                Console.WriteLine($"Calling Stream.ReadAsync(buffer: {buffer}, offset + bytesRead: {offset + bytesRead}, total - bytesRead: {total - bytesRead})");
                var got = await stream.ReadAsync(buffer, offset + bytesRead, total - bytesRead, cancellationToken);
                Console.WriteLine($"Stream.ReadAsync got {got}");

                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException();
                } else if (got <= 0)
                {
                    throw new SocketException(); // Connection error
                }
                bytesRead += got;
            }
        }

        private async Task<string> ReadJsonAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            // First 4 bytes contain the payload length in little endian.
            // Read the exact number of bytes mentioned 
            byte[] messageLengthBytes = new byte[4];
            await ReadBytesAsync(stream, messageLengthBytes, 0, 4, cancellationToken);

            // Convert little-endian to int
            int messageLength = BitConverter.ToInt32(messageLengthBytes, 0);

            // Read the message stream
            byte[] messageBytes = new byte[messageLength];
            await ReadBytesAsync(stream, messageBytes, 0, messageLength, cancellationToken);

            string json = Encoding.UTF8.GetString(messageBytes);

            Console.WriteLine($"[SessionManagementConnection] Received message: {json}");
            LogBuffer.Add($"[RECV] {json}");

            return json;
        }

        private async Task SendJsonAsync(NetworkStream stream, string json, CancellationToken cancellationToken)
        {
            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);

            // Step 1: Send 4-byte length prefix (little-endian)
            byte[] lengthBytes = BitConverter.GetBytes(jsonBytes.Length);
            await stream.WriteAsync(lengthBytes, 0, 4, cancellationToken);

            // Step 2: Send actual JSON
            await stream.WriteAsync(jsonBytes, 0, jsonBytes.Length, cancellationToken);

            Console.WriteLine($"[SessionManagementConnection] Sent message: {json}");
            LogBuffer.Add($"[SEND] {json}");
        }

        /// <summary>
        /// getServerID() will fetch or create a server ID 
        /// ServerID is generated once per PC/machine. To store it permanently, we generate a registry entry for it
        /// You can find it under HKEY_CURRENT_USER\software\CloudXR\ServerID
        /// Apple Vision Pro stores this ServerID in its persistent storage. 
        /// When pairing a second time, AVP will check if ServerID is already present. Then, skip QR code pairing. 
        /// </summary>
        /// <returns></returns>
        private string getServerID()
        {
            /// Create registry entry
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\CloudXR"))
            {
                string ID = key.GetValue("ServerID") as string;

                if (string.IsNullOrEmpty(ID))
                {
                    /// create a 32 char guid. "N" will remove dashes
                    string newID = Guid.NewGuid().ToString("N");
                    key.SetValue("ServerID", newID);
                    Console.WriteLine($"[Registry] Created new Server ID: {newID}");
                    return newID;
                }

                Console.WriteLine($"[Registry] Using existing Server ID: {ID}");
                return ID;
            }
        }

        public async Task RequestDisconnect(CancellationToken cancellationToken)
        {
            string sessionID = currentSession?.sessionID;
            if (sessionID == null || _currentStream == null) {
                return;
            }

            await SendDisconnectMessage(sessionID, cancellationToken);
        }

        public async Task MediaStreamIsReady(CancellationToken cancellationToken)
        {
            string sessionID = currentSession?.sessionID;
            if (sessionID == null || _currentStream == null)
            {
                return;
            }

            var streamIsReadyMessage = new MediaStreamIsReadyMessage
            {
                SessionID = sessionID
            };
            await SendJsonAsync(_currentStream, JsonConvert.SerializeObject(streamIsReadyMessage), cancellationToken);
        }

        ~SessionManagementConnection()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
