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

#pragma comment(lib, "D3D11.lib")
#pragma comment(lib, "D3dcompiler.lib")
#pragma comment(lib, "Dxgi.lib")

#define XR_USE_PLATFORM_WIN32
#define XR_USE_GRAPHICS_API_D3D11

#include <dxgi1_2.h>
#include <d3d11.h>
#include <directxmath.h>
#include <d3dcompiler.h>
#include <openxr/openxr.h>
#include <openxr/openxr_platform.h>

#include <thread>
#include <vector>
#include <algorithm>
#include <atomic>
#include <windows.h>
#include "MessageChannel.h"

using namespace std;
using namespace DirectX;

struct swapchain_surfdata_t {
	ID3D11DepthStencilView* depth_view;
	ID3D11RenderTargetView* target_view;
};

struct swapchain_t {
	XrSwapchain                      handle;
	int32_t                          width;
	int32_t                          height;
	vector<XrSwapchainImageD3D11KHR> surface_images;
	vector<swapchain_surfdata_t>     surface_data;
};

struct input_state_t {
	XrActionSet actionSet;
	XrAction    poseAction;
	XrAction    selectAction;
	XrPath      handSubactionPath[2];
	XrSpace     handSpace[2];
	XrPosef     handPose[2];
	XrBool32    renderHand[2];
	XrBool32    handSelect[2];
};

PFN_xrGetD3D11GraphicsRequirementsKHR ext_xrGetD3D11GraphicsRequirementsKHR = nullptr;
PFN_xrCreateDebugUtilsMessengerEXT    ext_xrCreateDebugUtilsMessengerEXT    = nullptr;
PFN_xrDestroyDebugUtilsMessengerEXT   ext_xrDestroyDebugUtilsMessengerEXT   = nullptr;

struct app_transform_buffer_t {
	XMFLOAT4X4 world;
	XMFLOAT4X4 viewproj;
};

XrFormFactor            app_config_form = XR_FORM_FACTOR_HEAD_MOUNTED_DISPLAY;
XrViewConfigurationType app_config_view = XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO;
bool                    app_is_ios_mode = false;

ID3D11VertexShader*    app_vshader;
ID3D11PixelShader*     app_pshader;
ID3D11InputLayout*     app_shader_layout;
ID3D11Buffer*          app_constant_buffer;
ID3D11Buffer*          app_vertex_buffer;
ID3D11Buffer*          app_index_buffer;
ID3D11RasterizerState* app_rasterizer_state;

void app_init();
void app_draw(XrCompositionLayerProjectionView& layerView);

const XrPosef              xr_pose_identity = {{0, 0, 0, 1}, {0, 0, 0}};
XrSession                  xr_session       = {};
XrInstance                 xr_instance      = {};
XrSessionState             xr_session_state = XR_SESSION_STATE_UNKNOWN;
bool                       xr_running       = false;
XrSpace                    xr_app_space     = {};
input_state_t              xr_input         = {};
XrDebugUtilsMessengerEXT   xr_debug         = {};
XrSystemId                 xr_system_id     = XR_NULL_SYSTEM_ID;

vector<XrView>                  xr_views;
vector<XrViewConfigurationView> xr_config_views;
vector<swapchain_t>             xr_swapchains;

bool openxr_init(const char* app_name, int64_t swapchain_format);
void openxr_shutdown();
void openxr_poll_events(bool& exit);
void openxr_render_frame();
bool openxr_render_layer(XrTime predictedTime, vector<XrCompositionLayerProjectionView>& projectionViews, XrCompositionLayerProjection& layer);

ID3D11Device*        d3d_device        = nullptr;
ID3D11DeviceContext* d3d_context       = nullptr;
int64_t              d3d_swapchain_fmt = DXGI_FORMAT_B8G8R8A8_UNORM_SRGB;

// Window swap chain for spectator view
IDXGISwapChain*         window_swapchain = nullptr;
ID3D11RenderTargetView* window_rtv        = nullptr;
UINT                    window_width      = 0;
UINT                    window_height     = 0;

bool d3d_init(LUID& adapter_luid);
void d3d_shutdown();
IDXGIAdapter1* d3d_get_adapter(LUID& adapter_luid);
swapchain_surfdata_t d3d_make_surface_data(XrBaseInStructure& swapchainImage);
void d3d_render_layer(XrCompositionLayerProjectionView& layerView, swapchain_surfdata_t& surface);
void d3d_swapchain_destroy(swapchain_t& swapchain);
XMMATRIX d3d_xr_projection(XrFovf fov, float clip_near, float clip_far);
ID3DBlob* d3d_compile_shader(const char* hlsl, const char* entrypoint, const char* target);
bool window_swapchain_init();
void window_present_vr_view();
void window_handle_resize();

// Cube shader with lighting
constexpr char screen_shader_code[] = R"_(
cbuffer TransformBuffer : register(b0) {
	float4x4 world;
	float4x4 viewproj;
};

struct vsIn {
	float3 pos : SV_POSITION;
	float3 color : COLOR;
	float3 normal : NORMAL;
};

struct psIn {
	float4 pos: SV_POSITION;
	float3 color: COLOR;
};

psIn vs(vsIn input) {
	psIn output;
	output.pos = mul(float4(input.pos, 1), world);
	output.pos = mul(output.pos, viewproj);

	// Lighting calculation
	float3 lightDir = normalize(float3(0.5, 0.8, 0.3)); // Light from top-front-right
	float3 worldNormal = mul(input.normal, (float3x3)world); // Transform normal to world space
	worldNormal = normalize(worldNormal);

	// Diffuse lighting (dot product of normal and light direction)
	float diffuse = max(dot(worldNormal, lightDir), 0.0);

	// Ambient + Diffuse lighting
	float ambient = 0.3; // Base ambient light
	float lighting = ambient + (diffuse * 0.7); // 30% ambient + 70% diffuse

	// Apply lighting to color
	output.color = input.color * lighting;

	return output;
}

float4 ps(psIn input) : SV_TARGET {
    return float4(input.color, 1.0);
}

)_";

