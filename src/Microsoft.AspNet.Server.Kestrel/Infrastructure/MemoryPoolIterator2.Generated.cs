
using System;

namespace Microsoft.AspNet.Server.Kestrel.Infrastructure
{
    public partial struct MemoryPoolIterator2
    {
        private const byte _colon = 58;
        private const int _headerAcce = 1701012321;
        private const int _headerAllo = 1869376609;
        private const int _headerAuth = 1752462689;
        private const int _headerCach = 1751343459;
        private const int _headerConn = 1852731235;
        private const int _headerCont = 1953394531;
        private const int _headerCook = 1802465123;
        private const int _headerDate = 1702125924;
        private const int _headerExpe = 1701869669;
        private const int _headerExpi = 1768978533;
        private const int _headerFrom = 1836020326;
        private const int _headerHost = 1953722216;
        private const int _headerIf_M = 1831691881;
        private const int _headerIf_N = 1848469097;
        private const int _headerIf_R = 1915577961;
        private const int _headerIf_U = 1965909609;
        private const int _headerKeep = 1885693291;
        private const int _headerLast = 1953718636;
        private const int _headerMax_ = 762863981;
        private const int _headerOrig = 1734963823;
        private const int _headerPrag = 1734439536;
        private const int _headerProx = 2020569712;
        private const int _headerRang = 1735287154;
        private const int _headerRefe = 1701209458;
        private const int _headerTrai = 1767993972;
        private const int _headerTran = 1851880052;
        private const int _headerUpgr = 1919381621;
        private const int _headerUser = 1919251317;
        private const int _headerWarn = 1852989815;


