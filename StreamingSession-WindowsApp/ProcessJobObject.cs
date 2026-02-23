//===----------------------------------------------------------------------===//
// Copyright © 2026 Apple Inc. and the StreamingSession project authors.
//
// Licensed under the MIT license (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// StreamingSession/LICENSE.txt
//
//===----------------------------------------------------------------------===//

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FoveatedStreaming.WindowsSample
{
    /// <summary>
    /// Manages process lifetime using Windows Job Objects.
    /// When the job is disposed (or the parent process crashes), all assigned
    /// child processes are automatically terminated by the operating system.
    /// </summary>
    public class ProcessJobObject : IDisposable
    {
        #region P/Invoke Declarations

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(
            IntPtr hJob,
            JobObjectInfoType infoType,
            IntPtr lpJobObjectInfo,
            uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        private enum JobObjectInfoType
        {
            BasicLimitInformation = 2,
            ExtendedLimitInformation = 9
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        // When this flag is set, closing the job handle terminates all processes in the job
        private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;

        #endregion

        private IntPtr _jobHandle = IntPtr.Zero;
        private bool _disposed = false;

        /// <summary>
        /// Creates a new Job Object configured to terminate child processes when closed.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if job object creation fails.</exception>
        public ProcessJobObject()
        {
            _jobHandle = CreateJobObject(IntPtr.Zero, null);
            if (_jobHandle == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"Failed to create job object. Win32 error: {error}");
            }

            if (!ConfigureKillOnClose())
            {
                CloseHandle(_jobHandle);
                _jobHandle = IntPtr.Zero;
                throw new InvalidOperationException("Failed to configure job object with KILL_ON_JOB_CLOSE flag.");
            }
        }

        /// <summary>
        /// Configures the job object to terminate all processes when the job handle is closed.
        /// </summary>
        private bool ConfigureKillOnClose()
        {
            var extendedInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                }
            };

            int size = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
            IntPtr pInfo = Marshal.AllocHGlobal(size);

            try
            {
                Marshal.StructureToPtr(extendedInfo, pInfo, false);
                return SetInformationJobObject(
                    _jobHandle,
                    JobObjectInfoType.ExtendedLimitInformation,
                    pInfo,
                    (uint)size);
            }
            finally
            {
                Marshal.FreeHGlobal(pInfo);
            }
        }

        /// <summary>
        /// Assigns a process to this job object. The process will be automatically
        /// terminated when the job is disposed or the parent process crashes.
        /// </summary>
        /// <param name="process">The process to assign to the job.</param>
        /// <returns>True if successful, false otherwise.</returns>
        public bool AssignProcess(Process process)
        {
            if (_disposed)
            {
                Console.WriteLine("[ProcessJobObject] Cannot assign process: job object is disposed.");
                return false;
            }

            if (process == null)
            {
                Console.WriteLine("[ProcessJobObject] Cannot assign process: process is null.");
                return false;
            }

            if (process.HasExited)
            {
                Console.WriteLine("[ProcessJobObject] Cannot assign process: process has already exited.");
                return false;
            }

            try
            {
                if (!AssignProcessToJobObject(_jobHandle, process.Handle))
                {
                    int error = Marshal.GetLastWin32Error();
                    Console.WriteLine($"[ProcessJobObject] Failed to assign process to job. Win32 error: {error}");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ProcessJobObject] Exception assigning process to job: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Disposes the job object, which triggers termination of all assigned processes.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (_jobHandle != IntPtr.Zero)
                {
                    CloseHandle(_jobHandle);
                    _jobHandle = IntPtr.Zero;
                }
                _disposed = true;
            }
        }

        ~ProcessJobObject()
        {
            Dispose(false);
        }
    }
}