// Cube geometry
float screen_verts[] = {
	// Position (x, y, z) + Color (r, g, b) + Normal (x, y, z) - White/Light gray
	// Light gray RGB: (0.95, 0.95, 0.95) - very light gray, almost white
	// Front face (normal pointing forward: 0, 0, 1)
	-0.5f, -0.5f,  0.5f,   0.95f, 0.95f, 0.95f,   0.0f, 0.0f, 1.0f,  // 0
	 0.5f, -0.5f,  0.5f,   0.95f, 0.95f, 0.95f,   0.0f, 0.0f, 1.0f,  // 1
	 0.5f,  0.5f,  0.5f,   0.95f, 0.95f, 0.95f,   0.0f, 0.0f, 1.0f,  // 2
	-0.5f,  0.5f,  0.5f,   0.95f, 0.95f, 0.95f,   0.0f, 0.0f, 1.0f,  // 3
	// Back face (normal pointing backward: 0, 0, -1)
	-0.5f, -0.5f, -0.5f,   0.95f, 0.95f, 0.95f,   0.0f, 0.0f, -1.0f,  // 4
	 0.5f, -0.5f, -0.5f,   0.95f, 0.95f, 0.95f,   0.0f, 0.0f, -1.0f,  // 5
	 0.5f,  0.5f, -0.5f,   0.95f, 0.95f, 0.95f,   0.0f, 0.0f, -1.0f,  // 6
	-0.5f,  0.5f, -0.5f,   0.95f, 0.95f, 0.95f,   0.0f, 0.0f, -1.0f,  // 7
	// Left face (normal pointing left: -1, 0, 0)
	-0.5f, -0.5f, -0.5f,   0.95f, 0.95f, 0.95f,   -1.0f, 0.0f, 0.0f,  // 8
	-0.5f, -0.5f,  0.5f,   0.95f, 0.95f, 0.95f,   -1.0f, 0.0f, 0.0f,  // 9
	-0.5f,  0.5f,  0.5f,   0.95f, 0.95f, 0.95f,   -1.0f, 0.0f, 0.0f,  // 10
	-0.5f,  0.5f, -0.5f,   0.95f, 0.95f, 0.95f,   -1.0f, 0.0f, 0.0f,  // 11
	// Right face (normal pointing right: 1, 0, 0)
	 0.5f, -0.5f, -0.5f,   0.95f, 0.95f, 0.95f,   1.0f, 0.0f, 0.0f,  // 12
	 0.5f, -0.5f,  0.5f,   0.95f, 0.95f, 0.95f,   1.0f, 0.0f, 0.0f,  // 13
	 0.5f,  0.5f,  0.5f,   0.95f, 0.95f, 0.95f,   1.0f, 0.0f, 0.0f,  // 14
	 0.5f,  0.5f, -0.5f,   0.95f, 0.95f, 0.95f,   1.0f, 0.0f, 0.0f,  // 15
	// Top face (normal pointing up: 0, 1, 0)
	-0.5f,  0.5f, -0.5f,   0.95f, 0.95f, 0.95f,   0.0f, 1.0f, 0.0f,  // 16
	-0.5f,  0.5f,  0.5f,   0.95f, 0.95f, 0.95f,   0.0f, 1.0f, 0.0f,  // 17
	 0.5f,  0.5f,  0.5f,   0.95f, 0.95f, 0.95f,   0.0f, 1.0f, 0.0f,  // 18
	 0.5f,  0.5f, -0.5f,   0.95f, 0.95f, 0.95f,   0.0f, 1.0f, 0.0f,  // 19
	// Bottom face (normal pointing down: 0, -1, 0)
	-0.5f, -0.5f, -0.5f,   0.95f, 0.95f, 0.95f,   0.0f, -1.0f, 0.0f,  // 20
	-0.5f, -0.5f,  0.5f,   0.95f, 0.95f, 0.95f,   0.0f, -1.0f, 0.0f,  // 21
	 0.5f, -0.5f,  0.5f,   0.95f, 0.95f, 0.95f,   0.0f, -1.0f, 0.0f,  // 22
	 0.5f, -0.5f, -0.5f,   0.95f, 0.95f, 0.95f,   0.0f, -1.0f, 0.0f,  // 23
};

uint16_t screen_inds[] = {
	// Front face
	0, 1, 2,  0, 2, 3,
	// Back face
	5, 4, 7,  5, 7, 6,
	// Left face
	8, 9, 10,  8, 10, 11,
	// Right face
	12, 13, 14,  12, 14, 15,
	// Top face
	16, 17, 18,  16, 18, 19,
	// Bottom face
	20, 21, 22,  20, 22, 23
};

HWND g_debug_window = nullptr;

LRESULT CALLBACK DebugWindowProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam) {
	switch (msg) {
	case WM_PAINT: {
		PAINTSTRUCT ps;
		HDC hdc = BeginPaint(hwnd, &ps);

		// Set text color to green
		SetTextColor(hdc, RGB(0, 255, 0));
		SetBkColor(hdc, RGB(30, 30, 30));

		// Draw text
		const char* text = "StreamingSession OpenXR App is running";
		TextOutA(hdc, 20, 20, text, strlen(text));

		EndPaint(hwnd, &ps);
		break;
	}
	case WM_CLOSE:
		DestroyWindow(hwnd);
		return 0;
	case WM_DESTROY:
		PostQuitMessage(0);
		return 0;
	}
	return DefWindowProc(hwnd, msg, wParam, lParam);
}

void create_window() {
	WNDCLASS wc = {};
	wc.lpfnWndProc = DebugWindowProc;
	wc.hInstance = GetModuleHandle(nullptr);
	wc.lpszClassName = "DebugWindow";
	wc.hbrBackground = CreateSolidBrush(RGB(30, 30, 30));
	wc.hCursor = LoadCursor(nullptr, IDC_ARROW);

	RegisterClass(&wc);

	g_debug_window = CreateWindowA(
		"DebugWindow",
		"StreamingSession OpenXR App",
		WS_OVERLAPPEDWINDOW,
		100, 100, 600, 600,
		nullptr, nullptr, GetModuleHandle(nullptr), nullptr
	);

	ShowWindow(g_debug_window, SW_SHOW);
	UpdateWindow(g_debug_window);
}

