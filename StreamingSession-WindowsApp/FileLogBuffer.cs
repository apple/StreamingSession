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
using System.Threading;

namespace FoveatedStreamingSample
{
    /// <summary>
    /// A <see cref="LogBuffer"/> that tails a log file on disk,
    /// feeding newly-appended lines into the buffer in real time via a polling timer.
    /// Lines are added as-is (no extra timestamp) because the file already contains runtime timestamps.
    /// </summary>
    public class FileLogBuffer : LogBuffer, IDisposable
    {
        public string FilePath { get; }

        private const int PollIntervalMs = 500;

        private long _readPosition = 0;
        private readonly object _fileLock = new object();
        private readonly Timer _timer;

        public FileLogBuffer(string filePath)
        {
            FilePath = filePath;

            // Read any content already in the file before we start polling.
            ReadNewLines();

            _timer = new Timer(_ => ReadNewLines(), null, PollIntervalMs, PollIntervalMs);
        }

        private void ReadNewLines()
        {
            lock (_fileLock)
            {
                try
                {
                    using (var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    {
                        stream.Seek(_readPosition, SeekOrigin.Begin);
                        using (var reader = new StreamReader(stream))
                        {
                            string line;
                            while ((line = reader.ReadLine()) != null)
                            {
                                AddRaw(line);
                            }
                            _readPosition = stream.Position;
                        }
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        public void Dispose()
        {
            _timer.Dispose();
        }
    }
}
