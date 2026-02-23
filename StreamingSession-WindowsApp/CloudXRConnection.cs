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
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;
using static FoveatedStreaming.WindowsSample.NvCloudXR;
using FoveatedStreamingSample;

namespace FoveatedStreaming.WindowsSample
{
    /// <summary>
    /// Data for the QR code displayed during device pairing.
    /// The Vision Pro scans this to establish a trusted connection.
    /// </summary>
    internal struct BarcodePayload
    {
        /// <summary>SHA-256 hash of the CloudXR certificate, used to verify the streaming connection.</summary>
        public string certificateFingerprint;
        /// <summary>Token generated from the client ID, used to authenticate the Vision Pro.</summary>
        public string clientToken;
    }

    /// <summary>
    /// Exception thrown when an RPC call to NvStreamManager fails.
    /// </summary>
    internal class NVStreamManagerException : Exception
    {
        readonly string errorDescription;

        public NVStreamManagerException(nv_rpc_result_t result)
        {
            this.errorDescription = get_error_string(result);
        }
    }

    /// <summary>
    /// Manages the NVIDIA CloudXR streaming service for FoveatedStreaming.
    ///
    /// This class:
    /// <list type="bullet">
    ///   <item>Launches and monitors the NvStreamManager.exe process</item>
    ///   <item>Communicates with NvStreamManager via RPC (named pipes)</item>
    ///   <item>Generates pairing credentials (client tokens and certificate fingerprints)</item>
    ///   <item>Polls the CloudXR service status and emits state change events</item>
    /// </list>
    ///
    /// The actual video streaming is handled by CloudXR; this class manages the service lifecycle.
    /// </summary>
    internal class CloudXRConnection: IDisposable
    {
        /// <summary>
        /// Current state of the CloudXR streaming connection.
        /// </summary>
        public struct State: IEquatable<State>
        {
            /// <summary>True when the Vision Pro is connected to the CloudXR stream.</summary>
            public bool CloudXRClientIsConnected;
            /// <summary>True when the OpenXR runtime is running on this PC.</summary>
            public bool OpenXRRuntimeIsRunning;
            /// <summary>True when the OpenXR game is connected to the runtime.</summary>
            public bool GameIsConnected;

            public bool Equals(State other)
            {
                return CloudXRClientIsConnected == other.CloudXRClientIsConnected
                    && OpenXRRuntimeIsRunning == other.OpenXRRuntimeIsRunning
                    && GameIsConnected == other.GameIsConnected;
            }
        }

        /// <summary>Interval between CloudXR status polls (in seconds).</summary>
        private const double StatusPollIntervalSeconds = 0.2;
        /// <summary>Delay between state change checks (in seconds).</summary>
        private const double StateChangePollDelaySeconds = 0.05;

        /// Process object for NvStreamManager.exe
        private Process _NVStreamManagerProcess;

        /// Job object for automatic child process cleanup on crash
        private ProcessJobObject _JobObject;

        private RPCClient _RPCClient;
        private bool _DidDispose = false;

        public delegate Task StateChangedEvent(State state);
        public event StateChangedEvent StateChanged;

        private State _CurrentState;
        private readonly object _CurrentStateLock = new object();

        /// <summary>Log buffer for diagnostic logging of CloudXR/NvStreamManager events.</summary>
        public LogBuffer LogBuffer { get; }

        /// <summary>
        /// Recursively searches for the openxr_cloudxr.json file in the releases directory.
        ///
        /// IMPORTANT: There should only be ONE CloudXR runtime version folder in the Server\releases\ directory.
        /// If multiple CloudXR versions are present, this method will pick the first one found,
        /// which may not be the intended version.
        /// </summary>
        /// <param name="serverPath">Path to the Server directory</param>
        /// <param name="logBuffer">Optional log buffer for warnings</param>
        /// <returns>Full path to openxr_cloudxr.json, or null if not found</returns>
        private static string FindCloudXRRuntimeJson(string serverPath, LogBuffer logBuffer = null)
        {
            string releasesPath = System.IO.Path.Combine(serverPath, "releases");

            if (!Directory.Exists(releasesPath))
            {
                return null;
            }

            // Recursively search for openxr_cloudxr.json files
            string[] jsonFiles = Directory.GetFiles(releasesPath, "openxr_cloudxr.json", SearchOption.AllDirectories);

            if (jsonFiles.Length == 0)
            {
                return null;
            }

            if (jsonFiles.Length > 1)
            {
                string warningMessage = $"[WARNING] Found {jsonFiles.Length} openxr_cloudxr.json files. Using: {jsonFiles[0]}. Ensure only one CloudXR version is deployed.";
                logBuffer?.Add(warningMessage);
                Console.WriteLine(warningMessage);
            }

            return jsonFiles[0];
        }

