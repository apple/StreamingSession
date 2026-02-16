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
using System.Net;
using System.Text;
using System.Threading.Tasks;

using Makaretu.Dns;

namespace FoveatedStreaming.WindowsSample
{
    /// <summary>
    /// Advertises this PC on the local network using Bonjour (mDNS/DNS-SD).
    ///
    /// Vision Pro devices discover available PCs by scanning for the
    /// <c>_apple-foveated-streaming._tcp</c> service type. The TXT record
    /// includes the app's bundle identifier so Vision Pro can connect to
    /// the correct PC when multiple games are running.
    /// </summary>
    internal class BonjourConnection: IDisposable
    {
        public readonly string BundleID;
        public readonly IPEndPoint Endpoint;

        /// <summary>
        /// The mDNS service type for FoveatedStreaming. Vision Pro scans for this service.
        /// </summary>
        public static readonly string ServiceType = "_apple-foveated-streaming._tcp";

        /// Profile of the app to be advertised on mDNS 
        private ServiceProfile _Profile;

        /// The service object that will advertise our app on mDNS
        private ServiceDiscovery _Discovery;

        private bool _DidDispose = false;

        public BonjourConnection(string bundleID, IPEndPoint endpoint)
        {
            this.BundleID = bundleID;
            this.Endpoint = endpoint;

            string instanceName = System.Net.Dns.GetHostName();
            _Profile = new ServiceProfile(instanceName, ServiceType, (ushort)endpoint.Port, new[] { endpoint.Address });

            _Profile.Resources.Add(new TXTRecord
            {
                Name = _Profile.FullyQualifiedName,
                Strings = new List<string>
                    {
                        $"{ProtocolConstants.BundleIdKey}={BundleID}"
                    }
            });

            _Discovery = new ServiceDiscovery();
            _Discovery.Advertise(_Profile);
            _Discovery.Announce(_Profile);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_DidDispose)
            {
                _Discovery.Unadvertise(_Profile);

                if (disposing)
                {
                    _Discovery.Dispose();
                }

                _DidDispose = true;
            }
        }

        ~BonjourConnection()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
