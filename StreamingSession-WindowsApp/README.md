# FoveatedStreaming Windows Sample

A Windows sample app for streaming desktop OpenXR applications to Apple Vision Pro with the Foveated Streaming framework.

This application serves as the streaming host, implementing a TCP-based session management protocol that handles device discovery, authentication, session life cycle management, and CloudXR interoperation.

## Project Structure

```
StreamingSession-WindowsApp/
├── App.config                       # Application configuration
├── App.xaml                         # WPF Application definition
├── App.xaml.cs                      # Application startup logic
├── MainWindow.xaml                  # Main window UI (XAML)
├── MainWindow.xaml.cs               # Main window code-behind
├── MainViewModel.cs                 # MVVM ViewModel for UI binding
├── ConnectionManager.cs             # Orchestrates all three connection types
├── SessionManagementConnection.cs   # TCP session management protocol
├── CloudXRConnection.cs             # NVIDIA CloudXR service management
├── BonjourConnection.cs             # mDNS service discovery
├── NvCloudXR.cs                     # P/Invoke wrapper for NVIDIA APIs
├── tcpMessageClasses.cs             # Protocol message definitions
├── FoveatedStreamingSample.sln      # Visual Studio solution file
├── FoveatedStreamingSample.csproj   # Project configuration
├── packages.config                  # NuGet package dependencies
└── Properties/                      # Assembly info and project metadata
```

## CloudXR Setup

Place the CloudXR server binaries in the same folder as your executable:

```
bin/
├── Server/
│   ├── releases/                    # CloudXR 6.0 binaries
│   ├── CloudXrService.exe           # CloudXR service application
│   └── NvStreamManager.exe          # NVIDIA Stream Manager
├── NvStreamManagerClient.h          # NVIDIA API header file
└── NvStreamManagerClient.dll        # NVIDIA API dll file
```

NVIDIA provides these binaries separately.

## Building

1. Open `FoveatedStreamingSample.sln` in Visual Studio 2022 or newer
2. Build the solution (Ctrl+Shift+B) or click Build > Build Solution

## Running
1. Switch your OpenXR runtime to the CloudXR's openxr_cloudxr.json
2. Launch FoveatedStreamingSample.exe
3. Input your client app's bundle ID in the App Bundle ID field to advertise the endpoint over mDNS

## Requirements

- Visual Studio 2022 or newer (Windows only)
- .NET Framework 4.7.2
- Windows 10 SDK (10.0.22621.0)
