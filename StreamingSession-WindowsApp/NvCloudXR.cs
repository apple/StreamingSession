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
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace FoveatedStreaming.WindowsSample
{
    /// <summary>
    /// P/Invoke wrapper for NvStreamManagerClient.dll (NVIDIA CloudXR native library).
    ///
    /// This class provides C# bindings to the NVIDIA CloudXR RPC API, which communicates
    /// with the NvStreamManager process to control the streaming service.
    ///
    /// The DLL must be placed in the same directory as the executable.
    /// </summary>
    internal class NvCloudXR
    {
        const string DLL_NAME = "NvStreamManagerClient.dll";

        /// <summary>RPC call result codes.</summary>
        public enum nv_rpc_result_t
        {
            NV_RPC_SUCCESS = 0,
            NV_RPC_ERROR_INVALID_HANDLE = -1,
            NV_RPC_ERROR_CONNECTION_FAILED = -2,
            NV_RPC_ERROR_RPC_CALL_FAILED = -3,
            NV_RPC_ERROR_INVALID_PARAMETER = -4,
            NV_RPC_ERROR_MEMORY_ALLOCATION = -5,
            NV_RPC_ERROR_UNKNOWN = -999
        }

        public enum nv_crypto_algorithm_t
        {
            NV_CRYPTO_ALG_MD5 = 0,
            NV_CRYPTO_ALG_SHA1 = 1,
            NV_CRYPTO_ALG_SHA256 = 2,
            NV_CRYPTO_ALG_SHA512 = 3
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct nv_service_status_t
        {
            [MarshalAs(UnmanagedType.I1)] public bool openxr_runtime_running;
            [MarshalAs(UnmanagedType.I1)] public bool openxr_app_connected;
            [MarshalAs(UnmanagedType.I1)] public bool cloudxr_client_connected;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            public byte[] openxr_log_file_path_bytes;

            public UIntPtr openxr_log_file_path_length;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4096)]
            public int[] reserved;

            public string OpenXRLogFilePath => System.Text.Encoding.ASCII.GetString(openxr_log_file_path_bytes, 0, (int)openxr_log_file_path_length);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct nv_string_array_t
        {
            public IntPtr strings;  // char** -> pointer to array of char*
            public IntPtr count;    // size_t -> use IntPtr for size_t
        }

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern nv_rpc_result_t nv_rpc_client_create(string pipe_name, out IntPtr client_out);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern nv_rpc_result_t nv_rpc_client_destroy(IntPtr client);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern nv_rpc_result_t nv_rpc_client_connect(IntPtr client);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern nv_rpc_result_t nv_rpc_client_disconnect(IntPtr client);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern nv_rpc_result_t nv_rpc_client_set_client_id(
            IntPtr client, 
            string clientId, 
            UIntPtr clientIdLength, 
            StringBuilder tokenBuffer, 
            UIntPtr tokenSize, 
            out UIntPtr tokenSizeOut);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern nv_rpc_result_t nv_rpc_client_start_cxr_service(
            IntPtr client, 
            string service_version, 
            UIntPtr service_version_length);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern nv_rpc_result_t nv_rpc_client_stop_cxr_service(IntPtr client);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern nv_rpc_result_t nv_rpc_client_get_supported_versions(IntPtr client, out nv_string_array_t versions_out);

        [DllImport(DLL_NAME)]
        private static extern nv_rpc_result_t nv_rpc_client_free_string_array(ref nv_string_array_t string_array);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern nv_rpc_result_t nv_rpc_client_get_crypto_key_fingerprint(
            IntPtr client, 
            nv_crypto_algorithm_t algorithm, 
            StringBuilder fingerprint_out, 
            UIntPtr fingerprint_size);


        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr nv_rpc_client_get_error_string(nv_rpc_result_t result);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern nv_rpc_result_t nv_rpc_client_get_cxr_service_status(IntPtr client, out nv_service_status_t status_out);

        public static string get_error_string(nv_rpc_result_t result)
        {
            IntPtr ptr = nv_rpc_client_get_error_string(result);
            return ptr != IntPtr.Zero ? Marshal.PtrToStringAnsi(ptr) : "Unknown error";
        }


        public static string[] get_supported_versions(IntPtr client)
        {
            nv_string_array_t versions;
            nv_rpc_result_t result = nv_rpc_client_get_supported_versions(client, out versions);

            if (result != nv_rpc_result_t.NV_RPC_SUCCESS)
            {
                return new string[0]; // Or throw exception
            }

            try
            {
                if (versions.strings == IntPtr.Zero)
                {
                    Console.WriteLine("Failed to get supported versions");
                    return new string[0];
                }

                int count = versions.count.ToInt32();
                string[] results = new string[count];

                for (int i = 0; i < count; i++)
                {
                    IntPtr strPtr = Marshal.ReadIntPtr(versions.strings, i * IntPtr.Size);
                    if (strPtr != IntPtr.Zero)
                    {
                        results[i] = Marshal.PtrToStringAnsi(strPtr);
                    }
                }

                return results;
            }
            finally
            {
                // Always free the C memory!
                nv_rpc_client_free_string_array(ref versions);
            }
        }
    }
}

