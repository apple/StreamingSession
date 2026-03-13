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
using System.ComponentModel;
using System.Diagnostics;

namespace FoveatedStreaming.WindowsSample
{
    /// <summary>
    /// Performs registry writes to HKLM by spawning an elevated reg.exe process.
    /// Each call triggers a UAC prompt if the app is not already running as admin.
    /// </summary>
    internal static class RegistryElevator
    {
        private const string OpenXRKey = @"HKLM\SOFTWARE\Khronos\OpenXR\1";

        /// <summary>
        /// Sets the given JSON path as the ActiveRuntime in HKLM.
        /// </summary>
        public static void SetActiveRuntime(string jsonPath)
        {
            RunElevated("add", $@"""{OpenXRKey}"" /v ""ActiveRuntime"" /t REG_SZ /d ""{jsonPath}"" /f");
        }

        private static void RunElevated(string command, string arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "reg.exe",
                Arguments = $"{command} {arguments}",
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            try
            {
                using (Process proc = Process.Start(startInfo))
                {
                    proc.WaitForExit();
                }
            }
            catch (Win32Exception ex)
            {
                // Error code 1223: user cancelled the UAC prompt
                if (ex.NativeErrorCode != 1223)
                {
                    throw;
                }
            }
        }
    }
}
