// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using BenchmarkDotNet.Attributes;

namespace Microsoft.AspNetCore.Server.Kestrel.Performance
{
    [Config(typeof(CoreConfig))]
    public class StringToAsciiBytesBenchmark
    {
        private const int Iterations = 5_000;

        private byte[] _asciiBytes = new byte[1024];
        private string _asciiString;

        [Params(
            BenchmarkTypes.Chunked,
            BenchmarkTypes.TextPlain,
            BenchmarkTypes.Date,
            BenchmarkTypes.Cookie
        )]
        public BenchmarkTypes Type { get; set; }

        [Setup]
        public void Setup()
        {
            switch (Type)
            {
                case BenchmarkTypes.Chunked:
                    _asciiString = "Chunked";
                    break;
                case BenchmarkTypes.TextPlain:
                    _asciiString = "text/plain";
                    break;
                case BenchmarkTypes.Date:
                    _asciiString = "Wed, 22 Jun 2016 20:08:29 GMT";
                    break;
                case BenchmarkTypes.Cookie:
                    _asciiString = "prov=20629ccd-8b0f-e8ef-2935-cd26609fc0bc; __qca=P0-1591065732-1479167353442; _ga=GA1.2.1298898376.1479167354; _gat=1; sgt=id=9519gfde_3347_4762_8762_df51458c8ec2; acct=t=why-is-%e0%a5%a7%e0%a5%a8%e0%a5%a9-numeric&s=why-is-%e0%a5%a7%e0%a5%a8%e0%a5%a9-numeric";
                    break;
            }

            Verify();
        }

        [Benchmark(Baseline = true, OperationsPerInvoke = Iterations)]
        public byte[] EncodingAsciiGetBytes()
        {
            for (uint i = 0; i < Iterations; i++)
            {
                Encoding.ASCII.GetBytes(_asciiString, 0, _asciiString.Length, _asciiBytes, 0);
            }

            return _asciiBytes;
        }

        [Benchmark(OperationsPerInvoke = Iterations)]
        public unsafe byte[] KestrelAsciiCharsToBytes()
        {
            for (uint i = 0; i < Iterations; i++)
            {
                fixed (byte* pBytes = &_asciiBytes[0])
                fixed (char* pString = _asciiString)
                {
                    KestrelAsciiCharsToBytes(pString, pBytes, _asciiString.Length);
                }
            }

            return _asciiBytes;
        }

        [Benchmark(OperationsPerInvoke = Iterations)]
        public unsafe byte[] KestrelAsciiCharsVectorized()
        {
            for (uint i = 0; i < Iterations; i++)
            {
                fixed (byte* pBytes = &_asciiBytes[0])
                fixed (char* pString = _asciiString)
                {
                    KestrelAsciiCharsVectorized(pString, pBytes, _asciiString.Length);
                }
            }

            return _asciiBytes;
        }