int __stdcall wWinMain(HINSTANCE, HINSTANCE, LPWSTR cmdLine, int) {

	// Parse command line arguments                                                                                                                                                                                                                         
	if (cmdLine && wcsstr(cmdLine, L"-iOS")) {
		app_is_ios_mode = true;
		app_config_form = XR_FORM_FACTOR_HANDHELD_DISPLAY;
		app_config_view = XR_VIEW_CONFIGURATION_TYPE_PRIMARY_MONO;
		OutputDebugStringA("Running in iOS mode: XR_FORM_FACTOR_HANDHELD_DISPLAY + XR_VIEW_CONFIGURATION_TYPE_PRIMARY_MONO\n");
	} else {
		OutputDebugStringA("Running in Immersive Mode: XR_FORM_FACTOR_HEAD_MOUNTED_DISPLAY + XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO\n");
	}

	create_window();

	if (!openxr_init("3D Cube", d3d_swapchain_fmt)) {
		d3d_shutdown();
		MessageBox(nullptr, "OpenXR initialization failed\n", "Error", 1);
		return 1;
	}
	app_init();

	// Initialize window swap chain for spectator view
	if (!window_swapchain_init()) {
		OutputDebugStringA("Warning: Failed to create window swap chain\n");
	}

	// Start connection process asynchronously - NON-BLOCKING
	if (xr_opaque_channel != XR_NULL_HANDLE) {
		xr_opaque_connection_thread = std::thread(opaque_channel_connect_async);
	}

	bool quit = false;
	while (!quit) {

		MSG msg;
		while (PeekMessage(&msg, nullptr, 0, 0, PM_REMOVE)) {
			if (msg.message == WM_QUIT) {
				quit = true;
			}
			TranslateMessage(&msg);
			DispatchMessage(&msg);
		}

		if (quit) break;
		
		openxr_poll_events(quit);
		static int frame_counter = 0;
		static int message_number = 0;

		if (xr_running) {
			openxr_render_frame();

			// Render to window for spectator view
			window_present_vr_view();

			if (xr_session_state != XR_SESSION_STATE_VISIBLE &&
				xr_session_state != XR_SESSION_STATE_FOCUSED) {
				this_thread::sleep_for(chrono::milliseconds(250));
			}

			// Only send data if connected
			frame_counter++;
			if (xr_opaque_connected && frame_counter >= 90) {
				char message[128];
				sprintf_s(message, "Message #%d: OpenXR application data", message_number);
				opaque_channel_send_data((const uint8_t*)message, strlen(message) + 1);
				message_number++;
			}
		}
	}

	// Cleanup
	xr_opaque_connecting = false;
	if (xr_opaque_connection_thread.joinable()) {
		xr_opaque_connection_thread.join();
	}
	opaque_channel_shutdown();
	openxr_shutdown();
	d3d_shutdown();
	return 0;
}

