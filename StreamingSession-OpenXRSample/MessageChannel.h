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

#pragma once
#include <openxr/openxr.h>
#include <openxr/openxr_platform.h>
#include <thread>
#include <vector>
#include <algorithm>
#include <atomic>

// Handle type
XR_DEFINE_HANDLE(XrOpaqueDataChannelNV)

// Structure type constants
#define XR_TYPE_OPAQUE_DATA_CHANNEL_CREATE_INFO_NV ((XrStructureType)1000500000)
#define XR_TYPE_OPAQUE_DATA_CHANNEL_STATE_NV ((XrStructureType)1000500001)

// Status enum
typedef enum XrOpaqueDataChannelStatusNV {
	XR_OPAQUE_DATA_CHANNEL_STATUS_CONNECTING_NV = 0,
	XR_OPAQUE_DATA_CHANNEL_STATUS_CONNECTED_NV = 1,
	XR_OPAQUE_DATA_CHANNEL_STATUS_SHUTTING_NV = 2,
	XR_OPAQUE_DATA_CHANNEL_STATUS_DISCONNECTED_NV = 3,
} XrOpaqueDataChannelStatusNV;

// GUID structure (if not already defined)
#ifndef XrGuid
typedef struct XrGuid {
	uint32_t data1;
	uint16_t data2;
	uint16_t data3;
	uint8_t data4[8];
} XrGuid;
#endif

// Structure definitions
typedef struct XrOpaqueDataChannelCreateInfoNV {
	XrStructureType type;
	const void* next;
	XrSystemId systemId;
	XrGuid uuid;
} XrOpaqueDataChannelCreateInfoNV;

typedef struct XrOpaqueDataChannelStateNV {
	XrStructureType type;
	void* next;
	XrOpaqueDataChannelStatusNV state;
} XrOpaqueDataChannelStateNV;

extern XrSystemId     xr_system_id;
extern XrInstance     xr_instance;

extern std::atomic<bool> xr_opaque_connected;
extern std::atomic<bool> xr_opaque_connecting;
extern std::thread xr_opaque_connection_thread;
extern XrOpaqueDataChannelNV  xr_opaque_channel;
extern std::atomic<bool>      xr_opaque_running;
extern std::thread            xr_opaque_thread;

// FUNCTION POINTER TYPE DEFINITIONS
typedef XrResult(XRAPI_PTR* PFN_xrCreateOpaqueDataChannelNV)(
	XrInstance instance,
	const XrOpaqueDataChannelCreateInfoNV* createInfo,
	XrOpaqueDataChannelNV* opaqueDataChannel);

typedef XrResult(XRAPI_PTR* PFN_xrDestroyOpaqueDataChannelNV)(
	XrOpaqueDataChannelNV opaqueDataChannel);

typedef XrResult(XRAPI_PTR* PFN_xrGetOpaqueDataChannelStateNV)(
	XrOpaqueDataChannelNV opaqueDataChannel,
	XrOpaqueDataChannelStateNV* state);

typedef XrResult(XRAPI_PTR* PFN_xrShutdownOpaqueDataChannelNV)(
	XrOpaqueDataChannelNV opaqueDataChannel);

typedef XrResult(XRAPI_PTR* PFN_xrSendOpaqueDataChannelNV)(
	XrOpaqueDataChannelNV opaqueDataChannel,
	uint32_t opaqueDataInputCount,
	const uint8_t* opaqueDatas);

typedef XrResult(XRAPI_PTR* PFN_xrReceiveOpaqueDataChannelNV)(
	XrOpaqueDataChannelNV opaqueDataChannel,
	uint32_t opaqueDataCapacityInput,
	uint32_t* opaqueDataCountOutput,
	uint8_t* opaqueDatas);

// Error codes
#define XR_ERROR_CHANNEL_ALREADY_CREATED_NV ((XrResult)-1000500000)
#define XR_ERROR_CHANNEL_NOT_CONNECTED_NV ((XrResult)-1000500001)

// Opaque data channel APIs
extern PFN_xrCreateOpaqueDataChannelNV       ext_xrCreateOpaqueDataChannelNV;
extern PFN_xrDestroyOpaqueDataChannelNV      ext_xrDestroyOpaqueDataChannelNV;
extern PFN_xrGetOpaqueDataChannelStateNV     ext_xrGetOpaqueDataChannelStateNV;
extern PFN_xrShutdownOpaqueDataChannelNV     ext_xrShutdownOpaqueDataChannelNV;
extern PFN_xrSendOpaqueDataChannelNV         ext_xrSendOpaqueDataChannelNV;
extern PFN_xrReceiveOpaqueDataChannelNV      ext_xrReceiveOpaqueDataChannelNV;

bool opaque_channel_init();
bool opaque_channel_wait_connection();
void opaque_channel_connect_async();
void opaque_channel_receive_loop();
bool opaque_channel_send_data(const uint8_t* data, size_t size);
void opaque_channel_shutdown();
void opaque_channel_receive_loop();
bool opaque_channel_send_data(const uint8_t* data, size_t size);




