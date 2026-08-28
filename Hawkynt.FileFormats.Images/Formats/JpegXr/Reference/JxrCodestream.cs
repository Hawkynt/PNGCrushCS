namespace SharpAstro.Jxr;

/// <summary>
/// Assembles / parses a complete single-tile <b>SPATIAL</b>, <b>OL_NONE</b>,
/// <b>YUV444</b>, <b>BD8 RGB</b> JPEG XR codestream — the "Rung 7e" bridge
/// between the working OL_NONE codec (<see cref="TileImageCodec"/>) and the
/// container (<see cref="JxrContainer"/>). The byte layout mirrors jxrlib's
/// reference encoder exactly (verified against <c>JxrEncApp -f -l 0 -d 3 -q 1</c>):
/// <code>
///   IMAGE_HEADER         (T.832 §8.3)   — WMPHOTO sig + flags + dims
///   IMAGE_PLANE_HEADER   (T.832 §8.4)   — YUV444, all bands, uniform QP (uQPMode 0x750)
///   PROFILE_LEVEL_INFO                  — 00 04 6f ff 00 01 (writeIndexTableNull, cNumBitIO==0)
///   PACKET_HEADER                       — 00 00 01 00 (spatial, tile 0)
///   CODED_TILE (spatial)                — per MB: DC, LP, HP(CBP + per-subblock AC/FL)
/// </code>
/// In SPATIAL mode jxrlib aliases all four band bit-streams to one stream
/// (strcodec.c:771), so the four band coders are driven through a single
/// <see cref="BitWriter"/>/<see cref="BitReader"/> — the per-MB write order
/// (DC → LP → CBP → per-subblock AC then FL) already matches segenc.c.
/// </summary>
internal static class JxrCodestream
{
    // jxrlib writeIndexTableNull (cNumBitIO==0): a 4-byte "subsequent bytes" vlw_esc
    // followed by the default conformance record + LAST_FLAG.
    private const int DefaultProfileIdc = 111; // 0x6f
    private const int DefaultLevelIdc = 255;   // 0xff

    // Trailing zero bytes appended to the decode buffer so the entropy decoder's
    // speculative peeks past the last macroblock never overrun the codestream.
    private const int EndPeekSlackBytes = 16;

    /// <summary>
    /// Encode a <paramref name="width"/>×<paramref name="height"/> BD8 RGB image
    /// (each channel <c>width*height</c> samples, raster order) into a JXR codestream.
    /// Arbitrary dimensions are allowed (the partial right/bottom macroblocks are edge-
    /// replicated; see <see cref="RequirePositiveDims"/>). QP indices are 0 for lossless.
    /// </summary>
    public static byte[] Encode(ReadOnlySpan<int> r, ReadOnlySpan<int> g, ReadOnlySpan<int> b,
                                int width, int height, int qpDc = 0, int qpLp = 0, int qpHp = 0, int overlap = 0,
                                JxrOutputBitDepth bd = JxrOutputBitDepth.Bd8, JxrTileLayout? tiles = null,
                                JxrInternalColorFormat internalClrFmt = JxrInternalColorFormat.YUV444,
                                int trimFlexBits = 0, bool noFlexBits = false, bool frequencyMode = false)
    {
        if (internalClrFmt is JxrInternalColorFormat.YUV420 or JxrInternalColorFormat.YUV422)
            return EncodeChroma(r, g, b, width, height, internalClrFmt, qpDc, qpLp, qpHp, overlap, bd);
        if (tiles is { } layout && (layout.NumVerTiles > 1 || layout.NumHorTiles > 1))
            return EncodeMultiTile(r, g, b, width, height, layout, qpDc, qpLp, qpHp, overlap, bd);
        if (frequencyMode)
            return EncodeFrequency(r, g, b, width, height, qpDc, qpLp, qpHp, overlap, bd, noFlexBits);
        RequirePositiveDims(width, height);
        int mbCols = MbCount(width), mbRows = MbCount(height);
        // jxrlib forces scaled-arith unless (lossless QP ≤ 1 AND all bands present); NO_FLEXBITS
        // (sbSubband != SB_ALL) therefore scales BD8/BD16/BD16S. BD32* is forced non-scaled
        // unconditionally (strenc.c:962) — its lossy QP uses the non-scaled quantizer.
        bool scaled = (ScaledArith(qpDc, qpLp, qpHp) || noFlexBits) && bd != JxrOutputBitDepth.Bd32S;
        var (qDc, qLp, qHp) = Quantizers(qpDc, qpLp, qpHp, scaled);
        var (qDcUV, qLpUV) = ChromaDcLpQuantizers(qpDc, qpLp, scaled); // half-step chroma DC/LP in scaled mode
        int bias = LumaBias(bd);
        int shift = scaled ? SignalTransform.ScaledShift : 0; // <<3 scaled-arith input scaling
        var bands = noFlexBits ? JxrBandsPresent.NoFlexbits : JxrBandsPresent.AllBands;

        var w = new BitWriter();
        WriteImageHeader(w, width, height, overlap, JxrOutputColorFormat.Rgb, bd, trimFlexBits);
        WritePlaneHeader(w, qpDc, qpLp, qpHp, scaled, bd, bands: bands);
        WriteProfileLevelInfo(w);
        WritePacketHeader(w, trimFlexBits);

        // Color-transform + load every macroblock into the whole-image YUV planes,
        // then run the overlap + 2-stage PCT across the grid (jxrlib's sliding
        // 2-MB-row window), then per-MB quantize + entropy code.
        var planes = OverlapTransform.AllocatePlanes(mbCols, mbRows);
        var (mr, mg, mb) = (new int[256], new int[256], new int[256]);
        for (var mbR = 0; mbR < mbRows; mbR++)
            for (var mbC = 0; mbC < mbCols; mbC++)
            {
                ExtractMb(r, g, b, width, height, mbR, mbC, mr, mg, mb);
                SignalTransform.LoadColor(mr, mg, mb, planes[0], planes[1], planes[2],
                                          OverlapTransform.MbBase(mbCols, mbR, mbC), bias, shift);
            }

        OverlapTransform.Forward(planes, mbCols, mbRows, overlap, scaled);

        // SPATIAL: all four band streams alias one writer (BitWriter is a class).
        var ctx = new CodingContext(ColorFormat.Yuv444, 3) { TrimFlexBits = trimFlexBits, NoFlexBits = noFlexBits };
        var tile = new TileCoder(mbCols);
        for (var mbR = 0; mbR < mbRows; mbR++)
        {
            for (var mbC = 0; mbC < mbCols; mbC++)
            {
                var block = new Macroblock(3);
                int baseOff = OverlapTransform.MbBase(mbCols, mbR, mbC);
                for (var ch = 0; ch < 3; ch++)
                    SignalTransform.QuantizeExtract(planes[ch], baseOff, block, ch,
                                                    ch == 0 ? qDc : qDcUV, ch == 0 ? qLp : qLpUV, qHp);
                tile.EncodeMacroblock(ctx, block, mbC, mbR, w, w, w, w);
            }
            tile.AdvanceRow();
        }

        FillToByte(w);
        return w.ToArray();
    }

    // Single-tile FREQUENCY-mode YUV444 RGB encode — jxrlib's DEFAULT bitstream ordering (SPATIAL is the
    // `-f` opt-in). The colour load + overlap + per-MB entropy are byte-identical to SPATIAL; the only
    // difference is that the four bands are written to FOUR separate packets (DC / LP / HP / FLEXBITS,
    // packet types 1/2/3/4) instead of interleaved into one — each band's data is unchanged because the
    // bands use independent adaptive models. The band packets are located by a 4-entry INDEX_TABLE (same
    // 0x0001 + cumulative-vlw_esc-offsets + 0xFF format as the multi-tile tile table).
    private static byte[] EncodeFrequency(ReadOnlySpan<int> r, ReadOnlySpan<int> g, ReadOnlySpan<int> b,
                                          int width, int height, int qpDc, int qpLp, int qpHp, int overlap,
                                          JxrOutputBitDepth bd, bool noFlexBits)
    {
        RequirePositiveDims(width, height);
        int mbCols = MbCount(width), mbRows = MbCount(height);
        bool scaled = (ScaledArith(qpDc, qpLp, qpHp) || noFlexBits) && bd != JxrOutputBitDepth.Bd32S;
        var (qDc, qLp, qHp) = Quantizers(qpDc, qpLp, qpHp, scaled);
        var (qDcUV, qLpUV) = ChromaDcLpQuantizers(qpDc, qpLp, scaled);
        int bias = LumaBias(bd);
        int shift = scaled ? SignalTransform.ScaledShift : 0;
        var bands = noFlexBits ? JxrBandsPresent.NoFlexbits : JxrBandsPresent.AllBands;
        int cSB = noFlexBits ? 3 : 4; // number of subband packets (DC, LP, HP, [FLEXBITS])

        var planes = OverlapTransform.AllocatePlanes(mbCols, mbRows);
        var (mr, mg, mb) = (new int[256], new int[256], new int[256]);
        for (var mbR = 0; mbR < mbRows; mbR++)
            for (var mbC = 0; mbC < mbCols; mbC++)
            {
                ExtractMb(r, g, b, width, height, mbR, mbC, mr, mg, mb);
                SignalTransform.LoadColor(mr, mg, mb, planes[0], planes[1], planes[2],
                                          OverlapTransform.MbBase(mbCols, mbR, mbC), bias, shift);
            }
        OverlapTransform.Forward(planes, mbCols, mbRows, overlap, scaled);

        // One writer per band, each opening with its packet header (00 00 01 [type], type = band+1).
        var bandW = new BitWriter[cSB];
        for (var k = 0; k < cSB; k++) { bandW[k] = new BitWriter(); WriteBandPacketHeader(bandW[k], k + 1); }
        var (dcW, lpW, acW) = (bandW[0], bandW[1], bandW[2 < cSB ? 2 : 0]);
        var flW = cSB > 3 ? bandW[3] : new BitWriter(); // discarded when NO_FLEXBITS

        var ctx = new CodingContext(ColorFormat.Yuv444, 3) { NoFlexBits = noFlexBits };
        var tile = new TileCoder(mbCols);
        for (var mbR = 0; mbR < mbRows; mbR++)
        {
            for (var mbC = 0; mbC < mbCols; mbC++)
            {
                var block = new Macroblock(3);
                int baseOff = OverlapTransform.MbBase(mbCols, mbR, mbC);
                for (var ch = 0; ch < 3; ch++)
                    SignalTransform.QuantizeExtract(planes[ch], baseOff, block, ch,
                                                    ch == 0 ? qDc : qDcUV, ch == 0 ? qLp : qLpUV, qHp);
                tile.EncodeMacroblock(ctx, block, mbC, mbR, dcW, lpW, acW, flW);
            }
            tile.AdvanceRow();
        }

        var bandPackets = new byte[cSB][];
        for (var k = 0; k < cSB; k++) { FillToByte(bandW[k]); bandPackets[k] = bandW[k].ToArray(); }

        var hw = new BitWriter();
        WriteImageHeader(hw, width, height, overlap, JxrOutputColorFormat.Rgb, bd, frequencyMode: true);
        WritePlaneHeader(hw, qpDc, qpLp, qpHp, scaled, bd, bands: bands);

        // FREQUENCY band index table. A header-only (≤ MINIMUM_PACKET_LENGTH) band carries no data —
        // jxrlib marks it 0xFF in the index, omits it from the stream, and does not advance the offset.
        hw.WriteBits(IndexTableTiles.IndexTableStartCode, 16);
        var emitted = new System.Collections.Generic.List<byte[]>(cSB);
        long cum = 0;
        foreach (byte[] pkt in bandPackets)
        {
            if (pkt.Length <= MinimumPacketLength) { hw.WriteBits(0xFF, 8); } // empty band marker
            else { WriteVlwEsc(hw, cum); cum += pkt.Length; emitted.Add(pkt); }
        }
        hw.WriteBits(0xFF, 8); // end escape
        FillToByte(hw);
        return Concat(hw.ToArray(), emitted.ToArray());
    }

    private const int MinimumPacketLength = 4; // jxrlib MINIMUM_PACKET_LENGTH (a header-only packet)

    // FREQUENCY packet header: 00 00 01 [type] (single tile ⇒ pID 0). type: 1=DC, 2=LP, 3=HP/AC, 4=FLEXBITS.
    private static void WriteBandPacketHeader(BitWriter w, int type)
    {
        w.WriteBits(0, 8); w.WriteBits(0, 8); w.WriteBits(1, 8); w.WriteBits((uint)type, 8);
    }

