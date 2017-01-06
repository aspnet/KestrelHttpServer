// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Infrastructure;
using Microsoft.Extensions.Primitives;

namespace Microsoft.AspNetCore.Server.Kestrel.Internal.Http
{
    public partial class FrameRequestHeaders
    {
        private static readonly HeaderKeyStringData[][] _keyStringDataByLength = new HeaderKeyStringData[][]
        {
            NoHeaders,
            NoHeaders,
            new HeaderKeyStringData[] {
                new HeaderKeyStringData((int)HeaderIndex.TE, ShortHeader, ShortHeader, 4292870111u, 4522068u, 0, 0),},
            new HeaderKeyStringData[] {
                new HeaderKeyStringData((int)HeaderIndex.Via, ShortHeader, ShortHeader, 4292870111u, 4784214u, (ushort)65503u, (ushort)65u),},
            new HeaderKeyStringData[] {
                new HeaderKeyStringData((int)HeaderIndex.Host, new ulong[] {18437736737013759967uL}, new ulong[] {23644254531158088uL}, 0, 0, 0, 0),
                new HeaderKeyStringData((int)HeaderIndex.Date, new ulong[] {18437736737013759967uL}, new ulong[] {19422134174548036uL}, 0, 0, 0, 0),
                new HeaderKeyStringData((int)HeaderIndex.From, new ulong[] {18437736737013759967uL}, new ulong[] {21673912514510918uL}, 0, 0, 0, 0),},
            new HeaderKeyStringData[] {
                new HeaderKeyStringData((int)HeaderIndex.Allow, new ulong[] {18437736737013759967uL}, new ulong[] {22236849582637121uL}, 0, 0, (ushort)65503u, (ushort)87u),
                new HeaderKeyStringData((int)HeaderIndex.Range, new ulong[] {18437736737013759967uL}, new ulong[] {19985058358165586uL}, 0, 0, (ushort)65503u, (ushort)69u),},
            new HeaderKeyStringData[] {
                new HeaderKeyStringData((int)HeaderIndex.Accept, new ulong[] {18437736737013759967uL}, new ulong[] {19422061160235073uL}, 4292870111u, 5505104u, 0, 0),
                new HeaderKeyStringData((int)HeaderIndex.Pragma, new ulong[] {18437736737013759967uL}, new ulong[] {19985002524704848uL}, 4292870111u, 4259917u, 0, 0),
                new HeaderKeyStringData((int)HeaderIndex.Cookie, new ulong[] {18437736737013759967uL}, new ulong[] {21110962560892995uL}, 4292870111u, 4522057u, 0, 0),
                new HeaderKeyStringData((int)HeaderIndex.Expect, new ulong[] {18437736737013759967uL}, new ulong[] {19422116996186181uL}, 4292870111u, 5505091u, 0, 0),
                new HeaderKeyStringData((int)HeaderIndex.Origin, new ulong[] {18437736737013759967uL}, new ulong[] {19985036884443215uL}, 4292870111u, 5111881u, 0, 0),},
            new HeaderKeyStringData[] {
                new HeaderKeyStringData((int)HeaderIndex.Trailer, new ulong[] {18437736737013759967uL}, new ulong[] {20547952478126164uL}, 4292870111u, 4522060u, (ushort)65503u, (ushort)82u),
                new HeaderKeyStringData((int)HeaderIndex.Upgrade, new ulong[] {18437736737013759967uL}, new ulong[] {23081253038194773uL}, 4292870111u, 4456513u, (ushort)65503u, (ushort)69u),
                new HeaderKeyStringData((int)HeaderIndex.Warning, new ulong[] {18437736737013759967uL}, new ulong[] {21955400375009367uL}, 4292870111u, 5111881u, (ushort)65503u, (ushort)71u),
                new HeaderKeyStringData((int)HeaderIndex.Expires, new ulong[] {18437736737013759967uL}, new ulong[] {20548016903028805uL}, 4292870111u, 4522066u, (ushort)65503u, (ushort)83u),
                new HeaderKeyStringData((int)HeaderIndex.Referer, new ulong[] {18437736737013759967uL}, new ulong[] {19422074045268050uL}, 4292870111u, 4522066u, (ushort)65503u, (ushort)82u),},
            new HeaderKeyStringData[] {
                new HeaderKeyStringData((int)HeaderIndex.IfMatch, new ulong[] {18437736874452713439uL, 18437736737013759967uL}, new ulong[] {21673766484836425uL, 20266486091481153uL}, 0, 0, 0, 0),
                new HeaderKeyStringData((int)HeaderIndex.IfRange, new ulong[] {18437736874452713439uL, 18437736737013759967uL}, new ulong[] {23081141368389705uL, 19422078340825153uL}, 0, 0, 0, 0),},
            new HeaderKeyStringData[] {
                new HeaderKeyStringData((int)HeaderIndex.Translate, new ulong[] {18437736737013759967uL, 18437736737013759967uL}, new ulong[] {21955327361679444uL, 23644177221550163uL}, 0, 0, (ushort)65503u, (ushort)69u),},
            new HeaderKeyStringData[] {
                new HeaderKeyStringData((int)HeaderIndex.UserAgent, new ulong[] {18437736737013759967uL, 18437736737013759999uL}, new ulong[] {23081244448456789uL, 19422078339973165uL}, 4292870111u, 5505102u, 0, 0),
                new HeaderKeyStringData((int)HeaderIndex.Connection, new ulong[] {18437736737013759967uL, 18437736737013759967uL}, new ulong[] {21955383196057667uL, 20548034081521733uL}, 4292870111u, 5111887u, 0, 0),
                new HeaderKeyStringData((int)HeaderIndex.KeepAlive, new ulong[] {18437736737013759967uL, 18437736737013759999uL}, new ulong[] {22518294494117963uL, 20547999721652269uL}, 4292870111u, 4522070u, 0, 0),},
            new HeaderKeyStringData[] {
                new HeaderKeyStringData((int)HeaderIndex.ContentMD5, new ulong[] {18437736737013759967uL, 18446743936268500959uL}, new ulong[] {23644233056321603uL, 12666734734344261uL}, 4292870111u, 4456525u, (ushort)65535u, (ushort)53u),},
            new HeaderKeyStringData[] {
                new HeaderKeyStringData((int)HeaderIndex.ContentType, new ulong[] {18437736737013759967uL, 18446743936268500959uL, 18437736737013759967uL}, new ulong[] {23644233056321603uL, 12666734734344261uL, 19422116996251732uL}, 0, 0, 0, 0),
                new HeaderKeyStringData((int)HeaderIndex.MaxForwards, new ulong[] {18446743936268500959uL, 18437736737013759967uL, 18437736737013759967uL}, new ulong[] {12666751913361485uL, 24488675166322758uL, 23362715130134593uL}, 0, 0, 0, 0),},
            new HeaderKeyStringData[] {
                new HeaderKeyStringData((int)HeaderIndex.CacheControl, new ulong[] {18437736737013759967uL, 18437736737015857119uL, 18437736737013759967uL}, new ulong[] {20266486090235971uL, 22236810925899845uL, 22236875352965198uL}, 0, 0, (ushort)65503u, (ushort)76u),
                new HeaderKeyStringData((int)HeaderIndex.ContentRange, new ulong[] {18437736737013759967uL, 18446743936268500959uL, 18437736737013759967uL}, new ulong[] {23644233056321603uL, 12666734734344261uL, 19985058358165586uL}, 0, 0, (ushort)65503u, (ushort)69u),
                new HeaderKeyStringData((int)HeaderIndex.LastModified, new ulong[] {18437736737013759967uL, 18437736737013759999uL, 18437736737013759967uL}, new ulong[] {23644254530240588uL, 19140637723787309uL, 19422086930235465uL}, 0, 0, (ushort)65503u, (ushort)68u),
                new HeaderKeyStringData((int)HeaderIndex.Authorization, new ulong[] {18437736737013759967uL, 18437736737013759967uL, 18437736737013759967uL}, new ulong[] {20266559105990721uL, 25333061441945679uL, 22236836698259521uL}, 0, 0, (ushort)65503u, (ushort)78u),
                new HeaderKeyStringData((int)HeaderIndex.IfNoneMatch, new ulong[] {18437736874452713439uL, 18446743936268500959uL, 18437736737013759967uL}, new ulong[] {21955241461547081uL, 12666670309834831uL, 18859184221126733uL}, 0, 0, (ushort)65503u, (ushort)72u),},
            new HeaderKeyStringData[] {
                new HeaderKeyStringData((int)HeaderIndex.ContentLength, new ulong[] {18437736737013759967uL, 18446743936268500959uL, 18437736737013759967uL}, new ulong[] {23644233056321603uL, 12666734734344261uL, 19985058358427724uL}, 4292870111u, 4718676u, 0, 0),
                new HeaderKeyStringData((int)HeaderIndex.AcceptCharset, new ulong[] {18437736737013759967uL, 18437736874452713439uL, 18437736737013759967uL}, new ulong[] {19422061160235073uL, 18859016718647376uL, 23362775258562632uL}, 4292870111u, 5505093u, 0, 0),},
            new HeaderKeyStringData[] {
                new HeaderKeyStringData((int)HeaderIndex.AcceptEncoding, new ulong[] {18437736737013759967uL, 18437736874452713439uL, 18437736737013759967uL}, new ulong[] {19422061160235073uL, 19421966672068688uL, 19140637723131982uL}, 4292870111u, 5111881u, (ushort)65503u, (ushort)71u),
                new HeaderKeyStringData((int)HeaderIndex.AcceptLanguage, new ulong[] {18437736737013759967uL, 18437736874452713439uL, 18437736737013759967uL}, new ulong[] {19422061160235073uL, 21392291509043280uL, 23925677968195649uL}, 4292870111u, 4653121u, (ushort)65503u, (ushort)69u),},
            new HeaderKeyStringData[] {
                new HeaderKeyStringData((int)HeaderIndex.ContentEncoding, new ulong[] {18437736737013759967uL, 18446743936268500959uL, 18437736737013759967uL, 18437736737013759967uL}, new ulong[] {23644233056321603uL, 12666734734344261uL, 22236810928062533uL, 19985058358689860uL}, 0, 0, 0, 0),
                new HeaderKeyStringData((int)HeaderIndex.ContentLanguage, new ulong[] {18437736737013759967uL, 18446743936268500959uL, 18437736737013759967uL, 18437736737013759967uL}, new ulong[] {23644233056321603uL, 12666734734344261uL, 19985058358165580uL, 19422078339973205uL}, 0, 0, 0, 0),
                new HeaderKeyStringData((int)HeaderIndex.ContentLocation, new ulong[] {18437736737013759967uL, 18446743936268500959uL, 18437736737013759967uL, 18437736737013759967uL}, new ulong[] {23644233056321603uL, 12666734734344261uL, 18296161254178892uL, 21955387490631764uL}, 0, 0, 0, 0),},
            new HeaderKeyStringData[] {
                new HeaderKeyStringData((int)HeaderIndex.TransferEncoding, new ulong[] {18437736737013759967uL, 18437736737013759967uL, 18437736737013759999uL, 18437736737013759967uL}, new ulong[] {21955327361679444uL, 23081244447604819uL, 18859158451585069uL, 21955361720500303uL}, 0, 0, (ushort)65503u, (ushort)71u),
                new HeaderKeyStringData((int)HeaderIndex.IfModifiedSince, new ulong[] {18437736874452713439uL, 18437736737013759967uL, 18446743936268500959uL, 18437736737013759967uL}, new ulong[] {21673766484836425uL, 19703561906815055uL, 12666666014277705uL, 18859158451847251uL}, 0, 0, (ushort)65503u, (ushort)69u),},
            NoHeaders,
            new HeaderKeyStringData[] {
                new HeaderKeyStringData((int)HeaderIndex.IfUnmodifiedSince, new ulong[] {18437736874452713439uL, 18437736737013759967uL, 18437736737013759967uL, 18437736737015857119uL}, new ulong[] {23925566298521673uL, 19140637723787342uL, 19422086930235465uL, 20548029785112644uL}, 4292870111u, 4390990u, (ushort)65503u, (ushort)69u),
                new HeaderKeyStringData((int)HeaderIndex.ProxyAuthorization, new ulong[] {18437736737013759967uL, 18437736737015857119uL, 18437736737013759967uL, 18437736737013759967uL}, new ulong[] {24770137258328144uL, 23925652196229209uL, 23081287397408852uL, 23644177222467657uL}, 4292870111u, 5177417u, (ushort)65503u, (ushort)78u),},
            NoHeaders,
            NoHeaders,
            NoHeaders,
            NoHeaders,
            NoHeaders,
            NoHeaders,
            NoHeaders,
            NoHeaders,
            NoHeaders,
            new HeaderKeyStringData[] {
                new HeaderKeyStringData((int)HeaderIndex.AccessControlRequestMethod, new ulong[] {18437736737013759967uL, 18437736874452713439uL, 18437736737013759967uL, 18437736874452713439uL, 18437736737013759967uL, 18437736874452713439uL, 18437736737013759967uL}, new ulong[] {19422061160235073uL, 18859016718581843uL, 23081308872638543uL, 23081141368782927uL, 19422138470563909uL, 21673766485753939uL, 22236832403292229uL}, 0, 0, (ushort)65503u, (ushort)68u),},
            new HeaderKeyStringData[] {
                new HeaderKeyStringData((int)HeaderIndex.AccessControlRequestHeaders, new ulong[] {18437736737013759967uL, 18437736874452713439uL, 18437736737013759967uL, 18437736874452713439uL, 18437736737013759967uL, 18437736874452713439uL, 18437736737013759967uL}, new ulong[] {19422061160235073uL, 18859016718581843uL, 23081308872638543uL, 23081141368782927uL, 19422138470563909uL, 20266391602200659uL, 19422065455071301uL}, 4292870111u, 5439570u, 0, 0),},
        };