        private class RPCClient: IDisposable
        {
            private readonly IntPtr _ClientPointer;

            public bool IsCloudXRServiceRunning
            {
                get => _IsCloudXRServiceRunning;
            }

            private bool _IsCloudXRServiceRunning = false;

            private bool _IsConnected = false;
            private bool _DidDispose = false;

            public RPCClient()
            {
                nv_rpc_result_t result = NvCloudXR.nv_rpc_client_create(null, out IntPtr clientPointer);
                if (result != nv_rpc_result_t.NV_RPC_SUCCESS)
                {
                    throw new NVStreamManagerException(result);
                }

                this._ClientPointer = clientPointer;
            }

            private void ConnectIfNeeded()
            {
                if (_DidDispose)
                {
                    throw new ObjectDisposedException(nameof(RPCClient));
                }

                if (_IsConnected)
                {
                    return;
                }

                nv_rpc_result_t result = NvCloudXR.nv_rpc_client_connect(_ClientPointer);
                if (result != nv_rpc_result_t.NV_RPC_SUCCESS)
                {
                    throw new NVStreamManagerException(result);
                }
                _IsConnected = true;
            }

            public void StartCloudXRService(string version = "6.0.0")
            {
                if (_DidDispose)
                {
                    throw new ObjectDisposedException(nameof(RPCClient));
                }

                ConnectIfNeeded();

                nv_rpc_result_t result = NvCloudXR.nv_rpc_client_start_cxr_service(_ClientPointer, version, (UIntPtr)version.Length);
                if (result != nv_rpc_result_t.NV_RPC_SUCCESS)
                {
                    _IsCloudXRServiceRunning = false;
                    throw new NVStreamManagerException(result);
                }

                _IsCloudXRServiceRunning = true;
            }

            public void StopCloudXRService()
            {
                if (_DidDispose)
                {
                    throw new ObjectDisposedException(nameof(RPCClient));
                }

                if (!_IsConnected)
                {
                    Console.WriteLine("[CloudXRConnection] Called RPCClient.StopCloudXRService when we have no RPC connection.");
                    return;
                }

                nv_rpc_result_t result = NvCloudXR.nv_rpc_client_stop_cxr_service(_ClientPointer);
                _IsCloudXRServiceRunning = false; // Correct with or without error.
                if (result != nv_rpc_result_t.NV_RPC_SUCCESS)
                {
                    throw new NVStreamManagerException(result);
                }
            }

            public nv_service_status_t? QueryStatus()
            {
                if (_DidDispose)
                {
                    throw new ObjectDisposedException(nameof(RPCClient));
                }

                ConnectIfNeeded();

                if (!_IsCloudXRServiceRunning)
                {
                    return null;
                }

                nv_service_status_t status;
                nv_rpc_result_t result = NvCloudXR.nv_rpc_client_get_cxr_service_status(_ClientPointer, out status);
                if (result == nv_rpc_result_t.NV_RPC_SUCCESS)
                {
                    return status;
                } else
                {
                    return null;
                }
            }

            public string GenerateClientToken(string clientID)
            {
                if (_DidDispose)
                {
                    throw new ObjectDisposedException(nameof(RPCClient));
                }

                ConnectIfNeeded();

                StringBuilder tokenBuffer = new StringBuilder(256);
                UIntPtr token_size = UIntPtr.Zero;
                nv_rpc_result_t result = NvCloudXR.nv_rpc_client_set_client_id(
                    _ClientPointer,
                    clientID,
                    (UIntPtr)clientID.Length,
                    tokenBuffer,
                    (UIntPtr)tokenBuffer.Capacity,
                    out token_size);

                if (result != nv_rpc_result_t.NV_RPC_SUCCESS)
                {
                    throw new NVStreamManagerException(result);
                }

                return tokenBuffer.ToString();
            }

            public string GenerateCryptoKeyFingerprint()
            {
                if (_DidDispose)
                {
                    throw new ObjectDisposedException(nameof(RPCClient));
                }

                ConnectIfNeeded();

                StringBuilder fingerprint = new StringBuilder(256);
                nv_rpc_result_t result = nv_rpc_client_get_crypto_key_fingerprint(
                    _ClientPointer,
                    nv_crypto_algorithm_t.NV_CRYPTO_ALG_SHA256,
                    fingerprint,
                    (UIntPtr)fingerprint.Capacity);

                if (result != nv_rpc_result_t.NV_RPC_SUCCESS)
                {
                    throw new NVStreamManagerException(result);
                }

                return fingerprint.ToString();
            }

