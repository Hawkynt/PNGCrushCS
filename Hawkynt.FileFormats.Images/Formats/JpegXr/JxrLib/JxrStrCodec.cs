using System;
using System.IO;

namespace FileFormat.JpegXr.JxrLib;

/// <summary>
/// One image plane decoder (jxrlib CWMImageStrCodec): headers, tile/packet bookkeeping and the
/// macroblock-row pipeline. An interleaved alpha plane is a second instance linked through <see cref="Next"/>.
/// </summary>
internal sealed partial class JxrStrCodec {

  private const int _CODEC_VERSION = 1;
  private const int _SUBVERSION = 0;
  private const int _SUBVERSION_NEWSCALING_SOFT_TILES = 1;
  private const int _SUBVERSION_NEWSCALING_HARD_TILES = 9;
  private const long _MAX_WORKING_INTS = 1L << 26;

  private readonly byte[] _data;

  internal JxrImageInfo II;
  internal JxrCodecParams SCP;
  internal JxrCoreParams P;
  internal JxrDecoderParams D = new();
  internal readonly JxrMBInfo MB = new();

  internal int CSB;
  internal bool UVResolutionChange;
  internal JxrBitReader IOHeader;
  internal long[] IndexTable = [];
  internal int TileRow;
  internal int TileColumn;
  internal bool CtxLeft;
  internal bool CtxTop;
  internal bool ResetRGITotals;
  internal bool ResetContext;
  internal JxrTile[] Tiles = [];
  internal JxrBitReader[] BitIO = [];
  internal int NumBitIO;
  internal long HeaderSize;
  internal JxrCodingContext[] Contexts = [];

  internal int MbWidth;
  internal int MbHeight;
  internal int Row;
  internal int Column;

  // hard tile boundary tracking of the altered-operator transform (persists between macroblocks)
  internal int HtMbX;
  internal int HtMbY;
  internal int HtTileX;
  internal int HtTileY;
  internal bool VertTileBoundary;
  internal bool HoriTileBoundary;
  internal bool OneMBLeftVertTB;
  internal bool OneMBRightVertTB;
  internal readonly int[,] PredBefore = new int[2, 2];
  internal readonly int[,] PredAfter = new int[2, 2];

  /// <summary>Working memory: per channel the previous and current macroblock row, laid out back to back.</summary>
  internal int[] Mem = [];
  internal readonly int[] A0 = new int[JxrTables.MaxChannels];
  internal readonly int[] A1 = new int[JxrTables.MaxChannels];
  internal readonly int[] P0 = new int[JxrTables.MaxChannels];
  internal readonly int[] P1 = new int[JxrTables.MaxChannels];
  internal int[] ResU = [];
  internal int[] ResV = [];

  internal JxrPredInfo[][] PredInfo = new JxrPredInfo[JxrTables.MaxChannels][];
  internal JxrPredInfo[][] PredInfoPrevRow = new JxrPredInfo[JxrTables.MaxChannels][];

  internal JxrStrCodec? Next;
  internal JxrStrCodec? Partner;
  internal bool Secondary;

  // output buffer (jxrlib CWMImageBufferInfo)
  internal byte[] Out = [];
  internal int OutLines;
  internal int OutStride;
  internal int LinesDecoded;

  private JxrStrCodec(byte[] data, JxrImageInfo ii, JxrCodecParams scp, JxrCoreParams p) {
    this._data = data;
    this.II = ii;
    this.SCP = scp;
    this.P = p;
    this.IOHeader = new JxrBitReader(data);
  }

  private static InvalidDataException _Bad(string what) => new($"JPEG XR: {what}.");

  #region headers