bool openxr_init(const char* app_name, int64_t swapchain_format) {

	SetProcessDPIAware();
	vector<const char*> use_extensions;
	const char* ask_extensions[] = { 
		XR_KHR_D3D11_ENABLE_EXTENSION_NAME, // Use Direct3D11 for rendering
		XR_EXT_DEBUG_UTILS_EXTENSION_NAME,  // Debug utils for extra info
		"XR_NVX1_opaque_data_channel",
	};

	uint32_t ext_count = 0;
	xrEnumerateInstanceExtensionProperties(nullptr, 0, &ext_count, nullptr);
	vector<XrExtensionProperties> xr_exts(ext_count, { XR_TYPE_EXTENSION_PROPERTIES });
	xrEnumerateInstanceExtensionProperties(nullptr, ext_count, &ext_count, xr_exts.data());

	OutputDebugStringA("OpenXR extensions available:\n");
	for (size_t i = 0; i < xr_exts.size(); i++) {
		OutputDebugStringA(xr_exts[i].extensionName);

		for (int32_t ask = 0; ask < _countof(ask_extensions); ask++) {
			if (strcmp(ask_extensions[ask], xr_exts[i].extensionName) == 0) {
				use_extensions.push_back(ask_extensions[ask]);
				break;
			}
		}
	}

	if (!std::any_of(use_extensions.begin(), use_extensions.end(),
		[](const char* ext) {
			return strcmp(ext, XR_KHR_D3D11_ENABLE_EXTENSION_NAME) == 0;
		}))
		return false;

	// Initialize OpenXR with the extensions we've found
	XrInstanceCreateInfo createInfo = { XR_TYPE_INSTANCE_CREATE_INFO };
	createInfo.enabledExtensionCount      = use_extensions.size();
	createInfo.enabledExtensionNames      = use_extensions.data();
	createInfo.applicationInfo.apiVersion = XR_CURRENT_API_VERSION;
	strcpy_s(createInfo.applicationInfo.applicationName, app_name);
	xrCreateInstance(&createInfo, &xr_instance);

	// Check if OpenXR is available on this system. If null, an OpenXR runtime
	// must be installed and activated.
	if (xr_instance == nullptr)
		return false;

	xrGetInstanceProcAddr(xr_instance, "xrCreateDebugUtilsMessengerEXT", (PFN_xrVoidFunction*)(&ext_xrCreateDebugUtilsMessengerEXT));
	xrGetInstanceProcAddr(xr_instance, "xrDestroyDebugUtilsMessengerEXT", (PFN_xrVoidFunction*)(&ext_xrDestroyDebugUtilsMessengerEXT));
	xrGetInstanceProcAddr(xr_instance, "xrGetD3D11GraphicsRequirementsKHR", (PFN_xrVoidFunction*)(&ext_xrGetD3D11GraphicsRequirementsKHR));

	// Opaque data channel APIs
	xrGetInstanceProcAddr(xr_instance, "xrCreateOpaqueDataChannelNV", (PFN_xrVoidFunction*)(&ext_xrCreateOpaqueDataChannelNV));
	xrGetInstanceProcAddr(xr_instance, "xrDestroyOpaqueDataChannelNV", (PFN_xrVoidFunction*)(&ext_xrDestroyOpaqueDataChannelNV));
	xrGetInstanceProcAddr(xr_instance, "xrGetOpaqueDataChannelStateNV", (PFN_xrVoidFunction*)(&ext_xrGetOpaqueDataChannelStateNV));
	xrGetInstanceProcAddr(xr_instance, "xrShutdownOpaqueDataChannelNV", (PFN_xrVoidFunction*)(&ext_xrShutdownOpaqueDataChannelNV));
	xrGetInstanceProcAddr(xr_instance, "xrSendOpaqueDataChannelNV", (PFN_xrVoidFunction*)(&ext_xrSendOpaqueDataChannelNV));
	xrGetInstanceProcAddr(xr_instance, "xrReceiveOpaqueDataChannelNV", (PFN_xrVoidFunction*)(&ext_xrReceiveOpaqueDataChannelNV));

	// Debug output
	if (ext_xrCreateOpaqueDataChannelNV) {
		OutputDebugStringA("Successfully loaded opaque data channel functions\n");
	}
	else {
		OutputDebugStringA("Failed to load opaque data channel functions\n");
	}

	// Here's some extra information about the message types and severities:
	// https://www.khronos.org/registry/OpenXR/specs/1.0/html/xrspec.html#debug-message-categorization
	XrDebugUtilsMessengerCreateInfoEXT debug_info = { XR_TYPE_DEBUG_UTILS_MESSENGER_CREATE_INFO_EXT };
	debug_info.messageTypes =
		XR_DEBUG_UTILS_MESSAGE_TYPE_GENERAL_BIT_EXT     |
		XR_DEBUG_UTILS_MESSAGE_TYPE_VALIDATION_BIT_EXT  |
		XR_DEBUG_UTILS_MESSAGE_TYPE_PERFORMANCE_BIT_EXT |
		XR_DEBUG_UTILS_MESSAGE_TYPE_CONFORMANCE_BIT_EXT;
	debug_info.messageSeverities =
		XR_DEBUG_UTILS_MESSAGE_SEVERITY_VERBOSE_BIT_EXT |
		XR_DEBUG_UTILS_MESSAGE_SEVERITY_INFO_BIT_EXT |
		XR_DEBUG_UTILS_MESSAGE_SEVERITY_WARNING_BIT_EXT |
		XR_DEBUG_UTILS_MESSAGE_SEVERITY_ERROR_BIT_EXT;
	debug_info.userCallback = [](XrDebugUtilsMessageSeverityFlagsEXT severity, XrDebugUtilsMessageTypeFlagsEXT types, const XrDebugUtilsMessengerCallbackDataEXT* msg, void* user_data) {
		printf("%s: %s\n", msg->functionName, msg->message);

		char text[512];
		sprintf_s(text, "%s: %s", msg->functionName, msg->message);
		OutputDebugStringA(text);
		return (XrBool32)XR_FALSE;
	};

	if (ext_xrCreateDebugUtilsMessengerEXT)
		ext_xrCreateDebugUtilsMessengerEXT(xr_instance, &debug_info, &xr_debug);
	
	// Request a form factor from the device (HMD, Handheld, etc.)
	XrSystemGetInfo systemInfo = { XR_TYPE_SYSTEM_GET_INFO };
	systemInfo.formFactor = app_config_form;
	xrGetSystem(xr_instance, &systemInfo, &xr_system_id);

	XrGraphicsRequirementsD3D11KHR requirement = { XR_TYPE_GRAPHICS_REQUIREMENTS_D3D11_KHR };
	ext_xrGetD3D11GraphicsRequirementsKHR(xr_instance, xr_system_id, &requirement);
	if (!d3d_init(requirement.adapterLuid))
		return false;

	XrGraphicsBindingD3D11KHR binding = { XR_TYPE_GRAPHICS_BINDING_D3D11_KHR };
	binding.device = d3d_device;
	XrSessionCreateInfo sessionInfo = { XR_TYPE_SESSION_CREATE_INFO };
	sessionInfo.next     = &binding;
	sessionInfo.systemId = xr_system_id;
	xrCreateSession(xr_instance, &sessionInfo, &xr_session);

	// Unable to start a session, may not have an MR device attached or ready
	if (xr_session == nullptr)
		return false;

	XrReferenceSpaceCreateInfo ref_space = { XR_TYPE_REFERENCE_SPACE_CREATE_INFO };
	ref_space.poseInReferenceSpace = xr_pose_identity;
	ref_space.referenceSpaceType   = XR_REFERENCE_SPACE_TYPE_LOCAL;
	xrCreateReferenceSpace(xr_session, &ref_space, &xr_app_space);

	uint32_t view_count = 0;
	xrEnumerateViewConfigurationViews(xr_instance, xr_system_id, app_config_view, 0, &view_count, nullptr);
	xr_config_views.resize(view_count, { XR_TYPE_VIEW_CONFIGURATION_VIEW });
	xr_views.resize(view_count, { XR_TYPE_VIEW });
	xrEnumerateViewConfigurationViews(xr_instance, xr_system_id, app_config_view, view_count, &view_count, xr_config_views.data());
	for (uint32_t i = 0; i < view_count; i++) {
		XrViewConfigurationView& view           = xr_config_views[i];
		XrSwapchainCreateInfo    swapchain_info = { XR_TYPE_SWAPCHAIN_CREATE_INFO };
		XrSwapchain              handle;
		swapchain_info.arraySize   = 1;
		swapchain_info.mipCount    = 1;
		swapchain_info.faceCount   = 1;
		swapchain_info.format      = swapchain_format;
		swapchain_info.width       = view.recommendedImageRectWidth;
		swapchain_info.height      = view.recommendedImageRectHeight;
		swapchain_info.sampleCount = view.recommendedSwapchainSampleCount;
		swapchain_info.usageFlags  = XR_SWAPCHAIN_USAGE_SAMPLED_BIT | XR_SWAPCHAIN_USAGE_COLOR_ATTACHMENT_BIT;
		xrCreateSwapchain(xr_session, &swapchain_info, &handle);

		uint32_t surface_count = 0;
		xrEnumerateSwapchainImages(handle, 0, &surface_count, nullptr);

		swapchain_t swapchain = {};
		swapchain.width  = swapchain_info.width;
		swapchain.height = swapchain_info.height;
		swapchain.handle = handle;
		swapchain.surface_images.resize(surface_count, { XR_TYPE_SWAPCHAIN_IMAGE_D3D11_KHR });
		swapchain.surface_data.resize(surface_count);
		xrEnumerateSwapchainImages(swapchain.handle, surface_count, &surface_count, (XrSwapchainImageBaseHeader*)swapchain.surface_images.data());
		for (uint32_t i = 0; i < surface_count; i++) {
			swapchain.surface_data[i] = d3d_make_surface_data((XrBaseInStructure&)swapchain.surface_images[i]);
		}
		xr_swapchains.push_back(swapchain);
	}

	if (!opaque_channel_init()) {
		OutputDebugStringA("Warning: Failed to initialize opaque data channel\n");
	}

	return true;
}

