namespace FileFormat.JpegXr.JxrLib;

/// <summary>Internal (coded) and external colour formats; the numbering is the one stored in the codestream.</summary>
internal enum JxrColorFormat {
  YOnly = 0,
  Yuv420 = 1,
  Yuv422 = 2,
  Yuv444 = 3,
  Cmyk = 4,
  NComponent = 6,
  Rgb = 7,
  Rgbe = 8,
}

/// <summary>Sample layout of the external pixel format, as stored in the codestream's output bit depth field.</summary>
internal enum JxrBitDepth {
  Bd1 = 0,
  Bd8 = 1,
  Bd16 = 2,
  Bd16S = 3,
  Bd16F = 4,
  Bd32 = 5,
  Bd32S = 6,
  Bd32F = 7,
  Bd5 = 8,
  Bd10 = 9,
  Bd565 = 10,
  Bd1Alt = 0xf,
}

internal enum JxrSubband {
  All = 0,
  NoFlexbits = 1,
  NoHighpass = 2,
  DcOnly = 3,
  Isolated = 4,
}

internal enum JxrOverlap {
  None = 0,
  One = 1,
  Two = 2,
}

/// <summary>Image description shared between container, header and output stage (jxrlib CWMImageInfo).</summary>
internal sealed class JxrImageInfo {
  internal int Width;
  internal int Height;
  internal JxrColorFormat ColorFormat;
  internal JxrBitDepth BitDepth;
  internal int BitsPerUnit;
  internal int LeadingPadding;
  internal bool Rgb;
  internal int ChromaCenteringX;
  internal int ChromaCenteringY;
  internal int RoiLeftX;
  internal int RoiWidth;
  internal int RoiTopY;
  internal int RoiHeight;
  internal bool SkipFlexbits;
  internal int ThumbnailWidth;
  internal int ThumbnailHeight;
  internal int Orientation;
  internal int PostProcStrength;

  internal JxrImageInfo Clone() => (JxrImageInfo)this.MemberwiseClone();
}

/// <summary>Codec parameters (jxrlib CWMIStrCodecParam), the decoder-relevant subset.</summary>
internal sealed class JxrCodecParams {
  internal JxrColorFormat ColorFormat;
  internal JxrOverlap Overlap;
  internal bool FrequencyOrder;
  internal int Channels;
  internal int AlphaMode;
  internal JxrSubband Subband;
  internal int NumSliceMinus1V;
  internal uint[] TileX = new uint[JxrTables.MaxTiles];
  internal int NumSliceMinus1H;
  internal uint[] TileY = new uint[JxrTables.MaxTiles];
  internal int LenMantissaOrShift;
  internal sbyte ExpBias;
  internal bool BlackWhite;
  internal bool UseHardTileBoundaries;

  internal JxrCodecParams Clone() {
    var c = (JxrCodecParams)this.MemberwiseClone();
    c.TileX = (uint[])this.TileX.Clone();
    c.TileY = (uint[])this.TileY.Clone();
    return c;
  }
}

/// <summary>Core parameters read from the image and plane headers (jxrlib CCoreParameters).</summary>
internal sealed class JxrCoreParams {
  internal int Version;
  internal int SubVersion;
  internal JxrColorFormat ColorFormat;
  internal bool RbSwapped;
  internal bool AlphaChannel;
  internal bool ScaledArith;
  internal bool IndexTable;
  internal bool TrimFlexbitsFlag;
  internal bool UseHardTileBoundaries;
  internal int NumChannels;
  internal int ExtraPixelsTop;
  internal int ExtraPixelsLeft;
  internal int ExtraPixelsBottom;
  internal int ExtraPixelsRight;
  internal bool Transcode;
  internal uint QpMode;
  internal byte[] QpIndexDC = new byte[JxrTables.MaxChannels];
  internal byte[] QpIndexLP = new byte[JxrTables.MaxChannels];
  internal byte[] QpIndexHP = new byte[JxrTables.MaxChannels];

  internal JxrCoreParams Clone() {
    var c = (JxrCoreParams)this.MemberwiseClone();
    c.QpIndexDC = (byte[])this.QpIndexDC.Clone();
    c.QpIndexLP = (byte[])this.QpIndexLP.Clone();
    c.QpIndexHP = (byte[])this.QpIndexHP.Clone();
    return c;
  }
}

/// <summary>One quantiser step (jxrlib CWMIQuantizer).</summary>
internal struct JxrQuantizer {
  internal byte Index;
  internal int QP;
  internal int Offset;
  internal int Man;
  internal int Exp;
}

