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
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace FoveatedStreaming.WindowsSample
{
    /// <summary>
    /// Visual indicator for connection health, displayed as colored dots in the UI.
    /// </summary>
    public enum ConnectionStatus
    {
        /// <summary>Green - fully connected and operational.</summary>
        Running,
        /// <summary>Yellow - partially connected or in a transitional state.</summary>
        Paused,
        /// <summary>Red - disconnected or not started.</summary>
        Stopped
    }

    /// <summary>
    /// MVVM ViewModel for the main window. Exposes connection status and configuration
    /// properties for data binding with the WPF UI.
    /// </summary>
    public class MainViewModel: INotifyPropertyChanged
    {
        private ConnectionStatus _bonjourStatus = ConnectionStatus.Stopped;
        private ConnectionStatus _sessionManagementStatus = ConnectionStatus.Stopped;
        private ConnectionStatus _cloudXRStatus = ConnectionStatus.Stopped;

        private string _bonjourStatusText = "Uninitialized";
        private string _sessionManagementStatusText = "Uninitialized";
        private string _cloudXRStatusText = "Uninitialized";

        private bool _displayDisconnectButton = false;
        private bool _displayStartPanel = true;

        private ObservableCollection<string> _localIPAddresses;
        private string _selectedIPAddress;

        private string _bundleId = "com.company.placeHolderBundleID";
        private string _port = "55000";
        private bool _forceQRCode = false;

        private ImageSource _qrImageSource = null;

        public ObservableCollection<string> LocalIPAddresses
        {
            get => _localIPAddresses;
            set
            {
                _localIPAddresses = value;
                OnPropertyChanged(nameof(LocalIPAddresses));
            }
        }

        public string SelectedIPAddress
        {
            get => _selectedIPAddress;
            set
            {
                _selectedIPAddress = value;
                OnPropertyChanged(nameof(SelectedIPAddress));
            }
        }

        public ConnectionStatus BonjourStatus
        {
            get => _bonjourStatus;
            set
            {
                _bonjourStatus = value;
                OnPropertyChanged(nameof(BonjourStatus));
            }
        }

        public string SessionManagementStatusText
        {
            get => _sessionManagementStatusText;
            set
            {
                _sessionManagementStatusText = value;
                OnPropertyChanged(nameof(SessionManagementStatusText));
            }
        }

        public string CloudXRStatusText
        {
            get => _cloudXRStatusText;
            set
            {
                _cloudXRStatusText = value;
                OnPropertyChanged(nameof(CloudXRStatusText));
            }
        }

        public string BonjourStatusText
        {
            get => _bonjourStatusText;
            set
            {
                _bonjourStatusText = value;
                OnPropertyChanged(nameof(BonjourStatusText));
            }
        }

        public ConnectionStatus SessionManagementStatus
        {
            get => _sessionManagementStatus;
            set
            {
                _sessionManagementStatus = value;
                OnPropertyChanged(nameof(SessionManagementStatus));
            }
        }

        public ConnectionStatus CloudXRStatus
        {
            get => _cloudXRStatus;
            set
            {
                _cloudXRStatus = value;
                OnPropertyChanged(nameof(CloudXRStatus));
            }
        }

        public bool DisplayDisconnectButton
        {
            get => _displayDisconnectButton;

            set
            {
                _displayDisconnectButton = value;
                OnPropertyChanged(nameof(DisplayDisconnectButton));
            }
        }

        public bool DisplayStartPanel
        {
            get => _displayStartPanel;

            set
            {
                _displayStartPanel = value;
                OnPropertyChanged(nameof(DisplayStartPanel));
            }
        }

        public string BundleID
        {
            get => _bundleId;
            set { _bundleId = value; OnPropertyChanged(nameof(BundleID)); }
        }

        public string Port
        {
            get => _port;
            set { _port = value; OnPropertyChanged(nameof(Port)); }
        }

        public bool ForceQRCode
        {
            get => _forceQRCode;
            set { _forceQRCode = value; OnPropertyChanged(nameof(ForceQRCode)); }
        }

        public ImageSource QRImageSource
        {
            get => _qrImageSource;
            set { _qrImageSource = value; OnPropertyChanged(nameof(QRImageSource)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
