# StreamingSession OpenXR Sample

OpenXR sample application demonstrating integration with Apple's Foveated Streaming framework for streaming PC-based OpenXR content to Apple Vision Pro.

## Project Structure

```
StreamingSession-OpenXRSample/
├── main.cpp                                  # Main application and OpenXR initialization
├── MessageChannel.h                          # Opaque data channel interface
├── MessageChannel.cpp                        # Opaque data channel implementation
├── StreamingSession-OpenXRSample.sln         # Visual Studio solution file
├── StreamingSession-OpenXRSample.vcxproj     # Project configuration
├── StreamingSession-OpenXRSample.vcxproj.filters  # Project file organization
└── packages.config                           # NuGet package dependencies
```

## Features

- **OpenXR Integration**: Full OpenXR implementation with Direct3D 11 rendering
- **3D Scene Rendering**: Animated 3D cube with lighting and shading
- **Opaque Data Channel**: Custom message channel for bi-directional communication with Apple Vision Pro
- **Spectator View**: Window-based view showing what's being rendered in VR
- **Dual Mode Support**:
  - Immersive Mode: `XR_FORM_FACTOR_HEAD_MOUNTED_DISPLAY` with stereo rendering
  - iOS Mode: `XR_FORM_FACTOR_HANDHELD_DISPLAY` with mono rendering (use `-iOS` command line flag)

## OpenXR Extension Requirements

This sample uses the following OpenXR extensions:
- `XR_KHR_D3D11_enable` - Direct3D 11 graphics API support
- `XR_EXT_debug_utils` - Debug utilities for development
- `XR_NVX1_opaque_data_channel` - Custom data channel for Apple Vision Pro communication

## Building

1. Open `StreamingSession-OpenXRSample.sln` in Visual Studio 2022 or newer
2. Restore NuGet packages (should happen automatically)
3. Build the solution (Ctrl+Shift+B) or click Build > Build Solution

## Requirements

- Visual Studio 2022 or newer (Windows only)
- Windows 10 SDK (10.0 or newer)
- OpenXR Loader (automatically included via NuGet package)
- Direct3D 11 capable graphics card
- OpenXR runtime (e.g., SteamVR, Windows Mixed Reality, or Apple Vision Pro runtime)

## Running the Application

### Immersive Mode (Default)
```bash
StreamingSession-OpenXRSample.exe
```

### iOS Mode
```bash
StreamingSession-OpenXRSample.exe -iOS
```

The application will:
1. Initialize OpenXR with the specified form factor
2. Create a Direct3D 11 device and swapchains
3. Set up the opaque data channel for Apple Vision Pro communication
4. Display a spectator window showing the VR view
5. Render an animated 3D cube in the VR environment
6. Send periodic messages to Apple Vision Pro when connected

## Architecture

### Main Components

- **OpenXR Initialization** (`openxr_init`): Sets up the OpenXR instance, session, and extensions
- **Direct3D 11 Setup** (`d3d_init`): Initializes the D3D11 device and creates rendering resources
- **Render Loop** (`openxr_render_frame`): Handles frame rendering and composition
- **Message Channel** (`MessageChannel.cpp`): Manages bidirectional data communication
- **Window View** (`window_present_vr_view`): Provides spectator view of VR content

### Communication Flow

1. Application creates an opaque data channel with a unique UUID
2. Connection is established asynchronously when Apple Vision Pro connects
3. Once connected, the application can send/receive custom data
4. Messages are sent every 90 frames when the channel is active

## Code Structure

### Rendering Pipeline
1. `xrWaitFrame` - Wait for next frame timing
2. `xrBeginFrame` - Begin frame rendering
3. `xrLocateViews` - Get current view transforms
4. For each view:
   - Acquire swapchain image
   - Render 3D scene with proper projection
   - Release swapchain image
5. `xrEndFrame` - Submit rendered layers

### Shader System
- Vertex shader transforms geometry with world and view-projection matrices
- Per-vertex lighting with configurable directional light
- Simple Phong-style ambient + diffuse lighting model

## Customization

### Changing the 3D Scene
Modify the `screen_verts`, `screen_inds`, and `screen_shader_code` in main.cpp:200 to render different geometry.

### Adjusting the Message Protocol
Update `MessageChannel.cpp` to implement custom message formats and handling logic.

### Modifying Render Settings
- Cube position and scale: main.cpp:980
- Background color: main.cpp:869
- Animation speed: main.cpp:977
- Camera near/far planes: main.cpp:954

## Troubleshooting

### OpenXR Runtime Not Found
- Ensure an OpenXR runtime is installed and active
- Check Windows Mixed Reality Portal or SteamVR settings

### Connection Issues
- Verify the Apple Vision Pro app is running and ready to connect
- Check that the opaque data channel UUID matches on both sides
- Review debug output in Visual Studio's Output window

### Rendering Issues
- Verify Direct3D 11 is properly initialized
- Check that graphics drivers are up to date
- Review shader compilation errors in debug output