/// <summary>Per tile column quantiser sets; a channel's array is shared between tiles when the quantiser is uniform.</summary>
internal sealed class JxrTile {
  internal readonly JxrQuantizer[]?[] QuantizerDC = new JxrQuantizer[]?[JxrTables.MaxChannels];
  internal readonly JxrQuantizer[]?[] QuantizerLP = new JxrQuantizer[]?[JxrTables.MaxChannels];
  internal readonly JxrQuantizer[]?[] QuantizerHP = new JxrQuantizer[]?[JxrTables.MaxChannels];
  internal int NumQPLP = 1;
  internal int NumQPHP = 1;
  internal int BitsLP;
  internal int BitsHP;
  internal bool UseDC;
  internal bool UseLP;
  internal int ChModeDC;
  internal readonly int[] ChModeLP = new int[16];
  internal readonly int[] ChModeHP = new int[16];
}

/// <summary>Per macroblock prediction state kept for the next macroblock and the next row.</summary>
internal sealed class JxrPredInfo {
  internal int QPIndex;
  internal int Cbp;
  internal int DC;
  internal readonly int[] AD = new int[6];
}

/// <summary>Scan order slot with its adaptive hit counter.</summary>
internal struct JxrAdaptiveScan {
  internal uint Total;
  internal uint Scan;
}

/// <summary>Adaptive fixed-length-code model of one band: how many low bits are sent raw for luma [0] and chroma [1].</summary>
internal sealed class JxrAdaptiveModel {
  internal readonly int[] FlcState = new int[2];
  internal readonly int[] FlcBits = new int[2];
  internal int Band;

  internal void Reset(int band, int bits) {
    this.FlcState[0] = this.FlcState[1] = 0;
    this.FlcBits[0] = this.FlcBits[1] = bits;
    this.Band = band;
  }
}

/// <summary>Entropy decoding state of one tile column (jxrlib CCodingContext).</summary>
internal sealed class JxrCodingContext {
  internal JxrBitReader? IODC;
  internal JxrBitReader? IOLP;
  internal JxrBitReader? IOAC;
  internal JxrBitReader? IOFL;
  internal JxrAdaptiveHuffman AdaptHuffCBPCY = null!;
  internal JxrAdaptiveHuffman AdaptHuffCBPCY1 = null!;
  internal readonly JxrAdaptiveHuffman[] AHexpt = new JxrAdaptiveHuffman[JxrTables.NumVlcTables];
  internal readonly JxrAdaptiveScan[] ScanLowpass = new JxrAdaptiveScan[16];
  internal readonly JxrAdaptiveScan[] ScanHoriz = new JxrAdaptiveScan[16];
  internal readonly JxrAdaptiveScan[] ScanVert = new JxrAdaptiveScan[16];
  internal readonly JxrAdaptiveModel ModelAC = new();
  internal readonly JxrAdaptiveModel ModelLP = new();
  internal readonly JxrAdaptiveModel ModelDC = new();
  internal int CBPCountZero;
  internal int CBPCountMax;
  internal readonly int[] CbpCount0 = new int[2];
  internal readonly int[] CbpCount1 = new int[2];
  internal readonly int[] CbpState = new int[2];
  internal int TrimFlexBits;
}

/// <summary>Decoder-side region parameters, shared by the image plane and its interleaved alpha plane.</summary>
internal sealed class JxrDecoderParams {
  internal bool DecodeFullFrame;
  internal bool SkipFlexbits;
  internal int ThumbnailScale;
  internal bool DecodeHP;
  internal bool DecodeLP;
  internal int RoiLeftX;
  internal int RoiRightX;
  internal int RoiTopY;
  internal int RoiBottomY;
  internal int[] OffsetX = [];
  internal int[] OffsetY = [];
}

/// <summary>Coefficients and side information of the macroblock being decoded.</summary>
internal sealed class JxrMBInfo {
  internal readonly int[][] BlockDC = CreateBlocks();
  internal int Orientation;
  internal readonly int[] Cbp = new int[JxrTables.MaxChannels];
  internal readonly int[] DiffCbp = new int[JxrTables.MaxChannels];
  internal int QIndexLP;
  internal int QIndexHP;

  private static int[][] CreateBlocks() {
    var r = new int[JxrTables.MaxChannels][];
    for (var i = 0; i < r.Length; ++i)
      r[i] = new int[16];
    return r;
  }
}