    // Single-tile SPATIAL YUV420/422 BD8 RGB encode — the inverse of DecodeChroma. jxrlib forces
    // scaled-arithmetic mode for subsampled chroma (even at QP 1, since the chroma resolution change
    // disqualifies the lossless fast-path), so the colour load scales the RGB input <<3 and the chroma
    // is downsampled (5-tap [1,4,6,4,1]/16, in the YCoCg-R domain) to the reduced grid before the
    // transform. Luma is a full 444-style plane. Ports strenc.c inputMBRow + downsampleUV + the 420_UV
    // / 422_UV forward transform loops (incl. the forward POT pre-filter for OL_ONE/OL_TWO).
    private static byte[] EncodeChroma(ReadOnlySpan<int> r, ReadOnlySpan<int> g, ReadOnlySpan<int> b,
                                       int width, int height, JxrInternalColorFormat clrFmt,
                                       int qpDc, int qpLp, int qpHp, int overlap, JxrOutputBitDepth bd)
    {
        RequirePositiveDims(width, height);
        var cf = clrFmt == JxrInternalColorFormat.YUV420 ? ColorFormat.Yuv420 : ColorFormat.Yuv422;
        int mbCols = MbCount(width), mbRows = MbCount(height);
        bool scaled = true; // jxrlib m_bUVResolutionChange forces bScaledArith for subsampled chroma
        var (qDc, qLp, qHp) = Quantizers(qpDc, qpLp, qpHp, scaled);
        var (qDcUV, qLpUV) = ChromaDcLpQuantizers(qpDc, qpLp, scaled); // half-step chroma DC/LP in scaled mode
        int bias = LumaBias(bd);
        int shift = SignalTransform.ScaledShift; // <<3 input scaling (SHIFTZERO + QPFRACBITS)
        int chromaBlocks = MacroblockLayout.ChromaBlocks(cf);

        var w = new BitWriter();
        WriteImageHeader(w, width, height, overlap, JxrOutputColorFormat.Rgb, bd); // external format is still RGB
        WritePlaneHeader(w, qpDc, qpLp, qpHp, scaled, bd, clrFmt: clrFmt);
        WriteProfileLevelInfo(w);
        WritePacketHeader(w);

        // Colour-transform + <<3-scaled load into the full-res luma plane and full-res U,V planes.
        var luma = OverlapTransform.AllocatePlanes(mbCols, mbRows, 1)[0];
        var full = OverlapTransform.AllocatePlanes(mbCols, mbRows, 2); // full-res chroma U,V (pre-downsample)
        var (mr, mg, mb) = (new int[256], new int[256], new int[256]);
        for (var mbR = 0; mbR < mbRows; mbR++)
            for (var mbC = 0; mbC < mbCols; mbC++)
            {
                ExtractMb(r, g, b, width, height, mbR, mbC, mr, mg, mb);
                SignalTransform.LoadColor(mr, mg, mb, luma, full[0], full[1],
                                          OverlapTransform.MbBase(mbCols, mbR, mbC), bias, shift);
            }

        // Downsample full-res chroma → reduced chroma, then forward-transform both planes.
        var reduced = ChromaOverlapTransform.AllocatePlanes(mbCols, mbRows, cf);
        ChromaDownsample.Downsample(cf, full[0], full[1], reduced[0], reduced[1], mbCols, mbRows);

        OverlapTransform.Forward(new[] { luma }, mbCols, mbRows, overlap, scaled);
        if (overlap == 0)
            for (var mbR = 0; mbR < mbRows; mbR++)
                for (var mbC = 0; mbC < mbCols; mbC++)
                {
                    int rb = ChromaOverlapTransform.MbBase(mbCols, mbR, mbC, cf);
                    ChromaTransform.ForwardMbNoOverlap(reduced[0], rb, cf);
                    ChromaTransform.ForwardMbNoOverlap(reduced[1], rb, cf);
                }
        else
            ChromaOverlapTransform.Forward(reduced, mbCols, mbRows, overlap, cf);

        // Per-MB quantize (luma full, chroma reduced) + entropy code.
        var ctx = new CodingContext(cf, 3);
        var tile = new TileCoder(mbCols, 3, cf);
        for (var mbR = 0; mbR < mbRows; mbR++)
        {
            for (var mbC = 0; mbC < mbCols; mbC++)
            {
                var block = new Macroblock(3, chromaBlocks);
                int lumaBase = OverlapTransform.MbBase(mbCols, mbR, mbC);
                int rb = ChromaOverlapTransform.MbBase(mbCols, mbR, mbC, cf);
                SignalTransform.QuantizeExtract(luma, lumaBase, block, 0, qDc, qLp, qHp);
                SignalTransform.QuantizeExtractChroma(reduced[0], rb, block, 1, qDcUV, qLpUV, qHp, cf);
                SignalTransform.QuantizeExtractChroma(reduced[1], rb, block, 2, qDcUV, qLpUV, qHp, cf);
                tile.EncodeMacroblock(ctx, block, mbC, mbR, w, w, w, w);
            }
            tile.AdvanceRow();
        }

        FillToByte(w);
        return w.ToArray();
    }

    // Multi-tile SPATIAL encode (SOFT tiles — jxrlib's default). The overlap/PCT runs over the whole
    // image exactly as single-tile (soft tiling does NOT gate the overlap at tile edges), then each
    // tile is entropy-coded independently: a fresh CodingContext + TileCoder over the tile's MB
    // sub-rectangle in LOCAL coordinates, so DC/LP/CBP prediction and the adaptive contexts reset at
    // every tile edge (mirroring jxrlib's per-column context reset at tile-row boundaries — the MB
    // visiting order is identical). Tiles are concatenated in raster order behind an INDEX_TABLE_TILES
    // of cumulative byte offsets. Ports jxrlib strenc.c encodeMB / writeIndexTable / getTilePos.
    private static byte[] EncodeMultiTile(
        ReadOnlySpan<int> r, ReadOnlySpan<int> g, ReadOnlySpan<int> b,
        int width, int height, JxrTileLayout layout, int qpDc, int qpLp, int qpHp, int overlap, JxrOutputBitDepth bd)
    {
        RequirePositiveDims(width, height);
        int mbCols = MbCount(width), mbRows = MbCount(height);
        bool scaled = ScaledArith(qpDc, qpLp, qpHp);
        var (qDc, qLp, qHp) = Quantizers(qpDc, qpLp, qpHp, scaled);
        var (qDcUV, qLpUV) = ChromaDcLpQuantizers(qpDc, qpLp, scaled); // half-step chroma DC/LP in scaled mode
        int bias = LumaBias(bd);
        int shift = scaled ? SignalTransform.ScaledShift : 0; // <<3 scaled-arith input scaling (lossy QP)

        // Whole-image colour load — byte-identical to the single-tile path; the overlap + per-tile
        // entropy + headers are the shared multi-tile core.
        var planes = OverlapTransform.AllocatePlanes(mbCols, mbRows);
        var (mr, mg, mb) = (new int[256], new int[256], new int[256]);
        for (var mbR = 0; mbR < mbRows; mbR++)
            for (var mbC = 0; mbC < mbCols; mbC++)
            {
                ExtractMb(r, g, b, width, height, mbR, mbC, mr, mg, mb);
                SignalTransform.LoadColor(mr, mg, mb, planes[0], planes[1], planes[2],
                                          OverlapTransform.MbBase(mbCols, mbR, mbC), bias, shift);
            }

        return EncodeMultiTileCore(planes, mbCols, mbRows, layout, overlap, scaled, ColorFormat.Yuv444, 3,
            qDc, qLp, qHp, qDcUV, qLpUV,
            hw => WriteImageHeaderTiled(hw, width, height, overlap, layout, JxrOutputColorFormat.Rgb, bd),
            hw => WritePlaneHeader(hw, qpDc, qpLp, qpHp, scaled, bd));
    }

    // Shared multi-tile core: takes the already-loaded whole-image planes, runs the whole-image overlap
    // (soft tiling — POT spans tile edges), then entropy-codes each tile independently (fresh
    // CodingContext + TileCoder in tile-local MB coords), and assembles the tiled image/plane headers +
    // INDEX_TABLE_TILES + concatenated tile bodies. Generic over channel count / colour format / the
    // resolved quantizers, so every pixel format (RGB / Y-only, all bit depths) shares one tile engine.
    private static byte[] EncodeMultiTileCore(
        int[][] planes, int mbCols, int mbRows, JxrTileLayout layout, int overlap, bool scaled,
        ColorFormat cf, int channels, JxrQuantizer qDc, JxrQuantizer qLp, JxrQuantizer qHp,
        JxrQuantizer qDcUV, JxrQuantizer qLpUV, Action<BitWriter> writeImageHeader, Action<BitWriter> writePlaneHeader)
    {
        OverlapTransform.Forward(planes, mbCols, mbRows, overlap, scaled);

        int[] tileX = TileBoundaries(layout.TileWidthInMb, mbCols);
        int[] tileY = TileBoundaries(layout.TileHeightInMb, mbRows);
        int numCols = tileX.Length - 1, numRows = tileY.Length - 1;

        // Entropy-code each tile independently into its own byte-aligned buffer.
        var tileBytes = new byte[numRows * numCols][];
        for (var tr = 0; tr < numRows; tr++)
            for (var tc = 0; tc < numCols; tc++)
            {
                int tileIdx = tr * numCols + tc;
                int tmbW = tileX[tc + 1] - tileX[tc], tmbH = tileY[tr + 1] - tileY[tr];
                var tw = new BitWriter();
                WritePacketHeaderTile(tw, tileIdx);

                var ctx = new CodingContext(cf, channels);
                var tile = new TileCoder(tmbW, channels, cf);
                for (var ly = 0; ly < tmbH; ly++)
                {
                    for (var lx = 0; lx < tmbW; lx++)
                    {
                        int baseOff = OverlapTransform.MbBase(mbCols, tileY[tr] + ly, tileX[tc] + lx);
                        var block = new Macroblock(channels);
                        for (var ch = 0; ch < channels; ch++)
                            SignalTransform.QuantizeExtract(planes[ch], baseOff, block, ch,
                                                            ch == 0 ? qDc : qDcUV, ch == 0 ? qLp : qLpUV, qHp);
                        tile.EncodeMacroblock(ctx, block, lx, ly, tw, tw, tw, tw);
                    }
                    tile.AdvanceRow();
                }
                FillToByte(tw);
                tileBytes[tileIdx] = tw.ToArray();
            }

        // Header (image header with tiling + plane header) + INDEX_TABLE_TILES, then the tiles.
        var hw = new BitWriter();
        writeImageHeader(hw);
        writePlaneHeader(hw);
        WriteIndexTable(hw, tileBytes);
        return Concat(hw.ToArray(), tileBytes);
    }

    // Cumulative MB boundaries [0, …, totalMb] from per-tile sizes that EXCLUDE the last tile (its
    // extent is implicit; T.832 §8.3.10). Result length = numTiles + 1.
    private static int[] TileBoundaries(int[] sizesExclLast, int totalMb)
    {
        var b = new int[sizesExclLast.Length + 2];
        int acc = 0;
        for (var i = 0; i < sizesExclLast.Length; i++) { b[i] = acc; acc += sizesExclLast[i]; }
        b[sizesExclLast.Length] = acc;
        b[sizesExclLast.Length + 1] = totalMb;
        return b;
    }

    private static void WritePacketHeaderTile(BitWriter w, int tileIndex)
    {
        // jxrlib writePacketHeader: 00 00 01 [(pID<<3)|type]; SPATIAL type=0, pID = tileIndex & 0x1F.
        int pID = tileIndex & 0x1F;
        w.WriteBits(0, 8); w.WriteBits(0, 8); w.WriteBits(1, 8); w.WriteBits((uint)(pID << 3), 8);
    }

    private static void WriteImageHeaderTiled(BitWriter w, int width, int height, int overlap,
                                              JxrTileLayout layout, JxrOutputColorFormat clrFmt, JxrOutputBitDepth bitDepth)
    {
        int mbW = MbCount(width), mbH = MbCount(height);
        var ih = new ImageHeader
        {
            HardTilingFlag = false,            // SOFT tiles (jxrlib default): overlap spans tile edges
            TilingFlag = true,
            FrequencyModeCodestreamFlag = false,
            SpatialXfrmSubordinate = 0,
            IndexTablePresentFlag = true,      // required for multi-tile (strenc.c StrIOEncInit)
            OverlapMode = overlap,
            ShortHeaderFlag = mbW <= 255 && mbH <= 255,
            LongWordFlag = true,
            WindowingFlag = false,
            TrimFlexBitsFlag = false,
            RedBlueNotSwappedFlag = false,
            PremultipliedAlphaFlag = false,
            AlphaImagePlaneFlag = false,
            OutputClrFmt = clrFmt,
            OutputBitDepth = bitDepth,
            WidthMinus1 = (uint)(width - 1),
            HeightMinus1 = (uint)(height - 1),
            NumVerTilesMinus1 = layout.NumVerTiles - 1,
            NumHorTilesMinus1 = layout.NumHorTiles - 1,
            TileWidthInMb = layout.TileWidthInMb,
            TileHeightInMb = layout.TileHeightInMb,
        };
        ih.Write(w);
    }