        private static readonly HeaderKeyByteData[] NoRequestHeaders = new HeaderKeyByteData[0];
        private static readonly HeaderKeyByteData[][] _keyByteDataByLength = new HeaderKeyByteData[][]
        {
            NoRequestHeaders,
            NoRequestHeaders,
            new HeaderKeyByteData[] {
                new HeaderKeyByteData((int)HeaderIndex.TE, ShortHeader,ShortHeader,0, 0, (ushort)57311u, (ushort)17748u, 0, 0),},
            new HeaderKeyByteData[] {
                new HeaderKeyByteData((int)HeaderIndex.Via, ShortHeader,ShortHeader,0, 0, (ushort)57311u, (ushort)18774u, (byte)223u, (byte)65u),},
            new HeaderKeyByteData[] {
                new HeaderKeyByteData((int)HeaderIndex.Host, ShortHeader,ShortHeader,3755991007u, 1414745928u, 0, 0, 0, 0),
                new HeaderKeyByteData((int)HeaderIndex.Date, ShortHeader,ShortHeader,3755991007u, 1163149636u, 0, 0, 0, 0),
                new HeaderKeyByteData((int)HeaderIndex.From, ShortHeader,ShortHeader,3755991007u, 1297044038u, 0, 0, 0, 0),},
            new HeaderKeyByteData[] {
                new HeaderKeyByteData((int)HeaderIndex.Allow, ShortHeader,ShortHeader,3755991007u, 1330400321u, 0, 0, (byte)223u, (byte)87u),
                new HeaderKeyByteData((int)HeaderIndex.Range, ShortHeader,ShortHeader,3755991007u, 1196310866u, 0, 0, (byte)223u, (byte)69u),},
            new HeaderKeyByteData[] {
                new HeaderKeyByteData((int)HeaderIndex.Accept, ShortHeader,ShortHeader,3755991007u, 1162036033u, (ushort)57311u, (ushort)21584u, 0, 0),
                new HeaderKeyByteData((int)HeaderIndex.Pragma, ShortHeader,ShortHeader,3755991007u, 1195463248u, (ushort)57311u, (ushort)16717u, 0, 0),
                new HeaderKeyByteData((int)HeaderIndex.Cookie, ShortHeader,ShortHeader,3755991007u, 1263488835u, (ushort)57311u, (ushort)17737u, 0, 0),
                new HeaderKeyByteData((int)HeaderIndex.Expect, ShortHeader,ShortHeader,3755991007u, 1162893381u, (ushort)57311u, (ushort)21571u, 0, 0),
                new HeaderKeyByteData((int)HeaderIndex.Origin, ShortHeader,ShortHeader,3755991007u, 1195987535u, (ushort)57311u, (ushort)20041u, 0, 0),},
            new HeaderKeyByteData[] {
                new HeaderKeyByteData((int)HeaderIndex.Trailer, ShortHeader,ShortHeader,3755991007u, 1229017684u, (ushort)57311u, (ushort)17740u, (byte)223u, (byte)82u),
                new HeaderKeyByteData((int)HeaderIndex.Upgrade, ShortHeader,ShortHeader,3755991007u, 1380405333u, (ushort)57311u, (ushort)17473u, (byte)223u, (byte)69u),
                new HeaderKeyByteData((int)HeaderIndex.Warning, ShortHeader,ShortHeader,3755991007u, 1314013527u, (ushort)57311u, (ushort)20041u, (byte)223u, (byte)71u),
                new HeaderKeyByteData((int)HeaderIndex.Expires, ShortHeader,ShortHeader,3755991007u, 1230002245u, (ushort)57311u, (ushort)17746u, (byte)223u, (byte)83u),
                new HeaderKeyByteData((int)HeaderIndex.Referer, ShortHeader,ShortHeader,3755991007u, 1162233170u, (ushort)57311u, (ushort)17746u, (byte)223u, (byte)82u),},
            new HeaderKeyByteData[] {
                new HeaderKeyByteData((int)HeaderIndex.IfMatch, new ulong[] {16131858542893195231uL},new ulong[] {5207098233614845513uL},0, 0, 0, 0, 0, 0),
                new HeaderKeyByteData((int)HeaderIndex.IfRange, new ulong[] {16131858542893195231uL},new ulong[] {4992044754422023753uL},0, 0, 0, 0, 0, 0),},
            new HeaderKeyByteData[] {
                new HeaderKeyByteData((int)HeaderIndex.Translate, new ulong[] {16131858542891098079uL},new ulong[] {6071217693351039572uL},0, 0, 0, 0, (byte)223u, (byte)69u),},
            new HeaderKeyByteData[] {
                new HeaderKeyByteData((int)HeaderIndex.UserAgent, new ulong[] {16131858680330051551uL},new ulong[] {4992030374873092949uL},0, 0, (ushort)57311u, (ushort)21582u, 0, 0),
                new HeaderKeyByteData((int)HeaderIndex.Connection, new ulong[] {16131858542891098079uL},new ulong[] {5283922227757993795uL},0, 0, (ushort)57311u, (ushort)20047u, 0, 0),
                new HeaderKeyByteData((int)HeaderIndex.KeepAlive, new ulong[] {16131858680330051551uL},new ulong[] {5281668125874799947uL},0, 0, (ushort)57311u, (ushort)17750u, 0, 0),},
            new HeaderKeyByteData[] {
                new HeaderKeyByteData((int)HeaderIndex.ContentMD5, new ulong[] {18437701552104792031uL},new ulong[] {3266321689424580419uL},0, 0, (ushort)57311u, (ushort)17485u, (byte)255u, (byte)53u),},
            new HeaderKeyByteData[] {
                new HeaderKeyByteData((int)HeaderIndex.ContentType, new ulong[] {18437701552104792031uL},new ulong[] {3266321689424580419uL},3755991007u, 1162893652u, 0, 0, 0, 0),
                new HeaderKeyByteData((int)HeaderIndex.MaxForwards, new ulong[] {16131858543427968991uL},new ulong[] {6292178792217067853uL},3755991007u, 1396986433u, 0, 0, 0, 0),},
            new HeaderKeyByteData[] {
                new HeaderKeyByteData((int)HeaderIndex.CacheControl, new ulong[] {16131893727263186911uL},new ulong[] {5711458528024281411uL},3755991007u, 1330795598u, 0, 0, (byte)223u, (byte)76u),
                new HeaderKeyByteData((int)HeaderIndex.ContentRange, new ulong[] {18437701552104792031uL},new ulong[] {3266321689424580419uL},3755991007u, 1196310866u, 0, 0, (byte)223u, (byte)69u),
                new HeaderKeyByteData((int)HeaderIndex.LastModified, new ulong[] {16131858680330051551uL},new ulong[] {4922237774822850892uL},3755991007u, 1162430025u, 0, 0, (byte)223u, (byte)68u),
                new HeaderKeyByteData((int)HeaderIndex.Authorization, new ulong[] {16131858542891098079uL},new ulong[] {6505821637182772545uL},3755991007u, 1330205761u, 0, 0, (byte)223u, (byte)78u),
                new HeaderKeyByteData((int)HeaderIndex.IfNoneMatch, new ulong[] {18437701552106889183uL},new ulong[] {3262099607620765257uL},3755991007u, 1129595213u, 0, 0, (byte)223u, (byte)72u),},
            new HeaderKeyByteData[] {
                new HeaderKeyByteData((int)HeaderIndex.ContentLength, new ulong[] {18437701552104792031uL},new ulong[] {3266321689424580419uL},3755991007u, 1196311884u, (ushort)57311u, (ushort)18516u, 0, 0),
                new HeaderKeyByteData((int)HeaderIndex.AcceptCharset, new ulong[] {16140865742145839071uL},new ulong[] {4840617878229304129uL},3755991007u, 1397899592u, (ushort)57311u, (ushort)21573u, 0, 0),},
            new HeaderKeyByteData[] {
                new HeaderKeyByteData((int)HeaderIndex.AcceptEncoding, new ulong[] {16140865742145839071uL},new ulong[] {4984733066305160001uL},3755991007u, 1146045262u, (ushort)57311u, (ushort)20041u, (byte)223u, (byte)71u),
                new HeaderKeyByteData((int)HeaderIndex.AcceptLanguage, new ulong[] {16140865742145839071uL},new ulong[] {5489136224570655553uL},3755991007u, 1430736449u, (ushort)57311u, (ushort)18241u, (byte)223u, (byte)69u),},
            new HeaderKeyByteData[] {
                new HeaderKeyByteData((int)HeaderIndex.ContentEncoding, new ulong[] {18437701552104792031uL, 16131858542891098079uL},new ulong[] {3266321689424580419uL, 5138124782612729413uL},0, 0, 0, 0, 0, 0),
                new HeaderKeyByteData((int)HeaderIndex.ContentLanguage, new ulong[] {18437701552104792031uL, 16131858542891098079uL},new ulong[] {3266321689424580419uL, 4992030546487820620uL},0, 0, 0, 0, 0, 0),
                new HeaderKeyByteData((int)HeaderIndex.ContentLocation, new ulong[] {18437701552104792031uL, 16131858542891098079uL},new ulong[] {3266321689424580419uL, 5642809484339531596uL},0, 0, 0, 0, 0, 0),},
            new HeaderKeyByteData[] {
                new HeaderKeyByteData((int)HeaderIndex.TransferEncoding, new ulong[] {16131858542891098079uL, 16131858542891098111uL},new ulong[] {5928221808112259668uL, 5641115115480565037uL},0, 0, 0, 0, (byte)223u, (byte)71u),
                new HeaderKeyByteData((int)HeaderIndex.IfModifiedSince, new ulong[] {16131858542893195231uL, 16131858543427968991uL},new ulong[] {5064654363342751305uL, 4849894470315165001uL},0, 0, 0, 0, (byte)223u, (byte)69u),},
            NoRequestHeaders,
            new HeaderKeyByteData[] {
                new HeaderKeyByteData((int)HeaderIndex.IfUnmodifiedSince, new ulong[] {16131858542893195231uL, 16131893727263186911uL},new ulong[] {4922237916571059785uL, 5283616559079179849uL},0, 0, (ushort)57311u, (ushort)17230u, (byte)223u, (byte)69u),
                new HeaderKeyByteData((int)HeaderIndex.ProxyAuthorization, new ulong[] {16131893727263186911uL, 16131858542891098079uL},new ulong[] {6143241228466999888uL, 6071233043632179284uL},0, 0, (ushort)57311u, (ushort)20297u, (byte)223u, (byte)78u),},
            NoRequestHeaders,
            NoRequestHeaders,
            NoRequestHeaders,
            NoRequestHeaders,
            NoRequestHeaders,
            NoRequestHeaders,
            NoRequestHeaders,
            NoRequestHeaders,
            NoRequestHeaders,
            new HeaderKeyByteData[] {
                new HeaderKeyByteData((int)HeaderIndex.AccessControlRequestMethod, new ulong[] {16140865742145839071uL, 16140865742145839071uL, 16140865742145839071uL},new ulong[] {4840616791602578241uL, 5921472988629454415uL, 5561193831494668613uL},3755991007u, 1330140229u, 0, 0, (byte)223u, (byte)68u),},
            new HeaderKeyByteData[] {
                new HeaderKeyByteData((int)HeaderIndex.AccessControlRequestHeaders, new ulong[] {16140865742145839071uL, 16140865742145839071uL, 16140865742145839071uL},new ulong[] {4840616791602578241uL, 5921472988629454415uL, 5200905861305028933uL},3755991007u, 1162101061u, (ushort)57311u, (ushort)21330u, 0, 0),},
        };

