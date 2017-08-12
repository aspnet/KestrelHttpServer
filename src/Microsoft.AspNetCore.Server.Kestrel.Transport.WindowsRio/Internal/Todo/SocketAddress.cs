// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text;

namespace System.Net.Internals
{
    internal class SocketAddress
    {
        internal static readonly int IPv6AddressSize = SocketAddressPal.IPv6AddressSize;
        internal static readonly int IPv4AddressSize = SocketAddressPal.IPv4AddressSize;

        internal int InternalSize;
        internal byte[] Buffer;

        private const int MinSize = 2;
        private const int MaxSize = 32; // IrDA requires 32 bytes

        public AddressFamily Family
        {
            get
            {
                return SocketAddressPal.GetAddressFamily(Buffer);
            }
        }

        public int Size
        {
            get
            {
                return InternalSize;
            }
        }

        public SocketAddress(AddressFamily family) : this(family, MaxSize)
        {
        }

        public SocketAddress(AddressFamily family, int size)
        {
            if (size < MinSize)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            InternalSize = size;
            Buffer = new byte[(size / IntPtr.Size + 2) * IntPtr.Size];

            SocketAddressPal.SetAddressFamily(Buffer, family);
        }

        internal SocketAddress(IPAddress ipAddress)
            : this(ipAddress.AddressFamily,
                ((ipAddress.AddressFamily == AddressFamily.InterNetwork) ? IPv4AddressSize : IPv6AddressSize))
        {
            // No Port.
            SocketAddressPal.SetPort(Buffer, 0);

            if (ipAddress.AddressFamily == AddressFamily.InterNetworkV6)
            {
                SocketAddressPal.SetIPv6Address(Buffer, ipAddress.GetAddressBytes(), (uint)ipAddress.ScopeId);
            }
            else
            {
                Debug.Assert(ipAddress.AddressFamily == AddressFamily.InterNetwork);
                SocketAddressPal.SetIPv4Address(Buffer, ipAddress.GetAddressBytes());
            }
        }

        internal SocketAddress(IPAddress ipaddress, int port)
            : this(ipaddress)
        {
            SocketAddressPal.SetPort(Buffer, unchecked((ushort)port));
        }

        // For ReceiveFrom we need to pin address size, using reserved Buffer space.
        internal void CopyAddressSizeIntoBuffer()
        {
            Buffer[Buffer.Length - IntPtr.Size] = unchecked((byte)(InternalSize));
            Buffer[Buffer.Length - IntPtr.Size + 1] = unchecked((byte)(InternalSize >> 8));
            Buffer[Buffer.Length - IntPtr.Size + 2] = unchecked((byte)(InternalSize >> 16));
            Buffer[Buffer.Length - IntPtr.Size + 3] = unchecked((byte)(InternalSize >> 24));
        }

        // Can be called after the above method did work.
        internal int GetAddressSizeOffset()
        {
            return Buffer.Length - IntPtr.Size;
        }
    }
}
