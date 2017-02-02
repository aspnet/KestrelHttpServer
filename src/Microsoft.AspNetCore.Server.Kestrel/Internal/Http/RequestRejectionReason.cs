﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{
    public enum RequestRejectionReason
    {
        UnrecognizedHTTPVersion,
        HeadersCorruptedInvalidHeaderSequence,
        HeaderLineMustNotStartWithWhitespace,
        NoColonCharacterFoundInHeaderLine,
        WhitespaceIsNotAllowedInHeaderName,
        HeaderValueMustNotContainCR,
        HeaderValueLineFoldingNotSupported,
        InvalidRequestLine,
        MalformedRequestInvalidHeaders,
        InvalidContentLength,
        MultipleContentLengths,
        UnexpectedEndOfRequestContent,
        BadChunkSuffix,
        BadChunkSizeData,
        ChunkedRequestIncomplete,
        PathContainsNullCharacters,
        InvalidCharactersInHeaderName,
        NonAsciiOrNullCharactersInRequestLine,
        NonAsciiOrNullCharactersInRequestHeader,
        RequestLineTooLong,
        HeadersExceedMaxTotalSize,
        MissingCRInHeaderLine,
        TooManyHeaders,
        RequestTimeout,
        FinalTransferCodingNotChunked,
        LengthRequired,
        LengthRequiredHttp10,
    }
}