  /// <summary>Reads one image plane header (jxrlib ReadImagePlaneHeader); returns the byte position after it.</summary>
  private static long _ReadImagePlaneHeader(JxrImageInfo ii, JxrCodecParams scp, JxrCoreParams sc, JxrHeaderBitReader sb) {
    var cf = (int)sb.Get(3);
    if (cf > (int)JxrColorFormat.NComponent || cf == 5)
      throw _Bad("unsupported internal colour format");
    sc.ColorFormat = (JxrColorFormat)cf;
    scp.ColorFormat = sc.ColorFormat;
    sc.ScaledArith = sb.Get(1) != 0;
    scp.Subband = (JxrSubband)sb.Get(4);

    switch (sc.ColorFormat) {
      case JxrColorFormat.YOnly:
        sc.NumChannels = 1;
        break;
      case JxrColorFormat.Yuv420:
        sc.NumChannels = 3;
        sb.Get(1);
        ii.ChromaCenteringX = (int)sb.Get(3);
        sb.Get(1);
        ii.ChromaCenteringY = (int)sb.Get(3);
        break;
      case JxrColorFormat.Yuv422:
        sc.NumChannels = 3;
        sb.Get(1);
        ii.ChromaCenteringX = (int)sb.Get(3);
        sb.Get(4);
        break;
      case JxrColorFormat.Yuv444:
        sc.NumChannels = 3;
        sb.Get(4);
        sb.Get(4);
        break;
      case JxrColorFormat.NComponent:
        sc.NumChannels = (int)sb.Get(4) + 1;
        sb.Get(4);
        break;
      case JxrColorFormat.Cmyk:
        sc.NumChannels = 4;
        break;
    }

    switch (ii.BitDepth) {
      case JxrBitDepth.Bd16:
      case JxrBitDepth.Bd16S:
      case JxrBitDepth.Bd32:
      case JxrBitDepth.Bd32S:
        scp.LenMantissaOrShift = (int)sb.Get(8);
        break;
      case JxrBitDepth.Bd32F:
        scp.LenMantissaOrShift = (int)sb.Get(8);
        scp.ExpBias = (sbyte)(byte)sb.Get(8);
        break;
    }

    // quantisation: bit 0/1/2 = DC/LP/HP vary per tile, bits 3-4/5-6/7-8 = channel mode, 0x200/0x400 = LP/HP independent of DC/LP
    sc.QpMode = 0;
    if (sb.Get(1) == 1)
      sc.QpMode += (uint)_ReadQuantizerSB(sc.QpIndexDC, sb, sc.NumChannels) << 3;
    else
      ++sc.QpMode;

    if (scp.Subband != JxrSubband.DcOnly) {
      if (sb.Get(1) == 0) {
        sc.QpMode += 0x200;
        if (sb.Get(1) == 1)
          sc.QpMode += (uint)_ReadQuantizerSB(sc.QpIndexLP, sb, sc.NumChannels) << 5;
        else
          sc.QpMode += 2;
      } else
        sc.QpMode += ((sc.QpMode & 1) << 1) + ((sc.QpMode & 0x18) << 2);

      if (scp.Subband != JxrSubband.NoHighpass) {
        if (sb.Get(1) == 0) {
          sc.QpMode += 0x400;
          if (sb.Get(1) == 1)
            sc.QpMode += (uint)_ReadQuantizerSB(sc.QpIndexHP, sb, sc.NumChannels) << 7;
          else
            sc.QpMode += 4;
        } else
          sc.QpMode += ((sc.QpMode & 2) << 1) + ((sc.QpMode & 0x60) << 2);
      }
    }

    if (scp.Subband == JxrSubband.DcOnly)
      sc.QpMode |= 0x200;
    else if (scp.Subband == JxrSubband.NoHighpass)
      sc.QpMode |= 0x400;

    if ((sc.QpMode & 0x600) == 0)
      throw _Bad("frame level quantisers not specified");

    sb.FlushToByte();
    return sb.BytePosition;
  }

  private static int _ReadQuantizerSB(byte[] qpIndex, JxrHeaderBitReader sb, int channels) {
    var chMode = 0;
    if (channels >= JxrTables.MaxChannels)
      return 0;
    if (channels > 1)
      chMode = (int)sb.Get(2);
    qpIndex[0] = (byte)sb.Get(8);
    if (chMode == 1)
      qpIndex[1] = (byte)sb.Get(8);
    else if (chMode > 0)
      for (var i = 1; i < channels; ++i)
        qpIndex[i] = (byte)sb.Get(8);
    return chMode;
  }