        public unsafe bool SeekCommonHeader()
        {
            if (BitConverter.IsLittleEndian != true)
            {
                return false;
            }

            if (IsDefault)
            {
                return false;
            }

            var block = _block;
            var index = _index;
            var following = block.End - index;

            if (following < 4)
            {
                return false;
            }

            fixed (byte* ptr = &block.Array[index])
            {
                var fourLowerChars = *(int*)(ptr) | 0x20202020;

                switch (fourLowerChars)
                {
                    case _headerHost:
                        if (following >= 4 && *(ptr + 4) == _colon) // Host
                        {
                            _index = index + 4;
                            return true;
                        }
                        return false;
                    case _headerFrom:
                        if (following >= 4 && *(ptr + 4) == _colon) // From
                        {
                            _index = index + 4;
                            return true;
                        }
                        return false;
                    case _headerDate:
                        if (following >= 4 && *(ptr + 4) == _colon) // Date
                        {
                            _index = index + 4;
                            return true;
                        }
                        return false;
                    case _headerRang:
                        if (following >= 5 && *(ptr + 5) == _colon) // Range
                        {
                            _index = index + 5;
                            return true;
                        }
                        return false;
                    case _headerAllo:
                        if (following >= 5 && *(ptr + 5) == _colon) // Allow
                        {
                            _index = index + 5;
                            return true;
                        }
                        return false;
                    case _headerPrag:
                        if (following >= 6 && *(ptr + 6) == _colon) // Pragma
                        {
                            _index = index + 6;
                            return true;
                        }
                        return false;
                    case _headerOrig:
                        if (following >= 6 && *(ptr + 6) == _colon) // Origin
                        {
                            _index = index + 6;
                            return true;
                        }
                        return false;
                    case _headerExpe:
                        if (following >= 6 && *(ptr + 6) == _colon) // Expect
                        {
                            _index = index + 6;
                            return true;
                        }
                        return false;
                    case _headerCook:
                        if (following >= 6 && *(ptr + 6) == _colon) // Cookie
                        {
                            _index = index + 6;
                            return true;
                        }
                        return false;
                    case _headerAcce:
                        if (following >= 6 && *(ptr + 6) == _colon) // Accept
                        {
                            _index = index + 6;
                            return true;
                        }
    
                        if (following >= 14 && *(ptr + 14) == _colon) // Accept-Charset
                        {
                            _index = index + 14;
                            return true;
                        }
    
                        if (following >= 15 && *(ptr + 15) == _colon) // Accept-Language, Accept-Encoding
                        {
                            _index = index + 15;
                            return true;
                        }
    
                        if (following >= 29 && *(ptr + 29) == _colon) // Access-Control-Request-Method
                        {
                            _index = index + 29;
                            return true;
                        }
    
                        if (following >= 30 && *(ptr + 30) == _colon) // Access-Control-Request-Headers
                        {
                            _index = index + 30;
                            return true;
                        }
                        return false;
                    case _headerWarn:
                        if (following >= 7 && *(ptr + 7) == _colon) // Warning
                        {
                            _index = index + 7;
                            return true;
                        }
                        return false;
                    case _headerUpgr:
                        if (following >= 7 && *(ptr + 7) == _colon) // Upgrade
                        {
                            _index = index + 7;
                            return true;
                        }
                        return false;
                    case _headerTrai:
                        if (following >= 7 && *(ptr + 7) == _colon) // Trailer
                        {
                            _index = index + 7;
                            return true;
                        }
                        return false;
                    case _headerRefe:
                        if (following >= 7 && *(ptr + 7) == _colon) // Referer
                        {
                            _index = index + 7;
                            return true;
                        }
                        return false;
                    case _headerExpi:
                        if (following >= 7 && *(ptr + 7) == _colon) // Expires
                        {
                            _index = index + 7;
                            return true;
                        }
                        return false;
                    case _headerIf_R:
                        if (following >= 8 && *(ptr + 8) == _colon) // If-Range
                        {
                            _index = index + 8;
                            return true;
                        }
                        return false;
                    case _headerIf_M:
                        if (following >= 8 && *(ptr + 8) == _colon) // If-Match
                        {
                            _index = index + 8;
                            return true;
                        }
    
                        if (following >= 17 && *(ptr + 17) == _colon) // If-Modified-Since
                        {
                            _index = index + 17;
                            return true;
                        }
                        return false;
                    case _headerTran:
                        if (following >= 9 && *(ptr + 9) == _colon) // Translate
                        {
                            _index = index + 9;
                            return true;
                        }
    
                        if (following >= 17 && *(ptr + 17) == _colon) // Transfer-Encoding
                        {
                            _index = index + 17;
                            return true;
                        }
                        return false;
                    case _headerUser:
                        if (following >= 10 && *(ptr + 10) == _colon) // User-Agent
                        {
                            _index = index + 10;
                            return true;
                        }
                        return false;
                    case _headerKeep:
                        if (following >= 10 && *(ptr + 10) == _colon) // Keep-Alive
                        {
                            _index = index + 10;
                            return true;
                        }
                        return false;
                    case _headerConn:
                        if (following >= 10 && *(ptr + 10) == _colon) // Connection
                        {
                            _index = index + 10;
                            return true;
                        }
                        return false;
                    case _headerCont:
                        if (following >= 11 && *(ptr + 11) == _colon) // Content-MD5
                        {
                            _index = index + 11;
                            return true;
                        }
    
                        if (following >= 12 && *(ptr + 12) == _colon) // Content-Type
                        {
                            _index = index + 12;
                            return true;
                        }
    
                        if (following >= 13 && *(ptr + 13) == _colon) // Content-Range
                        {
                            _index = index + 13;
                            return true;
                        }
    
                        if (following >= 14 && *(ptr + 14) == _colon) // Content-Length
                        {
                            _index = index + 14;
                            return true;
                        }
    
                        if (following >= 16 && *(ptr + 16) == _colon) // Content-Location, Content-Language, Content-Encoding
                        {
                            _index = index + 16;
                            return true;
                        }
                        return false;
                    case _headerMax_:
                        if (following >= 12 && *(ptr + 12) == _colon) // Max-Forwards
                        {
                            _index = index + 12;
                            return true;
                        }
                        return false;
                    case _headerLast:
                        if (following >= 13 && *(ptr + 13) == _colon) // Last-Modified
                        {
                            _index = index + 13;
                            return true;
                        }
                        return false;
                    case _headerIf_N:
                        if (following >= 13 && *(ptr + 13) == _colon) // If-None-Match
                        {
                            _index = index + 13;
                            return true;
                        }
                        return false;
                    case _headerCach:
                        if (following >= 13 && *(ptr + 13) == _colon) // Cache-Control
                        {
                            _index = index + 13;
                            return true;
                        }
                        return false;
                    case _headerAuth:
                        if (following >= 13 && *(ptr + 13) == _colon) // Authorization
                        {
                            _index = index + 13;
                            return true;
                        }
                        return false;
                    case _headerProx:
                        if (following >= 19 && *(ptr + 19) == _colon) // Proxy-Authorization
                        {
                            _index = index + 19;
                            return true;
                        }
                        return false;
                    case _headerIf_U:
                        if (following >= 19 && *(ptr + 19) == _colon) // If-Unmodified-Since
                        {
                            _index = index + 19;
                            return true;
                        }
                        return false;
    
                    default:
                        return false;
                }
            }

        }
    }
}