    // INDEX_TABLE_TILES (T.832 §8.7.1.3): start code 0x0001, one cumulative-byte-offset entry per tile
    // (raster order, first entry = 0), then jxrlib's single 0xFF end escape — read back as a vlw_esc
    // "subsequent bytes = 0", so NO PROFILE_LEVEL_INFO block follows (unlike the single-tile
    // writeIndexTableNull path). All tiles here are far larger than MINIMUM_PACKET_LENGTH, so every
    // entry uses the plain (escByte 0) vlw_esc form.
    private static void WriteIndexTable(BitWriter w, byte[][] tileBytes)
    {
        w.WriteBits(IndexTableTiles.IndexTableStartCode, 16);
        long cum = 0;
        foreach (byte[] t in tileBytes) { WriteVlwEsc(w, cum); cum += t.Length; }
        w.WriteBits(0xFF, 8); // PutVLWordEsc(0xff, 0) end escape
        FillToByte(w);
    }

    private static byte[] Concat(byte[] header, byte[][] parts)
    {
        int total = header.Length;
        foreach (byte[] p in parts) total += p.Length;
        var outBuf = new byte[total];
        int pos = 0;
        Array.Copy(header, 0, outBuf, pos, header.Length); pos += header.Length;
        foreach (byte[] p in parts) { Array.Copy(p, 0, outBuf, pos, p.Length); pos += p.Length; }
        return outBuf;
    }

    // Multi-tile SPATIAL decode (inverse of EncodeMultiTile): consume INDEX_TABLE_TILES, then decode
    // each tile in raster order from its own packet — a fresh CodingContext + TileCoder over the tile's
    // MB sub-rectangle in LOCAL coordinates — dequantizing into the whole-image YUV planes. Tiles are
    // byte-aligned, so align the reader before each packet header. The whole-image inverse overlap runs
    // in the caller afterwards (soft tiling — the overlap spans tile edges).
    private static void DecodeTilesIntoPlanes(
        ref BitReader reader, ImageHeader ih, int[][] planes, int mbCols, int mbRows,
        JxrQuantizer qDc, JxrQuantizer qLp, JxrQuantizer qHp, JxrQuantizer qDcUV, JxrQuantizer qLpUV,
        ColorFormat cf = ColorFormat.Yuv444, int channels = 3)
    {
        int[] tileX = TileBoundaries(ih.TileWidthInMb, mbCols);
        int[] tileY = TileBoundaries(ih.TileHeightInMb, mbRows);
        int numCols = tileX.Length - 1, numRows = tileY.Length - 1;

        // INDEX_TABLE_TILES: start code 0x0001, one cumulative-offset entry per tile, then the 0xFF end
        // escape (a vlw_esc that reads back as 0 ⇒ no PROFILE_LEVEL_INFO). We decode sequentially, so
        // the offsets themselves are unused. The plane header left the reader byte-aligned.
        uint startCode = reader.ReadBits(16);
        if (startCode != IndexTableTiles.IndexTableStartCode)
            throw new InvalidDataException($"INDEX_TABLE_TILES start code mismatch: got 0x{startCode:X4}.");
        for (var i = 0; i < numCols * numRows; i++) ReadVlwEsc(ref reader);
        ReadVlwEsc(ref reader); // 0xFF end escape
        AlignToByte(ref reader);

        for (var tr = 0; tr < numRows; tr++)
            for (var tc = 0; tc < numCols; tc++)
            {
                ReadPacketHeader(ref reader);
                int tmbW = tileX[tc + 1] - tileX[tc], tmbH = tileY[tr + 1] - tileY[tr];
                var ctx = new CodingContext(cf, channels);
                var tile = new TileCoder(tmbW, channels, cf);
                for (var ly = 0; ly < tmbH; ly++)
                {
                    for (var lx = 0; lx < tmbW; lx++)
                    {
                        var block = new Macroblock(channels);
                        tile.DecodeMacroblock(ctx, block, lx, ly, ref reader, ref reader, ref reader, ref reader, qHp.Qp);
                        int baseOff = OverlapTransform.MbBase(mbCols, tileY[tr] + ly, tileX[tc] + lx);
                        for (var ch = 0; ch < channels; ch++)
                            SignalTransform.DequantizeRestore(block, ch, planes[ch], baseOff,
                                                              ch == 0 ? qDc.Qp : qDcUV.Qp, ch == 0 ? qLp.Qp : qLpUV.Qp);
                    }
                    tile.AdvanceRow();
                }
                AlignToByte(ref reader); // each tile is byte-aligned (FillToByte on encode)
            }
    }

    // Decode a single-tile FREQUENCY-mode YUV444 codestream into the whole-image planes. The four bands
    // live in four separate packets (DC/LP/HP/FLEXBITS) located by the index table; we open one reader
    // per band (past its 4-byte 00 00 01 [type] packet header) and run the SAME per-MB entropy decode
    // as SPATIAL, just sourcing each band from its own reader. `padded` is the whole codestream buffer;
    // `reader` is positioned at the index table (right after the plane header). NO_FLEXBITS ⇒ no FLEX packet.
    private static void DecodeFrequencyIntoPlanes(ref BitReader reader, byte[] padded, int[][] planes,
        int mbCols, int mbRows, bool noFlexBits, int[] dcQp, int[] lpQp, int[] hpQp)
    {
        int cSB = noFlexBits ? 3 : 4;
        uint startCode = reader.ReadBits(16);
        if (startCode != IndexTableTiles.IndexTableStartCode)
            throw new InvalidDataException($"FREQUENCY index table start code mismatch: got 0x{startCode:X4}.");
        var off = new long[cSB];
        var empty = new bool[cSB];
        for (var k = 0; k < cSB; k++) off[k] = ReadFreqIndexEntry(ref reader, out empty[k]); // cumulative offsets; 0xFF ⇒ empty band
        ReadVlwEsc(ref reader); // 0xFF end escape
        AlignToByte(ref reader);
        int headerSize = reader.BytePosition; // band packets begin here; off[] is relative to it

        // One reader per band, positioned past its 4-byte packet header. An empty (header-only, omitted)
        // band — typically FLEXBITS on smooth images — is never read (mbits=0 ⇒ no flex), so leave its
        // reader at 0. FLEX is also absent under NO_FLEXBITS (cSB == 3).
        const int packetHeaderBytes = 4;
        BitReader Band(int k) { var br = new BitReader(padded); if (!empty[k]) br.SeekToByte(headerSize + (int)off[k] + packetHeaderBytes); return br; }
        var dcReader = Band(0);
        var lpReader = Band(1);
        var acReader = Band(2);
        var flReader = cSB > 3 ? Band(3) : new BitReader(padded);

        var ctx = new CodingContext(ColorFormat.Yuv444, 3) { NoFlexBits = noFlexBits };
        var tile = new TileCoder(mbCols);
        for (var mbR = 0; mbR < mbRows; mbR++)
        {
            for (var mbC = 0; mbC < mbCols; mbC++)
            {
                var block = new Macroblock(3);
                tile.DecodeMacroblock(ctx, block, mbC, mbR, ref dcReader, ref lpReader, ref acReader, ref flReader, hpQp[0], hpQp);
                int baseOff = OverlapTransform.MbBase(mbCols, mbR, mbC);
                for (var ch = 0; ch < 3; ch++)
                    SignalTransform.DequantizeRestore(block, ch, planes[ch], baseOff, dcQp[ch], lpQp[ch]);
            }
            tile.AdvanceRow();
        }
    }

    /// <summary>
    /// Encode a <paramref name="width"/>×<paramref name="height"/> BD8 grayscale image
    /// (<c>width*height</c> samples, raster order, values 0..255) into a single-tile
    /// SPATIAL <b>Y-only</b> JXR codestream. No colour transform — the single channel is
    /// the Y plane. Arbitrary dimensions are allowed (partial MBs edge-replicated); QP indices are 0 for lossless.
    /// </summary>
    public static byte[] EncodeGray(ReadOnlySpan<int> y, int width, int height,
                                    int qpDc = 0, int qpLp = 0, int qpHp = 0, int overlap = 0,
                                    JxrOutputBitDepth bd = JxrOutputBitDepth.Bd8, bool noFlexBits = false,
                                    JxrTileLayout? tiles = null)
    {
        RequirePositiveDims(width, height);
        int mbCols = MbCount(width), mbRows = MbCount(height);
        // NO_FLEXBITS (sbSubband != SB_ALL) forces scaled-arith for BD8/BD16/BD16S; BD32* is forced
        // non-scaled unconditionally (strenc.c:962) — its lossy QP uses the non-scaled quantizer.
        bool scaled = (ScaledArith(qpDc, qpLp, qpHp) || noFlexBits) && bd != JxrOutputBitDepth.Bd32S;
        var (qDc, qLp, qHp) = Quantizers(qpDc, qpLp, qpHp, scaled);
        int bias = LumaBias(bd);
        int shift = scaled ? SignalTransform.ScaledShift : 0; // <<3 scaled-arith input scaling (lossy QP / NO_FLEXBITS)
        var bands = noFlexBits ? JxrBandsPresent.NoFlexbits : JxrBandsPresent.AllBands;

        var planes = OverlapTransform.AllocatePlanes(mbCols, mbRows, 1);
        var my = new int[256];
        for (var mbR = 0; mbR < mbRows; mbR++)
            for (var mbC = 0; mbC < mbCols; mbC++)
            {
                ExtractMbGray(y, width, height, mbR, mbC, my);
                SignalTransform.LoadGray(my, planes[0], OverlapTransform.MbBase(mbCols, mbR, mbC), bias, shift);
            }

        if (tiles is { } layout && (layout.NumVerTiles > 1 || layout.NumHorTiles > 1))
            return EncodeMultiTileCore(planes, mbCols, mbRows, layout, overlap, scaled, ColorFormat.YOnly, 1,
                qDc, qLp, qHp, qDc, qLp,
                hw => WriteImageHeaderTiled(hw, width, height, overlap, layout, JxrOutputColorFormat.YOnly, bd),
                hw => WritePlaneHeaderGray(hw, qpDc, qpLp, qpHp, scaled, bd, bands: bands));

        var w = new BitWriter();
        WriteImageHeader(w, width, height, overlap, JxrOutputColorFormat.YOnly, bd);
        WritePlaneHeaderGray(w, qpDc, qpLp, qpHp, scaled, bd, bands: bands);
        WriteProfileLevelInfo(w);
        WritePacketHeader(w);

        OverlapTransform.Forward(planes, mbCols, mbRows, overlap, scaled);

        var ctx = new CodingContext(ColorFormat.YOnly, 1) { NoFlexBits = noFlexBits };
        var tile = new TileCoder(mbCols, 1, ColorFormat.YOnly);
        for (var mbR = 0; mbR < mbRows; mbR++)
        {
            for (var mbC = 0; mbC < mbCols; mbC++)
            {
                var block = new Macroblock(1);
                int baseOff = OverlapTransform.MbBase(mbCols, mbR, mbC);
                SignalTransform.QuantizeExtract(planes[0], baseOff, block, 0, qDc, qLp, qHp);
                tile.EncodeMacroblock(ctx, block, mbC, mbR, w, w, w, w);
            }
            tile.AdvanceRow();
        }

        FillToByte(w);
        return w.ToArray();
    }

