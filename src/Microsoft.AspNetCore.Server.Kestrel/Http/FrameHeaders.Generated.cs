
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Server.Kestrel.Infrastructure;
using Microsoft.Extensions.Primitives;

namespace Microsoft.AspNetCore.Server.Kestrel.Http 
{

    public partial class FrameRequestHeaders
    {
        
        private long _bits = 0;
        private HeaderReferences _headers;
        
        public StringValues HeaderConnection
        {
            get
            {
                if (((_bits & 1L) != 0))
                {
                    return _headers._Connection;
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= 1L;
                _headers._Connection = value; 
            }
        }
        public StringValues HeaderTransferEncoding
        {
            get
            {
                if (((_bits & 2L) != 0))
                {
                    return _headers._TransferEncoding;
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= 2L;
                _headers._TransferEncoding = value; 
            }
        }
        public StringValues HeaderContentLength
        {
            get
            {
                if (((_bits & 4L) != 0))
                {
                    return _headers._ContentLength;
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= 4L;
                _headers._ContentLength = value; 
            }
        }
        public StringValues HeaderAccept
        {
            get
            {
                if (((_bits & 8L) != 0))
                {
                    return _headers._Accept;
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= 8L;
                _headers._Accept = value; 
            }
        }
        public StringValues HeaderAcceptEncoding
        {
            get
            {
                if (((_bits & 16L) != 0))
                {
                    return _headers._AcceptEncoding;
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= 16L;
                _headers._AcceptEncoding = value; 
            }
        }
        public StringValues HeaderAcceptLanguage
        {
            get
            {
                if (((_bits & 32L) != 0))
                {
                    return _headers._AcceptLanguage;
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= 32L;
                _headers._AcceptLanguage = value; 
            }
        }
        public StringValues HeaderCookie
        {
            get
            {
                if (((_bits & 64L) != 0))
                {
                    return _headers._Cookie;
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= 64L;
                _headers._Cookie = value; 
            }
        }
        public StringValues HeaderHost
        {
            get
            {
                if (((_bits & 128L) != 0))
                {
                    return _headers._Host;
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= 128L;
                _headers._Host = value; 
            }
        }
        public StringValues HeaderReferer
        {
            get
            {
                if (((_bits & 256L) != 0))
                {
                    return _headers._Referer;
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= 256L;
                _headers._Referer = value; 
            }
        }
        public StringValues HeaderUserAgent
        {
            get
            {
                if (((_bits & 512L) != 0))
                {
                    return _headers._UserAgent;
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= 512L;
                _headers._UserAgent = value; 
            }
        }
        
        protected override int GetCountFast()
        {
            return BitCount(_bits) + (MaybeUnknown?.Count ?? 0);
        }
        protected override StringValues GetValueFast(string key)
        {
            switch (key.Length)
            {
                case 10:
                    {
                        if ("Connection".Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            if (((_bits & 1L) != 0))
                            {
                                return _headers._Connection;
                            }
                            else
                            {
                                ThrowKeyNotFoundException();
                            }
                        }
                    
                        if ("User-Agent".Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            if (((_bits & 512L) != 0))
                            {
                                return _headers._UserAgent;
                            }
                            else
                            {
                                ThrowKeyNotFoundException();
                            }
                        }
                    }
                    break;

                case 17:
                    {
                        if ("Transfer-Encoding".Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            if (((_bits & 2L) != 0))
                            {
                                return _headers._TransferEncoding;
                            }
                            else
                            {
                                ThrowKeyNotFoundException();
                            }
                        }
                    }
                    break;

                case 14:
                    {
                        if ("Content-Length".Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            if (((_bits & 4L) != 0))
                            {
                                return _headers._ContentLength;
                            }
                            else
                            {
                                ThrowKeyNotFoundException();
                            }
                        }
                    }
                    break;

                case 6:
                    {
                        if ("Accept".Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            if (((_bits & 8L) != 0))
                            {
                                return _headers._Accept;
                            }
                            else
                            {
                                ThrowKeyNotFoundException();
                            }
                        }
                    
                        if ("Cookie".Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            if (((_bits & 64L) != 0))
                            {
                                return _headers._Cookie;
                            }
                            else
                            {
                                ThrowKeyNotFoundException();
                            }
                        }
                    }
                    break;

                case 15:
                    {
                        if ("Accept-Encoding".Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            if (((_bits & 16L) != 0))
                            {
                                return _headers._AcceptEncoding;
                            }
                            else
                            {
                                ThrowKeyNotFoundException();
                            }
                        }
                    
                        if ("Accept-Language".Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            if (((_bits & 32L) != 0))
                            {
                                return _headers._AcceptLanguage;
                            }
                            else
                            {
                                ThrowKeyNotFoundException();
                            }
                        }
                    }
                    break;

                case 4:
                    {
                        if ("Host".Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            if (((_bits & 128L) != 0))
                            {
                                return _headers._Host;
                            }
                            else
                            {
                                ThrowKeyNotFoundException();
                            }
                        }
                    }
                    break;

                case 7:
                    {
                        if ("Referer".Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            if (((_bits & 256L) != 0))
                            {
                                return _headers._Referer;
                            }
                            else
                            {
                                ThrowKeyNotFoundException();
                            }
                        }
                    }
                    break;
}
            if (MaybeUnknown == null) 
            {
                ThrowKeyNotFoundException();
            }
            return MaybeUnknown[key];
        }
        protected override bool TryGetValueFast(string key, out StringValues value)
        {
            switch (key.Length)
            {
                case 10:
                    {
                        if ("Connection".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            if (((_bits & 1L) != 0))
                            {
                                value = _headers._Connection;
                                return true;
                            }
                            else
                            {
                                value = StringValues.Empty;
                                return false;
                            }
                        }
                    
                        if ("User-Agent".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            if (((_bits & 512L) != 0))
                            {
                                value = _headers._UserAgent;
                                return true;
                            }
                            else
                            {
                                value = StringValues.Empty;
                                return false;
                            }
                        }
                    }
                    break;

                case 17:
                    {
                        if ("Transfer-Encoding".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            if (((_bits & 2L) != 0))
                            {
                                value = _headers._TransferEncoding;
                                return true;
                            }
                            else
                            {
                                value = StringValues.Empty;
                                return false;
                            }
                        }
                    }
                    break;

                case 14:
                    {
                        if ("Content-Length".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            if (((_bits & 4L) != 0))
                            {
                                value = _headers._ContentLength;
                                return true;
                            }
                            else
                            {
                                value = StringValues.Empty;
                                return false;
                            }
                        }
                    }
                    break;

                case 6:
                    {
                        if ("Accept".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            if (((_bits & 8L) != 0))
                            {
                                value = _headers._Accept;
                                return true;
                            }
                            else
                            {
                                value = StringValues.Empty;
                                return false;
                            }
                        }
                    
                        if ("Cookie".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            if (((_bits & 64L) != 0))
                            {
                                value = _headers._Cookie;
                                return true;
                            }
                            else
                            {
                                value = StringValues.Empty;
                                return false;
                            }
                        }
                    }
                    break;

                case 15:
                    {
                        if ("Accept-Encoding".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            if (((_bits & 16L) != 0))
                            {
                                value = _headers._AcceptEncoding;
                                return true;
                            }
                            else
                            {
                                value = StringValues.Empty;
                                return false;
                            }
                        }
                    
                        if ("Accept-Language".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            if (((_bits & 32L) != 0))
                            {
                                value = _headers._AcceptLanguage;
                                return true;
                            }
                            else
                            {
                                value = StringValues.Empty;
                                return false;
                            }
                        }
                    }
                    break;

                case 4:
                    {
                        if ("Host".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            if (((_bits & 128L) != 0))
                            {
                                value = _headers._Host;
                                return true;
                            }
                            else
                            {
                                value = StringValues.Empty;
                                return false;
                            }
                        }
                    }
                    break;

                case 7:
                    {
                        if ("Referer".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            if (((_bits & 256L) != 0))
                            {
                                value = _headers._Referer;
                                return true;
                            }
                            else
                            {
                                value = StringValues.Empty;
                                return false;
                            }
                        }
                    }
                    break;
}
            value = StringValues.Empty;
            return MaybeUnknown?.TryGetValue(key, out value) ?? false;
        }
        protected override void SetValueFast(string key, StringValues value)
        {
            
            switch (key.Length)
            {
                case 10:
                    {
                        if ("Connection".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            _bits |= 1L;
                            _headers._Connection = value;
                            return;
                        }
                    
                        if ("User-Agent".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            _bits |= 512L;
                            _headers._UserAgent = value;
                            return;
                        }
                    }
                    break;

                case 17:
                    {
                        if ("Transfer-Encoding".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            _bits |= 2L;
                            _headers._TransferEncoding = value;
                            return;
                        }
                    }
                    break;

                case 14:
                    {
                        if ("Content-Length".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            _bits |= 4L;
                            _headers._ContentLength = value;
                            return;
                        }
                    }
                    break;

                case 6:
                    {
                        if ("Accept".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            _bits |= 8L;
                            _headers._Accept = value;
                            return;
                        }
                    
                        if ("Cookie".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            _bits |= 64L;
                            _headers._Cookie = value;
                            return;
                        }
                    }
                    break;

                case 15:
                    {
                        if ("Accept-Encoding".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            _bits |= 16L;
                            _headers._AcceptEncoding = value;
                            return;
                        }
                    
                        if ("Accept-Language".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            _bits |= 32L;
                            _headers._AcceptLanguage = value;
                            return;
                        }
                    }
                    break;

                case 4:
                    {
                        if ("Host".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            _bits |= 128L;
                            _headers._Host = value;
                            return;
                        }
                    }
                    break;

                case 7:
                    {
                        if ("Referer".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            _bits |= 256L;
                            _headers._Referer = value;
                            return;
                        }
                    }
                    break;
}
            
            Unknown[key] = value;
        }
        protected override void AddValueFast(string key, StringValues value)
        {
            
            switch (key.Length)
            {
                case 10:
                    {
                        if ("Connection".Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            if (((_bits & 1L) != 0))
                            {
                                ThrowDuplicateKeyException();
                            }
                            _bits |= 1L;
                            _headers._Connection = value;
                            return;
                        }
                    
                        if ("User-Agent".Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            if (((_bits & 512L) != 0))
                            {
                                ThrowDuplicateKeyException();
                            }
                            _bits |= 512L;
                            _headers._UserAgent = value;
                            return;
                        }
                    }
                    break;
            
                case 17:
                    {
                        if ("Transfer-Encoding".Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            if (((_bits & 2L) != 0))
                            {
                                ThrowDuplicateKeyException();
                            }
                            _bits |= 2L;
                            _headers._TransferEncoding = value;
                            return;
                        }
                    }
                    break;
            
                case 14:
                    {
                        if ("Content-Length".Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            if (((_bits & 4L) != 0))
                            {
                                ThrowDuplicateKeyException();
                            }
                            _bits |= 4L;
                            _headers._ContentLength = value;
                            return;
                        }
                    }
                    break;
            
                case 6:
                    {
                        if ("Accept".Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            if (((_bits & 8L) != 0))
                            {
                                ThrowDuplicateKeyException();
                            }
                            _bits |= 8L;
                            _headers._Accept = value;
                            return;
                        }
                    
                        if ("Cookie".Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            if (((_bits & 64L) != 0))
                            {
                                ThrowDuplicateKeyException();
                            }
                            _bits |= 64L;
                            _headers._Cookie = value;
                            return;
                        }
                    }
                    break;
            
                case 15:
                    {
                        if ("Accept-Encoding".Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            if (((_bits & 16L) != 0))
                            {
                                ThrowDuplicateKeyException();
                            }
                            _bits |= 16L;
                            _headers._AcceptEncoding = value;
                            return;
                        }
                    
                        if ("Accept-Language".Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            if (((_bits & 32L) != 0))
                            {
                                ThrowDuplicateKeyException();
                            }
                            _bits |= 32L;
                            _headers._AcceptLanguage = value;
                            return;
                        }
                    }
                    break;
            
                case 4:
                    {
                        if ("Host".Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            if (((_bits & 128L) != 0))
                            {
                                ThrowDuplicateKeyException();
                            }
                            _bits |= 128L;
                            _headers._Host = value;
                            return;
                        }
                    }
                    break;
            
                case 7:
                    {
                        if ("Referer".Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            if (((_bits & 256L) != 0))
                            {
                                ThrowDuplicateKeyException();
                            }
                            _bits |= 256L;
                            _headers._Referer = value;
                            return;
                        }
                    }
                    break;
            }
            
            Unknown.Add(key, value);
        }
        protected override bool RemoveFast(string key)
        {
            switch (key.Length)
            {
                case 10:
                    {
                        if ("Connection".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            if (((_bits & 1L) != 0))
                            {
                                _bits &= ~1L;
                                _headers._Connection = StringValues.Empty;
                                return true;
                            }
                            else
                            {
                                return false;
                            }
                        }
                    
                        if ("User-Agent".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            if (((_bits & 512L) != 0))
                            {
                                _bits &= ~512L;
                                _headers._UserAgent = StringValues.Empty;
                                return true;
                            }
                            else
                            {
                                return false;
                            }
                        }
                    }
                    break;
            
                case 17:
                    {
                        if ("Transfer-Encoding".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            if (((_bits & 2L) != 0))
                            {
                                _bits &= ~2L;
                                _headers._TransferEncoding = StringValues.Empty;
                                return true;
                            }
                            else
                            {
                                return false;
                            }
                        }
                    }
                    break;
            
                case 14:
                    {
                        if ("Content-Length".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            if (((_bits & 4L) != 0))
                            {
                                _bits &= ~4L;
                                _headers._ContentLength = StringValues.Empty;
                                return true;
                            }
                            else
                            {
                                return false;
                            }
                        }
                    }
                    break;
            
                case 6:
                    {
                        if ("Accept".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            if (((_bits & 8L) != 0))
                            {
                                _bits &= ~8L;
                                _headers._Accept = StringValues.Empty;
                                return true;
                            }
                            else
                            {
                                return false;
                            }
                        }
                    
                        if ("Cookie".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            if (((_bits & 64L) != 0))
                            {
                                _bits &= ~64L;
                                _headers._Cookie = StringValues.Empty;
                                return true;
                            }
                            else
                            {
                                return false;
                            }
                        }
                    }
                    break;
            
                case 15:
                    {
                        if ("Accept-Encoding".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            if (((_bits & 16L) != 0))
                            {
                                _bits &= ~16L;
                                _headers._AcceptEncoding = StringValues.Empty;
                                return true;
                            }
                            else
                            {
                                return false;
                            }
                        }
                    
                        if ("Accept-Language".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            if (((_bits & 32L) != 0))
                            {
                                _bits &= ~32L;
                                _headers._AcceptLanguage = StringValues.Empty;
                                return true;
                            }
                            else
                            {
                                return false;
                            }
                        }
                    }
                    break;
            
                case 4:
                    {
                        if ("Host".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            if (((_bits & 128L) != 0))
                            {
                                _bits &= ~128L;
                                _headers._Host = StringValues.Empty;
                                return true;
                            }
                            else
                            {
                                return false;
                            }
                        }
                    }
                    break;
            
                case 7:
                    {
                        if ("Referer".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            if (((_bits & 256L) != 0))
                            {
                                _bits &= ~256L;
                                _headers._Referer = StringValues.Empty;
                                return true;
                            }
                            else
                            {
                                return false;
                            }
                        }
                    }
                    break;
            }
            return MaybeUnknown?.Remove(key) ?? false;
        }
        protected override void ClearFast()
        {
            _bits = 0;
            _headers = default(HeaderReferences);
            MaybeUnknown?.Clear();
        }
        
        protected override void CopyToFast(KeyValuePair<string, StringValues>[] array, int arrayIndex)
        {
            if (arrayIndex < 0)
            {
                ThrowArgumentException();
            }
            
                if (((_bits & 1L) != 0)) 
                {
                    if (arrayIndex == array.Length)
                    {
                        ThrowArgumentException();
                    }

                    array[arrayIndex] = new KeyValuePair<string, StringValues>("Connection", _headers._Connection);
                    ++arrayIndex;
                }
            
                if (((_bits & 2L) != 0)) 
                {
                    if (arrayIndex == array.Length)
                    {
                        ThrowArgumentException();
                    }

                    array[arrayIndex] = new KeyValuePair<string, StringValues>("Transfer-Encoding", _headers._TransferEncoding);
                    ++arrayIndex;
                }
            
                if (((_bits & 4L) != 0)) 
                {
                    if (arrayIndex == array.Length)
                    {
                        ThrowArgumentException();
                    }

                    array[arrayIndex] = new KeyValuePair<string, StringValues>("Content-Length", _headers._ContentLength);
                    ++arrayIndex;
                }
            
                if (((_bits & 8L) != 0)) 
                {
                    if (arrayIndex == array.Length)
                    {
                        ThrowArgumentException();
                    }

                    array[arrayIndex] = new KeyValuePair<string, StringValues>("Accept", _headers._Accept);
                    ++arrayIndex;
                }
            
                if (((_bits & 16L) != 0)) 
                {
                    if (arrayIndex == array.Length)
                    {
                        ThrowArgumentException();
                    }

                    array[arrayIndex] = new KeyValuePair<string, StringValues>("Accept-Encoding", _headers._AcceptEncoding);
                    ++arrayIndex;
                }
            
                if (((_bits & 32L) != 0)) 
                {
                    if (arrayIndex == array.Length)
                    {
                        ThrowArgumentException();
                    }

                    array[arrayIndex] = new KeyValuePair<string, StringValues>("Accept-Language", _headers._AcceptLanguage);
                    ++arrayIndex;
                }
            
                if (((_bits & 64L) != 0)) 
                {
                    if (arrayIndex == array.Length)
                    {
                        ThrowArgumentException();
                    }

                    array[arrayIndex] = new KeyValuePair<string, StringValues>("Cookie", _headers._Cookie);
                    ++arrayIndex;
                }
            
                if (((_bits & 128L) != 0)) 
                {
                    if (arrayIndex == array.Length)
                    {
                        ThrowArgumentException();
                    }

                    array[arrayIndex] = new KeyValuePair<string, StringValues>("Host", _headers._Host);
                    ++arrayIndex;
                }
            
                if (((_bits & 256L) != 0)) 
                {
                    if (arrayIndex == array.Length)
                    {
                        ThrowArgumentException();
                    }

                    array[arrayIndex] = new KeyValuePair<string, StringValues>("Referer", _headers._Referer);
                    ++arrayIndex;
                }
            
                if (((_bits & 512L) != 0)) 
                {
                    if (arrayIndex == array.Length)
                    {
                        ThrowArgumentException();
                    }

                    array[arrayIndex] = new KeyValuePair<string, StringValues>("User-Agent", _headers._UserAgent);
                    ++arrayIndex;
                }
            
            ((ICollection<KeyValuePair<string, StringValues>>)MaybeUnknown)?.CopyTo(array, arrayIndex);
        }
        
        
        public unsafe void Append(byte[] keyBytes, int keyOffset, int keyLength, string value)
        {
            fixed (byte* ptr = &keyBytes[keyOffset]) 
            { 
                var pUB = ptr; 
                var pUL = (ulong*)pUB; 
                var pUI = (uint*)pUB; 
                var pUS = (ushort*)pUB;
                switch (keyLength)
                {
                    case 10:
                        {
                            if ((((pUL[0] & 16131858542891098079uL) == 5283922227757993795uL) && ((pUS[4] & 57311u) == 20047u))) 
                            {
                                if (((_bits & 1L) != 0))
                                {
                                    _headers._Connection = AppendValue(_headers._Connection, value);
                                }
                                else
                                {
                                    _bits |= 1L;
                                    _headers._Connection = new StringValues(value);
                                }
                                return;
                            }
                        
                            if ((((pUL[0] & 16131858680330051551uL) == 4992030374873092949uL) && ((pUS[4] & 57311u) == 21582u))) 
                            {
                                if (((_bits & 512L) != 0))
                                {
                                    _headers._UserAgent = AppendValue(_headers._UserAgent, value);
                                }
                                else
                                {
                                    _bits |= 512L;
                                    _headers._UserAgent = new StringValues(value);
                                }
                                return;
                            }
                        }
                        break;
                
                    case 17:
                        {
                            if ((((pUL[0] & 16131858542891098079uL) == 5928221808112259668uL) && ((pUL[1] & 16131858542891098111uL) == 5641115115480565037uL) && ((pUB[16] & 223u) == 71u))) 
                            {
                                if (((_bits & 2L) != 0))
                                {
                                    _headers._TransferEncoding = AppendValue(_headers._TransferEncoding, value);
                                }
                                else
                                {
                                    _bits |= 2L;
                                    _headers._TransferEncoding = new StringValues(value);
                                }
                                return;
                            }
                        }
                        break;
                
                    case 14:
                        {
                            if ((((pUL[0] & 18437701552104792031uL) == 3266321689424580419uL) && ((pUI[2] & 3755991007u) == 1196311884u) && ((pUS[6] & 57311u) == 18516u))) 
                            {
                                if (((_bits & 4L) != 0))
                                {
                                    _headers._ContentLength = AppendValue(_headers._ContentLength, value);
                                }
                                else
                                {
                                    _bits |= 4L;
                                    _headers._ContentLength = new StringValues(value);
                                }
                                return;
                            }
                        }
                        break;
                
                    case 6:
                        {
                            if ((((pUI[0] & 3755991007u) == 1162036033u) && ((pUS[2] & 57311u) == 21584u))) 
                            {
                                if (((_bits & 8L) != 0))
                                {
                                    _headers._Accept = AppendValue(_headers._Accept, value);
                                }
                                else
                                {
                                    _bits |= 8L;
                                    _headers._Accept = new StringValues(value);
                                }
                                return;
                            }
                        
                            if ((((pUI[0] & 3755991007u) == 1263488835u) && ((pUS[2] & 57311u) == 17737u))) 
                            {
                                if (((_bits & 64L) != 0))
                                {
                                    _headers._Cookie = AppendValue(_headers._Cookie, value);
                                }
                                else
                                {
                                    _bits |= 64L;
                                    _headers._Cookie = new StringValues(value);
                                }
                                return;
                            }
                        }
                        break;
                
                    case 15:
                        {
                            if ((((pUL[0] & 16140865742145839071uL) == 4984733066305160001uL) && ((pUI[2] & 3755991007u) == 1146045262u) && ((pUS[6] & 57311u) == 20041u) && ((pUB[14] & 223u) == 71u))) 
                            {
                                if (((_bits & 16L) != 0))
                                {
                                    _headers._AcceptEncoding = AppendValue(_headers._AcceptEncoding, value);
                                }
                                else
                                {
                                    _bits |= 16L;
                                    _headers._AcceptEncoding = new StringValues(value);
                                }
                                return;
                            }
                        
                            if ((((pUL[0] & 16140865742145839071uL) == 5489136224570655553uL) && ((pUI[2] & 3755991007u) == 1430736449u) && ((pUS[6] & 57311u) == 18241u) && ((pUB[14] & 223u) == 69u))) 
                            {
                                if (((_bits & 32L) != 0))
                                {
                                    _headers._AcceptLanguage = AppendValue(_headers._AcceptLanguage, value);
                                }
                                else
                                {
                                    _bits |= 32L;
                                    _headers._AcceptLanguage = new StringValues(value);
                                }
                                return;
                            }
                        }
                        break;
                
                    case 4:
                        {
                            if ((((pUI[0] & 3755991007u) == 1414745928u))) 
                            {
                                if (((_bits & 128L) != 0))
                                {
                                    _headers._Host = AppendValue(_headers._Host, value);
                                }
                                else
                                {
                                    _bits |= 128L;
                                    _headers._Host = new StringValues(value);
                                }
                                return;
                            }
                        }
                        break;
                
                    case 7:
                        {
                            if ((((pUI[0] & 3755991007u) == 1162233170u) && ((pUS[2] & 57311u) == 17746u) && ((pUB[6] & 223u) == 82u))) 
                            {
                                if (((_bits & 256L) != 0))
                                {
                                    _headers._Referer = AppendValue(_headers._Referer, value);
                                }
                                else
                                {
                                    _bits |= 256L;
                                    _headers._Referer = new StringValues(value);
                                }
                                return;
                            }
                        }
                        break;
                }
            }
            var key = System.Text.Encoding.ASCII.GetString(keyBytes, keyOffset, keyLength);
            StringValues existing;
            Unknown.TryGetValue(key, out existing);
            Unknown[key] = AppendValue(existing, value);
        }
        private struct HeaderReferences
        {
            public StringValues _Connection;
            public StringValues _TransferEncoding;
            public StringValues _ContentLength;
            public StringValues _Accept;
            public StringValues _AcceptEncoding;
            public StringValues _AcceptLanguage;
            public StringValues _Cookie;
            public StringValues _Host;
            public StringValues _Referer;
            public StringValues _UserAgent;
            
        }

        public partial struct Enumerator
        {
            public bool MoveNext()
            {
                switch (_state)
                {
                    
                        case 0:
                            goto state0;
                    
                        case 1:
                            goto state1;
                    
                        case 2:
                            goto state2;
                    
                        case 3:
                            goto state3;
                    
                        case 4:
                            goto state4;
                    
                        case 5:
                            goto state5;
                    
                        case 6:
                            goto state6;
                    
                        case 7:
                            goto state7;
                    
                        case 8:
                            goto state8;
                    
                        case 9:
                            goto state9;
                    
                    default:
                        goto state_default;
                }
                
                state0:
                    if (((_bits & 1L) != 0))
                    {
                        _current = new KeyValuePair<string, StringValues>("Connection", _collection._headers._Connection);
                        _state = 1;
                        return true;
                    }
                
                state1:
                    if (((_bits & 2L) != 0))
                    {
                        _current = new KeyValuePair<string, StringValues>("Transfer-Encoding", _collection._headers._TransferEncoding);
                        _state = 2;
                        return true;
                    }
                
                state2:
                    if (((_bits & 4L) != 0))
                    {
                        _current = new KeyValuePair<string, StringValues>("Content-Length", _collection._headers._ContentLength);
                        _state = 3;
                        return true;
                    }
                
                state3:
                    if (((_bits & 8L) != 0))
                    {
                        _current = new KeyValuePair<string, StringValues>("Accept", _collection._headers._Accept);
                        _state = 4;
                        return true;
                    }
                
                state4:
                    if (((_bits & 16L) != 0))
                    {
                        _current = new KeyValuePair<string, StringValues>("Accept-Encoding", _collection._headers._AcceptEncoding);
                        _state = 5;
                        return true;
                    }
                
                state5:
                    if (((_bits & 32L) != 0))
                    {
                        _current = new KeyValuePair<string, StringValues>("Accept-Language", _collection._headers._AcceptLanguage);
                        _state = 6;
                        return true;
                    }
                
                state6:
                    if (((_bits & 64L) != 0))
                    {
                        _current = new KeyValuePair<string, StringValues>("Cookie", _collection._headers._Cookie);
                        _state = 7;
                        return true;
                    }
                
                state7:
                    if (((_bits & 128L) != 0))
                    {
                        _current = new KeyValuePair<string, StringValues>("Host", _collection._headers._Host);
                        _state = 8;
                        return true;
                    }
                
                state8:
                    if (((_bits & 256L) != 0))
                    {
                        _current = new KeyValuePair<string, StringValues>("Referer", _collection._headers._Referer);
                        _state = 9;
                        return true;
                    }
                
                state9:
                    if (((_bits & 512L) != 0))
                    {
                        _current = new KeyValuePair<string, StringValues>("User-Agent", _collection._headers._UserAgent);
                        _state = 10;
                        return true;
                    }
                
                state_default:
                    if (!_hasUnknown || !_unknownEnumerator.MoveNext())
                    {
                        _current = default(KeyValuePair<string, StringValues>);
                        return false;
                    }
                    _current = _unknownEnumerator.Current;
                    return true;
            }
        }
    }

    public partial class FrameResponseHeaders
    {
        private static byte[] _headerBytes = new byte[]
        {
            13,10,67,111,110,110,101,99,116,105,111,110,58,32,13,10,84,114,97,110,115,102,101,114,45,69,110,99,111,100,105,110,103,58,32,13,10,67,111,110,116,101,110,116,45,76,101,110,103,116,104,58,32,13,10,67,111,110,116,101,110,116,45,84,121,112,101,58,32,13,10,68,97,116,101,58,32,13,10,83,101,114,118,101,114,58,32,
        };
        
        private long _bits = 0;
        private HeaderReferences _headers;
        
        public StringValues HeaderConnection
        {
            get
            {
                if (((_bits & 1L) != 0))
                {
                    return _headers._Connection;
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= 1L;
                _headers._Connection = value; 
                _headers._rawConnection = null;
            }
        }
        public StringValues HeaderTransferEncoding
        {
            get
            {
                if (((_bits & 2L) != 0))
                {
                    return _headers._TransferEncoding;
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= 2L;
                _headers._TransferEncoding = value; 
                _headers._rawTransferEncoding = null;
            }
        }
        public StringValues HeaderContentLength
        {
            get
            {
                if (((_bits & 4L) != 0))
                {
                    return _headers._ContentLength;
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= 4L;
                _headers._ContentLength = value; 
                _headers._rawContentLength = null;
            }
        }
        public StringValues HeaderContentType
        {
            get
            {
                if (((_bits & 8L) != 0))
                {
                    return _headers._ContentType;
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= 8L;
                _headers._ContentType = value; 
            }
        }
        public StringValues HeaderDate
        {
            get
            {
                if (((_bits & 16L) != 0))
                {
                    return _headers._Date;
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= 16L;
                _headers._Date = value; 
                _headers._rawDate = null;
            }
        }
        public StringValues HeaderServer
        {
            get
            {
                if (((_bits & 32L) != 0))
                {
                    return _headers._Server;
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= 32L;
                _headers._Server = value; 
                _headers._rawServer = null;
            }
        }
        
        public void SetRawConnection(StringValues value, byte[] raw)
        {
            _bits |= 1L;
            _headers._Connection = value; 
            _headers._rawConnection = raw;
        }
        public void SetRawTransferEncoding(StringValues value, byte[] raw)
        {
            _bits |= 2L;
            _headers._TransferEncoding = value; 
            _headers._rawTransferEncoding = raw;
        }
        public void SetRawContentLength(StringValues value, byte[] raw)
        {
            _bits |= 4L;
            _headers._ContentLength = value; 
            _headers._rawContentLength = raw;
        }
        public void SetRawDate(StringValues value, byte[] raw)
        {
            _bits |= 16L;
            _headers._Date = value; 
            _headers._rawDate = raw;
        }
        public void SetRawServer(StringValues value, byte[] raw)
        {
            _bits |= 32L;
            _headers._Server = value; 
            _headers._rawServer = raw;
        }
        protected override int GetCountFast()
        {
            return BitCount(_bits) + (MaybeUnknown?.Count ?? 0);
        }
        protected override StringValues GetValueFast(string key)
        {
            switch (key.Length)
            {
                case 10:
                    {
                        if ("Connection".Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            if (((_bits & 1L) != 0))
                            {
                                return _headers._Connection;
                            }
                            else
                            {
                                ThrowKeyNotFoundException();
                            }
                        }
                    }
                    break;

                case 17:
                    {
                        if ("Transfer-Encoding".Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            if (((_bits & 2L) != 0))
                            {
                                return _headers._TransferEncoding;
                            }
                            else
                            {
                                ThrowKeyNotFoundException();
                            }
                        }
                    }
                    break;

                case 14:
                    {
                        if ("Content-Length".Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            if (((_bits & 4L) != 0))
                            {
                                return _headers._ContentLength;
                            }
                            else
                            {
                                ThrowKeyNotFoundException();
                            }
                        }
                    }
                    break;

                case 12:
                    {
                        if ("Content-Type".Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            if (((_bits & 8L) != 0))
                            {
                                return _headers._ContentType;
                            }
                            else
                            {
                                ThrowKeyNotFoundException();
                            }
                        }
                    }
                    break;

                case 4:
                    {
                        if ("Date".Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            if (((_bits & 16L) != 0))
                            {
                                return _headers._Date;
                            }
                            else
                            {
                                ThrowKeyNotFoundException();
                            }
                        }
                    }
                    break;

                case 6:
                    {
                        if ("Server".Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            if (((_bits & 32L) != 0))
                            {
                                return _headers._Server;
                            }
                            else
                            {
                                ThrowKeyNotFoundException();
                            }
                        }
                    }
                    break;
}
            if (MaybeUnknown == null) 
            {
                ThrowKeyNotFoundException();
            }
            return MaybeUnknown[key];
        }
        protected override bool TryGetValueFast(string key, out StringValues value)
        {
            switch (key.Length)
            {
                case 10:
                    {
                        if ("Connection".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            if (((_bits & 1L) != 0))
                            {
                                value = _headers._Connection;
                                return true;
                            }
                            else
                            {
                                value = StringValues.Empty;
                                return false;
                            }
                        }
                    }
                    break;

                case 17:
                    {
                        if ("Transfer-Encoding".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            if (((_bits & 2L) != 0))
                            {
                                value = _headers._TransferEncoding;
                                return true;
                            }
                            else
                            {
                                value = StringValues.Empty;
                                return false;
                            }
                        }
                    }
                    break;

                case 14:
                    {
                        if ("Content-Length".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            if (((_bits & 4L) != 0))
                            {
                                value = _headers._ContentLength;
                                return true;
                            }
                            else
                            {
                                value = StringValues.Empty;
                                return false;
                            }
                        }
                    }
                    break;

                case 12:
                    {
                        if ("Content-Type".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            if (((_bits & 8L) != 0))
                            {
                                value = _headers._ContentType;
                                return true;
                            }
                            else
                            {
                                value = StringValues.Empty;
                                return false;
                            }
                        }
                    }
                    break;

                case 4:
                    {
                        if ("Date".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            if (((_bits & 16L) != 0))
                            {
                                value = _headers._Date;
                                return true;
                            }
                            else
                            {
                                value = StringValues.Empty;
                                return false;
                            }
                        }
                    }
                    break;

                case 6:
                    {
                        if ("Server".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            if (((_bits & 32L) != 0))
                            {
                                value = _headers._Server;
                                return true;
                            }
                            else
                            {
                                value = StringValues.Empty;
                                return false;
                            }
                        }
                    }
                    break;
}
            value = StringValues.Empty;
            return MaybeUnknown?.TryGetValue(key, out value) ?? false;
        }
        protected override void SetValueFast(string key, StringValues value)
        {
            ValidateHeaderCharacters(value);
            switch (key.Length)
            {
                case 10:
                    {
                        if ("Connection".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            _bits |= 1L;
                            _headers._Connection = value;
                            _headers._rawConnection = null;
                            return;
                        }
                    }
                    break;

                case 17:
                    {
                        if ("Transfer-Encoding".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            _bits |= 2L;
                            _headers._TransferEncoding = value;
                            _headers._rawTransferEncoding = null;
                            return;
                        }
                    }
                    break;

                case 14:
                    {
                        if ("Content-Length".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            _bits |= 4L;
                            _headers._ContentLength = value;
                            _headers._rawContentLength = null;
                            return;
                        }
                    }
                    break;

                case 12:
                    {
                        if ("Content-Type".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            _bits |= 8L;
                            _headers._ContentType = value;
                            return;
                        }
                    }
                    break;

                case 4:
                    {
                        if ("Date".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            _bits |= 16L;
                            _headers._Date = value;
                            _headers._rawDate = null;
                            return;
                        }
                    }
                    break;

                case 6:
                    {
                        if ("Server".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            _bits |= 32L;
                            _headers._Server = value;
                            _headers._rawServer = null;
                            return;
                        }
                    }
                    break;
}
            ValidateHeaderCharacters(key);
            Unknown[key] = value;
        }
        protected override void AddValueFast(string key, StringValues value)
        {
            ValidateHeaderCharacters(value);
            switch (key.Length)
            {
                case 10:
                    {
                        if ("Connection".Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            if (((_bits & 1L) != 0))
                            {
                                ThrowDuplicateKeyException();
                            }
                            _bits |= 1L;
                            _headers._Connection = value;
                            _headers._rawConnection = null;
                            return;
                        }
                    }
                    break;
            
                case 17:
                    {
                        if ("Transfer-Encoding".Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            if (((_bits & 2L) != 0))
                            {
                                ThrowDuplicateKeyException();
                            }
                            _bits |= 2L;
                            _headers._TransferEncoding = value;
                            _headers._rawTransferEncoding = null;
                            return;
                        }
                    }
                    break;
            
                case 14:
                    {
                        if ("Content-Length".Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            if (((_bits & 4L) != 0))
                            {
                                ThrowDuplicateKeyException();
                            }
                            _bits |= 4L;
                            _headers._ContentLength = value;
                            _headers._rawContentLength = null;
                            return;
                        }
                    }
                    break;
            
                case 12:
                    {
                        if ("Content-Type".Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            if (((_bits & 8L) != 0))
                            {
                                ThrowDuplicateKeyException();
                            }
                            _bits |= 8L;
                            _headers._ContentType = value;
                            return;
                        }
                    }
                    break;
            
                case 4:
                    {
                        if ("Date".Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            if (((_bits & 16L) != 0))
                            {
                                ThrowDuplicateKeyException();
                            }
                            _bits |= 16L;
                            _headers._Date = value;
                            _headers._rawDate = null;
                            return;
                        }
                    }
                    break;
            
                case 6:
                    {
                        if ("Server".Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            if (((_bits & 32L) != 0))
                            {
                                ThrowDuplicateKeyException();
                            }
                            _bits |= 32L;
                            _headers._Server = value;
                            _headers._rawServer = null;
                            return;
                        }
                    }
                    break;
            }
            ValidateHeaderCharacters(key);
            Unknown.Add(key, value);
        }
        protected override bool RemoveFast(string key)
        {
            switch (key.Length)
            {
                case 10:
                    {
                        if ("Connection".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            if (((_bits & 1L) != 0))
                            {
                                _bits &= ~1L;
                                _headers._Connection = StringValues.Empty;
                                _headers._rawConnection = null;
                                return true;
                            }
                            else
                            {
                                return false;
                            }
                        }
                    }
                    break;
            
                case 17:
                    {
                        if ("Transfer-Encoding".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            if (((_bits & 2L) != 0))
                            {
                                _bits &= ~2L;
                                _headers._TransferEncoding = StringValues.Empty;
                                _headers._rawTransferEncoding = null;
                                return true;
                            }
                            else
                            {
                                return false;
                            }
                        }
                    }
                    break;
            
                case 14:
                    {
                        if ("Content-Length".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            if (((_bits & 4L) != 0))
                            {
                                _bits &= ~4L;
                                _headers._ContentLength = StringValues.Empty;
                                _headers._rawContentLength = null;
                                return true;
                            }
                            else
                            {
                                return false;
                            }
                        }
                    }
                    break;
            
                case 12:
                    {
                        if ("Content-Type".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            if (((_bits & 8L) != 0))
                            {
                                _bits &= ~8L;
                                _headers._ContentType = StringValues.Empty;
                                return true;
                            }
                            else
                            {
                                return false;
                            }
                        }
                    }
                    break;
            
                case 4:
                    {
                        if ("Date".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            if (((_bits & 16L) != 0))
                            {
                                _bits &= ~16L;
                                _headers._Date = StringValues.Empty;
                                _headers._rawDate = null;
                                return true;
                            }
                            else
                            {
                                return false;
                            }
                        }
                    }
                    break;
            
                case 6:
                    {
                        if ("Server".Equals(key, StringComparison.OrdinalIgnoreCase)) 
                        {
                            if (((_bits & 32L) != 0))
                            {
                                _bits &= ~32L;
                                _headers._Server = StringValues.Empty;
                                _headers._rawServer = null;
                                return true;
                            }
                            else
                            {
                                return false;
                            }
                        }
                    }
                    break;
            }
            return MaybeUnknown?.Remove(key) ?? false;
        }
        protected override void ClearFast()
        {
            _bits = 0;
            _headers = default(HeaderReferences);
            MaybeUnknown?.Clear();
        }
        
        protected override void CopyToFast(KeyValuePair<string, StringValues>[] array, int arrayIndex)
        {
            if (arrayIndex < 0)
            {
                ThrowArgumentException();
            }
            
                if (((_bits & 1L) != 0)) 
                {
                    if (arrayIndex == array.Length)
                    {
                        ThrowArgumentException();
                    }

                    array[arrayIndex] = new KeyValuePair<string, StringValues>("Connection", _headers._Connection);
                    ++arrayIndex;
                }
            
                if (((_bits & 2L) != 0)) 
                {
                    if (arrayIndex == array.Length)
                    {
                        ThrowArgumentException();
                    }

                    array[arrayIndex] = new KeyValuePair<string, StringValues>("Transfer-Encoding", _headers._TransferEncoding);
                    ++arrayIndex;
                }
            
                if (((_bits & 4L) != 0)) 
                {
                    if (arrayIndex == array.Length)
                    {
                        ThrowArgumentException();
                    }

                    array[arrayIndex] = new KeyValuePair<string, StringValues>("Content-Length", _headers._ContentLength);
                    ++arrayIndex;
                }
            
                if (((_bits & 8L) != 0)) 
                {
                    if (arrayIndex == array.Length)
                    {
                        ThrowArgumentException();
                    }

                    array[arrayIndex] = new KeyValuePair<string, StringValues>("Content-Type", _headers._ContentType);
                    ++arrayIndex;
                }
            
                if (((_bits & 16L) != 0)) 
                {
                    if (arrayIndex == array.Length)
                    {
                        ThrowArgumentException();
                    }

                    array[arrayIndex] = new KeyValuePair<string, StringValues>("Date", _headers._Date);
                    ++arrayIndex;
                }
            
                if (((_bits & 32L) != 0)) 
                {
                    if (arrayIndex == array.Length)
                    {
                        ThrowArgumentException();
                    }

                    array[arrayIndex] = new KeyValuePair<string, StringValues>("Server", _headers._Server);
                    ++arrayIndex;
                }
            
            ((ICollection<KeyValuePair<string, StringValues>>)MaybeUnknown)?.CopyTo(array, arrayIndex);
        }
        
        protected void CopyToFast(ref MemoryPoolIterator output)
        {
            
                if (((_bits & 1L) != 0)) 
                { 
                    if (_headers._rawConnection != null) 
                    {
                        output.CopyFrom(_headers._rawConnection, 0, _headers._rawConnection.Length);
                    } 
                    else 
                        foreach (var value in _headers._Connection)
                        {
                            if (value != null)
                            {
                                output.CopyFrom(_headerBytes, 0, 14);
                                output.CopyFromAscii(value);
                            }
                        }
                }
            
                if (((_bits & 2L) != 0)) 
                { 
                    if (_headers._rawTransferEncoding != null) 
                    {
                        output.CopyFrom(_headers._rawTransferEncoding, 0, _headers._rawTransferEncoding.Length);
                    } 
                    else 
                        foreach (var value in _headers._TransferEncoding)
                        {
                            if (value != null)
                            {
                                output.CopyFrom(_headerBytes, 14, 21);
                                output.CopyFromAscii(value);
                            }
                        }
                }
            
                if (((_bits & 4L) != 0)) 
                { 
                    if (_headers._rawContentLength != null) 
                    {
                        output.CopyFrom(_headers._rawContentLength, 0, _headers._rawContentLength.Length);
                    } 
                    else 
                        foreach (var value in _headers._ContentLength)
                        {
                            if (value != null)
                            {
                                output.CopyFrom(_headerBytes, 35, 18);
                                output.CopyFromAscii(value);
                            }
                        }
                }
            
                if (((_bits & 8L) != 0)) 
                { 
                        foreach (var value in _headers._ContentType)
                        {
                            if (value != null)
                            {
                                output.CopyFrom(_headerBytes, 53, 16);
                                output.CopyFromAscii(value);
                            }
                        }
                }
            
                if (((_bits & 16L) != 0)) 
                { 
                    if (_headers._rawDate != null) 
                    {
                        output.CopyFrom(_headers._rawDate, 0, _headers._rawDate.Length);
                    } 
                    else 
                        foreach (var value in _headers._Date)
                        {
                            if (value != null)
                            {
                                output.CopyFrom(_headerBytes, 69, 8);
                                output.CopyFromAscii(value);
                            }
                        }
                }
            
                if (((_bits & 32L) != 0)) 
                { 
                    if (_headers._rawServer != null) 
                    {
                        output.CopyFrom(_headers._rawServer, 0, _headers._rawServer.Length);
                    } 
                    else 
                        foreach (var value in _headers._Server)
                        {
                            if (value != null)
                            {
                                output.CopyFrom(_headerBytes, 77, 10);
                                output.CopyFromAscii(value);
                            }
                        }
                }
            
        }
        
        private struct HeaderReferences
        {
            public StringValues _Connection;
            public StringValues _TransferEncoding;
            public StringValues _ContentLength;
            public StringValues _ContentType;
            public StringValues _Date;
            public StringValues _Server;
            
            public byte[] _rawConnection;
            public byte[] _rawTransferEncoding;
            public byte[] _rawContentLength;
            public byte[] _rawDate;
            public byte[] _rawServer;
        }

        public partial struct Enumerator
        {
            public bool MoveNext()
            {
                switch (_state)
                {
                    
                        case 0:
                            goto state0;
                    
                        case 1:
                            goto state1;
                    
                        case 2:
                            goto state2;
                    
                        case 3:
                            goto state3;
                    
                        case 4:
                            goto state4;
                    
                        case 5:
                            goto state5;
                    
                    default:
                        goto state_default;
                }
                
                state0:
                    if (((_bits & 1L) != 0))
                    {
                        _current = new KeyValuePair<string, StringValues>("Connection", _collection._headers._Connection);
                        _state = 1;
                        return true;
                    }
                
                state1:
                    if (((_bits & 2L) != 0))
                    {
                        _current = new KeyValuePair<string, StringValues>("Transfer-Encoding", _collection._headers._TransferEncoding);
                        _state = 2;
                        return true;
                    }
                
                state2:
                    if (((_bits & 4L) != 0))
                    {
                        _current = new KeyValuePair<string, StringValues>("Content-Length", _collection._headers._ContentLength);
                        _state = 3;
                        return true;
                    }
                
                state3:
                    if (((_bits & 8L) != 0))
                    {
                        _current = new KeyValuePair<string, StringValues>("Content-Type", _collection._headers._ContentType);
                        _state = 4;
                        return true;
                    }
                
                state4:
                    if (((_bits & 16L) != 0))
                    {
                        _current = new KeyValuePair<string, StringValues>("Date", _collection._headers._Date);
                        _state = 5;
                        return true;
                    }
                
                state5:
                    if (((_bits & 32L) != 0))
                    {
                        _current = new KeyValuePair<string, StringValues>("Server", _collection._headers._Server);
                        _state = 6;
                        return true;
                    }
                
                state_default:
                    if (!_hasUnknown || !_unknownEnumerator.MoveNext())
                    {
                        _current = default(KeyValuePair<string, StringValues>);
                        return false;
                    }
                    _current = _unknownEnumerator.Current;
                    return true;
            }
        }
    }
}