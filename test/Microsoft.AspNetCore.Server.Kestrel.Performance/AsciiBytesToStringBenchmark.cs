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
    public class AsciiBytesToStringBenchmark
    {
        private const int Iterations = 5_000;

        private byte[] _asciiBytes;
        private string _asciiString = new string('\0', 1024);

        [Params(
            BenchmarkTypes.KeepAlive,
            BenchmarkTypes.Accept,
            BenchmarkTypes.UserAgent,
            BenchmarkTypes.Cookie
        )]
        public BenchmarkTypes Type { get; set; }

        [Setup]
        public void Setup()
        {
            switch (Type)
            {
                case BenchmarkTypes.KeepAlive:
                    _asciiBytes = Encoding.ASCII.GetBytes("keep-alive");
                    break;
                case BenchmarkTypes.Accept:
                    _asciiBytes = Encoding.ASCII.GetBytes("text/plain,text/html;q=0.9,application/xhtml+xml;q=0.9,application/xml;q=0.8,*/*;q=0.7");
                    break;
                case BenchmarkTypes.UserAgent:
                    _asciiBytes = Encoding.ASCII.GetBytes("Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/54.0.2840.99 Safari/537.36");
                    break;
                case BenchmarkTypes.Cookie:
                    _asciiBytes = Encoding.ASCII.GetBytes("prov=20629ccd-8b0f-e8ef-2935-cd26609fc0bc; __qca=P0-1591065732-1479167353442; _ga=GA1.2.1298898376.1479167354; _gat=1; sgt=id=9519gfde_3347_4762_8762_df51458c8ec2; acct=t=why-is-%e0%a5%a7%e0%a5%a8%e0%a5%a9-numeric&s=why-is-%e0%a5%a7%e0%a5%a8%e0%a5%a9-numeric");
                    break;
            }

            Verify();
        }

        [Benchmark(Baseline = true, OperationsPerInvoke = Iterations)]
        public unsafe string EncodingAsciiGetChars()
        {
            for (uint i = 0; i < Iterations; i++)
            {
                fixed (byte* pBytes = &_asciiBytes[0])
                fixed (char* pString = _asciiString)
                {
                    Encoding.ASCII.GetChars(pBytes, _asciiBytes.Length, pString, _asciiBytes.Length);
                }
            }

            return _asciiString;
        }

        [Benchmark(OperationsPerInvoke = Iterations)]
        public unsafe byte[] KestrelBytesToString()
        {
            for (uint i = 0; i < Iterations; i++)
            {
                fixed (byte* pBytes = &_asciiBytes[0])
                fixed (char* pString = _asciiString)
                {
                    TryGetAsciiString(pBytes, pString, _asciiBytes.Length);
                }
            }

            return _asciiBytes;
        }

        [Benchmark(OperationsPerInvoke = Iterations)]
        public unsafe byte[] KestrelBytesToStringVectorized()
        {
            for (uint i = 0; i < Iterations; i++)
            {
                fixed (byte* pBytes = &_asciiBytes[0])
                fixed (char* pString = _asciiString)
                {
                    TryGetAsciiStringVectorized(pBytes, pString, _asciiBytes.Length);
                }
            }

            return _asciiBytes;
        }

        public static unsafe bool TryGetAsciiStringVectorized(byte* input, char* output, int count)
        {
            var pInput = input;
            var pOutput = output;
            var pEnd = pInput + count;

            bool isValid = true;

            if (Vector.IsHardwareAccelerated && count >= Vector<sbyte>.Count)
            {
                goto Vectorized;
            }

         NonVectorized:
            while (pInput <= pEnd - sizeof(long))
            {
                var in0 = *(long*)(pInput);
                isValid &= (((in0 - 0x0101010101010101L) | in0) & unchecked((long)0x8080808080808080L)) == 0;

                *(pOutput) = (char)*(pInput);
                *(pOutput + 1) = (char)*(pInput + 1);
                *(pOutput + 2) = (char)*(pInput + 2);
                *(pOutput + 3) = (char)*(pInput + 3);
                *(pOutput + 4) = (char)*(pInput + 4);
                *(pOutput + 5) = (char)*(pInput + 5);
                *(pOutput + 6) = (char)*(pInput + 6);
                *(pOutput + 7) = (char)*(pInput + 7);

                pInput += sizeof(long);
                pOutput += sizeof(long);
            }
            if (pInput <= pEnd - sizeof(int))
            {
                var in0 = *(int*)(pInput);
                isValid &= (((in0 - 0x01010101) | in0) & unchecked((int)0x80808080)) == 0;

                *(pOutput) = (char)*(pInput);
                *(pOutput + 1) = (char)*(pInput + 1);
                *(pOutput + 2) = (char)*(pInput + 2);
                *(pOutput + 3) = (char)*(pInput + 3);

                pInput += sizeof(int);
                pOutput += sizeof(int);
            }
            if (pInput <= pEnd - sizeof(short))
            {
                var in0 = *(short*)(pInput);
                isValid &= (((short)(in0 - 0x0101) | in0) & unchecked((short)0x8080)) == 0;

                *(pOutput) = (char)*(pInput);
                *(pOutput + 1) = (char)*(pInput + 1);

                pInput += sizeof(short);
                pOutput += sizeof(short);
            }
            if (pInput < pEnd)
            {
                isValid &= *(pInput) > 0;
                *pOutput = (char)*pInput;
            }

            return isValid;

        Vectorized:
            do
            {
                var in0 = Unsafe.AsRef<Vector<sbyte>>(pInput);
                isValid &= Vector.GreaterThanAll(in0, Vector<sbyte>.Zero);

                Vector.Widen(in0, out Unsafe.AsRef<Vector<short>>(pOutput), out Unsafe.AsRef<Vector<short>>(pOutput + Vector<short>.Count));

                pInput += Vector<sbyte>.Count;
                pOutput += Vector<sbyte>.Count;
            } while (pInput < pEnd - Vector<sbyte>.Count);

            goto NonVectorized;
        }

        public static unsafe bool TryGetAsciiString(byte* input, char* output, int count)
        {
            var i = 0;
            sbyte* signedInput = (sbyte*)input;

            bool isValid = true;
            while (i < count - 11)
            {
                isValid = isValid && *signedInput > 0 && *(signedInput + 1) > 0 && *(signedInput + 2) > 0 &&
                    *(signedInput + 3) > 0 && *(signedInput + 4) > 0 && *(signedInput + 5) > 0 && *(signedInput + 6) > 0 &&
                    *(signedInput + 7) > 0 && *(signedInput + 8) > 0 && *(signedInput + 9) > 0 && *(signedInput + 10) > 0 &&
                    *(signedInput + 11) > 0;

                i += 12;
                *(output) = (char)*(signedInput);
                *(output + 1) = (char)*(signedInput + 1);
                *(output + 2) = (char)*(signedInput + 2);
                *(output + 3) = (char)*(signedInput + 3);
                *(output + 4) = (char)*(signedInput + 4);
                *(output + 5) = (char)*(signedInput + 5);
                *(output + 6) = (char)*(signedInput + 6);
                *(output + 7) = (char)*(signedInput + 7);
                *(output + 8) = (char)*(signedInput + 8);
                *(output + 9) = (char)*(signedInput + 9);
                *(output + 10) = (char)*(signedInput + 10);
                *(output + 11) = (char)*(signedInput + 11);
                output += 12;
                signedInput += 12;
            }
            if (i < count - 5)
            {
                isValid = isValid && *signedInput > 0 && *(signedInput + 1) > 0 && *(signedInput + 2) > 0 &&
                    *(signedInput + 3) > 0 && *(signedInput + 4) > 0 && *(signedInput + 5) > 0;

                i += 6;
                *(output) = (char)*(signedInput);
                *(output + 1) = (char)*(signedInput + 1);
                *(output + 2) = (char)*(signedInput + 2);
                *(output + 3) = (char)*(signedInput + 3);
                *(output + 4) = (char)*(signedInput + 4);
                *(output + 5) = (char)*(signedInput + 5);
                output += 6;
                signedInput += 6;
            }
            if (i < count - 3)
            {
                isValid = isValid && *signedInput > 0 && *(signedInput + 1) > 0 && *(signedInput + 2) > 0 &&
                    *(signedInput + 3) > 0;

                i += 4;
                *(output) = (char)*(signedInput);
                *(output + 1) = (char)*(signedInput + 1);
                *(output + 2) = (char)*(signedInput + 2);
                *(output + 3) = (char)*(signedInput + 3);
                output += 4;
                signedInput += 4;
            }

            while (i < count)
            {
                isValid = isValid && *signedInput > 0;

                i++;
                *output = (char)*signedInput;
                output++;
                signedInput++;
            }

            return isValid;
        }

        private void Verify()
        {
            var verification = EncodingAsciiGetChars().Substring(0, _asciiBytes.Length);

            BlankString('\0');
            EncodingAsciiGetChars();
            VerifyString(verification, '\0');
            BlankString(' ');
            EncodingAsciiGetChars();
            VerifyString(verification, ' ');

            BlankString('\0');
            KestrelBytesToString();
            VerifyString(verification, '\0');
            BlankString(' ');
            KestrelBytesToString();
            VerifyString(verification, ' ');

            BlankString('\0');
            KestrelBytesToStringVectorized();
            VerifyString(verification, '\0');
            BlankString(' ');
            KestrelBytesToStringVectorized();
            VerifyString(verification, ' ');
        }

        private unsafe void BlankString(char ch)
        {
            fixed (char* pString = _asciiString)
            {
                for (var i = 0; i < _asciiString.Length; i++)
                {
                    *(pString + i) = ch;
                }
            }
        }

        private unsafe void VerifyString(string verification, char ch)
        {
            fixed (char* pString = _asciiString)
            {
                var i = 0;
                for (; i < verification.Length; i++)
                {
                    if (*(pString + i) != verification[i]) throw new Exception($"Verify failed, saw {(int)*(pString + i)} expected {(int)verification[i]} at position {i}");
                }
                for (; i < _asciiString.Length; i++)
                {
                    if (*(pString + i) != ch) throw new Exception($"Verify failed, saw {(int)*(pString + i)} expected {(int)ch} at position {i}"); ;
                }
            }
        }

        public enum BenchmarkTypes
        {
            KeepAlive,
            Accept,
            UserAgent,
            Cookie,
        }
    }
}