    /// <summary>
    /// Encode a <paramref name="width"/>×<paramref name="height"/> <b>BD32F</b> grayscale image
    /// (<c>width*height</c> floats, raster order, values verbatim — NOT normalized) into a single-
    /// tile SPATIAL Y-only JXR codestream. <paramref name="lenMantissa"/> (mantissa bits, jxrlib
    /// default 13) and <paramref name="expBias"/> (exponent bias, jxrlib default 0) parameterize
    /// the float↔pixel mapping and are written to the plane header. Arbitrary dimensions are
    /// allowed (partial MBs edge-replicated); QP indices are 0 for lossless (the codec is lossless on the float-pixel values).
    /// </summary>
    public static byte[] EncodeGrayF32(ReadOnlySpan<float> y, int width, int height,
                                       int lenMantissa = 13, int expBias = 4,
                                       int qpDc = 0, int qpLp = 0, int qpHp = 0, int overlap = 0,
                                       bool noFlexBits = false, JxrTileLayout? tiles = null)
    {
        RequirePositiveDims(width, height);
        int mbCols = MbCount(width), mbRows = MbCount(height);
        // BD32* forces bScaledArith FALSE unconditionally (strenc.c:962) — even with NO_FLEXBITS or
        // lossy QP. So BD32F lossy uses the non-scaled quantizer and no <<cShift input scaling.
        bool scaled = false;
        var (qDc, qLp, qHp) = Quantizers(qpDc, qpLp, qpHp, scaled);
        var bands = noFlexBits ? JxrBandsPresent.NoFlexbits : JxrBandsPresent.AllBands;

        var planes = OverlapTransform.AllocatePlanes(mbCols, mbRows, 1);
        var my = new float[256];
        for (var mbR = 0; mbR < mbRows; mbR++)
            for (var mbC = 0; mbC < mbCols; mbC++)
            {
                ExtractMbGrayF(y, width, height, mbR, mbC, my);
                SignalTransform.LoadGrayFloat(my, planes[0], OverlapTransform.MbBase(mbCols, mbR, mbC), expBias, lenMantissa);
            }

        if (tiles is { } layout && (layout.NumVerTiles > 1 || layout.NumHorTiles > 1))
            return EncodeMultiTileCore(planes, mbCols, mbRows, layout, overlap, scaled, ColorFormat.YOnly, 1,
                qDc, qLp, qHp, qDc, qLp,
                hw => WriteImageHeaderTiled(hw, width, height, overlap, layout, JxrOutputColorFormat.YOnly, JxrOutputBitDepth.Bd32F),
                hw => WritePlaneHeaderGray(hw, qpDc, qpLp, qpHp, scaled, JxrOutputBitDepth.Bd32F, lenMantissa, expBias, bands));

        var w = new BitWriter();
        WriteImageHeader(w, width, height, overlap, JxrOutputColorFormat.YOnly, JxrOutputBitDepth.Bd32F);
        WritePlaneHeaderGray(w, qpDc, qpLp, qpHp, scaled, JxrOutputBitDepth.Bd32F, lenMantissa, expBias, bands);
        WriteProfileLevelInfo(w);
        WritePacketHeader(w);

        OverlapTransform.Forward(planes, mbCols, mbRows, overlap, scaled);

        var ctx = new CodingContext(ColorFormat.YOnly, 1) { NoFlexBits = noFlexBits };
        var tile = new TileCoder(mbCols, 1, ColorFormat.YOnly);
        for (var mbR = 0; mbR < mbRows; mbR++)
        {
            for (var mbC = 0; mbC < mbCols; mbC++)
            {
                var block = new Macroblock(1);
                int baseOff = OverlapTransform.MbBase(mbCols, mbR, mbC);
                SignalTransform.QuantizeExtract(planes[0], baseOff, block, 0, qDc, qLp, qHp);
                tile.EncodeMacroblock(ctx, block, mbC, mbR, w, w, w, w);
            }
            tile.AdvanceRow();
        }

        FillToByte(w);
        return w.ToArray();
    }

    /// <summary>
    /// Decode a single-tile SPATIAL OL_NONE YUV444 BD8 JXR codestream back into a BD8 RGB
    /// image. Dimensions and per-band QP indices are read from the codestream headers.
    /// </summary>
    public static (int width, int height, int[] r, int[] g, int[] b) Decode(ReadOnlySpan<byte> codestream)
    {
        // jxrlib's BitIO always has a zero-filled packet buffer beyond the coded
        // bytes, so the entropy decoder's speculative peeks (VLC root, abs-level
        // escape) never read past valid memory. Mirror that with trailing zero
        // slack so the final macroblock's peeks don't overrun a tight buffer.
        var padded = new byte[codestream.Length + EndPeekSlackBytes];
        codestream.CopyTo(padded);
        var reader = new BitReader(padded);
        var ih = ImageHeader.Read(ref reader);
        if (ih.OverlapMode > 2)
            throw new NotSupportedException($"JxrCodestream decodes OL_NONE / OL_ONE / OL_TWO only (got overlap mode {ih.OverlapMode}).");
        int overlap = ih.OverlapMode;

        int width = (int)ih.WidthMinus1 + 1, height = (int)ih.HeightMinus1 + 1;
        RequirePositiveDims(width, height);
        var bd = ih.OutputBitDepth;
        int bias = LumaBias(bd), max = SampleMax(bd);
        var (clrFmt, dcIdx, lpIdx, hpIdx, scaled, bands) = ReadPlaneHeader(ref reader, bd);

        // Uniform (channel-0) quantizers drive the chroma-subsampled + multi-tile paths unchanged; the
        // single-tile YUV444 loop below uses the full per-channel arrays (distinct U/V from quality mode).
        var (qDc, qLp, qHp) = Quantizers(dcIdx[0], lpIdx[0], hpIdx[0], scaled);
        var (qDcUV, qLpUV) = ChromaDcLpQuantizers(dcIdx[0], lpIdx[0], scaled); // half-step chroma DC/LP in scaled mode
        int mbCols = MbCount(width), mbRows = MbCount(height);
        if (clrFmt is JxrInternalColorFormat.YUV420 or JxrInternalColorFormat.YUV422)
        {
            if (ih.TilingFlag) throw new NotSupportedException("Multi-tile YUV420/422 decode is not yet supported.");
            if (bands != JxrBandsPresent.AllBands) throw new NotSupportedException("YUV420/422 decode supports all-bands codestreams only.");
            return DecodeChroma(ref reader, clrFmt, mbCols, mbRows, width, height, bias, max, overlap, scaled, qDc, qLp, qHp, qDcUV, qLpUV);
        }
        bool noFlexBits = bands == JxrBandsPresent.NoFlexbits;
        int outShift = scaled ? SignalTransform.ScaledShift : 0; // scaled-arith output >>3 (e.g. NO_FLEXBITS BD8/16)
        var (r, g, b) = (new int[width * height], new int[width * height], new int[width * height]);

        // Per-MB entropy decode + dequantize into the whole-image YUV planes, then
        // run the inverse overlap + 2-stage PCT across the grid, then color-store.
        var planes = OverlapTransform.AllocatePlanes(mbCols, mbRows);
        var (dcQp, lpQp, hpQp) = PerChannelQp(dcIdx, lpIdx, hpIdx, scaled, 3); // distinct Y/U/V (quality mode)
        if (ih.TilingFlag)
        {
            DecodeTilesIntoPlanes(ref reader, ih, planes, mbCols, mbRows, qDc, qLp, qHp, qDcUV, qLpUV);
        }
        else if (ih.FrequencyModeCodestreamFlag)
        {
            DecodeFrequencyIntoPlanes(ref reader, padded, planes, mbCols, mbRows, noFlexBits, dcQp, lpQp, hpQp);
        }
        else
        {
            ReadProfileLevelInfo(ref reader);
            int trim = ReadPacketHeader(ref reader, ih.TrimFlexBitsFlag);
            var ctx = new CodingContext(ColorFormat.Yuv444, 3) { TrimFlexBits = trim, NoFlexBits = noFlexBits };
            var tile = new TileCoder(mbCols);
            for (var mbR = 0; mbR < mbRows; mbR++)
            {
                for (var mbC = 0; mbC < mbCols; mbC++)
                {
                    var block = new Macroblock(3);
                    // SPATIAL: alias one reader to all four band slots (BitReader is a ref struct).
                    tile.DecodeMacroblock(ctx, block, mbC, mbR, ref reader, ref reader, ref reader, ref reader, hpQp[0], hpQp);
                    int baseOff = OverlapTransform.MbBase(mbCols, mbR, mbC);
                    for (var ch = 0; ch < 3; ch++)
                        SignalTransform.DequantizeRestore(block, ch, planes[ch], baseOff, dcQp[ch], lpQp[ch]);
                }
                tile.AdvanceRow();
            }
        }

        OverlapTransform.Inverse(planes, mbCols, mbRows, overlap, scaled);

        var (mr, mg, mb) = (new int[256], new int[256], new int[256]);
        for (var mbR = 0; mbR < mbRows; mbR++)
            for (var mbC = 0; mbC < mbCols; mbC++)
            {
                int baseOff = OverlapTransform.MbBase(mbCols, mbR, mbC);
                SignalTransform.StoreColor(planes[0], planes[1], planes[2], baseOff, mr, mg, mb, bias, max, outShift,
                                           bd16Round: bd == JxrOutputBitDepth.Bd16, min: SampleMin(bd));
                StoreMb(r, g, b, width, height, mbR, mbC, mr, mg, mb);
            }
        return (width, height, r, g, b);
    }

    // Single-tile SPATIAL YUV420/422 BD8 RGB decode. Luma is a full 444-style plane through the
    // OverlapTransform; chroma is decoded into reduced (64/128 per MB) planes, inverse-transformed
    // per-MB (OL_NONE), upsampled with interpolateUV into slack planes aligned with luma, then
    // colour-stored. OL_ONE/OL_TWO chroma (POT overlap on the reduced grid) is a separate rung.
    private static (int width, int height, int[] r, int[] g, int[] b) DecodeChroma(
        ref BitReader reader, JxrInternalColorFormat clrFmt, int mbCols, int mbRows,
        int width, int height, int bias, int max, int overlap, bool scaled,
        JxrQuantizer qDc, JxrQuantizer qLp, JxrQuantizer qHp, JxrQuantizer qDcUV, JxrQuantizer qLpUV)
    {
        var cf = clrFmt == JxrInternalColorFormat.YUV420 ? ColorFormat.Yuv420 : ColorFormat.Yuv422;
        int chromaBlocks = MacroblockLayout.ChromaBlocks(cf);

        ReadProfileLevelInfo(ref reader);
        ReadPacketHeader(ref reader);

        var luma = OverlapTransform.AllocatePlanes(mbCols, mbRows, 1)[0]; // 256/MB slack plane
        var full = OverlapTransform.AllocatePlanes(mbCols, mbRows, 2);    // upsampled chroma U,V (slack)
        // Reduced-chroma U,V on the slacked ring grid (64/128 ints per MB) — the layout the
        // chroma POT windowed inverse indexes across MB boundaries; harmless for OL_NONE too.
        var reduced = ChromaOverlapTransform.AllocatePlanes(mbCols, mbRows, cf);
        var (reducedU, reducedV) = (reduced[0], reduced[1]);

        var ctx = new CodingContext(cf, 3);
        var tile = new TileCoder(mbCols, 3, cf);
        for (var mbR = 0; mbR < mbRows; mbR++)
        {
            for (var mbC = 0; mbC < mbCols; mbC++)
            {
                var block = new Macroblock(3, chromaBlocks);
                tile.DecodeMacroblock(ctx, block, mbC, mbR, ref reader, ref reader, ref reader, ref reader, qHp.Qp);
                int lumaBase = OverlapTransform.MbBase(mbCols, mbR, mbC);
                int reducedBase = ChromaOverlapTransform.MbBase(mbCols, mbR, mbC, cf);
                SignalTransform.DequantizeRestore(block, 0, luma, lumaBase, qDc.Qp, qLp.Qp);
                SignalTransform.DequantizeRestoreChroma(block, 1, reducedU, reducedBase, qDcUV.Qp, qLpUV.Qp, cf);
                SignalTransform.DequantizeRestoreChroma(block, 2, reducedV, reducedBase, qDcUV.Qp, qLpUV.Qp, cf);
            }
            tile.AdvanceRow();
        }

        OverlapTransform.Inverse(new[] { luma }, mbCols, mbRows, overlap, scaled);
        if (overlap == 0)
            for (var mbR = 0; mbR < mbRows; mbR++)
                for (var mbC = 0; mbC < mbCols; mbC++)
                {
                    int reducedBase = ChromaOverlapTransform.MbBase(mbCols, mbR, mbC, cf);
                    ChromaTransform.InverseMbNoOverlap(reducedU, reducedBase, cf);
                    ChromaTransform.InverseMbNoOverlap(reducedV, reducedBase, cf);
                }
        else
            ChromaOverlapTransform.Inverse(reduced, mbCols, mbRows, overlap, cf);

        ChromaUpsample.Interpolate(cf, reducedU, reducedV, full[0], full[1], mbCols, mbRows,
                                   (mbCols + 1) * 256, ChromaOverlapTransform.RowStride(mbCols, cf));

        var (r, g, b) = (new int[width * height], new int[width * height], new int[width * height]);
        var (mr, mg, mb) = (new int[256], new int[256], new int[256]);
        for (var mbR = 0; mbR < mbRows; mbR++)
            for (var mbC = 0; mbC < mbCols; mbC++)
            {
                int baseOff = OverlapTransform.MbBase(mbCols, mbR, mbC);
                // YUV420/422 always decode in scaled-arithmetic mode (scaled == true): the transform
                // ran on <<3-scaled input, so the output is shifted back down by ScaledShift.
                SignalTransform.StoreColor(luma, full[0], full[1], baseOff, mr, mg, mb, bias, max,
                                           scaled ? SignalTransform.ScaledShift : 0);
                StoreMb(r, g, b, width, height, mbR, mbC, mr, mg, mb);
            }
        return (width, height, r, g, b);
    }