        private unsafe static void KestrelAsciiCharsVectorized(char* input, byte* output, int length)
        {
            // Note: Not BIGENDIAN or check for non-ascii
            const int Shift16Shift24 = (1 << 16) | (1 << 24);
            const int Shift8Identity = (1 << 8) | (1);

            // Encode as bytes upto the first non-ASCII byte and return count encoded
            int i = 0;
            int ulongDoubleCount = 0;
            // Use Intrinsic switch
            if (IntPtr.Size == 8) // 64 bit
            {
                if (length < 4) goto trailing;

                int unaligned = (int)(((ulong)input) & 0x7) >> 1;
                // Unaligned chars
                for (; i < unaligned; i++)
                {
                    char ch = *(input + i);
                    *(output + i) = (byte)ch; // Cast convert
                }

                // Aligned
                ulongDoubleCount = (length - i) & ~0x7;

                if (Vector.IsHardwareAccelerated && i + Vector<byte>.Count <= ulongDoubleCount)
                {
                    goto vectorized;
                }

            nonvectorized:
                for (; i < ulongDoubleCount; i += 8)
                {
                    ulong inputUlong0 = *(ulong*)(input + i);
                    ulong inputUlong1 = *(ulong*)(input + i + 4);
                    // Pack 16 ASCII chars into 16 bytes
                    *(uint*)(output + i) =
                        ((uint)((inputUlong0 * Shift16Shift24) >> 24) & 0xffff) |
                        ((uint)((inputUlong0 * Shift8Identity) >> 24) & 0xffff0000);
                    *(uint*)(output + i + 4) =
                        ((uint)((inputUlong1 * Shift16Shift24) >> 24) & 0xffff) |
                        ((uint)((inputUlong1 * Shift8Identity) >> 24) & 0xffff0000);
                }
                if (length - 4 > i)
                {
                    ulong inputUlong = *(ulong*)(input + i);
                    // Pack 8 ASCII chars into 8 bytes
                    *(uint*)(output + i) =
                        ((uint)((inputUlong * Shift16Shift24) >> 24) & 0xffff) |
                        ((uint)((inputUlong * Shift8Identity) >> 24) & 0xffff0000);
                    i += 4;
                }
            trailing:
                for (; i < length; i++)
                {
                    char ch = *(input + i);
                    *(output + i) = (byte)ch; // Cast convert
                }
                return;
            vectorized:
                do
                {
                    Unsafe.AsRef<Vector<byte>>(output + i) = Vector.Narrow(Unsafe.AsRef<Vector<ushort>>(input + i), Unsafe.AsRef<Vector<ushort>>(input + i + Vector<ushort>.Count));
                    i += Vector<byte>.Count;
                } while (i + Vector<byte>.Count < ulongDoubleCount);

                goto nonvectorized;
            }
            else // 32 bit
            {
                // Unaligned chars
                if ((unchecked((int)input) & 0x2) != 0)
                {
                    char ch = *input;
                    i = 1;
                    *(output) = (byte)ch; // Cast convert
                }

                // Aligned
                int uintCount = (length - i) & ~0x3;
                for (; i < uintCount; i += 4)
                {
                    uint inputUint0 = *(uint*)(input + i);
                    uint inputUint1 = *(uint*)(input + i + 2);
                    // Pack 4 ASCII chars into 4 bytes
                    *(ushort*)(output + i) = (ushort)(inputUint0 | (inputUint0 >> 8));
                    *(ushort*)(output + i + 2) = (ushort)(inputUint1 | (inputUint1 >> 8));
                }
                if (length - 1 > i)
                {
                    uint inputUint = *(uint*)(input + i);
                    // Pack 2 ASCII chars into 2 bytes
                    *(ushort*)(output + i) = (ushort)(inputUint | (inputUint >> 8));
                    i += 2;
                }

                if (i < length)
                {
                    char ch = *(input + i);
                    *(output + i) = (byte)ch; // Cast convert
                }
            }
        }

