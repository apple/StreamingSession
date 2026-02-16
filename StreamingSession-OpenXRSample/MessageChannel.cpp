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

#include "MessageChannel.h"

#include <openxr/openxr.h>
#include <openxr/openxr_platform.h>
#include <thread>
#include <vector>
#include <algorithm>
#include <atomic>
#include <windows.h>

using namespace std;

PFN_xrCreateOpaqueDataChannelNV       ext_xrCreateOpaqueDataChannelNV       = nullptr;
PFN_xrDestroyOpaqueDataChannelNV      ext_xrDestroyOpaqueDataChannelNV      = nullptr;
PFN_xrGetOpaqueDataChannelStateNV     ext_xrGetOpaqueDataChannelStateNV     = nullptr;
PFN_xrShutdownOpaqueDataChannelNV     ext_xrShutdownOpaqueDataChannelNV     = nullptr;
PFN_xrSendOpaqueDataChannelNV         ext_xrSendOpaqueDataChannelNV         = nullptr;
PFN_xrReceiveOpaqueDataChannelNV      ext_xrReceiveOpaqueDataChannelNV      = nullptr;

XrOpaqueDataChannelNV xr_opaque_channel = XR_NULL_HANDLE;
std::atomic<bool>     xr_opaque_running{false};
std::atomic<bool>     xr_opaque_connected{false};
std::atomic<bool>     xr_opaque_connecting{false};
std::thread           xr_opaque_thread;
std::thread           xr_opaque_connection_thread;

bool opaque_channel_init() {
	if (!ext_xrCreateOpaqueDataChannelNV) {
		OutputDebugStringA("Opaque data channel functions not loaded\n");
		return false;
	}

	// Create a unique UUID for the channel
	XrGuid myUuid = {
		0x12345678, 0x1234, 0x1234,
		{0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0xF0}
	};

	XrOpaqueDataChannelCreateInfoNV createInfo = {
		XR_TYPE_OPAQUE_DATA_CHANNEL_CREATE_INFO_NV,
		nullptr,
		xr_system_id,
		myUuid
	};

	XrResult result = ext_xrCreateOpaqueDataChannelNV(xr_instance, &createInfo, &xr_opaque_channel);
	if (result != XR_SUCCESS) {
		char msg[256];
		sprintf_s(msg, "Failed to create opaque data channel: %d\n", result);
		OutputDebugStringA(msg);
		return false;
	}

	OutputDebugStringA("Opaque data channel created successfully\n");
	printf("Opaque data channel created successfully\n");
	return true;
}


bool opaque_channel_wait_connection() {
	XrOpaqueDataChannelStateNV state = {
		XR_TYPE_OPAQUE_DATA_CHANNEL_STATE_NV,
		nullptr
	};

	auto startTime = std::chrono::steady_clock::now();
	const int timeoutMs = 30000; // 30 seconds

	OutputDebugStringA("Waiting for CloudXR client to connect...\n");

	while (true) {
		XrResult result = ext_xrGetOpaqueDataChannelStateNV(xr_opaque_channel, &state);
		if (result != XR_SUCCESS) {
			char msg[256];
			sprintf_s(msg, "Failed to get channel state: %d\n", result);
			OutputDebugStringA(msg);
			return false;
		}

		switch (state.state) {
		case XR_OPAQUE_DATA_CHANNEL_STATUS_CONNECTED_NV:
			OutputDebugStringA("Opaque data channel connected!\n");
			return true;

		case XR_OPAQUE_DATA_CHANNEL_STATUS_DISCONNECTED_NV:
			OutputDebugStringA("Channel disconnected during connection attempt\n");
			return false;

		case XR_OPAQUE_DATA_CHANNEL_STATUS_CONNECTING_NV:
			// Still connecting, continue waiting
			break;

		default:
			char msg[256];
			sprintf_s(msg, "Unexpected channel state: %d\n", state.state);
			OutputDebugStringA(msg);
			break;
		}

		// Check timeout
		auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(
			std::chrono::steady_clock::now() - startTime).count();
		if (elapsed > timeoutMs) {
			OutputDebugStringA("Connection timeout\n");
			return false;
		}

		std::this_thread::sleep_for(std::chrono::milliseconds(100));
	}
}