    /// <summary>
    /// Decode a single-tile SPATIAL Y-only BD8 JXR codestream back into a BD8 grayscale
    /// image. Dimensions and per-band QP indices are read from the codestream headers.
    /// </summary>
    public static (int width, int height, int[] y) DecodeGray(ReadOnlySpan<byte> codestream)
    {
        var padded = new byte[codestream.Length + EndPeekSlackBytes];
        codestream.CopyTo(padded);
        var reader = new BitReader(padded);
        var ih = ImageHeader.Read(ref reader);
        if (ih.FrequencyModeCodestreamFlag)
            throw new NotSupportedException("JxrCodestream decodes SPATIAL codestreams only.");
        if (ih.OverlapMode > 2)
            throw new NotSupportedException($"JxrCodestream decodes OL_NONE / OL_ONE / OL_TWO only (got overlap mode {ih.OverlapMode}).");
        int overlap = ih.OverlapMode;

        int width = (int)ih.WidthMinus1 + 1, height = (int)ih.HeightMinus1 + 1;
        RequirePositiveDims(width, height);
        var bd = ih.OutputBitDepth;
        int bias = LumaBias(bd), max = SampleMax(bd);
        var (qpDc, qpLp, qpHp, scaled, _, _, bands) = ReadPlaneHeaderGray(ref reader, bd);

        var (qDc, qLp, qHp) = Quantizers(qpDc, qpLp, qpHp, scaled);
        int mbCols = MbCount(width), mbRows = MbCount(height);
        int outShift = scaled ? SignalTransform.ScaledShift : 0; // scaled-arith output >>3 (lossy QP / NO_FLEXBITS)
        var y = new int[width * height];

        var planes = OverlapTransform.AllocatePlanes(mbCols, mbRows, 1);
        if (ih.TilingFlag)
        {
            DecodeTilesIntoPlanes(ref reader, ih, planes, mbCols, mbRows, qDc, qLp, qHp, qDc, qLp,
                                  ColorFormat.YOnly, 1);
        }
        else
        {
            ReadProfileLevelInfo(ref reader);
            ReadPacketHeader(ref reader);
            var ctx = new CodingContext(ColorFormat.YOnly, 1) { NoFlexBits = bands == JxrBandsPresent.NoFlexbits };
            var tile = new TileCoder(mbCols, 1, ColorFormat.YOnly);
            for (var mbR = 0; mbR < mbRows; mbR++)
            {
                for (var mbC = 0; mbC < mbCols; mbC++)
                {
                    var block = new Macroblock(1);
                    tile.DecodeMacroblock(ctx, block, mbC, mbR, ref reader, ref reader, ref reader, ref reader, qHp.Qp);
                    int baseOff = OverlapTransform.MbBase(mbCols, mbR, mbC);
                    SignalTransform.DequantizeRestore(block, 0, planes[0], baseOff, qDc.Qp, qLp.Qp);
                }
                tile.AdvanceRow();
            }
        }

        OverlapTransform.Inverse(planes, mbCols, mbRows, overlap, scaled);

        var my = new int[256];
        for (var mbR = 0; mbR < mbRows; mbR++)
            for (var mbC = 0; mbC < mbCols; mbC++)
            {
                int baseOff = OverlapTransform.MbBase(mbCols, mbR, mbC);
                SignalTransform.StoreGray(planes[0], baseOff, my, bias, max, outShift,
                                          bd16Round: bd == JxrOutputBitDepth.Bd16, min: SampleMin(bd));
                StoreMbGray(y, width, height, mbR, mbC, my);
            }
        return (width, height, y);
    }

    /// <summary>
    /// Decode a single-tile SPATIAL Y-only <b>BD32F</b> JXR codestream back into a grayscale float
    /// image. Dimensions, per-band QP, and the float mapping (LEN_MANTISSA / EXP_BIAS) are read
    /// from the codestream headers.
    /// </summary>
    public static (int width, int height, float[] y) DecodeGrayF32(ReadOnlySpan<byte> codestream)
    {
        var padded = new byte[codestream.Length + EndPeekSlackBytes];
        codestream.CopyTo(padded);
        var reader = new BitReader(padded);
        var ih = ImageHeader.Read(ref reader);
        if (ih.FrequencyModeCodestreamFlag)
            throw new NotSupportedException("JxrCodestream decodes SPATIAL codestreams only.");
        if (ih.OverlapMode > 2)
            throw new NotSupportedException($"JxrCodestream decodes OL_NONE / OL_ONE / OL_TWO only (got overlap mode {ih.OverlapMode}).");
        int overlap = ih.OverlapMode;

        int width = (int)ih.WidthMinus1 + 1, height = (int)ih.HeightMinus1 + 1;
        RequirePositiveDims(width, height);
        var (qpDc, qpLp, qpHp, scaled, lenMantissa, expBias, bands) = ReadPlaneHeaderGray(ref reader, ih.OutputBitDepth);

        var (qDc, qLp, qHp) = Quantizers(qpDc, qpLp, qpHp, scaled);
        int mbCols = MbCount(width), mbRows = MbCount(height);
        var y = new float[width * height];

        var planes = OverlapTransform.AllocatePlanes(mbCols, mbRows, 1);
        if (ih.TilingFlag)
        {
            DecodeTilesIntoPlanes(ref reader, ih, planes, mbCols, mbRows, qDc, qLp, qHp, qDc, qLp,
                                  ColorFormat.YOnly, 1);
        }
        else
        {
            ReadProfileLevelInfo(ref reader);
            ReadPacketHeader(ref reader);
            var ctx = new CodingContext(ColorFormat.YOnly, 1) { NoFlexBits = bands == JxrBandsPresent.NoFlexbits };
            var tile = new TileCoder(mbCols, 1, ColorFormat.YOnly);
            for (var mbR = 0; mbR < mbRows; mbR++)
            {
                for (var mbC = 0; mbC < mbCols; mbC++)
                {
                    var block = new Macroblock(1);
                    tile.DecodeMacroblock(ctx, block, mbC, mbR, ref reader, ref reader, ref reader, ref reader, qHp.Qp);
                    int baseOff = OverlapTransform.MbBase(mbCols, mbR, mbC);
                    SignalTransform.DequantizeRestore(block, 0, planes[0], baseOff, qDc.Qp, qLp.Qp);
                }
                tile.AdvanceRow();
            }
        }

        OverlapTransform.Inverse(planes, mbCols, mbRows, overlap, scaled);

        var my = new float[256];
        for (var mbR = 0; mbR < mbRows; mbR++)
            for (var mbC = 0; mbC < mbCols; mbC++)
            {
                int baseOff = OverlapTransform.MbBase(mbCols, mbR, mbC);
                SignalTransform.StoreGrayFloat(planes[0], baseOff, my, expBias, lenMantissa);
                StoreMbGrayF(y, width, height, mbR, mbC, my);
            }
        return (width, height, y);
    }

    /// <summary>
    /// Encode a <paramref name="width"/>×<paramref name="height"/> <b>BD16F</b> half-float grayscale
    /// image into a single-tile SPATIAL Y-only JXR codestream. The half is kept as its raw
    /// sign-magnitude bit pattern (no bias, no float params in the header), so the round-trip is
    /// bit-exact. Arbitrary dimensions are allowed (partial MBs edge-replicated); QP indices are 0 for lossless.
    /// </summary>
    public static byte[] EncodeGrayHalf(ReadOnlySpan<Half> y, int width, int height,
                                        int qpDc = 0, int qpLp = 0, int qpHp = 0, int overlap = 0,
                                        JxrTileLayout? tiles = null)
    {
        RequirePositiveDims(width, height);
        int mbCols = MbCount(width), mbRows = MbCount(height);
        bool scaled = ScaledArith(qpDc, qpLp, qpHp);
        var (qDc, qLp, qHp) = Quantizers(qpDc, qpLp, qpHp, scaled);
        int shift = scaled ? SignalTransform.ScaledShift : 0; // <<3 scaled-arith input scaling (lossy QP)

        var planes = OverlapTransform.AllocatePlanes(mbCols, mbRows, 1);
        var my = new Half[256];
        for (var mbR = 0; mbR < mbRows; mbR++)
            for (var mbC = 0; mbC < mbCols; mbC++)
            {
                ExtractMb1(y, width, height, mbR, mbC, my);
                SignalTransform.LoadGrayHalf(my, planes[0], OverlapTransform.MbBase(mbCols, mbR, mbC), shift);
            }

        if (tiles is { } layout && (layout.NumVerTiles > 1 || layout.NumHorTiles > 1))
            return EncodeMultiTileCore(planes, mbCols, mbRows, layout, overlap, scaled, ColorFormat.YOnly, 1,
                qDc, qLp, qHp, qDc, qLp,
                hw => WriteImageHeaderTiled(hw, width, height, overlap, layout, JxrOutputColorFormat.YOnly, JxrOutputBitDepth.Bd16F),
                hw => WritePlaneHeaderGray(hw, qpDc, qpLp, qpHp, scaled, JxrOutputBitDepth.Bd16F));

        var w = new BitWriter();
        WriteImageHeader(w, width, height, overlap, JxrOutputColorFormat.YOnly, JxrOutputBitDepth.Bd16F);
        WritePlaneHeaderGray(w, qpDc, qpLp, qpHp, scaled, JxrOutputBitDepth.Bd16F);
        WriteProfileLevelInfo(w);
        WritePacketHeader(w);

        OverlapTransform.Forward(planes, mbCols, mbRows, overlap, scaled);

        var ctx = new CodingContext(ColorFormat.YOnly, 1);
        var tile = new TileCoder(mbCols, 1, ColorFormat.YOnly);
        for (var mbR = 0; mbR < mbRows; mbR++)
        {
            for (var mbC = 0; mbC < mbCols; mbC++)
            {
                var block = new Macroblock(1);
                int baseOff = OverlapTransform.MbBase(mbCols, mbR, mbC);
                SignalTransform.QuantizeExtract(planes[0], baseOff, block, 0, qDc, qLp, qHp);
                tile.EncodeMacroblock(ctx, block, mbC, mbR, w, w, w, w);
            }
            tile.AdvanceRow();
        }

        FillToByte(w);
        return w.ToArray();
    }

    /// <summary>Decode a single-tile SPATIAL Y-only BD16F JXR codestream back into a half-float grayscale image.</summary>
    public static (int width, int height, Half[] y) DecodeGrayHalf(ReadOnlySpan<byte> codestream)
    {
        var padded = new byte[codestream.Length + EndPeekSlackBytes];
        codestream.CopyTo(padded);
        var reader = new BitReader(padded);
        var ih = ImageHeader.Read(ref reader);
        if (ih.FrequencyModeCodestreamFlag)
            throw new NotSupportedException("JxrCodestream decodes SPATIAL codestreams only.");
        if (ih.OverlapMode > 2)
            throw new NotSupportedException($"JxrCodestream decodes OL_NONE / OL_ONE / OL_TWO only (got overlap mode {ih.OverlapMode}).");
        int overlap = ih.OverlapMode;

        int width = (int)ih.WidthMinus1 + 1, height = (int)ih.HeightMinus1 + 1;
        RequirePositiveDims(width, height);
        var (qpDc, qpLp, qpHp, scaled, _, _, _) = ReadPlaneHeaderGray(ref reader, ih.OutputBitDepth);

        var (qDc, qLp, qHp) = Quantizers(qpDc, qpLp, qpHp, scaled);
        int mbCols = MbCount(width), mbRows = MbCount(height);
        int outShift = scaled ? SignalTransform.ScaledShift : 0; // scaled-arith output >>3 (lossy QP)
        var y = new Half[width * height];

        var planes = OverlapTransform.AllocatePlanes(mbCols, mbRows, 1);
        if (ih.TilingFlag)
        {
            DecodeTilesIntoPlanes(ref reader, ih, planes, mbCols, mbRows, qDc, qLp, qHp, qDc, qLp,
                                  ColorFormat.YOnly, 1);
        }
        else
        {
            ReadProfileLevelInfo(ref reader);
            ReadPacketHeader(ref reader);
            var ctx = new CodingContext(ColorFormat.YOnly, 1);
            var tile = new TileCoder(mbCols, 1, ColorFormat.YOnly);
            for (var mbR = 0; mbR < mbRows; mbR++)
            {
                for (var mbC = 0; mbC < mbCols; mbC++)
                {
                    var block = new Macroblock(1);
                    tile.DecodeMacroblock(ctx, block, mbC, mbR, ref reader, ref reader, ref reader, ref reader, qHp.Qp);
                    int baseOff = OverlapTransform.MbBase(mbCols, mbR, mbC);
                    SignalTransform.DequantizeRestore(block, 0, planes[0], baseOff, qDc.Qp, qLp.Qp);
                }
                tile.AdvanceRow();
            }
        }

        OverlapTransform.Inverse(planes, mbCols, mbRows, overlap, scaled);

        var my = new Half[256];
        for (var mbR = 0; mbR < mbRows; mbR++)
            for (var mbC = 0; mbC < mbCols; mbC++)
            {
                int baseOff = OverlapTransform.MbBase(mbCols, mbR, mbC);
                SignalTransform.StoreGrayHalf(planes[0], baseOff, my, outShift);
                StoreMb1(y, width, height, mbR, mbC, my);
            }
        return (width, height, y);
    }