void openxr_shutdown() {

	for (int32_t i = 0; i < xr_swapchains.size(); i++) {
		xrDestroySwapchain(xr_swapchains[i].handle);
		d3d_swapchain_destroy(xr_swapchains[i]);
	}
	xr_swapchains.clear();

	if (xr_input.actionSet != XR_NULL_HANDLE) {
		if (xr_input.handSpace[0] != XR_NULL_HANDLE) xrDestroySpace(xr_input.handSpace[0]);
		if (xr_input.handSpace[1] != XR_NULL_HANDLE) xrDestroySpace(xr_input.handSpace[1]);
		xrDestroyActionSet(xr_input.actionSet);
	}
	if (xr_app_space != XR_NULL_HANDLE) xrDestroySpace   (xr_app_space);
	if (xr_session   != XR_NULL_HANDLE) xrDestroySession (xr_session);
	if (xr_debug     != XR_NULL_HANDLE) ext_xrDestroyDebugUtilsMessengerEXT(xr_debug);
	if (xr_instance  != XR_NULL_HANDLE) xrDestroyInstance(xr_instance);
}

void openxr_poll_events(bool& exit) {
	exit = false;

	XrEventDataBuffer event_buffer = { XR_TYPE_EVENT_DATA_BUFFER };

	while (xrPollEvent(xr_instance, &event_buffer) == XR_SUCCESS) {
		switch (event_buffer.type) {
		case XR_TYPE_EVENT_DATA_SESSION_STATE_CHANGED: {
			XrEventDataSessionStateChanged* changed = (XrEventDataSessionStateChanged*)&event_buffer;
			xr_session_state = changed->state;

			// Session state change is where we can begin and end sessions, as well as find quit messages
			switch (xr_session_state) {
			case XR_SESSION_STATE_READY: {
				XrSessionBeginInfo begin_info = { XR_TYPE_SESSION_BEGIN_INFO };
				begin_info.primaryViewConfigurationType = app_config_view;
				xrBeginSession(xr_session, &begin_info);
				xr_running = true;
			} break;
			case XR_SESSION_STATE_STOPPING: {
				xr_running = false;
				xrEndSession(xr_session);
			} break;
			case XR_SESSION_STATE_EXITING: exit = true; break;
			case XR_SESSION_STATE_LOSS_PENDING: exit = true; break;
			}
		} break;
		case XR_TYPE_EVENT_DATA_INSTANCE_LOSS_PENDING: exit = true; return;
		}
		event_buffer = { XR_TYPE_EVENT_DATA_BUFFER };
	}
}

void openxr_render_frame() {

	XrFrameState frame_state = { XR_TYPE_FRAME_STATE };
	xrWaitFrame(xr_session, nullptr, &frame_state);
	xrBeginFrame(xr_session, nullptr);

	XrCompositionLayerBaseHeader* layer = nullptr;
	XrCompositionLayerProjection layer_proj = { XR_TYPE_COMPOSITION_LAYER_PROJECTION };
	vector<XrCompositionLayerProjectionView> views;
	bool session_active = xr_session_state == XR_SESSION_STATE_VISIBLE || xr_session_state == XR_SESSION_STATE_FOCUSED;
	if (session_active && openxr_render_layer(frame_state.predictedDisplayTime, views, layer_proj)) {
		layer = (XrCompositionLayerBaseHeader*)&layer_proj;
	}

	XrFrameEndInfo end_info{ XR_TYPE_FRAME_END_INFO };
	end_info.displayTime          = frame_state.predictedDisplayTime;
	end_info.environmentBlendMode = XR_ENVIRONMENT_BLEND_MODE_OPAQUE;
	end_info.layerCount           = layer == nullptr ? 0 : 1;
	end_info.layers               = &layer;
	xrEndFrame(xr_session, &end_info);
}

bool openxr_render_layer(XrTime predictedTime, vector<XrCompositionLayerProjectionView>& views, XrCompositionLayerProjection& layer) {

	// Find the state and location of each viewpoint at the predicted time
	uint32_t         view_count  = 0;
	XrViewState      view_state  = { XR_TYPE_VIEW_STATE };
	XrViewLocateInfo locate_info = { XR_TYPE_VIEW_LOCATE_INFO };
	locate_info.viewConfigurationType = app_config_view;
	locate_info.displayTime           = predictedTime;
	locate_info.space                 = xr_app_space;
	xrLocateViews(xr_session, &locate_info, &view_state, (uint32_t)xr_views.size(), &view_count, xr_views.data());
	views.resize(view_count);

	for (uint32_t i = 0; i < view_count; i++) {

		uint32_t img_id;
		XrSwapchainImageAcquireInfo acquire_info = { XR_TYPE_SWAPCHAIN_IMAGE_ACQUIRE_INFO };
		xrAcquireSwapchainImage(xr_swapchains[i].handle, &acquire_info, &img_id);

		XrSwapchainImageWaitInfo wait_info = { XR_TYPE_SWAPCHAIN_IMAGE_WAIT_INFO };
		wait_info.timeout = XR_INFINITE_DURATION;
		xrWaitSwapchainImage(xr_swapchains[i].handle, &wait_info);

		// Set up rendering information for the current viewpoint
		views[i] = { XR_TYPE_COMPOSITION_LAYER_PROJECTION_VIEW };
		views[i].pose = xr_views[i].pose;
		views[i].fov  = xr_views[i].fov;
		views[i].subImage.swapchain        = xr_swapchains[i].handle;
		views[i].subImage.imageRect.offset = { 0, 0 };
		views[i].subImage.imageRect.extent = { xr_swapchains[i].width, xr_swapchains[i].height };

		d3d_render_layer(views[i], xr_swapchains[i].surface_data[img_id]);

		XrSwapchainImageReleaseInfo release_info = { XR_TYPE_SWAPCHAIN_IMAGE_RELEASE_INFO };
		xrReleaseSwapchainImage(xr_swapchains[i].handle, &release_info);
	}

	layer.space     = xr_app_space;
	layer.viewCount = (uint32_t)views.size();
	layer.views     = views.data();
	return true;
}