        private unsafe static void KestrelAsciiCharsToBytes(char* input, byte* output, int length)
        {
            // Note: Not BIGENDIAN or check for non-ascii
            const int Shift16Shift24 = (1 << 16) | (1 << 24);
            const int Shift8Identity = (1 << 8) | (1);

            // Encode as bytes upto the first non-ASCII byte and return count encoded
            int i = 0;
            // Use Intrinsic switch
            if (IntPtr.Size == 8) // 64 bit
            {
                if (length < 4) goto trailing;

                int unaligned = (int)(((ulong)input) & 0x7) >> 1;
                // Unaligned chars
                for (; i < unaligned; i++)
                {
                    char ch = *(input + i);
                    *(output + i) = (byte)ch; // Cast convert
                }

                // Aligned
                int ulongDoubleCount = (length - i) & ~0x7;
                for (; i < ulongDoubleCount; i += 8)
                {
                    ulong inputUlong0 = *(ulong*)(input + i);
                    ulong inputUlong1 = *(ulong*)(input + i + 4);
                    // Pack 16 ASCII chars into 16 bytes
                    *(uint*)(output + i) =
                        ((uint)((inputUlong0 * Shift16Shift24) >> 24) & 0xffff) |
                        ((uint)((inputUlong0 * Shift8Identity) >> 24) & 0xffff0000);
                    *(uint*)(output + i + 4) =
                        ((uint)((inputUlong1 * Shift16Shift24) >> 24) & 0xffff) |
                        ((uint)((inputUlong1 * Shift8Identity) >> 24) & 0xffff0000);
                }
                if (length - 4 > i)
                {
                    ulong inputUlong = *(ulong*)(input + i);
                    // Pack 8 ASCII chars into 8 bytes
                    *(uint*)(output + i) =
                        ((uint)((inputUlong * Shift16Shift24) >> 24) & 0xffff) |
                        ((uint)((inputUlong * Shift8Identity) >> 24) & 0xffff0000);
                    i += 4;
                }

            trailing:
                for (; i < length; i++)
                {
                    char ch = *(input + i);
                    *(output + i) = (byte)ch; // Cast convert
                }
            }
            else // 32 bit
            {
                // Unaligned chars
                if ((unchecked((int)input) & 0x2) != 0)
                {
                    char ch = *input;
                    i = 1;
                    *(output) = (byte)ch; // Cast convert
                }

                // Aligned
                int uintCount = (length - i) & ~0x3;
                for (; i < uintCount; i += 4)
                {
                    uint inputUint0 = *(uint*)(input + i);
                    uint inputUint1 = *(uint*)(input + i + 2);
                    // Pack 4 ASCII chars into 4 bytes
                    *(ushort*)(output + i) = (ushort)(inputUint0 | (inputUint0 >> 8));
                    *(ushort*)(output + i + 2) = (ushort)(inputUint1 | (inputUint1 >> 8));
                }
                if (length - 1 > i)
                {
                    uint inputUint = *(uint*)(input + i);
                    // Pack 2 ASCII chars into 2 bytes
                    *(ushort*)(output + i) = (ushort)(inputUint | (inputUint >> 8));
                    i += 2;
                }

                if (i < length)
                {
                    char ch = *(input + i);
                    *(output + i) = (byte)ch; // Cast convert
                    i = length;
                }
            }
        }

        private void Verify()
        {
            var verification0 = _asciiString + new string('\0', _asciiBytes.Length - _asciiString.Length);
            var verificationSpace = _asciiString + new string(' ', _asciiBytes.Length - _asciiString.Length);

            BlankArray('\0');
            EncodingAsciiGetBytes();
            VerifyString(verification0);
            BlankArray(' ');
            EncodingAsciiGetBytes();
            VerifyString(verificationSpace);

            BlankArray('\0');
            KestrelAsciiCharsToBytes();
            VerifyString(verification0);
            BlankArray(' ');
            KestrelAsciiCharsToBytes();
            VerifyString(verificationSpace);

            BlankArray('\0');
            KestrelAsciiCharsVectorized();
            VerifyString(verification0);
            BlankArray(' ');
            KestrelAsciiCharsVectorized();
            VerifyString(verificationSpace);
        }

        private void BlankArray(char ch)
        {
            for (var i = 0; i < _asciiBytes.Length; i++)
            {
                _asciiBytes[i] = (byte)ch;
            }
        }

        private unsafe void VerifyString(string verification)
        {
            for (var i = 0; i < verification.Length; i++)
            {
                if (_asciiBytes[i] != verification[i]) throw new Exception($"Verify failed, saw {(int)_asciiBytes[i]} expected {(int)verification[i]} at position {i}");
            }
        }

        public enum BenchmarkTypes
        {
            Chunked,
            TextPlain,
            Date,
            Cookie,
        }
    }
}