  /// <summary>Reads the image header and the first plane header (jxrlib ReadWMIHeader); returns the byte position after them.</summary>
  internal static long ReadWmiHeader(byte[] data, long pos, JxrImageInfo ii, JxrCodecParams scp, JxrCoreParams sc) {
    if (pos < 0 || pos + 8 > data.Length)
      throw _Bad("codestream offset outside the file");
    ReadOnlySpan<byte> sig = "WMPHOTO"u8;
    if (!data.AsSpan((int)pos, 7).SequenceEqual(sig))
      throw _Bad("missing WMPHOTO codestream signature");

    var sb = new JxrHeaderBitReader(data, pos + 8);
    if (sb.Get(4) != _CODEC_VERSION)
      throw _Bad("unsupported codec version");
    sc.Version = _CODEC_VERSION;
    var sub = (int)sb.Get(4);
    if (sub != _SUBVERSION && sub != _SUBVERSION_NEWSCALING_SOFT_TILES && sub != _SUBVERSION_NEWSCALING_HARD_TILES)
      throw _Bad("unsupported codec sub-version");
    sc.SubVersion = sub;
    sc.UseHardTileBoundaries = sub == _SUBVERSION_NEWSCALING_HARD_TILES;
    scp.UseHardTileBoundaries = sc.UseHardTileBoundaries;

    var tilingPresent = sb.Get(1) != 0;
    scp.FrequencyOrder = sb.Get(1) != 0;
    ii.Orientation = (int)sb.Get(3);
    sc.IndexTable = sb.Get(1) != 0;
    var ol = (int)sb.Get(2);
    if (ol == 3)
      throw _Bad("invalid overlap mode");
    scp.Overlap = (JxrOverlap)ol;

    var abbreviated = sb.Get(1) != 0;
    sb.Get(1); // long word flag, the decoder always works in 32-bit
    var inscribed = sb.Get(1) != 0;
    sc.TrimFlexbitsFlag = sb.Get(1) != 0;
    var tileStretch = sb.Get(1) != 0;
    sc.RbSwapped = sb.Get(1) != 0;
    sb.Get(1);
    sc.AlphaChannel = sb.Get(1) != 0;

    var cf = (int)sb.Get(4);
    if (cf > (int)JxrColorFormat.Rgbe || cf == 5)
      throw _Bad("unsupported output colour format");
    ii.ColorFormat = (JxrColorFormat)cf;
    var bd = (int)sb.Get(4);
    if (bd == (int)JxrBitDepth.Bd1Alt) {
      bd = (int)JxrBitDepth.Bd1;
      scp.BlackWhite = true;
    } else if (bd > (int)JxrBitDepth.Bd565)
      throw _Bad("unsupported output bit depth");
    ii.BitDepth = (JxrBitDepth)bd;

    var width = (long)(uint)(sb.Get(abbreviated ? 16 : 32) + 1);
    var height = (long)(uint)(sb.Get(abbreviated ? 16 : 32) + 1);
    if (width > JxrLibDecoder.MaxDimension || height > JxrLibDecoder.MaxDimension)
      throw _Bad("image dimensions too large");
    ii.Width = (int)width;
    ii.Height = (int)height;

    sc.ExtraPixelsTop = sc.ExtraPixelsLeft = sc.ExtraPixelsBottom = sc.ExtraPixelsRight = 0;
    if (!inscribed && (ii.Width & 0xf) != 0)
      sc.ExtraPixelsRight = 0x10 - (ii.Width & 0xf);
    if (!inscribed && (ii.Height & 0xf) != 0)
      sc.ExtraPixelsBottom = 0x10 - (ii.Height & 0xf);

    scp.NumSliceMinus1V = scp.NumSliceMinus1H = 0;
    if (tilingPresent) {
      scp.NumSliceMinus1V = (int)sb.Get(JxrTables.LogMaxTiles);
      scp.NumSliceMinus1H = (int)sb.Get(JxrTables.LogMaxTiles);
    }

    if (!sc.IndexTable && (scp.FrequencyOrder || scp.NumSliceMinus1V + scp.NumSliceMinus1H > 0))
      throw _Bad("tiled or frequency ordered stream without index table");

    scp.TileX[0] = scp.TileY[0] = 0;
    for (var i = 0; i < scp.NumSliceMinus1V; ++i)
      scp.TileX[i + 1] = sb.Get(abbreviated ? 8 : 16) + scp.TileX[i];
    for (var i = 0; i < scp.NumSliceMinus1H; ++i)
      scp.TileY[i + 1] = sb.Get(abbreviated ? 8 : 16) + scp.TileY[i];

    if (tileStretch)
      for (var i = 0; i < (scp.NumSliceMinus1V + 1) * (scp.NumSliceMinus1H + 1); ++i)
        sb.Get(8);

    if (inscribed) {
      sc.ExtraPixelsTop = (int)sb.Get(6);
      sc.ExtraPixelsLeft = (int)sb.Get(6);
      sc.ExtraPixelsBottom = (int)sb.Get(6);
      sc.ExtraPixelsRight = (int)sb.Get(6);
    }

    if (((ii.Width + sc.ExtraPixelsLeft + sc.ExtraPixelsRight) & 0xf) + ((ii.Height + sc.ExtraPixelsTop + sc.ExtraPixelsBottom) & 0xf) != 0) {
      if ((ii.Width & 0xf) + (ii.Height & 0xf) + sc.ExtraPixelsLeft + sc.ExtraPixelsTop != 0)
        throw _Bad("invalid inscribed window");
      if (ii.Width <= sc.ExtraPixelsRight || ii.Height <= sc.ExtraPixelsBottom)
        throw _Bad("invalid inscribed window");
      ii.Width -= sc.ExtraPixelsRight;
      ii.Height -= sc.ExtraPixelsBottom;
    }

    sb.FlushToByte();
    var end = _ReadImagePlaneHeader(ii, scp, sc, sb);

    scp.AlphaMode = sc.AlphaChannel ? scp.AlphaMode : 0;
    scp.Channels = sc.NumChannels;
    if (ii.BitDepth is JxrBitDepth.Bd5 or JxrBitDepth.Bd10 or JxrBitDepth.Bd565
        && scp.ColorFormat is not (JxrColorFormat.Yuv444 or JxrColorFormat.Yuv422 or JxrColorFormat.Yuv420 or JxrColorFormat.YOnly))
      throw _Bad("packed RGB output from an incompatible internal format");
    return end;
  }