bool d3d_init(LUID& adapter_luid) {
	IDXGIAdapter1* adapter = d3d_get_adapter(adapter_luid);
	D3D_FEATURE_LEVEL featureLevels[] = { D3D_FEATURE_LEVEL_11_0 };

	if (adapter == nullptr)
		return false;
	if (FAILED(D3D11CreateDevice(adapter, D3D_DRIVER_TYPE_UNKNOWN, 0, 0, featureLevels, _countof(featureLevels), D3D11_SDK_VERSION, &d3d_device, nullptr, &d3d_context)))
		return false;

	adapter->Release();
	return true;
}

bool window_swapchain_init() {
	if (!g_debug_window || !d3d_device) {
		return false;
	}

	// Get window dimensions
	RECT rect;
	GetClientRect(g_debug_window, &rect);
	UINT width = rect.right - rect.left;
	UINT height = rect.bottom - rect.top;

	// Create DXGI factory
	IDXGIFactory1* factory = nullptr;
	if (FAILED(CreateDXGIFactory1(__uuidof(IDXGIFactory1), (void**)&factory))) {
		return false;
	}

	// Create swap chain descriptor
	DXGI_SWAP_CHAIN_DESC swap_desc = {};
	swap_desc.BufferCount = 2;
	swap_desc.BufferDesc.Width = width;
	swap_desc.BufferDesc.Height = height;
	swap_desc.BufferDesc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
	swap_desc.BufferDesc.RefreshRate.Numerator = 60;
	swap_desc.BufferDesc.RefreshRate.Denominator = 1;
	swap_desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
	swap_desc.OutputWindow = g_debug_window;
	swap_desc.SampleDesc.Count = 1;
	swap_desc.Windowed = TRUE;
	swap_desc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD;

	// Create swap chain
	if (FAILED(factory->CreateSwapChain(d3d_device, &swap_desc, &window_swapchain))) {
		factory->Release();
		return false;
	}

	factory->Release();

	// Get back buffer and create render target view
	ID3D11Texture2D* back_buffer = nullptr;
	if (FAILED(window_swapchain->GetBuffer(0, __uuidof(ID3D11Texture2D), (void**)&back_buffer))) {
		return false;
	}

	HRESULT hr = d3d_device->CreateRenderTargetView(back_buffer, nullptr, &window_rtv);
	back_buffer->Release();

	if (FAILED(hr)) {
		return false;
	}

	// Store dimensions
	window_width = width;
	window_height = height;

	return true;
}

void window_handle_resize() {
	if (!window_swapchain || !g_debug_window) {
		return;
	}

	// Get new window dimensions
	RECT rect;
	GetClientRect(g_debug_window, &rect);
	UINT new_width = rect.right - rect.left;
	UINT new_height = rect.bottom - rect.top;

	// Only resize if dimensions actually changed
	if (new_width == window_width && new_height == window_height) {
		return;
	}

	if (new_width == 0 || new_height == 0) {
		return;
	}

	// Release old render target view
	if (window_rtv) {
		window_rtv->Release();
		window_rtv = nullptr;
	}

	// Resize swap chain buffers
	HRESULT hr = window_swapchain->ResizeBuffers(0, new_width, new_height, DXGI_FORMAT_UNKNOWN, 0);
	if (FAILED(hr)) {
		OutputDebugStringA("Failed to resize swap chain buffers\n");
		return;
	}

	// Get new back buffer and create render target view
	ID3D11Texture2D* back_buffer = nullptr;
	if (FAILED(window_swapchain->GetBuffer(0, __uuidof(ID3D11Texture2D), (void**)&back_buffer))) {
		return;
	}

	hr = d3d_device->CreateRenderTargetView(back_buffer, nullptr, &window_rtv);
	back_buffer->Release();

	if (SUCCEEDED(hr)) {
		window_width = new_width;
		window_height = new_height;
	}
}

void window_present_vr_view() {
	if (!window_swapchain || !window_rtv || xr_swapchains.empty()) {
		return;
	}

	// Handle window resize
	window_handle_resize();

	if (!window_rtv) return; // May have failed during resize

	// Get window dimensions
	RECT rect;
	GetClientRect(g_debug_window, &rect);
	UINT width = rect.right - rect.left;
	UINT height = rect.bottom - rect.top;

	if (width == 0 || height == 0) return;

	// Calculate aspect ratio
	float aspect = (float)width / (float)height;

	// Set viewport for window
	D3D11_VIEWPORT viewport = {};
	viewport.Width = (float)width;
	viewport.Height = (float)height;
	viewport.MinDepth = 0.0f;
	viewport.MaxDepth = 1.0f;
	d3d_context->RSSetViewports(1, &viewport);

	float clear_color[] = { 0.098f, 0.137f, 0.294f, 1.0f };
	d3d_context->ClearRenderTargetView(window_rtv, clear_color);
	d3d_context->OMSetRenderTargets(1, &window_rtv, nullptr);

	XrCompositionLayerProjectionView placeholder_view = { XR_TYPE_COMPOSITION_LAYER_PROJECTION_VIEW };

	float fov_vertical = 0.4f;
	float fov_horizontal = 2.0f * atanf(tanf(fov_vertical / 2.0f) * aspect);

	placeholder_view.fov.angleLeft = -fov_horizontal / 2.0f;
	placeholder_view.fov.angleRight = fov_horizontal / 2.0f;
	placeholder_view.fov.angleUp = fov_vertical / 2.0f;
	placeholder_view.fov.angleDown = -fov_vertical / 2.0f;

	placeholder_view.pose.orientation = { 0, 0, 0, 1 };
	placeholder_view.pose.position = { 0, -0.6f, 4.0f };

	app_draw(placeholder_view);

	// Present to window
	window_swapchain->Present(1, 0);
}

