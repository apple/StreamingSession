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
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace FoveatedStreaming.WindowsSample
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        //Exception handling
        protected override void OnStartup(StartupEventArgs e)
        {
            AppDomain.CurrentDomain.UnhandledException += HandleException;
            this.DispatcherUnhandledException += HandleUIException;
            base.OnStartup(e);
        }

        private void HandleException(object sender, UnhandledExceptionEventArgs e)
        {
            MessageBox.Show($"Something went wrong: {e.ExceptionObject}");
        }

        private void HandleUIException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show($"UI error: {e.Exception.Message}");
            e.Handled = true;
        }
    }
}