  /// <summary>Re-reads the header and reconciles caller supplied image information with it (jxrlib WMPhotoValidate).</summary>
  private static void _Validate(byte[] data, long pos, JxrImageInfo ii, JxrCodecParams scp) {
    var savedAlphaMode = scp.AlphaMode;
    var cII = new JxrImageInfo();
    ReadWmiHeader(data, pos, cII, scp, new JxrCoreParams());

    ii.BitDepth = cII.BitDepth;
    ii.Width = cII.Width;
    ii.Height = cII.Height;
    if (ii.Width == 0 || ii.Height == 0)
      throw _Bad("empty image");

    if (scp.AlphaMode > 1)
      scp.AlphaMode = savedAlphaMode;

    if (scp.ColorFormat == JxrColorFormat.NComponent)
      ii.ColorFormat = JxrColorFormat.NComponent;
    if (scp.ColorFormat == JxrColorFormat.Cmyk && ii.ColorFormat != JxrColorFormat.YOnly && ii.ColorFormat != JxrColorFormat.Rgb)
      ii.ColorFormat = JxrColorFormat.Cmyk;
    if (scp.ColorFormat == JxrColorFormat.Yuv422 && ii.ColorFormat == JxrColorFormat.Yuv420)
      ii.ColorFormat = JxrColorFormat.Yuv422;
    if (scp.ColorFormat == JxrColorFormat.Yuv444 && ii.ColorFormat is JxrColorFormat.Yuv422 or JxrColorFormat.Yuv420)
      ii.ColorFormat = JxrColorFormat.Yuv444;
    if (cII.ColorFormat == JxrColorFormat.Rgb && ii.ColorFormat != JxrColorFormat.YOnly && ii.ColorFormat != JxrColorFormat.NComponent)
      ii.ColorFormat = cII.ColorFormat;
    if (cII.ColorFormat == JxrColorFormat.Rgbe)
      ii.ColorFormat = JxrColorFormat.Rgbe;

    if (ii.ThumbnailWidth == 0 || ii.ThumbnailWidth > ii.Width)
      ii.ThumbnailWidth = ii.Width;
    if (ii.ThumbnailHeight == 0 || ii.ThumbnailHeight > ii.Height)
      ii.ThumbnailHeight = ii.Height;

    long scale = 1;
    if ((ii.Width + ii.ThumbnailWidth - 1) / ii.ThumbnailWidth != (ii.Height + ii.ThumbnailHeight - 1) / ii.ThumbnailHeight) {
      while ((ii.Width + scale - 1) / scale > ii.ThumbnailWidth && (ii.Height + scale - 1) / scale > ii.ThumbnailHeight && scale < (1L << 40))
        scale <<= 1;
    } else {
      scale = (ii.Width + ii.ThumbnailWidth - 1) / ii.ThumbnailWidth;
      if (scale == 0)
        scale = 1;
    }

    ii.ThumbnailWidth = (int)((ii.Width + scale - 1) / scale);
    ii.ThumbnailHeight = (int)((ii.Height + scale - 1) / scale);

    if (ii.RoiHeight == 0 || ii.RoiWidth == 0) {
      ii.RoiLeftX = ii.RoiTopY = 0;
      ii.RoiWidth = ii.ThumbnailWidth;
      ii.RoiHeight = ii.ThumbnailHeight;
    }

    if (ii.RoiLeftX >= ii.ThumbnailWidth)
      ii.RoiLeftX = 0;
    if (ii.RoiTopY >= ii.ThumbnailHeight)
      ii.RoiTopY = 0;
    if (ii.RoiLeftX + ii.RoiWidth > ii.ThumbnailWidth)
      ii.RoiWidth = ii.ThumbnailWidth - ii.RoiLeftX;
    if (ii.RoiTopY + ii.RoiHeight > ii.ThumbnailHeight)
      ii.RoiHeight = ii.ThumbnailHeight - ii.RoiTopY;
  }