            protected virtual void Dispose(bool disposing)
            {
                if (!_DidDispose)
                {
                    if (disposing)
                    {
                        // Intentionally Empty.
                        // To be filled with other managed (IDisposable) resources if appropriate.
                    }

                    if (_IsCloudXRServiceRunning)
                    {
                        StopCloudXRService();
                    }
                    nv_rpc_client_disconnect(this._ClientPointer);
                    nv_rpc_client_destroy(this._ClientPointer);

                    _IsConnected = false;
                    _DidDispose = true;
                }
            }

            ~RPCClient()
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

        public CloudXRConnection(LogBuffer logBuffer)
        {
            this.LogBuffer = logBuffer;

            // Set XR_RUNTIME_JSON environment variable for the current process
            string serverPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Server");
            string cloudXRRuntimeJsonPath = FindCloudXRRuntimeJson(serverPath, logBuffer);

            if (cloudXRRuntimeJsonPath != null)
            {
                Environment.SetEnvironmentVariable("XR_RUNTIME_JSON", cloudXRRuntimeJsonPath);
                LogBuffer.Add($"[ENV] Set XR_RUNTIME_JSON={cloudXRRuntimeJsonPath}");
            }
            else
            {
                LogBuffer.Add("[WARNING] Could not find openxr_cloudxr.json in releases directory");
            }

            CreateNVStreamManagerProcess();

            _RPCClient = new RPCClient();

            // Construct a task to continuously query the CloudXR state and notify consumers when the state changes.
            Task.Run(async () => await PollContinuouslyAsync());
        }

        private async Task PollContinuouslyAsync()
        {
            while (!_DidDispose)
            {
                State newState;
                newState.CloudXRClientIsConnected = false;
                newState.GameIsConnected = false;
                newState.OpenXRRuntimeIsRunning = false;

                try
                {
                    nv_service_status_t? status = _RPCClient.QueryStatus();

                    if (status != null)
                    {
                        newState.CloudXRClientIsConnected = status.Value.cloudxr_client_connected;
                        newState.GameIsConnected = status.Value.openxr_app_connected;
                        newState.OpenXRRuntimeIsRunning = status.Value.openxr_runtime_running;
                    }
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Unexpected exception when querying CloudXR status: {e}");
                }

                bool didChange = false;
                lock (_CurrentStateLock)
                {
                    didChange = !_CurrentState.Equals(newState);
                    if (didChange)
                    {
                        _CurrentState = newState;
                    }
                }

                if (didChange && StateChanged != null)
                {
                    await StateChanged.Invoke(newState);
                }

                await Task.Yield();
                await Task.Delay(TimeSpan.FromSeconds(StatusPollIntervalSeconds));
            }
        }

        private static Process GetProcessByPath(string targetPath)
        {
            // Standardize path to avoid false mismatches (casing/slashes)
            string normalizedTarget = Path.GetFullPath(targetPath).ToLowerInvariant();
            string procName = Path.GetFileNameWithoutExtension(targetPath);

            // Iterate through all running processes
            foreach (Process p in Process.GetProcessesByName(procName))
            {
                try
                {
                    // MainModule.FileName provides the full absolute path
                    string currentPath = p.MainModule?.FileName ?? "";
                    if (Path.GetFullPath(currentPath).ToLowerInvariant() == normalizedTarget)
                    {
                        return p; // Found a match; return the Process object for tracking
                    }
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Access denied for some system processes; skip them
                    continue;
                }
                catch (InvalidOperationException)
                {
                    // Process may have exited between GetProcesses() and accessing MainModule
                    continue;
                }
            }
            return null;
        }

        /// <summary>
        /// Finds any running CloudXR-related processes (NvStreamManager or CloudXRService).
        /// Used to detect orphaned processes from a previous crashed session.
        /// </summary>
        /// <returns>List of running CloudXR processes that the user may want to terminate.</returns>
        public static List<Process> GetExistingCloudXRProcesses()
        {
            var processes = new List<Process>();
            var processNames = new[] { "NvStreamManager", "CloudXRService" };

            foreach (var procName in processNames)
            {
                foreach (Process p in Process.GetProcessesByName(procName))
                {
                    try
                    {
                        // Verify we can access the process (filters out inaccessible system processes)
                        var _ = p.MainModule?.FileName;
                        processes.Add(p);
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        // Access denied for some system processes; skip them
                        continue;
                    }
                    catch (InvalidOperationException)
                    {
                        // Process may have exited between GetProcesses() and accessing MainModule
                        continue;
                    }
                }
            }

            return processes;
        }