    /// <summary>
    /// Encode a <paramref name="width"/>×<paramref name="height"/> <b>BD16F</b> half-float RGB image
    /// (three channels) into a single-tile SPATIAL YUV444 JXR codestream — YCoCg-R on the raw half
    /// magnitudes, bit-exact round-trip. Arbitrary dimensions are allowed (partial MBs edge-replicated); QP indices 0 for lossless.
    /// </summary>
    public static byte[] EncodeRgbHalf(ReadOnlySpan<Half> r, ReadOnlySpan<Half> g, ReadOnlySpan<Half> b,
                                       int width, int height, int qpDc = 0, int qpLp = 0, int qpHp = 0, int overlap = 0,
                                       bool noFlexBits = false, JxrTileLayout? tiles = null)
    {
        RequirePositiveDims(width, height);
        int mbCols = MbCount(width), mbRows = MbCount(height);
        // NO_FLEXBITS (sbSubband != SB_ALL) forces scaled-arith for BD16F (jxrlib strenc.c).
        bool scaled = ScaledArith(qpDc, qpLp, qpHp) || noFlexBits;
        var (qDc, qLp, qHp) = Quantizers(qpDc, qpLp, qpHp, scaled);
        var (qDcUV, qLpUV) = ChromaDcLpQuantizers(qpDc, qpLp, scaled);
        int shift = scaled ? SignalTransform.ScaledShift : 0;
        var bands = noFlexBits ? JxrBandsPresent.NoFlexbits : JxrBandsPresent.AllBands;

        var planes = OverlapTransform.AllocatePlanes(mbCols, mbRows);
        var (mr, mg, mb) = (new Half[256], new Half[256], new Half[256]);
        for (var mbR = 0; mbR < mbRows; mbR++)
            for (var mbC = 0; mbC < mbCols; mbC++)
            {
                ExtractMb1(r, width, height, mbR, mbC, mr);
                ExtractMb1(g, width, height, mbR, mbC, mg);
                ExtractMb1(b, width, height, mbR, mbC, mb);
                SignalTransform.LoadColorHalf(mr, mg, mb, planes[0], planes[1], planes[2],
                                              OverlapTransform.MbBase(mbCols, mbR, mbC), shift);
            }

        if (tiles is { } layout && (layout.NumVerTiles > 1 || layout.NumHorTiles > 1))
            return EncodeMultiTileCore(planes, mbCols, mbRows, layout, overlap, scaled, ColorFormat.Yuv444, 3,
                qDc, qLp, qHp, qDcUV, qLpUV,
                hw => WriteImageHeaderTiled(hw, width, height, overlap, layout, JxrOutputColorFormat.Rgb, JxrOutputBitDepth.Bd16F),
                hw => WritePlaneHeader(hw, qpDc, qpLp, qpHp, scaled, JxrOutputBitDepth.Bd16F, bands: bands));

        var w = new BitWriter();
        WriteImageHeader(w, width, height, overlap, JxrOutputColorFormat.Rgb, JxrOutputBitDepth.Bd16F);
        WritePlaneHeader(w, qpDc, qpLp, qpHp, scaled, JxrOutputBitDepth.Bd16F, bands: bands);
        WriteProfileLevelInfo(w);
        WritePacketHeader(w);

        OverlapTransform.Forward(planes, mbCols, mbRows, overlap, scaled);

        var ctx = new CodingContext(ColorFormat.Yuv444, 3) { NoFlexBits = noFlexBits };
        var tile = new TileCoder(mbCols);
        for (var mbR = 0; mbR < mbRows; mbR++)
        {
            for (var mbC = 0; mbC < mbCols; mbC++)
            {
                var block = new Macroblock(3);
                int baseOff = OverlapTransform.MbBase(mbCols, mbR, mbC);
                for (var ch = 0; ch < 3; ch++)
                    SignalTransform.QuantizeExtract(planes[ch], baseOff, block, ch,
                                                    ch == 0 ? qDc : qDcUV, ch == 0 ? qLp : qLpUV, qHp);
                tile.EncodeMacroblock(ctx, block, mbC, mbR, w, w, w, w);
            }
            tile.AdvanceRow();
        }

        FillToByte(w);
        return w.ToArray();
    }

    /// <summary>Decode a single-tile SPATIAL YUV444 BD16F JXR codestream back into half-float RGB channels.</summary>
    public static (int width, int height, Half[] r, Half[] g, Half[] b) DecodeRgbHalf(ReadOnlySpan<byte> codestream)
    {
        var padded = new byte[codestream.Length + EndPeekSlackBytes];
        codestream.CopyTo(padded);
        var reader = new BitReader(padded);
        var ih = ImageHeader.Read(ref reader);
        if (ih.FrequencyModeCodestreamFlag)
            throw new NotSupportedException("JxrCodestream decodes SPATIAL codestreams only.");
        if (ih.OverlapMode > 2)
            throw new NotSupportedException($"JxrCodestream decodes OL_NONE / OL_ONE / OL_TWO only (got overlap mode {ih.OverlapMode}).");
        int overlap = ih.OverlapMode;

        int width = (int)ih.WidthMinus1 + 1, height = (int)ih.HeightMinus1 + 1;
        RequirePositiveDims(width, height);
        var (clrFmt, dcIdx, lpIdx, hpIdx, scaled, bands) = ReadPlaneHeader(ref reader, ih.OutputBitDepth);
        if (clrFmt != JxrInternalColorFormat.YUV444)
            throw new NotSupportedException($"BD16F RGB decode supports YUV444 only (got {clrFmt}); chroma-subsampled BD16F is not yet wired.");

        var (qDc, qLp, qHp) = Quantizers(dcIdx[0], lpIdx[0], hpIdx[0], scaled);
        var (qDcUV, qLpUV) = ChromaDcLpQuantizers(dcIdx[0], lpIdx[0], scaled); // half-step chroma DC/LP in scaled mode
        int mbCols = MbCount(width), mbRows = MbCount(height);
        int outShift = scaled ? SignalTransform.ScaledShift : 0;
        var (r, g, b) = (new Half[width * height], new Half[width * height], new Half[width * height]);

        var planes = OverlapTransform.AllocatePlanes(mbCols, mbRows);
        if (ih.TilingFlag)
        {
            DecodeTilesIntoPlanes(ref reader, ih, planes, mbCols, mbRows, qDc, qLp, qHp, qDcUV, qLpUV);
        }
        else
        {
            ReadProfileLevelInfo(ref reader);
            ReadPacketHeader(ref reader);
            var ctx = new CodingContext(ColorFormat.Yuv444, 3) { NoFlexBits = bands == JxrBandsPresent.NoFlexbits };
            var tile = new TileCoder(mbCols);
            var (dcQp, lpQp, hpQp) = PerChannelQp(dcIdx, lpIdx, hpIdx, scaled, 3); // distinct Y/U/V (quality mode)
            for (var mbR = 0; mbR < mbRows; mbR++)
            {
                for (var mbC = 0; mbC < mbCols; mbC++)
                {
                    var block = new Macroblock(3);
                    tile.DecodeMacroblock(ctx, block, mbC, mbR, ref reader, ref reader, ref reader, ref reader, hpQp[0], hpQp);
                    int baseOff = OverlapTransform.MbBase(mbCols, mbR, mbC);
                    for (var ch = 0; ch < 3; ch++)
                        SignalTransform.DequantizeRestore(block, ch, planes[ch], baseOff, dcQp[ch], lpQp[ch]);
                }
                tile.AdvanceRow();
            }
        }

        OverlapTransform.Inverse(planes, mbCols, mbRows, overlap, scaled);

        var (mr, mg, mb) = (new Half[256], new Half[256], new Half[256]);
        for (var mbR = 0; mbR < mbRows; mbR++)
            for (var mbC = 0; mbC < mbCols; mbC++)
            {
                int baseOff = OverlapTransform.MbBase(mbCols, mbR, mbC);
                SignalTransform.StoreColorHalf(planes[0], planes[1], planes[2], baseOff, mr, mg, mb, outShift);
                StoreMb1(r, width, height, mbR, mbC, mr);
                StoreMb1(g, width, height, mbR, mbC, mg);
                StoreMb1(b, width, height, mbR, mbC, mb);
            }
        return (width, height, r, g, b);
    }

    // ---------------------------------------------------------------- headers

    private static void WriteImageHeader(BitWriter w, int width, int height, int overlap,
                                         JxrOutputColorFormat clrFmt = JxrOutputColorFormat.Rgb,
                                         JxrOutputBitDepth bitDepth = JxrOutputBitDepth.Bd8,
                                         int trimFlexBits = 0, bool frequencyMode = false)
    {
        int mbW = (width + 15) / 16, mbH = (height + 15) / 16;
        var ih = new ImageHeader
        {
            HardTilingFlag = false,
            TilingFlag = false,
            FrequencyModeCodestreamFlag = frequencyMode,   // SPATIAL unless frequency mode
            SpatialXfrmSubordinate = 0,
            IndexTablePresentFlag = frequencyMode,         // single-tile frequency needs the band index table
            OverlapMode = overlap,                          // OL_NONE / OL_ONE / OL_TWO
            ShortHeaderFlag = mbW <= 255 && mbH <= 255,     // jxrlib bAbbreviatedHeader
            LongWordFlag = true,                            // jxrlib always writes 1 here
            WindowingFlag = false,
            TrimFlexBitsFlag = trimFlexBits > 0,            // jxrlib bTrimFlexbitsFlag = (uiTrimFlexBits > 0)
            RedBlueNotSwappedFlag = false,
            PremultipliedAlphaFlag = false,
            AlphaImagePlaneFlag = false,
            OutputClrFmt = clrFmt,                          // CF_RGB = 7 / CF_YONLY = 0
            OutputBitDepth = bitDepth,                      // BD_8 = 1
            WidthMinus1 = (uint)(width - 1),
            HeightMinus1 = (uint)(height - 1),
        };
        ih.Write(w);
    }

    // Faithful port of jxrlib WriteImagePlaneHeader (strenc.c:748) for the
    // YUV444 / BD8 / all-bands / uQPMode==0x750 case: every band carries its own
    // plane-uniform quantizer in channel-mode INDEPENDENT (2), three equal QP indices.
    private static void WritePlaneHeader(BitWriter w, int qpDc, int qpLp, int qpHp, bool scaled, JxrOutputBitDepth bd,
                                         int lenMantissaOrShift = 0, int expBias = 0,
                                         JxrInternalColorFormat clrFmt = JxrInternalColorFormat.YUV444,
                                         JxrBandsPresent bands = JxrBandsPresent.AllBands)
    {
        w.WriteBits((uint)clrFmt, 3);                        // internal color format (YUV444 / YUV420 / YUV422)
        w.WriteBit(scaled);                                  // bScaledArith
        w.WriteBits((uint)bands, 4);                         // SB_ALL / SB_NO_FLEXBITS
        w.WriteBits(0, 4); w.WriteBits(0, 4);                // YUV: RESERVED_F, RESERVED_H
        WriteBitDepthParams(w, bd, lenMantissaOrShift, expBias); // SHIFT_BITS/LEN_MANTISSA+EXP_BIAS

        // DC: uniform flag = 1, then quantizer (chMode INDEPENDENT, 3 equal indices).
        w.WriteBit(true);
        WriteQuantizer(w, qpDc);

        // LP: USE_DC_QP flag = 0 (uQPMode|0x200 ⇒ own QP), uniform flag = 1, quantizer.
        w.WriteBit(false);
        w.WriteBit(true);
        WriteQuantizer(w, qpLp);

        // HP: USE_LP_QP flag = 0 (uQPMode|0x400 ⇒ own QP), uniform flag = 1, quantizer.
        w.WriteBit(false);
        w.WriteBit(true);
        WriteQuantizer(w, qpHp);

        FillToByte(w);
    }