  #endregion

  #region initialisation

  private static JxrStrCodec _InitializeStrDec(byte[] data, JxrCoreParams p, JxrImageInfo ii, JxrCodecParams scp) {
    var sc = new JxrStrCodec(data, ii.Clone(), scp.Clone(), p.Clone());
    sc.Row = 0;
    sc.Column = 0;
    sc.MbWidth = (sc.II.Width + 15) / 16;
    sc.MbHeight = (sc.II.Height + 15) / 16;
    return sc;
  }

  /// <summary>
  /// Creates the decoder for the codestream at <paramref name="pos"/> (jxrlib ImageStrDecInit).
  /// <paramref name="ii"/> and <paramref name="scp"/> are updated in place with the decoder's view, as jxrlib writes them back.
  /// </summary>
  internal static JxrStrCodec Create(byte[] data, long pos, JxrImageInfo ii, JxrCodecParams scp) {
    _Validate(data, pos, ii, scp);
    if (scp.Subband == JxrSubband.Isolated)
      throw _Bad("isolated subband stream cannot be decoded");

    var hdrII = new JxrImageInfo();
    var hdrSCP = new JxrCodecParams();
    var core = new JxrCoreParams();
    var afterHeader = ReadWmiHeader(data, pos, hdrII, hdrSCP, core);
    if (scp.ColorFormat != JxrColorFormat.Cmyk && ii.ColorFormat == JxrColorFormat.Cmyk)
      throw _Bad("CMYK output from a non-CMYK stream");

    var scII = ii.Clone();
    scII.Width += core.ExtraPixelsLeft + core.ExtraPixelsRight;
    scII.Height += core.ExtraPixelsTop + core.ExtraPixelsBottom;

    var cblkChroma = JxrTables.CblkChromas[(int)core.ColorFormat];
    var mbStride = 16 * 16;
    var mbChroma = 16 * cblkChroma;
    long cMacBlock = (scII.Width + 15) / 16;
    if ((mbStride + mbChroma * (long)(core.NumChannels - 1)) * 2 * cMacBlock > _MAX_WORKING_INTS)
      throw _Bad("image too large for the working buffers");

    var pSC = _InitializeStrDec(data, core, scII, scp);
    var mem = 0L;
    var stride = mbStride;
    for (var i = 0; i < pSC.P.NumChannels; ++i) {
      pSC.A0[i] = (int)mem;
      mem += (long)stride * pSC.MbWidth;
      pSC.A1[i] = (int)mem;
      mem += (long)stride * pSC.MbWidth;
      stride = mbChroma;
    }
    pSC.Mem = new int[mem];

    var ioPos = afterHeader;
    JxrStrCodec? pNextSC = null;
    if (pSC.P.AlphaChannel) {
      pNextSC = _InitializeStrDec(data, core, scII, scp);
      var sb = new JxrHeaderBitReader(data, ioPos);
      ioPos = _ReadImagePlaneHeader(pNextSC.II, pNextSC.SCP, pNextSC.P, sb);
      pNextSC.D = pSC.D;
      pNextSC.P.ColorFormat = JxrColorFormat.YOnly;
      pNextSC.P.NumChannels = 1;
      pNextSC.P.AlphaChannel = true;
      pNextSC.A0[0] = 0;
      pNextSC.A1[0] = mbStride * pNextSC.MbWidth;
      pNextSC.Mem = new int[2L * mbStride * pNextSC.MbWidth];
      pNextSC.IOHeader = pSC.IOHeader;
      pNextSC.Partner = pSC;
      pNextSC.Secondary = true;
    } else
      pSC.SCP.AlphaMode = 0;

    pSC._StrIODecInit(ioPos);
    pSC._StrDecInit();
    pNextSC?._StrDecInit();
    pSC.Next = pNextSC;

    ii.CopyFrom(pSC.II);
    scp.CopyFrom(pSC.SCP);
    return pSC;
  }

