// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using Microsoft.AspNetCore.Server.Kestrel.Http;
using System.Runtime.CompilerServices;

namespace Microsoft.AspNetCore.Server.Kestrel.Infrastructure
{
    class HttpComponentFactory : IHttpComponentFactory
    {
        private ComponentPool<Streams> _streamPool;
        private ComponentPool<Headers> _headerPool;

        public KestrelServerOptions ServerOptions { get; set; }

        private ComponentPool<Streams> StreamPool
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return (ServerOptions.MaxPooledStreams == 0) ?
                    null : Volatile.Read(ref _streamPool) ?? EnsurePoolCreated(ref _streamPool, ServerOptions.MaxPooledStreams);
            }
        }

        private ComponentPool<Headers> HeaderPool
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return (ServerOptions.MaxPooledHeaders == 0) ?
                    null : Volatile.Read(ref _headerPool) ?? EnsurePoolCreated(ref _headerPool, ServerOptions.MaxPooledHeaders);
            }
        }

        public HttpComponentFactory(KestrelServerOptions serverOptions)
        {
            ServerOptions = serverOptions;
        }

        public Streams CreateStreams(FrameContext owner)
        {
            Streams streams = null;

            if (!(StreamPool?.TryRent(out streams) ?? false))
            {
                streams = new Streams();
            }

            streams.Initialize(owner);

            return streams;
        }

        public void ResetStreams(Streams streams)
        {
            streams.Reset();
        }

        public void DisposeStreams(Streams streams)
        {
            ResetStreams(streams);
            StreamPool?.Return(streams);
        }

        public Headers CreateHeaders(DateHeaderValueManager dateValueManager)
        {
            Headers headers = null;

            if (!(HeaderPool?.TryRent(out headers) ?? false))
            {
                headers = new Headers();
            }

            headers.Initialize(dateValueManager);

            return headers;
        }

        public void DisposeHeaders(Headers headers)
        {
            ResetHeaders(headers);
            HeaderPool?.Return(headers);
        }

        public void ResetHeaders(Headers headers)
        {
            headers.Reset();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ComponentPool<T> EnsurePoolCreated<T>(ref ComponentPool<T> pool, int maxPooled) where T : class
        {
            Interlocked.CompareExchange(ref pool, CreatePool<T>(maxPooled), null);
            return pool;
        }

        private static ComponentPool<T> CreatePool<T>(int maxPooled) where T : class
        {
            return new ComponentPool<T>(maxPooled);
        }

        class ComponentPool<T> where T : class
        {
            private readonly T[] _objects;

            private SpinLock _lock; // do not make this readonly; it's a mutable struct
            private int _index = -1;

            /// <summary>
            /// Creates the pool with maxPooled objects.
            /// </summary>
            public ComponentPool(int maxPooled)
            {
                if (maxPooled <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(maxPooled));
                }

                _lock = new SpinLock(Debugger.IsAttached); // only enable thread tracking if debugger is attached; it adds non-trivial overheads to Enter/Exit
                _objects = new T[maxPooled];
            }

            /// <summary>Tries to take an object from the pool, returns true if sucessful.</summary>
            public bool TryRent(out T obj)
            {
                T[] objects = _objects;
                obj = null;
                // While holding the lock, grab whatever is at the next available index and
                // update the index.  We do as little work as possible while holding the spin
                // lock to minimize contention with other threads.  The try/finally is
                // necessary to properly handle thread aborts on platforms which have them.
                bool lockTaken = false;
                try
                {
                    _lock.Enter(ref lockTaken);

                    var removeIndex = _index;
                    if (removeIndex >= 0)
                    {
                        obj = objects[removeIndex];
                        objects[removeIndex] = null;
                        _index = removeIndex - 1;
                    }
                }
                finally
                {
                    if (lockTaken) _lock.Exit(false);
                }
                return obj != null;
            }

            /// <summary>
            /// Attempts to return the object to the pool.  If successful, the object will be stored
            /// in the pool; otherwise, the buffer won't be stored.
            /// </summary>
            public void Return(T obj)
            {
                if (obj == null)
                {
                    return;
                }

                // While holding the spin lock, if there's room available in the array,
                // put the object into the next available slot.  Otherwise, we just drop it.
                // The try/finally is necessary to properly handle thread aborts on platforms
                // which have them.
                bool lockTaken = false;
                try
                {
                    _lock.Enter(ref lockTaken);

                    var insertIndex = _index + 1;
                    if (insertIndex < _objects.Length)
                    {
                        _objects[insertIndex] = obj;
                        _index = insertIndex;
                    }
                }
                finally
                {
                    if (lockTaken) _lock.Exit(false);
                }
            }
        }
    }

    internal class Headers
    {
        public static readonly byte[] BytesServer = Encoding.ASCII.GetBytes("\r\nServer: Kestrel");

        public readonly FrameRequestHeaders RequestHeaders = new FrameRequestHeaders();
        public readonly FrameResponseHeaders ResponseHeaders = new FrameResponseHeaders();

        private DateHeaderValueManager _dateValueManager;

        public void Initialize(DateHeaderValueManager dateValueManager)
        {
            _dateValueManager = dateValueManager;
            ResponseHeaders.SetRawDate(
                dateValueManager.GetDateHeaderValue(),
                dateValueManager.GetDateHeaderValueBytes());
            ResponseHeaders.SetRawServer("Kestrel", BytesServer);
        }
        public void Reset()
        {
            RequestHeaders.Reset();
            ResponseHeaders.Reset();

            ResponseHeaders.SetRawDate(
                _dateValueManager.GetDateHeaderValue(),
                _dateValueManager.GetDateHeaderValueBytes());
            ResponseHeaders.SetRawServer("Kestrel", BytesServer);
        }

        public void Uninitialize()
        {
            RequestHeaders.Reset();
            ResponseHeaders.Reset();

            _dateValueManager = null;
        }
    }

    internal class Streams
    {
        public readonly FrameRequestStream RequestBody;
        public readonly FrameResponseStream ResponseBody;
        public readonly FrameDuplexStream DuplexStream;

        private FrameContext _owner;

        public Streams()
        {
            RequestBody = new FrameRequestStream();
            ResponseBody = new FrameResponseStream();
            DuplexStream = new FrameDuplexStream(RequestBody, ResponseBody);
        }

        public void Initialize(FrameContext renter)
        {
            _owner = renter;
            ResponseBody.Initialize(renter);
        }

        public void Reset()
        {
            ResponseBody.Uninitialize();
            ResponseBody.Initialize(_owner);
        }

        public void Uninitialize()
        {
            _owner = null;
            ResponseBody.Uninitialize();
        }
    }
}
