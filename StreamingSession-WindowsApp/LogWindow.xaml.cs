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

        public LogWindow(string title, LogBuffer logBuffer)
        {
            InitializeComponent();
            Title = title;
            _logBuffer = logBuffer;

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
    }
}