  private void _AllocateBitIOInfo() {
    var sub = this.SCP.Subband;
    this.CSB = sub == JxrSubband.DcOnly ? 1 : sub == JxrSubband.NoHighpass ? 2 : sub == JxrSubband.NoFlexbits ? 3 : 4;
    int numBitIO;
    if (!this.P.IndexTable)
      numBitIO = 0;
    else if (!this.SCP.FrequencyOrder)
      numBitIO = this.SCP.NumSliceMinus1V + 1;
    else
      numBitIO = (this.SCP.NumSliceMinus1V + 1) * this.CSB;

    if (numBitIO > JxrTables.MaxTiles * 4)
      throw _Bad("too many packets");

    if (numBitIO > 0) {
      this.BitIO = new JxrBitReader[numBitIO];
      for (var i = 0; i < numBitIO; ++i)
        this.BitIO[i] = new JxrBitReader(this._data);
      if (this.SCP.NumSliceMinus1H >= JxrTables.MaxTiles)
        throw _Bad("too many tile rows");
      this.IndexTable = new long[numBitIO * (this.SCP.NumSliceMinus1H + 1)];
    }

    this.NumBitIO = numBitIO;
  }

  private void _SetBitIOPointers() {
    if (this.NumBitIO > 0) {
      for (var i = 0; i <= this.SCP.NumSliceMinus1V; ++i) {
        var ctx = this.Contexts[i];
        if (!this.SCP.FrequencyOrder)
          ctx.IODC = ctx.IOLP = ctx.IOAC = ctx.IOFL = this.BitIO[i];
        else {
          var j = this.CSB;
          ctx.IODC = this.BitIO[i * j];
          if (j > 1)
            ctx.IOLP = this.BitIO[i * j + 1];
          if (j > 2)
            ctx.IOAC = this.BitIO[i * j + 2];
          if (j > 3)
            ctx.IOFL = this.BitIO[i * j + 3];
        }
      }
    } else {
      var ctx = this.Contexts[0];
      ctx.IODC = ctx.IOLP = ctx.IOAC = ctx.IOFL = this.IOHeader;
    }
  }

  /// <summary>Variable length byte-aligned integer used by the index table (jxrlib GetVLWordEsc).</summary>
  private static long _GetVLWordEsc(JxrBitReader io) {
    long s = io.Get32(8);
    if (s is 0xfd or 0xfe or 0xff)
      return 0;
    if (s < 0xfb)
      return (s << 8) | io.Get32(8);

    s -= 0xfb;
    if (s != 0) {
      s = (long)io.Get32(16) << 16;
      s = (s | io.Get32(16)) << 16;
      s <<= 16;
    }
    s |= (long)io.Get32(16) << 16;
    s |= io.Get32(16);
    return s;
  }

  private void _StrIODecInit(long ioPos) {
    this._AllocateBitIOInfo();
    this.IOHeader.Attach(ioPos);

    var io = this.IOHeader;
    if (this.NumBitIO > 0) {
      var entries = this.NumBitIO * (this.SCP.NumSliceMinus1H + 1);
      if (io.Get32(16) != 1)
        return;
      for (var i = 0; i < entries; ++i)
        this.IndexTable[i] = _GetVLWordEsc(io);
    }

    this.HeaderSize = _GetVLWordEsc(io);
    io.FlushToByte();
    this.HeaderSize += io.BytePosition;
  }