        private void CreateNVStreamManagerProcess()
        {
            if (_NVStreamManagerProcess != null)
            {
                Console.WriteLine("[CloudXRConnection] Called CreateNVStreamManagerProcess when there was already an open stream manager.");
                return;
            }

            string serverPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Server");
            string executablePath = System.IO.Path.Combine(serverPath, "NvStreamManager.exe");

            // First, try to see if NVStreamManager.exe has already been launched (perhaps by a previous launch which crashed).
            // If it has been launched, kill it to clear out any transient state (after all, we did crash).
            Process existingProcess = GetProcessByPath(executablePath);
            if (existingProcess != null)
            {
                existingProcess.Kill();
            }

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                WorkingDirectory = serverPath,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            // Explicitly set XR_RUNTIME_JSON for the child process
            string cloudXRRuntimeJsonPath = FindCloudXRRuntimeJson(serverPath, LogBuffer);
            if (cloudXRRuntimeJsonPath != null)
            {
                startInfo.EnvironmentVariables["XR_RUNTIME_JSON"] = cloudXRRuntimeJsonPath;
            }

            _NVStreamManagerProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

            _NVStreamManagerProcess.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data) && !e.Data.StartsWith("RPC GetCxrServiceStatus received") && !e.Data.StartsWith("Returning status - "))
                {
                    LogBuffer.Add($"[STDOUT] {e.Data}");
                }
            };

            _NVStreamManagerProcess.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    LogBuffer.Add($"[STDERR] {e.Data}");
                }
            };

            _NVStreamManagerProcess.Exited += (sender, args) =>
            {
                // Don't restart if we're disposing intentionally
                if (_DidDispose) return;

                // Re-create the stream manager process on exit.
                Console.WriteLine("[CloudXRConnection] Error: NVStreamManager exited!  Will attempt to re-launch.");
                int exitCode = _NVStreamManagerProcess?.ExitCode ?? -1;
                LogBuffer.Add($"[PROCESS] Exited (code: {exitCode}). Restarting...");
                _NVStreamManagerProcess = null;
                this.CreateNVStreamManagerProcess();
            };

            _NVStreamManagerProcess.Start();

            // Assign the process to a job object so it's automatically terminated if we crash
            if (_JobObject == null)
            {
                try
                {
                    _JobObject = new ProcessJobObject();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CloudXRConnection] Warning: Failed to create job object: {ex.Message}");
                    LogBuffer.Add("[PROCESS] Warning: Job object creation failed - process may not auto-terminate on crash");
                }
            }

            if (_JobObject != null)
            {
                if (_JobObject.AssignProcess(_NVStreamManagerProcess))
                {
                    LogBuffer.Add("[PROCESS] Assigned to job object for auto-cleanup on crash");
                }
                else
                {
                    LogBuffer.Add("[PROCESS] Warning: Job object assignment failed - process may not auto-terminate on crash");
                }
            }

            _NVStreamManagerProcess.BeginOutputReadLine();
            _NVStreamManagerProcess.BeginErrorReadLine();
            LogBuffer.Add("[PROCESS] Started NvStreamManager.exe");
        }

        public BarcodePayload GenerateBarcode(string clientID)
        {
            string clientToken = _RPCClient.GenerateClientToken(clientID);
            Console.WriteLine($"[CloudXRConnection] Client token is: {clientToken}");

            string certificateFingerprint = _RPCClient.GenerateCryptoKeyFingerprint();
            Console.WriteLine($"[CloudXRConnection] Certificate hash is: {certificateFingerprint}");

            BarcodePayload payload = new BarcodePayload
            {
                clientToken = clientToken,
                certificateFingerprint = certificateFingerprint
            };

            return payload;
        }

        /// <summary>
        /// Starts or stops the CloudXR streaming service.
        /// Waits until the OpenXR runtime state matches the requested state before returning.
        /// </summary>
        /// <param name="presentationRequested">True to start streaming, false to stop.</param>
        public async Task UpdateCloudXRPresentation(bool presentationRequested)
        {
            if (presentationRequested)
            {
                _RPCClient.StartCloudXRService();
            }
            else
            {
                _RPCClient.StopCloudXRService();
            }

            while (!_DidDispose)
            {
                bool isPresented = false;
                lock (_CurrentStateLock)
                {
                    isPresented = _CurrentState.OpenXRRuntimeIsRunning;
                }

                if (isPresented == presentationRequested)
                {
                    break;
                } else
                {
                    await Task.Yield();
                    await Task.Delay(TimeSpan.FromSeconds(StateChangePollDelaySeconds));
                }
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_DidDispose)
            {
                // Set this first to prevent Exited handler from restarting the process
                _DidDispose = true;

                if (disposing)
                {
                    _RPCClient.Dispose();
                    _JobObject?.Dispose();
                }

                if (_NVStreamManagerProcess != null && !_NVStreamManagerProcess.HasExited)
                {
                    _NVStreamManagerProcess.Kill();
                }
            }
        }

        ~CloudXRConnection()
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