void opaque_channel_connect_async() {
	xr_opaque_connecting = true;

	OutputDebugStringA("Starting async connection to CloudXR client...\n");

	XrOpaqueDataChannelStateNV state = {
		XR_TYPE_OPAQUE_DATA_CHANNEL_STATE_NV,
		nullptr
	};

	auto startTime = std::chrono::steady_clock::now();
	const int timeoutMs = 30000; // 30 seconds

	while (xr_opaque_connecting && !xr_opaque_connected) {
		XrResult result = ext_xrGetOpaqueDataChannelStateNV(xr_opaque_channel, &state);
		if (result != XR_SUCCESS) {
			char msg[256];
			sprintf_s(msg, "Failed to get channel state: %d\n", result);
			OutputDebugStringA(msg);
			break;
		}

		switch (state.state) {
		case XR_OPAQUE_DATA_CHANNEL_STATUS_CONNECTED_NV:
		{
			OutputDebugStringA("Opaque data channel connected!\n");
			xr_opaque_connected = true;
			xr_opaque_running = true;

			// Start receive loop
			xr_opaque_thread = std::thread(opaque_channel_receive_loop);

			// Send initial test data
			const uint8_t testData[] = { 0x01, 0x02, 0x03, 0x04, 0x05 };
			opaque_channel_send_data(testData, sizeof(testData));
			return;
		}

		case XR_OPAQUE_DATA_CHANNEL_STATUS_DISCONNECTED_NV:
			OutputDebugStringA("Channel disconnected during connection attempt\n");
			break;

		case XR_OPAQUE_DATA_CHANNEL_STATUS_CONNECTING_NV:
			// Still connecting, continue waiting
			break;
		}

		std::this_thread::sleep_for(std::chrono::milliseconds(100));
	}

	xr_opaque_connecting = false;
	OutputDebugStringA("Connection thread ended\n");
}

void opaque_channel_receive_loop() {
	uint8_t buffer[4096];

	OutputDebugStringA("Started opaque data channel receive loop\n");

	while (xr_opaque_running) {
		uint32_t receivedBytes = 0;
		XrResult result = ext_xrReceiveOpaqueDataChannelNV(xr_opaque_channel, sizeof(buffer),
			&receivedBytes, buffer);

		if (result == XR_SUCCESS && receivedBytes > 0) {
			char msg[256];
			sprintf_s(msg, "Received %u bytes from CloudXR client\n", receivedBytes);
			OutputDebugStringA(msg);

			// Process received data here
			// Example: Print first few bytes
			OutputDebugStringA("Data: ");
			for (uint32_t i = 0; i < min(receivedBytes, 16u); i++) {
				char hex[8];
				sprintf_s(hex, "%02X ", buffer[i]);
				OutputDebugStringA(hex);
			}
			OutputDebugStringA("\n");
		}

		// Check channel state
		XrOpaqueDataChannelStateNV state = {
			XR_TYPE_OPAQUE_DATA_CHANNEL_STATE_NV,
			nullptr
		};

		ext_xrGetOpaqueDataChannelStateNV(xr_opaque_channel, &state);

		if (state.state == XR_OPAQUE_DATA_CHANNEL_STATUS_DISCONNECTED_NV) {
			OutputDebugStringA("Channel disconnected, stopping receive loop\n");
			break;
		}

		std::this_thread::sleep_for(std::chrono::milliseconds(1));
	}

	OutputDebugStringA("Opaque data channel receive loop ended\n");
}

bool opaque_channel_send_data(const uint8_t* data, size_t size) {

	if (!ext_xrSendOpaqueDataChannelNV || xr_opaque_channel == XR_NULL_HANDLE) {
		return false;
	}

	XrResult result = ext_xrSendOpaqueDataChannelNV(xr_opaque_channel, (uint32_t)size, data);
	if (result == XR_SUCCESS) {
		char msg[256];
		sprintf_s(msg, "Sent %zu bytes to CloudXR client\n", size);
		OutputDebugStringA(msg);
		return true;
	}
	else {
		char msg[256];
		sprintf_s(msg, "Failed to send data: %d\n", result);
		OutputDebugStringA(msg);
		return false;
	}
}

void opaque_channel_shutdown() {
	xr_opaque_connecting = false; // Stop connection attempts
	xr_opaque_running = false;    // Stop receive loop

	if (xr_opaque_connection_thread.joinable()) {
		xr_opaque_connection_thread.join();
	}

	if (xr_opaque_thread.joinable()) {
		xr_opaque_thread.join();
	}

	if (xr_opaque_channel != XR_NULL_HANDLE && ext_xrShutdownOpaqueDataChannelNV) {
		ext_xrShutdownOpaqueDataChannelNV(xr_opaque_channel);
	}

	if (xr_opaque_channel != XR_NULL_HANDLE && ext_xrDestroyOpaqueDataChannelNV) {
		ext_xrDestroyOpaqueDataChannelNV(xr_opaque_channel);
		xr_opaque_channel = XR_NULL_HANDLE;
	}
}