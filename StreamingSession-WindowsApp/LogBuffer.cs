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
using System.Text;

namespace FoveatedStreamingSample
{
    /// <summary>
    /// Thread-safe in-memory log buffer for diagnostic logging.
    /// </summary>
    public class LogBuffer
    {
        public event Action<string> LogAdded;

        private readonly List<string> _entries = new List<string>();
        private readonly object _lock = new object();

        public void Add(string message)
        {
            string timestamped = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            lock (_lock)
            {
                _entries.Add(timestamped);
            }
            LogAdded?.Invoke(timestamped);
        }

        public string GetAll()
        {
            lock (_lock)
            {
                return string.Join(Environment.NewLine, _entries);
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _entries.Clear();
            }
        }
    }
}