IDXGIAdapter1* d3d_get_adapter(LUID& adapter_luid) {
	// Turn the LUID into a specific graphics device adapter
	IDXGIAdapter1*     final_adapter = nullptr;
	IDXGIAdapter1*     curr_adapter  = nullptr;
	IDXGIFactory1*     dxgi_factory;
	DXGI_ADAPTER_DESC1 adapter_desc;

	CreateDXGIFactory1(__uuidof(IDXGIFactory1), (void**)(&dxgi_factory));

	int curr = 0;
	while (dxgi_factory->EnumAdapters1(curr++, &curr_adapter) == S_OK) {
		curr_adapter->GetDesc1(&adapter_desc);

		if (memcmp(&adapter_desc.AdapterLuid, &adapter_luid, sizeof(&adapter_luid)) == 0) {
			final_adapter = curr_adapter;
			break;
		}
		curr_adapter->Release();
		curr_adapter = nullptr;
	}
	dxgi_factory->Release();
	return final_adapter;
}

void d3d_shutdown() {
	if (window_rtv) { window_rtv->Release(); window_rtv = nullptr; }
	if (window_swapchain) { window_swapchain->Release(); window_swapchain = nullptr; }
	if (d3d_context) { d3d_context->Release(); d3d_context = nullptr; }
	if (d3d_device) { d3d_device->Release(); d3d_device = nullptr; }
}

swapchain_surfdata_t d3d_make_surface_data(XrBaseInStructure& swapchain_img) {
	swapchain_surfdata_t result = {};

	// Get information about the swapchain image created by OpenXR
	XrSwapchainImageD3D11KHR& d3d_swapchain_img = (XrSwapchainImageD3D11KHR&)swapchain_img;
	D3D11_TEXTURE2D_DESC      color_desc;
	d3d_swapchain_img.texture->GetDesc(&color_desc);

	// Create a view resource for the swapchain image target that we can use to set up rendering.
	D3D11_RENDER_TARGET_VIEW_DESC target_desc = {};
	target_desc.ViewDimension = D3D11_RTV_DIMENSION_TEXTURE2D;
	target_desc.Format        = (DXGI_FORMAT)d3d_swapchain_fmt;
	d3d_device->CreateRenderTargetView(d3d_swapchain_img.texture, &target_desc, &result.target_view);

	// Create a depth buffer that matches the swapchain dimensions
	ID3D11Texture2D*     depth_texture;
	D3D11_TEXTURE2D_DESC depth_desc = {};
	depth_desc.SampleDesc.Count = 1;
	depth_desc.MipLevels        = 1;
	depth_desc.Width            = color_desc.Width;
	depth_desc.Height           = color_desc.Height;
	depth_desc.ArraySize        = color_desc.ArraySize;
	depth_desc.Format           = DXGI_FORMAT_R32_TYPELESS;
	depth_desc.BindFlags        = D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_DEPTH_STENCIL;
	d3d_device->CreateTexture2D(&depth_desc, nullptr, &depth_texture);

	// Create a view resource for the depth buffer for rendering setup
	D3D11_DEPTH_STENCIL_VIEW_DESC stencil_desc = {};
	stencil_desc.ViewDimension = D3D11_DSV_DIMENSION_TEXTURE2D;
	stencil_desc.Format        = DXGI_FORMAT_D32_FLOAT;
	d3d_device->CreateDepthStencilView(depth_texture, &stencil_desc, &result.depth_view);

	// We don't need direct access to the ID3D11Texture2D object anymore, we only need the view
	depth_texture->Release();

	return result;
}

void d3d_render_layer(XrCompositionLayerProjectionView& view, swapchain_surfdata_t& surface) {
	XrRect2Di&     rect     = view.subImage.imageRect;
	D3D11_VIEWPORT viewport = CD3D11_VIEWPORT((float)rect.offset.x, (float)rect.offset.y, (float)rect.extent.width, (float)rect.extent.height);
	d3d_context->RSSetViewports(1, &viewport);

	// Clear swapchain color and depth targets and prepare for rendering
	// Navy blue background color
	float clear[] = { 0.098f, 0.137f, 0.294f, 1.0f }; // R, G, B, A
	d3d_context->ClearRenderTargetView(surface.target_view, clear);
	d3d_context->ClearDepthStencilView(surface.depth_view, D3D11_CLEAR_DEPTH | D3D11_CLEAR_STENCIL, 1.0f, 0);
	d3d_context->OMSetRenderTargets(1, &surface.target_view, surface.depth_view);

	app_draw(view);
}

void d3d_swapchain_destroy(swapchain_t& swapchain) {
	for (uint32_t i = 0; i < swapchain.surface_data.size(); i++) {
		swapchain.surface_data[i].depth_view ->Release();
		swapchain.surface_data[i].target_view->Release();
	}
}

XMMATRIX d3d_xr_projection(XrFovf fov, float clip_near, float clip_far) {
	const float left  = clip_near * tanf(fov.angleLeft);
	const float right = clip_near * tanf(fov.angleRight);
	const float down  = clip_near * tanf(fov.angleDown);
	const float up    = clip_near * tanf(fov.angleUp);

	return XMMatrixPerspectiveOffCenterRH(left, right, down, up, clip_near, clip_far);
}

