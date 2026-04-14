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

> **Note:** CloudXR version 6.0.4 and above is now required.

Two separate downloads from NVIDIA NGC are required:

- **[CloudXR Runtime](https://catalog.ngc.nvidia.com/orgs/nvidia/resources/cloudxr-runtime)** — the OpenXR runtime binaries (e.g. `CloudXR-6.0.4-Win64-sdk`)
- **[CloudXR Stream Manager](https://catalog.ngc.nvidia.com/orgs/nvidia/resources/cloudxr-stream-manager)** — the stream management service (e.g. `Stream-Manager-6.0.3-win64`)

Place the files next to your built executable as follows:

```
bin/
├── Server/
│   ├── releases/
│   │   └── 6.0.4/                   # Contents of CloudXR-6.0.4-Win64-sdk/ placed here
│   │       ├── openxr_cloudxr.json
│   │       ├── openxr_cloudxr.dll
│   │       └── ...
│   ├── CloudXrService.exe           # From Stream Manager download: Server/
│   ├── NvStreamManager.exe          # From Stream Manager download: Server/
│   └── cloudxr-runtime.yaml        # From Stream Manager download: Server/
├── NvStreamManagerClient.h          # From Stream Manager download: SampleClient/
└── NvStreamManagerClient.dll        # From Stream Manager download: SampleClient/
```

> **Important:** The CloudXR runtime must be placed inside a subfolder named after its version number (e.g. `releases/6.0.4/`). The Stream Manager discovers available runtimes by looking for these version-named subdirectories. If the runtime files are placed directly in `releases/` without a version subfolder, the Stream Manager will fail to start the service. When multiple version folders are present, the Stream Manager automatically selects the highest version.

`NvStreamManagerClient.dll` and `NvStreamManagerClient.h` are found in the `SampleClient/` subfolder of the Stream Manager download — not in the main SDK archive.

`CloudXrService.exe` is the process host for the CloudXR OpenXR runtime. It is launched automatically by `NvStreamManager.exe` when a streaming session starts and does not need to be run manually.

### Configuring cloudxr-runtime.yaml for iOS

The `cloudxr-runtime.yaml` shipped with the Stream Manager is configured for Apple Vision Pro by default. To stream to an iOS device, two changes are required in `bin/Server/cloudxr-runtime.yaml`:

1. Set `deviceProfile` to `auto-native` — iOS clients will not connect with the default `apple-vision-pro` profile. See [deviceProfile](https://docs.nvidia.com/cloudxr-sdk/latest/usr_guide/cloudxr_runtime/runtime_mgmt_api.html#device-profile).
2. Set `runtimeFoveation` to `false` — leaving foveation enabled causes a black screen on iOS. See [runtimeFoveation](https://docs.nvidia.com/cloudxr-sdk/latest/usr_guide/cloudxr_runtime/runtime_mgmt_api.html#runtime-foveation).

## OpenXR Runtime Management

On startup, the application checks whether the active runtime is correctly configured and displays a warning if it is not. Clicking the "Fix" button allows the application to manage the active OpenXR runtime by writing to `HKEY_LOCAL_MACHINE\SOFTWARE\Khronos\OpenXR\1\ActiveRuntime` — you will be prompted for administrator permission if required.

## Building

1. Open `FoveatedStreamingSample.sln` in Visual Studio 2022 or newer
2. Build the solution (Ctrl+Shift+B) or click Build > Build Solution

## Running

1. Launch FoveatedStreamingSample.exe
2. If presented by a warning to fix the active runtime, click on "Fix".
2. Input your client app's bundle ID in the App Bundle ID field to advertise the endpoint over mDNS

## Requirements

- Visual Studio 2022 or newer (Windows only)
- .NET Framework 4.7.2
- Windows 10 SDK (10.0.22621.0)
