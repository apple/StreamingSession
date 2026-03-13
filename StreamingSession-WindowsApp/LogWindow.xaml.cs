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
using System.Windows;
using FoveatedStreamingSample;

namespace FoveatedStreaming.WindowsSample
{
    public partial class LogWindow : Window
    {
        private readonly LogBuffer _logBuffer;
        private readonly LogBuffer _runtimeLogBuffer;
        private LogWindow _runtimeLogWindow;

        public LogWindow(string title, LogBuffer logBuffer, LogBuffer runtimeLogBuffer = null)
        {
            InitializeComponent();
            Title = title;
            _logBuffer = logBuffer;
            _runtimeLogBuffer = runtimeLogBuffer;

            if (_runtimeLogBuffer != null)
            {
                RuntimeLogButton.Visibility = Visibility.Visible;
            }

            // Load existing logs
            LogTextBox.Text = _logBuffer.GetAll();
            ScrollToEnd();

            // Subscribe to new log entries
            _logBuffer.LogAdded += OnLogAdded;
            this.Closed += (s, e) => _logBuffer.LogAdded -= OnLogAdded;
        }

        private void OnLogAdded(string message)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (!string.IsNullOrEmpty(LogTextBox.Text))
                {
                    LogTextBox.AppendText(Environment.NewLine);
                }
                LogTextBox.AppendText(message);
                ScrollToEnd();
            });
        }

        private void ScrollToEnd()
        {
            LogTextBox.ScrollToEnd();
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            _logBuffer.Clear();
            LogTextBox.Clear();
        }

        private void RuntimeLogButton_Click(object sender, RoutedEventArgs e)
        {
            if (_runtimeLogWindow == null || !_runtimeLogWindow.IsLoaded)
            {
                _runtimeLogWindow = new LogWindow("Runtime Log", _runtimeLogBuffer);
            }
            _runtimeLogWindow.Show();
            _runtimeLogWindow.Activate();
        }
    }
}