    // Reads the YUV plane header into PER-CHANNEL QP index arrays (Y,U,V). Our encoder writes uniform
    // (all channels equal, LP/HP with their own QP), but a foreign file — notably anything from jxrlib's
    // quality mode (`-q 0.x`) — carries distinct U/V indices and may reuse the DC quantizer for LP and
    // the LP quantizer for HP (the USE_DC_QP / USE_LP_QP flags). We honour all of those so we can decode
    // real photographic .jxr. (DC_UNIFORM=0, i.e. per-tile/per-MB QP, is still unsupported.)
    private static (JxrInternalColorFormat clrFmt, int[] dcIdx, int[] lpIdx, int[] hpIdx, bool scaled, JxrBandsPresent bands) ReadPlaneHeader(ref BitReader r, JxrOutputBitDepth bd)
    {
        var clrFmt = (JxrInternalColorFormat)r.ReadBits(3);
        if (clrFmt is not (JxrInternalColorFormat.YUV444 or JxrInternalColorFormat.YUV420 or JxrInternalColorFormat.YUV422))
            throw new NotSupportedException($"JxrCodestream decodes YUV444 / YUV420 / YUV422 internal formats (got {clrFmt}).");
        bool scaled = r.ReadBit();
        var bands = (JxrBandsPresent)r.ReadBits(4);
        if (bands is not (JxrBandsPresent.AllBands or JxrBandsPresent.NoFlexbits))
            throw new NotSupportedException($"JxrCodestream decodes all-bands / no-flexbits codestreams only (got {bands}).");
        // 8 bits of colour params: 444 = RESERVED_F(4)+RESERVED_H(4); 422/420 pack CHROMA_CENTERING_X/Y
        // into the same 8 bits — the encoder writes zeros for all, so we just skip them.
        r.SkipBits(8);
        ReadBitDepthParams(ref r, bd); // SHIFT_BITS for BD16 (RGB int path ignores the value)

        if (!r.ReadBit()) throw new NotSupportedException("Non-uniform (per-tile) DC quantization not supported.");
        int[] dcIdx = ReadQuantizer3(ref r);

        int[] lpIdx;
        if (r.ReadBit()) lpIdx = dcIdx;                  // USE_DC_QP: LP reuses the DC quantizer
        else
        {
            if (!r.ReadBit()) throw new NotSupportedException("Non-uniform (per-tile) LP quantization not supported.");
            lpIdx = ReadQuantizer3(ref r);
        }

        int[] hpIdx;
        if (r.ReadBit()) hpIdx = lpIdx;                  // USE_LP_QP: HP reuses the LP quantizer
        else
        {
            if (!r.ReadBit()) throw new NotSupportedException("Non-uniform (per-tile) HP quantization not supported.");
            hpIdx = ReadQuantizer3(ref r);
        }

        AlignToByte(ref r);
        return (clrFmt, dcIdx, lpIdx, hpIdx, scaled, bands);
    }

    // jxrlib WriteImagePlaneHeader bit-depth params (strenc.c:777): BD16/BD16S write an 8-bit
    // SHIFT_BITS (the integer right-shift, 0 ⇒ full-precision lossless); BD32F writes LEN_MANTISSA(8)
    // then EXP_BIAS(8) for the custom-float mapping. BD8 and BD16F write nothing (BD16F's mantissa
    // length is implicit). `lenMantissaOrShift` is SHIFT_BITS for BD16 / LEN_MANTISSA for BD32F.
    private static void WriteBitDepthParams(BitWriter w, JxrOutputBitDepth bd, int lenMantissaOrShift, int expBias)
    {
        switch (bd)
        {
            case JxrOutputBitDepth.Bd16:
            case JxrOutputBitDepth.Bd16S:   // BD16/BD16S/BD32S all write an 8-bit SHIFT_BITS (strenc.c:778)
            case JxrOutputBitDepth.Bd32S:
                w.WriteBits((uint)lenMantissaOrShift, 8);
                break;
            case JxrOutputBitDepth.Bd32F:
                w.WriteBits((uint)lenMantissaOrShift, 8);  // LEN_MANTISSA
                w.WriteBits((byte)expBias, 8);             // EXP_BIAS (signed, low 8 bits)
                break;
        }
    }

    private static (int lenMantissaOrShift, int expBias) ReadBitDepthParams(ref BitReader r, JxrOutputBitDepth bd)
    {
        switch (bd)
        {
            case JxrOutputBitDepth.Bd16:
            case JxrOutputBitDepth.Bd16S:
            case JxrOutputBitDepth.Bd32S:
                return ((int)r.ReadBits(8), 0);
            case JxrOutputBitDepth.Bd32F:
                int lm = (int)r.ReadBits(8);
                int c = (sbyte)r.ReadBits(8);             // EXP_BIAS is signed
                return (lm, c);
            default:
                return (0, 0);
        }
    }

    // jxrlib writeQuantizer (strenc.c:59) for 3 channels in channel-mode INDEPENDENT (2):
    // PUTBITS(chMode,2) then Y, U, V QP indices. Our codec uses one QP per band, so all three equal.
    private static void WriteQuantizer(BitWriter w, int qpIndex)
    {
        w.WriteBits(2, 2);                       // cChMode = INDEPENDENT
        w.WriteBits((uint)qpIndex, 8);           // Y
        w.WriteBits((uint)qpIndex, 8);           // U
        w.WriteBits((uint)qpIndex, 8);           // V
    }

    // jxrlib readQuantizer for 3 channels: cChMode(2) then the per-channel QP indices. chMode 0
    // (UNIFORM) shares Y across U,V; chMode 1 (MIXED) writes one chroma index for both U,V; chMode 2
    // (INDEPENDENT) writes Y, U, V separately. Returns the resolved 3 per-channel indices — distinct
    // U/V indices are what jxrlib's quality mode (`-q 0.x`) emits from the DPK_QPS tables.
    private static int[] ReadQuantizer3(ref BitReader r)
    {
        int chMode = (int)r.ReadBits(2);
        int y = (int)r.ReadBits(8);
        if (chMode == 0) return [y, y, y];                                  // UNIFORM
        if (chMode == 1) { int uv = (int)r.ReadBits(8); return [y, uv, uv]; } // MIXED
        return [y, (int)r.ReadBits(8), (int)r.ReadBits(8)];                 // INDEPENDENT: Y, U, V
    }

    // Faithful port of WriteImagePlaneHeader for the YONLY / BD8 / all-bands / uQPMode==0x750
    // case. Differs from the YUV444 writer in two ways (strenc.c:772 default + writeQuantizer
    // cChannel==1): no RESERVED_F/RESERVED_H bytes, and each band's quantizer is just the 8-bit
    // Y index (writeQuantizer forces cChMode 0 and writes no channel-mode bits for one channel).
    private static void WritePlaneHeaderGray(BitWriter w, int qpDc, int qpLp, int qpHp, bool scaled, JxrOutputBitDepth bd,
                                             int lenMantissaOrShift = 0, int expBias = 0,
                                             JxrBandsPresent bands = JxrBandsPresent.AllBands)
    {
        w.WriteBits((uint)JxrInternalColorFormat.YOnly, 3); // internal color format = 0
        w.WriteBit(scaled);                                 // bScaledArith
        w.WriteBits((uint)bands, 4);                        // SB_ALL / SB_NO_FLEXBITS
        // YONLY: color-params switch hits default — no RESERVED_F / RESERVED_H.
        WriteBitDepthParams(w, bd, lenMantissaOrShift, expBias); // SHIFT_BITS/LEN_MANTISSA+EXP_BIAS

        w.WriteBit(true);                 // DC uniform
        w.WriteBits((uint)qpDc, 8);       // single-channel quantizer (Y index only)

        w.WriteBit(false);                // use DC QP? no (own LP QP)
        w.WriteBit(true);                 // LP uniform
        w.WriteBits((uint)qpLp, 8);

        w.WriteBit(false);                // use LP QP? no (own HP QP)
        w.WriteBit(true);                 // HP uniform
        w.WriteBits((uint)qpHp, 8);

        FillToByte(w);
    }

    private static (int qpDc, int qpLp, int qpHp, bool scaled, int lenMantissa, int expBias, JxrBandsPresent bands) ReadPlaneHeaderGray(ref BitReader r, JxrOutputBitDepth bd)
    {
        var clrFmt = (JxrInternalColorFormat)r.ReadBits(3);
        if (clrFmt != JxrInternalColorFormat.YOnly)
            throw new NotSupportedException($"JxrCodestream.DecodeGray expects YONLY internal format (got {clrFmt}).");
        bool scaled = r.ReadBit();
        var bands = (JxrBandsPresent)r.ReadBits(4);
        if (bands is not (JxrBandsPresent.AllBands or JxrBandsPresent.NoFlexbits))
            throw new NotSupportedException($"JxrCodestream.DecodeGray decodes all-bands / no-flexbits codestreams only (got {bands}).");
        // YONLY: no RESERVED bytes to skip.
        var (lenMantissa, expBias) = ReadBitDepthParams(ref r, bd); // SHIFT_BITS / LEN_MANTISSA + EXP_BIAS

        if (!r.ReadBit()) throw new NotSupportedException("Non-uniform DC quantization not supported.");
        int qpDc = (int)r.ReadBits(8);

        if (r.ReadBit()) throw new NotSupportedException("LP reusing DC quantizer not supported by this writer's mirror.");
        if (!r.ReadBit()) throw new NotSupportedException("Non-uniform LP quantization not supported.");
        int qpLp = (int)r.ReadBits(8);

        if (r.ReadBit()) throw new NotSupportedException("HP reusing LP quantizer not supported by this writer's mirror.");
        if (!r.ReadBit()) throw new NotSupportedException("Non-uniform HP quantization not supported.");
        int qpHp = (int)r.ReadBits(8);

        AlignToByte(ref r);
        return (qpDc, qpLp, qpHp, scaled, lenMantissa, expBias, bands);
    }

    private static void WriteProfileLevelInfo(BitWriter w)
    {
        // jxrlib writeIndexTableNull: PutVLWordEsc(0,4) then profile/level/LAST_FLAG.
        WriteVlwEsc(w, 4);
        w.WriteBits(DefaultProfileIdc, 8);
        w.WriteBits(DefaultLevelIdc, 8);
        w.WriteBits(1, 16); // LAST_FLAG = 1 (single conformance record)
    }

    private static void ReadProfileLevelInfo(ref BitReader r)
    {
        AlignToByte(ref r);
        long subsequent = ReadVlwEsc(ref r);
        // The "subsequent bytes" count covers profile+level+last-flag; skip them.
        r.SkipBits((int)subsequent * 8);
    }

    private static void WritePacketHeader(BitWriter w, int trimFlexBits = 0)
    {
        // writePacketHeader(pIO, ptPacketType=0 spatial, pID=0): 00 00 01 00.
        w.WriteBits(0, 8); w.WriteBits(0, 8); w.WriteBits(1, 8); w.WriteBits(0, 8);
        // jxrlib encodeMB: a 4-bit TRIM_FLEXBITS follows the SPATIAL packet header when the
        // image-header TRIM_FLEXBITS_FLAG is set (before the — here empty — tile band headers).
        if (trimFlexBits > 0) w.WriteBits((uint)trimFlexBits, 4);
    }

    // Returns the per-tile TRIM_FLEXBITS (0 when the image-header flag is clear).
    private static int ReadPacketHeader(ref BitReader r, bool trimFlexBitsFlag = false)
    {
        uint b0 = r.ReadBits(8), b1 = r.ReadBits(8), b2 = r.ReadBits(8), b3 = r.ReadBits(8);
        if (b0 != 0 || b1 != 0 || b2 != 1)
            throw new InvalidDataException($"Bad spatial packet header {b0:X2} {b1:X2} {b2:X2} {b3:X2} (expected 00 00 01 xx).");
        return trimFlexBitsFlag ? (int)r.ReadBits(4) : 0;
    }