ID3DBlob* d3d_compile_shader(const char* hlsl, const char* entrypoint, const char* target) {
	DWORD flags = D3DCOMPILE_PACK_MATRIX_COLUMN_MAJOR | D3DCOMPILE_ENABLE_STRICTNESS | D3DCOMPILE_WARNINGS_ARE_ERRORS;
#ifdef _DEBUG
	flags |= D3DCOMPILE_SKIP_OPTIMIZATION | D3DCOMPILE_DEBUG;
#else
	flags |= D3DCOMPILE_OPTIMIZATION_LEVEL3;
#endif

	ID3DBlob* compiled;
	ID3DBlob* errors;
	if (FAILED(D3DCompile(hlsl, strlen(hlsl), nullptr, nullptr, nullptr, entrypoint, target, flags, 0, &compiled, &errors)))
		printf("Error: D3DCompile failed %s", (char*)errors->GetBufferPointer());
	if (errors) errors->Release();

	return compiled;
}

void app_init() {
	// Compile shaders (use new cube shader code)
	ID3DBlob* vert_shader_blob = d3d_compile_shader(screen_shader_code, "vs", "vs_5_0");
	ID3DBlob* pixel_shader_blob = d3d_compile_shader(screen_shader_code, "ps", "ps_5_0");

	d3d_device->CreateVertexShader(vert_shader_blob->GetBufferPointer(), vert_shader_blob->GetBufferSize(), nullptr, &app_vshader);

	d3d_device->CreatePixelShader(pixel_shader_blob->GetBufferPointer(), pixel_shader_blob->GetBufferSize(), nullptr, &app_pshader);

	// Update vertex layout for color and normal coordinates
	D3D11_INPUT_ELEMENT_DESC vert_desc[] = {
		{"SV_POSITION", 0, DXGI_FORMAT_R32G32B32_FLOAT, 0, D3D11_APPEND_ALIGNED_ELEMENT, D3D11_INPUT_PER_VERTEX_DATA, 0},
		{"COLOR",       0, DXGI_FORMAT_R32G32B32_FLOAT, 0, D3D11_APPEND_ALIGNED_ELEMENT, D3D11_INPUT_PER_VERTEX_DATA, 0},
		{"NORMAL",      0, DXGI_FORMAT_R32G32B32_FLOAT, 0, D3D11_APPEND_ALIGNED_ELEMENT, D3D11_INPUT_PER_VERTEX_DATA, 0},
	};

	d3d_device->CreateInputLayout(vert_desc, (UINT)_countof(vert_desc), vert_shader_blob->GetBufferPointer(), vert_shader_blob->GetBufferSize(), &app_shader_layout);
	// Create GPU resources for cube
	D3D11_SUBRESOURCE_DATA vert_buff_data = { screen_verts };
	D3D11_SUBRESOURCE_DATA ind_buff_data = { screen_inds };
	CD3D11_BUFFER_DESC vert_buff_desc(sizeof(screen_verts), D3D11_BIND_VERTEX_BUFFER);
	CD3D11_BUFFER_DESC ind_buff_desc(sizeof(screen_inds), D3D11_BIND_INDEX_BUFFER);
	CD3D11_BUFFER_DESC const_buff_desc(sizeof(app_transform_buffer_t), D3D11_BIND_CONSTANT_BUFFER);

	d3d_device->CreateBuffer(&vert_buff_desc, &vert_buff_data, &app_vertex_buffer);
	d3d_device->CreateBuffer(&ind_buff_desc, &ind_buff_data, &app_index_buffer);
	d3d_device->CreateBuffer(&const_buff_desc, nullptr, &app_constant_buffer);

	// Create rasterizer state with no culling so all cube faces are visible
	D3D11_RASTERIZER_DESC raster_desc = {};
	raster_desc.FillMode = D3D11_FILL_SOLID;
	raster_desc.CullMode = D3D11_CULL_NONE;  // Disable back-face culling
	raster_desc.FrontCounterClockwise = FALSE;
	raster_desc.DepthClipEnable = TRUE;
	d3d_device->CreateRasterizerState(&raster_desc, &app_rasterizer_state);

}

void app_draw(XrCompositionLayerProjectionView& view) {
	static int frame_count = 0;
	frame_count++;

	// Set up camera matrices
	// Reading camera matrices from headset via OpenXR
	XMMATRIX mat_projection = d3d_xr_projection(view.fov, 0.05f, 100.0f);
	XMMATRIX mat_view = XMMatrixInverse(nullptr, XMMatrixAffineTransformation(
		DirectX::g_XMOne, DirectX::g_XMZero,
		XMLoadFloat4((XMFLOAT4*)&view.pose.orientation),
		XMLoadFloat3((XMFLOAT3*)&view.pose.position)));

	// Set shaders and buffers
	d3d_context->VSSetConstantBuffers(0, 1, &app_constant_buffer);
	d3d_context->VSSetShader(app_vshader, nullptr, 0);
	d3d_context->PSSetShader(app_pshader, nullptr, 0);

	// Set rasterizer state to disable culling
	d3d_context->RSSetState(app_rasterizer_state);

	// Set up cube mesh
	UINT strides[] = { sizeof(float) * 9 }; // pos(3) + color(3) + normal(3)
	UINT offsets[] = { 0 };
	d3d_context->IASetVertexBuffers(0, 1, &app_vertex_buffer, strides, offsets);
	d3d_context->IASetIndexBuffer(app_index_buffer, DXGI_FORMAT_R16_UINT, 0);
	d3d_context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
	d3d_context->IASetInputLayout(app_shader_layout);

	// Add rotation animation (single axis, slow spin)
	float angle = frame_count * 0.002f;
	XMMATRIX mat_rotation = XMMatrixRotationY(angle);

	XMMATRIX mat_model = XMMatrixScaling(0.7f, 0.7f, 0.7f) * mat_rotation * XMMatrixTranslation(0.0f, -0.6f, -2.0f);

	// Update shader constants
	app_transform_buffer_t transform_buffer;
	XMStoreFloat4x4(&transform_buffer.world, XMMatrixTranspose(mat_model));
	XMStoreFloat4x4(&transform_buffer.viewproj, XMMatrixTranspose(mat_view * mat_projection));

	d3d_context->UpdateSubresource(app_constant_buffer, 0, nullptr, &transform_buffer, 0, 0);
	d3d_context->DrawIndexed((UINT)_countof(screen_inds), 0, 0);
}