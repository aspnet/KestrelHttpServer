﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{
    public enum RequestRejectionReason
    {
        UnrecognizedHTTPVersion,
        HeadersCorruptedInvalidHeaderSequence,
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
        NonAsciiOrNullCharactersInInputString,
        RequestLineTooLong,
        HeadersExceedMaxTotalSize,
        MissingCRInHeaderLine,
        TooManyHeaders,
        RequestTimeout,
        FinalTransferCodingNotChunked,
        LengthRequired,
        LengthRequiredHttp10
    }
}