        private static readonly KeyValuePair<string, int>[] HeaderNames = new []
        {
            new KeyValuePair<string, int>("Accept", (int)HeaderIndex.Accept),
            new KeyValuePair<string, int>("Host", (int)HeaderIndex.Host),
            new KeyValuePair<string, int>("User-Agent", (int)HeaderIndex.UserAgent),
            new KeyValuePair<string, int>("Cache-Control", (int)HeaderIndex.CacheControl),
            new KeyValuePair<string, int>("Connection", (int)HeaderIndex.Connection),
            new KeyValuePair<string, int>("Date", (int)HeaderIndex.Date),
            new KeyValuePair<string, int>("Keep-Alive", (int)HeaderIndex.KeepAlive),
            new KeyValuePair<string, int>("Pragma", (int)HeaderIndex.Pragma),
            new KeyValuePair<string, int>("Trailer", (int)HeaderIndex.Trailer),
            new KeyValuePair<string, int>("Transfer-Encoding", (int)HeaderIndex.TransferEncoding),
            new KeyValuePair<string, int>("Upgrade", (int)HeaderIndex.Upgrade),
            new KeyValuePair<string, int>("Via", (int)HeaderIndex.Via),
            new KeyValuePair<string, int>("Warning", (int)HeaderIndex.Warning),
            new KeyValuePair<string, int>("Allow", (int)HeaderIndex.Allow),
            new KeyValuePair<string, int>("Content-Length", (int)HeaderIndex.ContentLength),
            new KeyValuePair<string, int>("Content-Type", (int)HeaderIndex.ContentType),
            new KeyValuePair<string, int>("Content-Encoding", (int)HeaderIndex.ContentEncoding),
            new KeyValuePair<string, int>("Content-Language", (int)HeaderIndex.ContentLanguage),
            new KeyValuePair<string, int>("Content-Location", (int)HeaderIndex.ContentLocation),
            new KeyValuePair<string, int>("Content-MD5", (int)HeaderIndex.ContentMD5),
            new KeyValuePair<string, int>("Content-Range", (int)HeaderIndex.ContentRange),
            new KeyValuePair<string, int>("Expires", (int)HeaderIndex.Expires),
            new KeyValuePair<string, int>("Last-Modified", (int)HeaderIndex.LastModified),
            new KeyValuePair<string, int>("Accept-Charset", (int)HeaderIndex.AcceptCharset),
            new KeyValuePair<string, int>("Accept-Encoding", (int)HeaderIndex.AcceptEncoding),
            new KeyValuePair<string, int>("Accept-Language", (int)HeaderIndex.AcceptLanguage),
            new KeyValuePair<string, int>("Authorization", (int)HeaderIndex.Authorization),
            new KeyValuePair<string, int>("Cookie", (int)HeaderIndex.Cookie),
            new KeyValuePair<string, int>("Expect", (int)HeaderIndex.Expect),
            new KeyValuePair<string, int>("From", (int)HeaderIndex.From),
            new KeyValuePair<string, int>("If-Match", (int)HeaderIndex.IfMatch),
            new KeyValuePair<string, int>("If-Modified-Since", (int)HeaderIndex.IfModifiedSince),
            new KeyValuePair<string, int>("If-None-Match", (int)HeaderIndex.IfNoneMatch),
            new KeyValuePair<string, int>("If-Range", (int)HeaderIndex.IfRange),
            new KeyValuePair<string, int>("If-Unmodified-Since", (int)HeaderIndex.IfUnmodifiedSince),
            new KeyValuePair<string, int>("Max-Forwards", (int)HeaderIndex.MaxForwards),
            new KeyValuePair<string, int>("Proxy-Authorization", (int)HeaderIndex.ProxyAuthorization),
            new KeyValuePair<string, int>("Referer", (int)HeaderIndex.Referer),
            new KeyValuePair<string, int>("Range", (int)HeaderIndex.Range),
            new KeyValuePair<string, int>("TE", (int)HeaderIndex.TE),
            new KeyValuePair<string, int>("Translate", (int)HeaderIndex.Translate),
            new KeyValuePair<string, int>("Origin", (int)HeaderIndex.Origin),
            new KeyValuePair<string, int>("Access-Control-Request-Method", (int)HeaderIndex.AccessControlRequestMethod),
            new KeyValuePair<string, int>("Access-Control-Request-Headers", (int)HeaderIndex.AccessControlRequestHeaders),
        };

