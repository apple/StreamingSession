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
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FoveatedStreaming.WindowsSample
{
    public class RequestConnectionMessage
    {
        public string Event { get; set; } = "RequestConnection";
        public string ProtocolVersion { get; set; }
        public string StreamingProvider { get; set; }
        public string StreamingProviderVersion { get; set; }
        public string UserInterfaceIdiom { get; set; }
        public string SessionID { get; set; } // Unique identifier for the foveated streaming session on the Vision Pro device.
        public string ClientID { get; set; } // Unique identifier representing the Vision Pro client device.
    }

    public class AcknowledgeConnectionMessage
    {
        public string Event { get; set; } = "AcknowledgeConnection";
        public string SessionID { get; set; }
        public string ServerID { get; set; }
        public string CertificateFingerprint { get; set; }
    }

    public class RequestBarcodePresentationMessage
    {
        public string Event { get; set; } = "RequestBarcodePresentation";
        public string SessionID { get; set; }
    }

    public class AcknowledgeBarcodePresentationMessage
    {
        public string Event { get; set; } = "AcknowledgeBarcodePresentation";
        public string SessionID { get; set; }
    }

    public class SessionStatusDidChangeMessage
    {
        public string Event { get; set; } = "SessionStatusDidChange";
        public string SessionID { get; set; }
        public string Status { get; set; } // Session status: WAITING, CONNECTING, CONNECTED, PAUSED, or DISCONNECTED.
    }

    public class MediaStreamIsReadyMessage
    {
        public string Event { get; set; } = "MediaStreamIsReady";
        public string SessionID { get; set; }
    }

    public class RequestSessionDisconnectMessage
    {
        public string Event { get; set; } = "RequestSessionDisconnect";
        public string SessionID { get; set; }
    }
}