  private void _StrDecInit() {
    var cfInt = this.P.ColorFormat;
    var cfExt = this.II.ColorFormat;

    this.UVResolutionChange = cfExt != JxrColorFormat.YOnly
      && ((cfInt == JxrColorFormat.Yuv420 && cfExt != JxrColorFormat.Yuv420) || (cfInt == JxrColorFormat.Yuv422 && cfExt != JxrColorFormat.Yuv422));
    if (this.UVResolutionChange) {
      var n = (cfExt == JxrColorFormat.Yuv422 ? 128 : 256) * this.MbWidth;
      this.ResU = new int[n];
      this.ResV = new int[n];
    }

    // prediction info: current and previous macroblock row per channel
    for (var i = 0; i < this.P.NumChannels; ++i) {
      this.PredInfo[i] = _NewPredRow(this.MbWidth);
      this.PredInfoPrevRow[i] = _NewPredRow(this.MbWidth);
    }

    if (this.SCP.NumSliceMinus1V >= JxrTables.MaxTiles)
      throw _Bad("too many tile columns");
    this.Tiles = new JxrTile[this.SCP.NumSliceMinus1V + 1];
    for (var i = 0; i < this.Tiles.Length; ++i)
      this.Tiles[i] = new JxrTile();

    var ch = this.P.NumChannels;
    var mode = this.P.QpMode;
    if ((mode & 1) == 0) {
      _AllocateQuantizer(this.Tiles[0].QuantizerDC, ch, 1);
      this._SetUniformQuantizer(0);
      for (var i = 0; i < ch; ++i)
        this.Tiles[0].QuantizerDC[i]![0].Index = this.P.QpIndexDC[i];
      _FormatQuantizer(this.Tiles[0].QuantizerDC, (int)((mode >> 3) & 3), ch, 0, true, this.P.ScaledArith);
    }

    if (this.SCP.Subband != JxrSubband.DcOnly) {
      if ((mode & 2) == 0) {
        _AllocateQuantizer(this.Tiles[0].QuantizerLP, ch, 1);
        this._SetUniformQuantizer(1);
        if ((mode & 0x200) == 0)
          this._UseDCQuantizer(0);
        else {
          for (var i = 0; i < ch; ++i)
            this.Tiles[0].QuantizerLP[i]![0].Index = this.P.QpIndexLP[i];
          _FormatQuantizer(this.Tiles[0].QuantizerLP, (int)((mode >> 5) & 3), ch, 0, true, this.P.ScaledArith);
        }
      }

      if (this.SCP.Subband != JxrSubband.NoHighpass && (mode & 4) == 0) {
        _AllocateQuantizer(this.Tiles[0].QuantizerHP, ch, 1);
        this._SetUniformQuantizer(2);
        if ((mode & 0x400) == 0)
          this._UseLPQuantizer(1, 0);
        else {
          for (var i = 0; i < ch; ++i)
            this.Tiles[0].QuantizerHP[i]![0].Index = this.P.QpIndexHP[i];
          _FormatQuantizer(this.Tiles[0].QuantizerHP, (int)((mode >> 7) & 3), ch, 0, false, this.P.ScaledArith);
        }
      }
    }

    this._AllocateCodingContexts(this.SCP.NumSliceMinus1V + 1);

    if (this.Secondary) {
      var primary = this.Partner!;
      this.IOHeader = primary.IOHeader;
      this.BitIO = primary.BitIO;
      this.NumBitIO = primary.NumBitIO;
      this.CSB = primary.CSB;
    }

    this._SetBitIOPointers();
  }

  private static JxrPredInfo[] _NewPredRow(int width) {
    var r = new JxrPredInfo[width];
    for (var j = 0; j < width; ++j)
      r[j] = new JxrPredInfo();
    return r;
  }

  #endregion

  #region quantisers

  private static void _AllocateQuantizer(JxrQuantizer[]?[] q, int channels, int qps) {
    if (qps > 16 || channels > JxrTables.MaxChannels)
      throw _Bad("invalid quantiser count");
    for (var c = 0; c < channels; ++c)
      q[c] = new JxrQuantizer[qps];
  }

  private static void _FormatQuantizer(JxrQuantizer[]?[] q, int chMode, int channels, int pos, bool shiftedUV, bool scaledArith) {
    for (var c = 0; c < channels; ++c) {
      if (c > 0) {
        if (chMode == 0)
          q[c]![pos] = q[0]![pos];
        else if (chMode == 1)
          q[c]![pos] = q[1]![pos];
      }
      _RemapQP(ref q[c]![pos], c > 0 && shiftedUV ? JxrTables.ShiftZero - 1 : JxrTables.ShiftZero, scaledArith);
    }
  }

  private void _SetUniformQuantizer(int band) {
    for (var c = 0; c < this.P.NumChannels; ++c)
      for (var t = 1; t <= this.SCP.NumSliceMinus1V; ++t)
        switch (band) {
          case 0: this.Tiles[t].QuantizerDC[c] = this.Tiles[0].QuantizerDC[c]; break;
          case 1: this.Tiles[t].QuantizerLP[c] = this.Tiles[0].QuantizerLP[c]; break;
          default: this.Tiles[t].QuantizerHP[c] = this.Tiles[0].QuantizerHP[c]; break;
        }
  }

  private void _UseDCQuantizer(int tile) {
    for (var c = 0; c < this.P.NumChannels; ++c)
      this.Tiles[tile].QuantizerLP[c]![0] = this.Tiles[tile].QuantizerDC[c]![0];
  }

  private void _UseLPQuantizer(int qps, int tile) {
    for (var c = 0; c < this.P.NumChannels; ++c)
      for (var q = 0; q < qps; ++q)
        this.Tiles[tile].QuantizerHP[c]![q] = this.Tiles[tile].QuantizerLP[c]![q];
  }