    // ---------------------------------------------------------------- vlw_esc (T.832 §8.2.4)

    private static void WriteVlwEsc(BitWriter w, long value)
    {
        if (value < 0xFB00) w.WriteBits((uint)value, 16);
        else { w.WriteBits(0xFB, 8); w.WriteBits((uint)value, 32); }
    }

    private static long ReadVlwEsc(ref BitReader r)
    {
        uint first = r.ReadBits(8);
        if (first < 0xFB) return (first << 8) | r.ReadBits(8);
        if (first == 0xFB) return r.ReadBits(32);
        if (first == 0xFC) { uint hi = r.ReadBits(32), lo = r.ReadBits(32); return ((long)hi << 32) | lo; }
        return 0; // 0xFD/0xFE/0xFF parser escape
    }

    // A FREQUENCY band index entry: a 0xFF first byte is the "empty band" escape (the band packet is
    // header-only and omitted from the stream); anything else is a real cumulative byte offset.
    private static long ReadFreqIndexEntry(ref BitReader r, out bool empty)
    {
        uint first = r.ReadBits(8);
        if (first == 0xFF) { empty = true; return 0; }
        empty = false;
        if (first < 0xFB) return (first << 8) | r.ReadBits(8);
        if (first == 0xFB) return r.ReadBits(32);
        if (first == 0xFC) { uint hi = r.ReadBits(32), lo = r.ReadBits(32); return ((long)hi << 32) | lo; }
        return 0;
    }

    // ---------------------------------------------------------------- helpers

    private static (JxrQuantizer dc, JxrQuantizer lp, JxrQuantizer hp) Quantizers(int qpDc, int qpLp, int qpHp, bool scaled)
        => (WithDcOffset(Quantization.Resolve(qpDc, scaled)), Quantization.Resolve(qpLp, scaled), Quantization.Resolve(qpHp, scaled));

    // jxrlib StrEncInit (strenc.c:1033) overrides the DC quantizer's forward-quantize rounding offset
    // to iQP>>1 — a wider deadzone than remapQP's (iQP*3+1)>>3 default, which LP and HP keep. This is
    // an encoder-only choice (the offset is the deadzone added before the reciprocal-multiply; the
    // decoder's DEQUANT is just raw*iQP and never reads iOffset), so applying it here is safe for the
    // shared decode paths too. At QP index ≤ 1 (lossless) iQP=1 ⇒ 1>>1=0 == remapQP's offset, so this
    // is a no-op outside lossy QP. Applies to every channel's DC band (luma and chroma).
    private static JxrQuantizer WithDcOffset(JxrQuantizer dc) { dc.Offset = dc.Qp >> 1; return dc; }

    // jxrlib formatQuantizer (strcodec.c:872) gives the chroma (U,V) channels' DC and LP quantizers
    // shift SHIFTZERO-1 in scaled-arith mode (bShiftedUV=TRUE) — a half step relative to luma —
    // while luma (channel 0) DC/LP and *every* channel's HP (bShiftedUV=FALSE) use SHIFTZERO. So only
    // the chroma DC/LP quantizers differ; HP is shared with luma. The shift is consulted only in the
    // scaled branch of remapQP, and QP index ≤ 1 is the lossless branch (which ignores it), so outside
    // scaled lossy QP these equal the luma quantizers — i.e. a no-op for lossless / NO_FLEXBITS paths.
    private const int ShiftZeroMinusOne = 0; // SHIFTZERO - 1 (SHIFTZERO == 1)
    private static (JxrQuantizer dc, JxrQuantizer lp) ChromaDcLpQuantizers(int qpDc, int qpLp, bool scaled)
        => (WithDcOffset(Quantization.Resolve(qpDc, scaled, ShiftZeroMinusOne)), Quantization.Resolve(qpLp, scaled, ShiftZeroMinusOne));

    // Resolve per-channel dequant step sizes (.Qp) from per-channel QP index arrays — the decode side of
    // jxrlib's quality mode, where Y/U/V carry distinct indices. Luma (ch 0) and every channel's HP use
    // SHIFTZERO; chroma (ch>0) DC/LP use SHIFTZERO-1 (the bShiftedUV half step). The forward-quantize
    // deadzone is irrelevant on decode (DEQUANT = raw*iQP), so only .Qp matters here.
    private static (int[] dcQp, int[] lpQp, int[] hpQp) PerChannelQp(int[] dcIdx, int[] lpIdx, int[] hpIdx, bool scaled, int channels)
    {
        var (dcQp, lpQp, hpQp) = (new int[channels], new int[channels], new int[channels]);
        for (var ch = 0; ch < channels; ch++)
        {
            // luma + HP use the default SHIFTZERO; chroma DC/LP use SHIFTZERO-1 (the half step).
            dcQp[ch] = (ch == 0 ? Quantization.Resolve(dcIdx[ch], scaled) : Quantization.Resolve(dcIdx[ch], scaled, ShiftZeroMinusOne)).Qp;
            lpQp[ch] = (ch == 0 ? Quantization.Resolve(lpIdx[ch], scaled) : Quantization.Resolve(lpIdx[ch], scaled, ShiftZeroMinusOne)).Qp;
            hpQp[ch] = Quantization.Resolve(hpIdx[ch], scaled).Qp;
        }
        return (dcQp, lpQp, hpQp);
    }

    // jxrlib StrEncInit: lossless (all bands QP index ≤ 1) ⇒ bScaledArith == FALSE.
    private static bool ScaledArith(int qpDc, int qpLp, int qpHp) => qpDc > 1 || qpLp > 1 || qpHp > 1;

    // Luma/sample level shift: jxrlib `iOffset = (1 << (bits-1)) << cShift` — 128 for BD8, 32768 for
    // BD16-unsigned. The SIGNED formats (BD16S/BD32S) are already centred at 0, so they use NO level
    // bias (strenc.c BD_16S/BD_32S loads subtract nothing). (SHIFT_BITS nLen = 0: full-precision.)
    private static int LumaBias(JxrOutputBitDepth bd) => bd switch
    {
        JxrOutputBitDepth.Bd16 => 32768,
        JxrOutputBitDepth.Bd16S or JxrOutputBitDepth.Bd32S => 0,
        _ => 128,
    };

    // Output clamp range (jxrlib _CLIP8 / _CLIPU16 unsigned, _CLIP16 / none signed).
    private static int SampleMax(JxrOutputBitDepth bd) => bd switch
    {
        JxrOutputBitDepth.Bd16 => 65535,
        JxrOutputBitDepth.Bd16S => 32767,
        JxrOutputBitDepth.Bd32S => int.MaxValue,
        _ => 255,
    };

    private static int SampleMin(JxrOutputBitDepth bd) => bd switch
    {
        JxrOutputBitDepth.Bd16S => -32768,
        JxrOutputBitDepth.Bd32S => int.MinValue,
        _ => 0,
    };

    private static void FillToByte(BitWriter w)
    {
        while ((w.BitPosition & 7) != 0) w.WriteBit(false);
    }

    private static void AlignToByte(ref BitReader r)
    {
        int slack = (8 - (r.BitPosition & 7)) & 7;
        if (slack > 0) r.SkipBits(slack);
    }

    // jxrlib supports arbitrary dimensions: WIDTH_MINUS1 / HEIGHT_MINUS1 carry the real
    // (unpadded) size, the coded grid is ceil(dim/16) macroblocks, the encoder edge-
    // replicates the partial right/bottom macroblocks (strenc.c padHorizontally replicates
    // the last column; inputMBRow replicates the last row by not advancing the source past
    // it), and the decoder crops the grid back to the real size on output (strdec.c
    // outputNChannel loops iColumn<cWidth / iRow<cHeight). There is NO WINDOWING_FLAG — the
    // flag (cExtraPixels*) is only emitted for compressed-domain transcoding (bTranscode),
    // which this codec never does. So sub-MB images are a pad-then-crop, not a windowed crop.
    private static void RequirePositiveDims(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentException("JxrCodestream requires positive width and height.");
    }

    // Number of macroblocks spanning a dimension: ceil(dim / 16).
    private static int MbCount(int dim) => (dim + 15) / 16;

    private static void ExtractMb(ReadOnlySpan<int> r, ReadOnlySpan<int> g, ReadOnlySpan<int> b, int width, int height,
                                  int mbR, int mbC, int[] mr, int[] mg, int[] mb)
    {
        for (var row = 0; row < 16; row++)
        {
            int sy = Math.Min(mbR * 16 + row, height - 1); // edge-replicate the last row past the bottom
            int rowBase = sy * width;
            int dst = row * 16;
            for (var col = 0; col < 16; col++)
            {
                int sx = Math.Min(mbC * 16 + col, width - 1); // edge-replicate the last column past the right
                mr[dst + col] = r[rowBase + sx];
                mg[dst + col] = g[rowBase + sx];
                mb[dst + col] = b[rowBase + sx];
            }
        }
    }

    private static void StoreMb(Span<int> r, Span<int> g, Span<int> b, int width, int height,
                                int mbR, int mbC, int[] mr, int[] mg, int[] mb)
    {
        for (var row = 0; row < 16; row++)
        {
            int dy = mbR * 16 + row;
            if (dy >= height) break;             // drop the padded rows below the image
            int rowBase = dy * width;
            int src = row * 16;
            for (var col = 0; col < 16; col++)
            {
                int dx = mbC * 16 + col;
                if (dx >= width) break;          // drop the padded columns past the right edge
                r[rowBase + dx] = mr[src + col];
                g[rowBase + dx] = mg[src + col];
                b[rowBase + dx] = mb[src + col];
            }
        }
    }

    private static void ExtractMbGray(ReadOnlySpan<int> y, int width, int height, int mbR, int mbC, int[] my)
    {
        for (var row = 0; row < 16; row++)
        {
            int rowBase = Math.Min(mbR * 16 + row, height - 1) * width;
            int dst = row * 16;
            for (var col = 0; col < 16; col++)
                my[dst + col] = y[rowBase + Math.Min(mbC * 16 + col, width - 1)];
        }
    }

    private static void StoreMbGray(Span<int> y, int width, int height, int mbR, int mbC, int[] my)
    {
        for (var row = 0; row < 16; row++)
        {
            int dy = mbR * 16 + row;
            if (dy >= height) break;
            int rowBase = dy * width;
            int src = row * 16;
            for (var col = 0; col < 16; col++)
            {
                int dx = mbC * 16 + col;
                if (dx >= width) break;
                y[rowBase + dx] = my[src + col];
            }
        }
    }

    private static void ExtractMbGrayF(ReadOnlySpan<float> y, int width, int height, int mbR, int mbC, float[] my)
    {
        for (var row = 0; row < 16; row++)
        {
            int rowBase = Math.Min(mbR * 16 + row, height - 1) * width;
            int dst = row * 16;
            for (var col = 0; col < 16; col++)
                my[dst + col] = y[rowBase + Math.Min(mbC * 16 + col, width - 1)];
        }
    }

    private static void StoreMbGrayF(Span<float> y, int width, int height, int mbR, int mbC, float[] my)
    {
        for (var row = 0; row < 16; row++)
        {
            int dy = mbR * 16 + row;
            if (dy >= height) break;
            int rowBase = dy * width;
            int src = row * 16;
            for (var col = 0; col < 16; col++)
            {
                int dx = mbC * 16 + col;
                if (dx >= width) break;
                y[rowBase + dx] = my[src + col];
            }
        }
    }

    // Generic single-channel macroblock copy (used by the half-float gray/RGB paths).
    // Encode side edge-replicates the partial right/bottom MB (Math.Min clamp).
    private static void ExtractMb1<T>(ReadOnlySpan<T> src, int width, int height, int mbR, int mbC, Span<T> mb)
    {
        for (var row = 0; row < 16; row++)
        {
            int rowBase = Math.Min(mbR * 16 + row, height - 1) * width;
            int d = row * 16;
            for (var col = 0; col < 16; col++)
                mb[d + col] = src[rowBase + Math.Min(mbC * 16 + col, width - 1)];
        }
    }

    // Decode side crops the padded MB grid back to the real dimensions.
    private static void StoreMb1<T>(Span<T> dst, int width, int height, int mbR, int mbC, ReadOnlySpan<T> mb)
    {
        for (var row = 0; row < 16; row++)
        {
            int dy = mbR * 16 + row;
            if (dy >= height) break;
            int rowBase = dy * width;
            int s = row * 16;
            for (var col = 0; col < 16; col++)
            {
                int dx = mbC * 16 + col;
                if (dx >= width) break;
                dst[rowBase + dx] = mb[s + col];
            }
        }
    }
}