        public FrameRequestHeaders() : base(HeaderNames, _keyStringDataByLength, new StringValues[44])
        {
        }

        public StringValues HeaderAccept
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.Accept)) != 0))
                {
                    return _headerData[(int)HeaderIndex.Accept];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.Accept);
                _headerData[(int)HeaderIndex.Accept] = value; 
            }
        }

        public StringValues HeaderHost
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.Host)) != 0))
                {
                    return _headerData[(int)HeaderIndex.Host];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.Host);
                _headerData[(int)HeaderIndex.Host] = value; 
            }
        }

        public StringValues HeaderUserAgent
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.UserAgent)) != 0))
                {
                    return _headerData[(int)HeaderIndex.UserAgent];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.UserAgent);
                _headerData[(int)HeaderIndex.UserAgent] = value; 
            }
        }

        public StringValues HeaderCacheControl
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.CacheControl)) != 0))
                {
                    return _headerData[(int)HeaderIndex.CacheControl];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.CacheControl);
                _headerData[(int)HeaderIndex.CacheControl] = value; 
            }
        }

        public StringValues HeaderConnection
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.Connection)) != 0))
                {
                    return _headerData[(int)HeaderIndex.Connection];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.Connection);
                _headerData[(int)HeaderIndex.Connection] = value; 
            }
        }

        public StringValues HeaderDate
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.Date)) != 0))
                {
                    return _headerData[(int)HeaderIndex.Date];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.Date);
                _headerData[(int)HeaderIndex.Date] = value; 
            }
        }

        public StringValues HeaderKeepAlive
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.KeepAlive)) != 0))
                {
                    return _headerData[(int)HeaderIndex.KeepAlive];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.KeepAlive);
                _headerData[(int)HeaderIndex.KeepAlive] = value; 
            }
        }

        public StringValues HeaderPragma
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.Pragma)) != 0))
                {
                    return _headerData[(int)HeaderIndex.Pragma];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.Pragma);
                _headerData[(int)HeaderIndex.Pragma] = value; 
            }
        }

        public StringValues HeaderTrailer
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.Trailer)) != 0))
                {
                    return _headerData[(int)HeaderIndex.Trailer];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.Trailer);
                _headerData[(int)HeaderIndex.Trailer] = value; 
            }
        }

        public StringValues HeaderTransferEncoding
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.TransferEncoding)) != 0))
                {
                    return _headerData[(int)HeaderIndex.TransferEncoding];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.TransferEncoding);
                _headerData[(int)HeaderIndex.TransferEncoding] = value; 
            }
        }

        public StringValues HeaderUpgrade
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.Upgrade)) != 0))
                {
                    return _headerData[(int)HeaderIndex.Upgrade];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.Upgrade);
                _headerData[(int)HeaderIndex.Upgrade] = value; 
            }
        }

        public StringValues HeaderVia
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.Via)) != 0))
                {
                    return _headerData[(int)HeaderIndex.Via];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.Via);
                _headerData[(int)HeaderIndex.Via] = value; 
            }
        }

        public StringValues HeaderWarning
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.Warning)) != 0))
                {
                    return _headerData[(int)HeaderIndex.Warning];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.Warning);
                _headerData[(int)HeaderIndex.Warning] = value; 
            }
        }

        public StringValues HeaderAllow
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.Allow)) != 0))
                {
                    return _headerData[(int)HeaderIndex.Allow];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.Allow);
                _headerData[(int)HeaderIndex.Allow] = value; 
            }
        }

        public StringValues HeaderContentLength
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.ContentLength)) != 0))
                {
                    return _headerData[(int)HeaderIndex.ContentLength];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.ContentLength);
                _headerData[(int)HeaderIndex.ContentLength] = value; 
            }
        }

        public StringValues HeaderContentType
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.ContentType)) != 0))
                {
                    return _headerData[(int)HeaderIndex.ContentType];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.ContentType);
                _headerData[(int)HeaderIndex.ContentType] = value; 
            }
        }

        public StringValues HeaderContentEncoding
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.ContentEncoding)) != 0))
                {
                    return _headerData[(int)HeaderIndex.ContentEncoding];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.ContentEncoding);
                _headerData[(int)HeaderIndex.ContentEncoding] = value; 
            }
        }

        public StringValues HeaderContentLanguage
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.ContentLanguage)) != 0))
                {
                    return _headerData[(int)HeaderIndex.ContentLanguage];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.ContentLanguage);
                _headerData[(int)HeaderIndex.ContentLanguage] = value; 
            }
        }

        public StringValues HeaderContentLocation
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.ContentLocation)) != 0))
                {
                    return _headerData[(int)HeaderIndex.ContentLocation];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.ContentLocation);
                _headerData[(int)HeaderIndex.ContentLocation] = value; 
            }
        }

        public StringValues HeaderContentMD5
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.ContentMD5)) != 0))
                {
                    return _headerData[(int)HeaderIndex.ContentMD5];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.ContentMD5);
                _headerData[(int)HeaderIndex.ContentMD5] = value; 
            }
        }

        public StringValues HeaderContentRange
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.ContentRange)) != 0))
                {
                    return _headerData[(int)HeaderIndex.ContentRange];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.ContentRange);
                _headerData[(int)HeaderIndex.ContentRange] = value; 
            }
        }

        public StringValues HeaderExpires
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.Expires)) != 0))
                {
                    return _headerData[(int)HeaderIndex.Expires];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.Expires);
                _headerData[(int)HeaderIndex.Expires] = value; 
            }
        }

        public StringValues HeaderLastModified
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.LastModified)) != 0))
                {
                    return _headerData[(int)HeaderIndex.LastModified];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.LastModified);
                _headerData[(int)HeaderIndex.LastModified] = value; 
            }
        }

        public StringValues HeaderAcceptCharset
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.AcceptCharset)) != 0))
                {
                    return _headerData[(int)HeaderIndex.AcceptCharset];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.AcceptCharset);
                _headerData[(int)HeaderIndex.AcceptCharset] = value; 
            }
        }

        public StringValues HeaderAcceptEncoding
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.AcceptEncoding)) != 0))
                {
                    return _headerData[(int)HeaderIndex.AcceptEncoding];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.AcceptEncoding);
                _headerData[(int)HeaderIndex.AcceptEncoding] = value; 
            }
        }

        public StringValues HeaderAcceptLanguage
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.AcceptLanguage)) != 0))
                {
                    return _headerData[(int)HeaderIndex.AcceptLanguage];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.AcceptLanguage);
                _headerData[(int)HeaderIndex.AcceptLanguage] = value; 
            }
        }

        public StringValues HeaderAuthorization
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.Authorization)) != 0))
                {
                    return _headerData[(int)HeaderIndex.Authorization];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.Authorization);
                _headerData[(int)HeaderIndex.Authorization] = value; 
            }
        }

        public StringValues HeaderCookie
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.Cookie)) != 0))
                {
                    return _headerData[(int)HeaderIndex.Cookie];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.Cookie);
                _headerData[(int)HeaderIndex.Cookie] = value; 
            }
        }

        public StringValues HeaderExpect
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.Expect)) != 0))
                {
                    return _headerData[(int)HeaderIndex.Expect];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.Expect);
                _headerData[(int)HeaderIndex.Expect] = value; 
            }
        }

        public StringValues HeaderFrom
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.From)) != 0))
                {
                    return _headerData[(int)HeaderIndex.From];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.From);
                _headerData[(int)HeaderIndex.From] = value; 
            }
        }

        public StringValues HeaderIfMatch
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.IfMatch)) != 0))
                {
                    return _headerData[(int)HeaderIndex.IfMatch];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.IfMatch);
                _headerData[(int)HeaderIndex.IfMatch] = value; 
            }
        }

        public StringValues HeaderIfModifiedSince
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.IfModifiedSince)) != 0))
                {
                    return _headerData[(int)HeaderIndex.IfModifiedSince];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.IfModifiedSince);
                _headerData[(int)HeaderIndex.IfModifiedSince] = value; 
            }
        }

        public StringValues HeaderIfNoneMatch
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.IfNoneMatch)) != 0))
                {
                    return _headerData[(int)HeaderIndex.IfNoneMatch];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.IfNoneMatch);
                _headerData[(int)HeaderIndex.IfNoneMatch] = value; 
            }
        }

        public StringValues HeaderIfRange
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.IfRange)) != 0))
                {
                    return _headerData[(int)HeaderIndex.IfRange];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.IfRange);
                _headerData[(int)HeaderIndex.IfRange] = value; 
            }
        }

        public StringValues HeaderIfUnmodifiedSince
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.IfUnmodifiedSince)) != 0))
                {
                    return _headerData[(int)HeaderIndex.IfUnmodifiedSince];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.IfUnmodifiedSince);
                _headerData[(int)HeaderIndex.IfUnmodifiedSince] = value; 
            }
        }

        public StringValues HeaderMaxForwards
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.MaxForwards)) != 0))
                {
                    return _headerData[(int)HeaderIndex.MaxForwards];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.MaxForwards);
                _headerData[(int)HeaderIndex.MaxForwards] = value; 
            }
        }

        public StringValues HeaderProxyAuthorization
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.ProxyAuthorization)) != 0))
                {
                    return _headerData[(int)HeaderIndex.ProxyAuthorization];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.ProxyAuthorization);
                _headerData[(int)HeaderIndex.ProxyAuthorization] = value; 
            }
        }

        public StringValues HeaderReferer
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.Referer)) != 0))
                {
                    return _headerData[(int)HeaderIndex.Referer];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.Referer);
                _headerData[(int)HeaderIndex.Referer] = value; 
            }
        }

        public StringValues HeaderRange
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.Range)) != 0))
                {
                    return _headerData[(int)HeaderIndex.Range];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.Range);
                _headerData[(int)HeaderIndex.Range] = value; 
            }
        }

        public StringValues HeaderTE
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.TE)) != 0))
                {
                    return _headerData[(int)HeaderIndex.TE];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.TE);
                _headerData[(int)HeaderIndex.TE] = value; 
            }
        }

        public StringValues HeaderTranslate
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.Translate)) != 0))
                {
                    return _headerData[(int)HeaderIndex.Translate];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.Translate);
                _headerData[(int)HeaderIndex.Translate] = value; 
            }
        }

        public StringValues HeaderOrigin
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.Origin)) != 0))
                {
                    return _headerData[(int)HeaderIndex.Origin];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.Origin);
                _headerData[(int)HeaderIndex.Origin] = value; 
            }
        }

        public StringValues HeaderAccessControlRequestMethod
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.AccessControlRequestMethod)) != 0))
                {
                    return _headerData[(int)HeaderIndex.AccessControlRequestMethod];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.AccessControlRequestMethod);
                _headerData[(int)HeaderIndex.AccessControlRequestMethod] = value; 
            }
        }

        public StringValues HeaderAccessControlRequestHeaders
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.AccessControlRequestHeaders)) != 0))
                {
                    return _headerData[(int)HeaderIndex.AccessControlRequestHeaders];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.AccessControlRequestHeaders);
                _headerData[(int)HeaderIndex.AccessControlRequestHeaders] = value; 
            }
        }

        private enum HeaderIndex
        {
            Accept = 0,
            Host = 1,
            UserAgent = 2,
            CacheControl = 3,
            Connection = 4,
            Date = 5,
            KeepAlive = 6,
            Pragma = 7,
            Trailer = 8,
            TransferEncoding = 9,
            Upgrade = 10,
            Via = 11,
            Warning = 12,
            Allow = 13,
            ContentLength = 14,
            ContentType = 15,
            ContentEncoding = 16,
            ContentLanguage = 17,
            ContentLocation = 18,
            ContentMD5 = 19,
            ContentRange = 20,
            Expires = 21,
            LastModified = 22,
            AcceptCharset = 23,
            AcceptEncoding = 24,
            AcceptLanguage = 25,
            Authorization = 26,
            Cookie = 27,
            Expect = 28,
            From = 29,
            IfMatch = 30,
            IfModifiedSince = 31,
            IfNoneMatch = 32,
            IfRange = 33,
            IfUnmodifiedSince = 34,
            MaxForwards = 35,
            ProxyAuthorization = 36,
            Referer = 37,
            Range = 38,
            TE = 39,
            Translate = 40,
            Origin = 41,
            AccessControlRequestMethod = 42,
            AccessControlRequestHeaders = 43,
        }
    }

    public partial class FrameResponseHeaders
    {
        private readonly static byte[][] _keyBytes = new byte[][]
        {
            new byte[]{13,10,67,111,110,110,101,99,116,105,111,110,58,32,},
            new byte[]{13,10,68,97,116,101,58,32,},
            new byte[]{13,10,67,111,110,116,101,110,116,45,76,101,110,103,116,104,58,32,},
            new byte[]{13,10,83,101,114,118,101,114,58,32,},
            new byte[]{13,10,67,111,110,116,101,110,116,45,84,121,112,101,58,32,},
            new byte[]{13,10,84,114,97,110,115,102,101,114,45,69,110,99,111,100,105,110,103,58,32,},
            new byte[]{13,10,67,97,99,104,101,45,67,111,110,116,114,111,108,58,32,},
            new byte[]{13,10,75,101,101,112,45,65,108,105,118,101,58,32,},
            new byte[]{13,10,80,114,97,103,109,97,58,32,},
            new byte[]{13,10,84,114,97,105,108,101,114,58,32,},
            new byte[]{13,10,85,112,103,114,97,100,101,58,32,},
            new byte[]{13,10,86,105,97,58,32,},
            new byte[]{13,10,87,97,114,110,105,110,103,58,32,},
            new byte[]{13,10,65,108,108,111,119,58,32,},
            new byte[]{13,10,67,111,110,116,101,110,116,45,69,110,99,111,100,105,110,103,58,32,},
            new byte[]{13,10,67,111,110,116,101,110,116,45,76,97,110,103,117,97,103,101,58,32,},
            new byte[]{13,10,67,111,110,116,101,110,116,45,76,111,99,97,116,105,111,110,58,32,},
            new byte[]{13,10,67,111,110,116,101,110,116,45,77,68,53,58,32,},
            new byte[]{13,10,67,111,110,116,101,110,116,45,82,97,110,103,101,58,32,},
            new byte[]{13,10,69,120,112,105,114,101,115,58,32,},
            new byte[]{13,10,76,97,115,116,45,77,111,100,105,102,105,101,100,58,32,},
            new byte[]{13,10,65,99,99,101,115,115,45,67,111,110,116,114,111,108,45,65,108,108,111,119,45,67,114,101,100,101,110,116,105,97,108,115,58,32,},
            new byte[]{13,10,65,99,99,101,115,115,45,67,111,110,116,114,111,108,45,65,108,108,111,119,45,72,101,97,100,101,114,115,58,32,},
            new byte[]{13,10,65,99,99,101,115,115,45,67,111,110,116,114,111,108,45,65,108,108,111,119,45,77,101,116,104,111,100,115,58,32,},
            new byte[]{13,10,65,99,99,101,115,115,45,67,111,110,116,114,111,108,45,65,108,108,111,119,45,79,114,105,103,105,110,58,32,},
            new byte[]{13,10,65,99,99,101,115,115,45,67,111,110,116,114,111,108,45,69,120,112,111,115,101,45,72,101,97,100,101,114,115,58,32,},
            new byte[]{13,10,65,99,99,101,115,115,45,67,111,110,116,114,111,108,45,77,97,120,45,65,103,101,58,32,},
            new byte[]{13,10,65,99,99,101,112,116,45,82,97,110,103,101,115,58,32,},
            new byte[]{13,10,65,103,101,58,32,},
            new byte[]{13,10,69,84,97,103,58,32,},
            new byte[]{13,10,76,111,99,97,116,105,111,110,58,32,},
            new byte[]{13,10,80,114,111,120,121,45,65,117,116,104,101,110,116,105,99,97,116,101,58,32,},
            new byte[]{13,10,82,101,116,114,121,45,65,102,116,101,114,58,32,},
            new byte[]{13,10,83,101,116,45,67,111,111,107,105,101,58,32,},
            new byte[]{13,10,86,97,114,121,58,32,},
            new byte[]{13,10,87,87,87,45,65,117,116,104,101,110,116,105,99,97,116,101,58,32,},
        };

        private static readonly HeaderKeyStringData[][] _keyStringDataByLength = new HeaderKeyStringData[][]
        {
            NoHeaders,
            NoHeaders,
            NoHeaders,
            new HeaderKeyStringData[] {
                new HeaderKeyStringData((int)HeaderIndex.Via, ShortHeader, ShortHeader, 4292870111u, 4784214u, (ushort)65503u, (ushort)65u),
                new HeaderKeyStringData((int)HeaderIndex.Age, ShortHeader, ShortHeader, 4292870111u, 4653121u, (ushort)65503u, (ushort)69u),},
            new HeaderKeyStringData[] {
                new HeaderKeyStringData((int)HeaderIndex.Date, new ulong[] {18437736737013759967uL}, new ulong[] {19422134174548036uL}, 0, 0, 0, 0),
                new HeaderKeyStringData((int)HeaderIndex.ETag, new ulong[] {18437736737013759967uL}, new ulong[] {19985002524835909uL}, 0, 0, 0, 0),
                new HeaderKeyStringData((int)HeaderIndex.Vary, new ulong[] {18437736737013759967uL}, new ulong[] {25051625118826582uL}, 0, 0, 0, 0),},
            new HeaderKeyStringData[] {
                new HeaderKeyStringData((int)HeaderIndex.Allow, new ulong[] {18437736737013759967uL}, new ulong[] {22236849582637121uL}, 0, 0, (ushort)65503u, (ushort)87u),},
            new HeaderKeyStringData[] {
                new HeaderKeyStringData((int)HeaderIndex.Server, new ulong[] {18437736737013759967uL}, new ulong[] {24207200188956755uL}, 4292870111u, 5374021u, 0, 0),
                new HeaderKeyStringData((int)HeaderIndex.Pragma, new ulong[] {18437736737013759967uL}, new ulong[] {19985002524704848uL}, 4292870111u, 4259917u, 0, 0),},
            new HeaderKeyStringData[] {
                new HeaderKeyStringData((int)HeaderIndex.Trailer, new ulong[] {18437736737013759967uL}, new ulong[] {20547952478126164uL}, 4292870111u, 4522060u, (ushort)65503u, (ushort)82u),
                new HeaderKeyStringData((int)HeaderIndex.Upgrade, new ulong[] {18437736737013759967uL}, new ulong[] {23081253038194773uL}, 4292870111u, 4456513u, (ushort)65503u, (ushort)69u),
                new HeaderKeyStringData((int)HeaderIndex.Warning, new ulong[] {18437736737013759967uL}, new ulong[] {21955400375009367uL}, 4292870111u, 5111881u, (ushort)65503u, (ushort)71u),
                new HeaderKeyStringData((int)HeaderIndex.Expires, new ulong[] {18437736737013759967uL}, new ulong[] {20548016903028805uL}, 4292870111u, 4522066u, (ushort)65503u, (ushort)83u),},
            new HeaderKeyStringData[] {
                new HeaderKeyStringData((int)HeaderIndex.Location, new ulong[] {18437736737013759967uL, 18437736737013759967uL}, new ulong[] {18296161254178892uL, 21955387490631764uL}, 0, 0, 0, 0),},
            NoHeaders,
            new HeaderKeyStringData[] {
                new HeaderKeyStringData((int)HeaderIndex.Connection, new ulong[] {18437736737013759967uL, 18437736737013759967uL}, new ulong[] {21955383196057667uL, 20548034081521733uL}, 4292870111u, 5111887u, 0, 0),
                new HeaderKeyStringData((int)HeaderIndex.KeepAlive, new ulong[] {18437736737013759967uL, 18437736737013759999uL}, new ulong[] {22518294494117963uL, 20547999721652269uL}, 4292870111u, 4522070u, 0, 0),
                new HeaderKeyStringData((int)HeaderIndex.SetCookie, new ulong[] {18446743936268500959uL, 18437736737013759967uL}, new ulong[] {12666734733754451uL, 21110962560892995uL}, 4292870111u, 4522057u, 0, 0),},
            new HeaderKeyStringData[] {
                new HeaderKeyStringData((int)HeaderIndex.ContentMD5, new ulong[] {18437736737013759967uL, 18446743936268500959uL}, new ulong[] {23644233056321603uL, 12666734734344261uL}, 4292870111u, 4456525u, (ushort)65535u, (ushort)53u),
                new HeaderKeyStringData((int)HeaderIndex.RetryAfter, new ulong[] {18437736737013759967uL, 18437736737015857119uL}, new ulong[] {23081308872048722uL, 19703527545569369uL}, 4292870111u, 4522068u, (ushort)65503u, (ushort)82u),},
            new HeaderKeyStringData[] {
                new HeaderKeyStringData((int)HeaderIndex.ContentType, new ulong[] {18437736737013759967uL, 18446743936268500959uL, 18437736737013759967uL}, new ulong[] {23644233056321603uL, 12666734734344261uL, 19422116996251732uL}, 0, 0, 0, 0),},
            new HeaderKeyStringData[] {
                new HeaderKeyStringData((int)HeaderIndex.CacheControl, new ulong[] {18437736737013759967uL, 18437736737015857119uL, 18437736737013759967uL}, new ulong[] {20266486090235971uL, 22236810925899845uL, 22236875352965198uL}, 0, 0, (ushort)65503u, (ushort)76u),
                new HeaderKeyStringData((int)HeaderIndex.ContentRange, new ulong[] {18437736737013759967uL, 18446743936268500959uL, 18437736737013759967uL}, new ulong[] {23644233056321603uL, 12666734734344261uL, 19985058358165586uL}, 0, 0, (ushort)65503u, (ushort)69u),
                new HeaderKeyStringData((int)HeaderIndex.LastModified, new ulong[] {18437736737013759967uL, 18437736737013759999uL, 18437736737013759967uL}, new ulong[] {23644254530240588uL, 19140637723787309uL, 19422086930235465uL}, 0, 0, (ushort)65503u, (ushort)68u),
                new HeaderKeyStringData((int)HeaderIndex.AcceptRanges, new ulong[] {18437736737013759967uL, 18437736874452713439uL, 18437736737013759967uL}, new ulong[] {19422061160235073uL, 23081141369307216uL, 19422078340825153uL}, 0, 0, (ushort)65503u, (ushort)83u),},
            new HeaderKeyStringData[] {
                new HeaderKeyStringData((int)HeaderIndex.ContentLength, new ulong[] {18437736737013759967uL, 18446743936268500959uL, 18437736737013759967uL}, new ulong[] {23644233056321603uL, 12666734734344261uL, 19985058358427724uL}, 4292870111u, 4718676u, 0, 0),},
            NoHeaders,
            new HeaderKeyStringData[] {
                new HeaderKeyStringData((int)HeaderIndex.ContentEncoding, new ulong[] {18437736737013759967uL, 18446743936268500959uL, 18437736737013759967uL, 18437736737013759967uL}, new ulong[] {23644233056321603uL, 12666734734344261uL, 22236810928062533uL, 19985058358689860uL}, 0, 0, 0, 0),
                new HeaderKeyStringData((int)HeaderIndex.ContentLanguage, new ulong[] {18437736737013759967uL, 18446743936268500959uL, 18437736737013759967uL, 18437736737013759967uL}, new ulong[] {23644233056321603uL, 12666734734344261uL, 19985058358165580uL, 19422078339973205uL}, 0, 0, 0, 0),
                new HeaderKeyStringData((int)HeaderIndex.ContentLocation, new ulong[] {18437736737013759967uL, 18446743936268500959uL, 18437736737013759967uL, 18437736737013759967uL}, new ulong[] {23644233056321603uL, 12666734734344261uL, 18296161254178892uL, 21955387490631764uL}, 0, 0, 0, 0),
                new HeaderKeyStringData((int)HeaderIndex.WWWAuthenticate, new ulong[] {18446743936268500959uL, 18437736737013759967uL, 18437736737013759967uL, 18437736737013759967uL}, new ulong[] {12666747619835991uL, 20266559105990721uL, 20548034082242629uL, 19422134174548035uL}, 0, 0, 0, 0),},
            new HeaderKeyStringData[] {
                new HeaderKeyStringData((int)HeaderIndex.TransferEncoding, new ulong[] {18437736737013759967uL, 18437736737013759967uL, 18437736737013759999uL, 18437736737013759967uL}, new ulong[] {21955327361679444uL, 23081244447604819uL, 18859158451585069uL, 21955361720500303uL}, 0, 0, (ushort)65503u, (ushort)71u),},
            new HeaderKeyStringData[] {
                new HeaderKeyStringData((int)HeaderIndex.ProxyAuthenticate, new ulong[] {18437736737013759967uL, 18437736737015857119uL, 18437736737013759967uL, 18437736737013759967uL}, new ulong[] {24770137258328144uL, 23925652196229209uL, 21955344540893268uL, 18296161253785684uL}, 4292870111u, 4522068u, 0, 0),},
            NoHeaders,
            NoHeaders,
            NoHeaders,
            new HeaderKeyStringData[] {
                new HeaderKeyStringData((int)HeaderIndex.AccessControlMaxAge, new ulong[] {18437736737013759967uL, 18437736874452713439uL, 18437736737013759967uL, 18437736874452713439uL, 18437736874452713439uL}, new ulong[] {19422061160235073uL, 18859016718581843uL, 23081308872638543uL, 21673766485229647uL, 18296066765488193uL}, 4292870111u, 4522055u, 0, 0),},
            NoHeaders,
            NoHeaders,
            NoHeaders,
            NoHeaders,
            new HeaderKeyStringData[] {
                new HeaderKeyStringData((int)HeaderIndex.AccessControlAllowOrigin, new ulong[] {18437736737013759967uL, 18437736874452713439uL, 18437736737013759967uL, 18437736874452713439uL, 18437736737013759967uL, 18437736737013759999uL}, new ulong[] {19422061160235073uL, 18859016718581843uL, 23081308872638543uL, 18296066764701775uL, 24488662281224268uL, 20548025492373549uL}, 4292870111u, 4784199u, (ushort)65503u, (ushort)78u),},
            new HeaderKeyStringData[] {
                new HeaderKeyStringData((int)HeaderIndex.AccessControlAllowHeaders, new ulong[] {18437736737013759967uL, 18437736874452713439uL, 18437736737013759967uL, 18437736874452713439uL, 18437736737013759967uL, 18437736737013759999uL, 18437736737013759967uL}, new ulong[] {19422061160235073uL, 18859016718581843uL, 23081308872638543uL, 18296066764701775uL, 24488662281224268uL, 18296169843654701uL, 23362775258824772uL}, 0, 0, 0, 0),
                new HeaderKeyStringData((int)HeaderIndex.AccessControlAllowMethods, new ulong[] {18437736737013759967uL, 18437736874452713439uL, 18437736737013759967uL, 18437736874452713439uL, 18437736737013759967uL, 18437736737013759999uL, 18437736737013759967uL}, new ulong[] {19422061160235073uL, 18859016718581843uL, 23081308872638543uL, 18296066764701775uL, 24488662281224268uL, 23644194401484845uL, 23362715129937992uL}, 0, 0, 0, 0),},
            new HeaderKeyStringData[] {
                new HeaderKeyStringData((int)HeaderIndex.AccessControlExposeHeaders, new ulong[] {18437736737013759967uL, 18437736874452713439uL, 18437736737013759967uL, 18437736874452713439uL, 18437736737013759967uL, 18437736737015857119uL, 18437736737013759967uL}, new ulong[] {19422061160235073uL, 18859016718581843uL, 23081308872638543uL, 19421966671544399uL, 23362762374643800uL, 19422082633629765uL, 23081244447473729uL}, 0, 0, (ushort)65503u, (ushort)83u),},
            NoHeaders,
            NoHeaders,
            new HeaderKeyStringData[] {
                new HeaderKeyStringData((int)HeaderIndex.AccessControlAllowCredentials, new ulong[] {18437736737013759967uL, 18437736874452713439uL, 18437736737013759967uL, 18437736874452713439uL, 18437736737013759967uL, 18437736737013759999uL, 18437736737013759967uL, 18437736737013759967uL}, new ulong[] {19422061160235073uL, 18859016718581843uL, 23081308872638543uL, 18296066764701775uL, 24488662281224268uL, 19422125584744493uL, 23644233055666244uL, 23362749488758857uL}, 0, 0, 0, 0),},
        };

        private static readonly KeyValuePair<string, int>[] HeaderNames = new []
        {
            new KeyValuePair<string, int>("Connection", (int)HeaderIndex.Connection),
            new KeyValuePair<string, int>("Date", (int)HeaderIndex.Date),
            new KeyValuePair<string, int>("Content-Length", (int)HeaderIndex.ContentLength),
            new KeyValuePair<string, int>("Server", (int)HeaderIndex.Server),
            new KeyValuePair<string, int>("Content-Type", (int)HeaderIndex.ContentType),
            new KeyValuePair<string, int>("Transfer-Encoding", (int)HeaderIndex.TransferEncoding),
            new KeyValuePair<string, int>("Cache-Control", (int)HeaderIndex.CacheControl),
            new KeyValuePair<string, int>("Keep-Alive", (int)HeaderIndex.KeepAlive),
            new KeyValuePair<string, int>("Pragma", (int)HeaderIndex.Pragma),
            new KeyValuePair<string, int>("Trailer", (int)HeaderIndex.Trailer),
            new KeyValuePair<string, int>("Upgrade", (int)HeaderIndex.Upgrade),
            new KeyValuePair<string, int>("Via", (int)HeaderIndex.Via),
            new KeyValuePair<string, int>("Warning", (int)HeaderIndex.Warning),
            new KeyValuePair<string, int>("Allow", (int)HeaderIndex.Allow),
            new KeyValuePair<string, int>("Content-Encoding", (int)HeaderIndex.ContentEncoding),
            new KeyValuePair<string, int>("Content-Language", (int)HeaderIndex.ContentLanguage),
            new KeyValuePair<string, int>("Content-Location", (int)HeaderIndex.ContentLocation),
            new KeyValuePair<string, int>("Content-MD5", (int)HeaderIndex.ContentMD5),
            new KeyValuePair<string, int>("Content-Range", (int)HeaderIndex.ContentRange),
            new KeyValuePair<string, int>("Expires", (int)HeaderIndex.Expires),
            new KeyValuePair<string, int>("Last-Modified", (int)HeaderIndex.LastModified),
            new KeyValuePair<string, int>("Access-Control-Allow-Credentials", (int)HeaderIndex.AccessControlAllowCredentials),
            new KeyValuePair<string, int>("Access-Control-Allow-Headers", (int)HeaderIndex.AccessControlAllowHeaders),
            new KeyValuePair<string, int>("Access-Control-Allow-Methods", (int)HeaderIndex.AccessControlAllowMethods),
            new KeyValuePair<string, int>("Access-Control-Allow-Origin", (int)HeaderIndex.AccessControlAllowOrigin),
            new KeyValuePair<string, int>("Access-Control-Expose-Headers", (int)HeaderIndex.AccessControlExposeHeaders),
            new KeyValuePair<string, int>("Access-Control-Max-Age", (int)HeaderIndex.AccessControlMaxAge),
            new KeyValuePair<string, int>("Accept-Ranges", (int)HeaderIndex.AcceptRanges),
            new KeyValuePair<string, int>("Age", (int)HeaderIndex.Age),
            new KeyValuePair<string, int>("ETag", (int)HeaderIndex.ETag),
            new KeyValuePair<string, int>("Location", (int)HeaderIndex.Location),
            new KeyValuePair<string, int>("Proxy-Authenticate", (int)HeaderIndex.ProxyAuthenticate),
            new KeyValuePair<string, int>("Retry-After", (int)HeaderIndex.RetryAfter),
            new KeyValuePair<string, int>("Set-Cookie", (int)HeaderIndex.SetCookie),
            new KeyValuePair<string, int>("Vary", (int)HeaderIndex.Vary),
            new KeyValuePair<string, int>("WWW-Authenticate", (int)HeaderIndex.WWWAuthenticate),
        };

        public byte[] _rawConnection;
        public byte[] _rawDate;
        public byte[] _rawContentLength;
        public byte[] _rawServer;
        public byte[] _rawTransferEncoding;

        public FrameResponseHeaders() : base(HeaderNames, _keyStringDataByLength, new StringValues[36])
        {
        }

        public StringValues HeaderConnection
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.Connection)) != 0))
                {
                    return _headerData[(int)HeaderIndex.Connection];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.Connection);
                _headerData[(int)HeaderIndex.Connection] = value; 
                _rawConnection = null;
            }
        }

        public StringValues HeaderDate
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.Date)) != 0))
                {
                    return _headerData[(int)HeaderIndex.Date];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.Date);
                _headerData[(int)HeaderIndex.Date] = value; 
                _rawDate = null;
            }
        }

        public StringValues HeaderContentLength
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.ContentLength)) != 0))
                {
                    return _headerData[(int)HeaderIndex.ContentLength];
                }
                return StringValues.Empty;
            }
            set
            {
                _contentLength = ParseContentLength(ref value);
                _bits |= (1L << (int)HeaderIndex.ContentLength);
                _headerData[(int)HeaderIndex.ContentLength] = value; 
                _rawContentLength = null;
            }
        }

        public StringValues HeaderServer
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.Server)) != 0))
                {
                    return _headerData[(int)HeaderIndex.Server];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.Server);
                _headerData[(int)HeaderIndex.Server] = value; 
                _rawServer = null;
            }
        }

        public StringValues HeaderContentType
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.ContentType)) != 0))
                {
                    return _headerData[(int)HeaderIndex.ContentType];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.ContentType);
                _headerData[(int)HeaderIndex.ContentType] = value; 
            }
        }

        public StringValues HeaderTransferEncoding
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.TransferEncoding)) != 0))
                {
                    return _headerData[(int)HeaderIndex.TransferEncoding];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.TransferEncoding);
                _headerData[(int)HeaderIndex.TransferEncoding] = value; 
                _rawTransferEncoding = null;
            }
        }

        public StringValues HeaderCacheControl
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.CacheControl)) != 0))
                {
                    return _headerData[(int)HeaderIndex.CacheControl];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.CacheControl);
                _headerData[(int)HeaderIndex.CacheControl] = value; 
            }
        }

        public StringValues HeaderKeepAlive
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.KeepAlive)) != 0))
                {
                    return _headerData[(int)HeaderIndex.KeepAlive];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.KeepAlive);
                _headerData[(int)HeaderIndex.KeepAlive] = value; 
            }
        }

        public StringValues HeaderPragma
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.Pragma)) != 0))
                {
                    return _headerData[(int)HeaderIndex.Pragma];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.Pragma);
                _headerData[(int)HeaderIndex.Pragma] = value; 
            }
        }

        public StringValues HeaderTrailer
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.Trailer)) != 0))
                {
                    return _headerData[(int)HeaderIndex.Trailer];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.Trailer);
                _headerData[(int)HeaderIndex.Trailer] = value; 
            }
        }

        public StringValues HeaderUpgrade
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.Upgrade)) != 0))
                {
                    return _headerData[(int)HeaderIndex.Upgrade];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.Upgrade);
                _headerData[(int)HeaderIndex.Upgrade] = value; 
            }
        }

        public StringValues HeaderVia
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.Via)) != 0))
                {
                    return _headerData[(int)HeaderIndex.Via];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.Via);
                _headerData[(int)HeaderIndex.Via] = value; 
            }
        }

        public StringValues HeaderWarning
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.Warning)) != 0))
                {
                    return _headerData[(int)HeaderIndex.Warning];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.Warning);
                _headerData[(int)HeaderIndex.Warning] = value; 
            }
        }

        public StringValues HeaderAllow
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.Allow)) != 0))
                {
                    return _headerData[(int)HeaderIndex.Allow];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.Allow);
                _headerData[(int)HeaderIndex.Allow] = value; 
            }
        }

        public StringValues HeaderContentEncoding
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.ContentEncoding)) != 0))
                {
                    return _headerData[(int)HeaderIndex.ContentEncoding];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.ContentEncoding);
                _headerData[(int)HeaderIndex.ContentEncoding] = value; 
            }
        }

        public StringValues HeaderContentLanguage
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.ContentLanguage)) != 0))
                {
                    return _headerData[(int)HeaderIndex.ContentLanguage];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.ContentLanguage);
                _headerData[(int)HeaderIndex.ContentLanguage] = value; 
            }
        }

        public StringValues HeaderContentLocation
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.ContentLocation)) != 0))
                {
                    return _headerData[(int)HeaderIndex.ContentLocation];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.ContentLocation);
                _headerData[(int)HeaderIndex.ContentLocation] = value; 
            }
        }

        public StringValues HeaderContentMD5
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.ContentMD5)) != 0))
                {
                    return _headerData[(int)HeaderIndex.ContentMD5];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.ContentMD5);
                _headerData[(int)HeaderIndex.ContentMD5] = value; 
            }
        }

        public StringValues HeaderContentRange
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.ContentRange)) != 0))
                {
                    return _headerData[(int)HeaderIndex.ContentRange];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.ContentRange);
                _headerData[(int)HeaderIndex.ContentRange] = value; 
            }
        }

        public StringValues HeaderExpires
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.Expires)) != 0))
                {
                    return _headerData[(int)HeaderIndex.Expires];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.Expires);
                _headerData[(int)HeaderIndex.Expires] = value; 
            }
        }

        public StringValues HeaderLastModified
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.LastModified)) != 0))
                {
                    return _headerData[(int)HeaderIndex.LastModified];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.LastModified);
                _headerData[(int)HeaderIndex.LastModified] = value; 
            }
        }

        public StringValues HeaderAccessControlAllowCredentials
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.AccessControlAllowCredentials)) != 0))
                {
                    return _headerData[(int)HeaderIndex.AccessControlAllowCredentials];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.AccessControlAllowCredentials);
                _headerData[(int)HeaderIndex.AccessControlAllowCredentials] = value; 
            }
        }

        public StringValues HeaderAccessControlAllowHeaders
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.AccessControlAllowHeaders)) != 0))
                {
                    return _headerData[(int)HeaderIndex.AccessControlAllowHeaders];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.AccessControlAllowHeaders);
                _headerData[(int)HeaderIndex.AccessControlAllowHeaders] = value; 
            }
        }

        public StringValues HeaderAccessControlAllowMethods
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.AccessControlAllowMethods)) != 0))
                {
                    return _headerData[(int)HeaderIndex.AccessControlAllowMethods];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.AccessControlAllowMethods);
                _headerData[(int)HeaderIndex.AccessControlAllowMethods] = value; 
            }
        }

        public StringValues HeaderAccessControlAllowOrigin
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.AccessControlAllowOrigin)) != 0))
                {
                    return _headerData[(int)HeaderIndex.AccessControlAllowOrigin];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.AccessControlAllowOrigin);
                _headerData[(int)HeaderIndex.AccessControlAllowOrigin] = value; 
            }
        }

        public StringValues HeaderAccessControlExposeHeaders
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.AccessControlExposeHeaders)) != 0))
                {
                    return _headerData[(int)HeaderIndex.AccessControlExposeHeaders];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.AccessControlExposeHeaders);
                _headerData[(int)HeaderIndex.AccessControlExposeHeaders] = value; 
            }
        }

        public StringValues HeaderAccessControlMaxAge
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.AccessControlMaxAge)) != 0))
                {
                    return _headerData[(int)HeaderIndex.AccessControlMaxAge];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.AccessControlMaxAge);
                _headerData[(int)HeaderIndex.AccessControlMaxAge] = value; 
            }
        }

        public StringValues HeaderAcceptRanges
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.AcceptRanges)) != 0))
                {
                    return _headerData[(int)HeaderIndex.AcceptRanges];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.AcceptRanges);
                _headerData[(int)HeaderIndex.AcceptRanges] = value; 
            }
        }

        public StringValues HeaderAge
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.Age)) != 0))
                {
                    return _headerData[(int)HeaderIndex.Age];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.Age);
                _headerData[(int)HeaderIndex.Age] = value; 
            }
        }

        public StringValues HeaderETag
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.ETag)) != 0))
                {
                    return _headerData[(int)HeaderIndex.ETag];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.ETag);
                _headerData[(int)HeaderIndex.ETag] = value; 
            }
        }

        public StringValues HeaderLocation
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.Location)) != 0))
                {
                    return _headerData[(int)HeaderIndex.Location];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.Location);
                _headerData[(int)HeaderIndex.Location] = value; 
            }
        }

        public StringValues HeaderProxyAuthenticate
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.ProxyAuthenticate)) != 0))
                {
                    return _headerData[(int)HeaderIndex.ProxyAuthenticate];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.ProxyAuthenticate);
                _headerData[(int)HeaderIndex.ProxyAuthenticate] = value; 
            }
        }

        public StringValues HeaderRetryAfter
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.RetryAfter)) != 0))
                {
                    return _headerData[(int)HeaderIndex.RetryAfter];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.RetryAfter);
                _headerData[(int)HeaderIndex.RetryAfter] = value; 
            }
        }

        public StringValues HeaderSetCookie
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.SetCookie)) != 0))
                {
                    return _headerData[(int)HeaderIndex.SetCookie];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.SetCookie);
                _headerData[(int)HeaderIndex.SetCookie] = value; 
            }
        }

        public StringValues HeaderVary
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.Vary)) != 0))
                {
                    return _headerData[(int)HeaderIndex.Vary];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.Vary);
                _headerData[(int)HeaderIndex.Vary] = value; 
            }
        }

        public StringValues HeaderWWWAuthenticate
        {
            get
            {
                if (((_bits & (1L << (int)HeaderIndex.WWWAuthenticate)) != 0))
                {
                    return _headerData[(int)HeaderIndex.WWWAuthenticate];
                }
                return StringValues.Empty;
            }
            set
            {
                _bits |= (1L << (int)HeaderIndex.WWWAuthenticate);
                _headerData[(int)HeaderIndex.WWWAuthenticate] = value; 
            }
        }

        public void SetRawConnection(StringValues value, byte[] raw)
        {
            _bits |= (1L << (int)HeaderIndex.Connection);
            _headerData[(int)HeaderIndex.Connection] = value;
            _rawConnection = raw;
        }

        public void SetRawDate(StringValues value, byte[] raw)
        {
            _bits |= (1L << (int)HeaderIndex.Date);
            _headerData[(int)HeaderIndex.Date] = value;
            _rawDate = raw;
        }

        public void SetRawContentLength(StringValues value, byte[] raw)
        {
            _contentLength = ParseContentLength(ref value);
            _bits |= (1L << (int)HeaderIndex.ContentLength);
            _headerData[(int)HeaderIndex.ContentLength] = value;
            _rawContentLength = raw;
        }

        public void SetRawServer(StringValues value, byte[] raw)
        {
            _bits |= (1L << (int)HeaderIndex.Server);
            _headerData[(int)HeaderIndex.Server] = value;
            _rawServer = raw;
        }

        public void SetRawTransferEncoding(StringValues value, byte[] raw)
        {
            _bits |= (1L << (int)HeaderIndex.TransferEncoding);
            _headerData[(int)HeaderIndex.TransferEncoding] = value;
            _rawTransferEncoding = raw;
        }

        protected override void ClearExtra(int index)
        {
            switch (index)
            {
                case (int)HeaderIndex.Connection: 
                    _rawConnection = null;
                    break;
                case (int)HeaderIndex.Date: 
                    _rawDate = null;
                    break;
                case (int)HeaderIndex.ContentLength:
                    _contentLength = null; 
                    _rawContentLength = null;
                    break;
                case (int)HeaderIndex.Server: 
                    _rawServer = null;
                    break;
                case (int)HeaderIndex.TransferEncoding: 
                    _rawTransferEncoding = null;
                    break;
            }
        }

        public void CopyTo(ref MemoryPoolIterator output)
        {
            var bits = _bits;
            _bits = 0;
            var flag = 1L;
            var headers = _headerData;
            for (var h = 0; h < headers.Length; h++)
            {
                var hasHeader = (bits & flag) != 0;
                flag = 1L << (h + 1);

                if (!hasHeader)
                {
                    continue;
                }

                switch (h)
                {
                    case (int)HeaderIndex.Connection:
                        if (_rawConnection != null)
                        {
                            output.CopyFrom(_rawConnection);
                            _rawConnection = null;
                            continue;
                        }
                        break;
                    case (int)HeaderIndex.Date:
                        if (_rawDate != null)
                        {
                            output.CopyFrom(_rawDate);
                            _rawDate = null;
                            continue;
                        }
                        break;
                    case (int)HeaderIndex.ContentLength:
                        if (_rawContentLength != null)
                        {
                            output.CopyFrom(_rawContentLength);
                            _rawContentLength = null;
                            continue;
                        }
                        break;
                    case (int)HeaderIndex.Server:
                        if (_rawServer != null)
                        {
                            output.CopyFrom(_rawServer);
                            _rawServer = null;
                            continue;
                        }
                        break;
                    case (int)HeaderIndex.TransferEncoding:
                        if (_rawTransferEncoding != null)
                        {
                            output.CopyFrom(_rawTransferEncoding);
                            _rawTransferEncoding = null;
                            continue;
                        }
                        break;
                }

                var values = _headerData[h];
                _headerData[h] = default(StringValues);
                var valueCount = values.Count;
                for (var v = 0; v < valueCount; v++)
                {
                    var value = values[v];
                    if (value != null)
                    {
                        output.CopyFrom(_keyBytes[h]);
                        output.CopyFromAscii(value);
                    }
                }

                if (bits < flag)
                {
                    break;
                }
            }

            if (MaybeUnknown != null)
            {
                CopyExtraTo(ref output);
            }
        }

        private enum HeaderIndex
        {
            Connection = 0,
            Date = 1,
            ContentLength = 2,
            Server = 3,
            ContentType = 4,
            TransferEncoding = 5,
            CacheControl = 6,
            KeepAlive = 7,
            Pragma = 8,
            Trailer = 9,
            Upgrade = 10,
            Via = 11,
            Warning = 12,
            Allow = 13,
            ContentEncoding = 14,
            ContentLanguage = 15,
            ContentLocation = 16,
            ContentMD5 = 17,
            ContentRange = 18,
            Expires = 19,
            LastModified = 20,
            AccessControlAllowCredentials = 21,
            AccessControlAllowHeaders = 22,
            AccessControlAllowMethods = 23,
            AccessControlAllowOrigin = 24,
            AccessControlExposeHeaders = 25,
            AccessControlMaxAge = 26,
            AcceptRanges = 27,
            Age = 28,
            ETag = 29,
            Location = 30,
            ProxyAuthenticate = 31,
            RetryAfter = 32,
            SetCookie = 33,
            Vary = 34,
            WWWAuthenticate = 35,
        }
    }
}