  private static int _DquantBits(int qps) => qps < 2 ? 0 : qps < 4 ? 1 : qps < 6 ? 2 : qps < 10 ? 3 : 4;

  /// <summary>Reciprocal (mantissa, exponent) per quantiser mantissa; only the exponent matters to the decoder.</summary>
  private static readonly (uint Man, int Exp)[] _QPRecipTable = [
    (0x0, 0), (0x0, 0), (0x0, 1), (0xaaaaaaab, 1),
    (0x0, 2), (0xcccccccd, 2), (0xaaaaaaab, 2), (0x92492493, 2),
    (0x0, 3), (0xe38e38e4, 3), (0xcccccccd, 3), (0xba2e8ba3, 3),
    (0xaaaaaaab, 3), (0x9d89d89e, 3), (0x92492493, 3), (0x88888889, 3),
    (0x0, 4), (0xf0f0f0f1, 4), (0xe38e38e4, 4), (0xd79435e6, 4),
    (0xcccccccd, 4), (0xc30c30c4, 4), (0xba2e8ba3, 4), (0xb21642c9, 4),
    (0xaaaaaaab, 4), (0xa3d70a3e, 4), (0x9d89d89e, 4), (0x97b425ee, 4),
    (0x92492493, 4), (0x8d3dcb09, 4), (0x88888889, 4), (0x84210843, 4),
  ];

  /// <summary>Maps a quantiser index to its step size (T.832 clause 9.7, jxrlib remapQP).</summary>
  private static void _RemapQP(ref JxrQuantizer q, int shift, bool scaledArith) {
    var index = q.Index;
    if (index == 0) {
      q.QP = 1;
      q.Man = q.Exp = q.Offset = 0;
      return;
    }

    int man, exp;
    if (!scaledArith) {
      const int ciShift = -JxrTables.QpFracBits;
      if (index < 32) {
        man = (index + 3) >> 2;
        exp = ciShift + 2;
      } else if (index < 48) {
        man = (16 + (index & 0xf) + 1) >> 1;
        exp = ((index >> 4) - 1) + 1 + ciShift;
      } else {
        man = 16 + (index & 0xf);
        exp = ((index >> 4) - 1) + ciShift;
      }
    } else if (index < 16) {
      man = index;
      exp = shift;
    } else {
      man = 16 + (index & 0xf);
      exp = ((index >> 4) - 1) + shift;
    }

    q.QP = man << exp;
    q.Man = unchecked((int)_QPRecipTable[man].Man);
    q.Exp = _QPRecipTable[man].Exp + exp;
    q.Offset = (q.QP * 3 + 1) >> 3;
  }

  #endregion
}

internal static class JxrCopyExtensions {
  internal static void CopyFrom(this JxrImageInfo target, JxrImageInfo source) {
    target.Width = source.Width;
    target.Height = source.Height;
    target.ColorFormat = source.ColorFormat;
    target.BitDepth = source.BitDepth;
    target.BitsPerUnit = source.BitsPerUnit;
    target.LeadingPadding = source.LeadingPadding;
    target.Rgb = source.Rgb;
    target.ChromaCenteringX = source.ChromaCenteringX;
    target.ChromaCenteringY = source.ChromaCenteringY;
    target.RoiLeftX = source.RoiLeftX;
    target.RoiWidth = source.RoiWidth;
    target.RoiTopY = source.RoiTopY;
    target.RoiHeight = source.RoiHeight;
    target.SkipFlexbits = source.SkipFlexbits;
    target.ThumbnailWidth = source.ThumbnailWidth;
    target.ThumbnailHeight = source.ThumbnailHeight;
    target.Orientation = source.Orientation;
    target.PostProcStrength = source.PostProcStrength;
  }

  internal static void CopyFrom(this JxrCodecParams target, JxrCodecParams source) {
    var c = source.Clone();
    target.ColorFormat = c.ColorFormat;
    target.Overlap = c.Overlap;
    target.FrequencyOrder = c.FrequencyOrder;
    target.Channels = c.Channels;
    target.AlphaMode = c.AlphaMode;
    target.Subband = c.Subband;
    target.NumSliceMinus1V = c.NumSliceMinus1V;
    target.TileX = c.TileX;
    target.NumSliceMinus1H = c.NumSliceMinus1H;
    target.TileY = c.TileY;
    target.LenMantissaOrShift = c.LenMantissaOrShift;
    target.ExpBias = c.ExpBias;
    target.BlackWhite = c.BlackWhite;
    target.UseHardTileBoundaries = c.UseHardTileBoundaries;
  }
}
