using System;
using System.IO;
using SharpAstro.Jxr;

namespace FileFormat.JpegXr;

/// <summary>
/// Bridges the one remaining T.832 adapter gap for the public 8-bit JPEG XR model: a
/// frequency-order Y-only codestream, as used by JXRLib for planar alpha in ordinary
/// 32bpp BGRA files. The signal, entropy, prediction, quantization, and overlap work is
/// performed by the vendored JXRLib-compatible managed core; this class only performs
/// the frequency packet/index-table orchestration that <see cref="JxrCodestream.DecodeGray"/>
/// does not currently expose.
/// </summary>
internal static class JpegXrFrequencyGrayDecoder {

  // JXRLib's BitIO keeps a zero-filled packet buffer past the coded payload. The reference
  // entropy decoder deliberately speculatively peeks a few bits at the end of the last MB,
  // so match JxrCodestream's decode-side padding.
  private const int _END_PEEK_SLACK_BYTES = 16;
  private const int _PACKET_HEADER_BYTES = 4;

  internal static (int width, int height, int[] y) Decode(ReadOnlySpan<byte> codestream) {
    var padded = new byte[codestream.Length + _END_PEEK_SLACK_BYTES];
    codestream.CopyTo(padded);

    var reader = new BitReader(padded);
    var imageHeader = ImageHeader.Read(ref reader);

    // Keep the existing, broader spatial/tiled grayscale implementation authoritative.
    if (!imageHeader.FrequencyModeCodestreamFlag)
      return JxrCodestream.DecodeGray(codestream);

    if (imageHeader.TilingFlag)
      throw new NotSupportedException("Frequency-order tiled Y-only JPEG XR is outside the current public planar-alpha adapter.");
    if (imageHeader.OverlapMode is < 0 or > 2)
      throw new NotSupportedException($"JPEG XR overlap mode {imageHeader.OverlapMode} is not supported.");
    if (imageHeader.OutputBitDepth != JxrOutputBitDepth.Bd8)
      throw new NotSupportedException($"The public JPEG XR planar-alpha adapter requires BD8 alpha, got {imageHeader.OutputBitDepth}.");

    var width = checked((int)imageHeader.WidthMinus1 + 1);
    var height = checked((int)imageHeader.HeightMinus1 + 1);
    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"Invalid JPEG XR alpha dimensions: {width}x{height}.");

    var planeHeader = ImagePlaneHeader.Read(ref reader, imageHeader.OutputBitDepth);
    if (planeHeader.InternalClrFmt != JxrInternalColorFormat.YOnly)
      throw new NotSupportedException($"JPEG XR planar alpha must use a Y-only plane, got {planeHeader.InternalClrFmt}.");
    if (!planeHeader.DcImagePlaneUniformFlag ||
        !planeHeader.LpImagePlaneUniformFlag ||
        !planeHeader.HpImagePlaneUniformFlag)
      throw new NotSupportedException("Frequency-order JPEG XR planar alpha with per-tile quantizer tables is not exposed by the current adapter.");

    var bandCount = planeHeader.BandsPresent switch {
      JxrBandsPresent.AllBands => 4,
      JxrBandsPresent.NoFlexbits => 3,
      _ => throw new NotSupportedException($"Frequency-order JPEG XR planar alpha with {planeHeader.BandsPresent} is not exposed by the current adapter.")
    };
    var noFlexBits = planeHeader.BandsPresent == JxrBandsPresent.NoFlexbits;

    var qDc = Quantization.Resolve(planeHeader.DcQuant, planeHeader.ScaledFlag);
    var qLp = Quantization.Resolve(planeHeader.LpQuant, planeHeader.ScaledFlag);
    var qHp = Quantization.Resolve(planeHeader.HpQuant, planeHeader.ScaledFlag);

    var mbCols = checked((width + 15) / 16);
    var mbRows = checked((height + 15) / 16);
    var planes = OverlapTransform.AllocatePlanes(mbCols, mbRows, 1);

    var startCode = reader.ReadBits(16);
    if (startCode != IndexTableTiles.IndexTableStartCode)
      throw new InvalidDataException($"JPEG XR frequency alpha index-table start code mismatch: got 0x{startCode:X4}.");

    var offsets = new long[bandCount];
    var empty = new bool[bandCount];
    for (var band = 0; band < bandCount; ++band)
      offsets[band] = _ReadFrequencyIndexEntry(ref reader, out empty[band]);

    _ReadVlwEsc(ref reader); // end escape / no profile-level block
    _AlignToByte(ref reader);
    var packetBase = reader.BytePosition;

    BitReader BandReader(int band) {
      var result = new BitReader(padded);
      if (!empty[band]) {
        var packetOffset = checked(packetBase + (int)offsets[band]);
        if (packetOffset < 0 || packetOffset > padded.Length - _PACKET_HEADER_BYTES)
          throw new InvalidDataException($"JPEG XR alpha band {band} packet points outside the codestream.");
        result.SeekToByte(packetOffset + _PACKET_HEADER_BYTES);
      }
      return result;
    }

    var dcReader = BandReader(0);
    var lpReader = BandReader(1);
    var acReader = BandReader(2);
    var flexReader = bandCount > 3 ? BandReader(3) : new BitReader(padded);

    var context = new CodingContext(ColorFormat.YOnly, 1) { NoFlexBits = noFlexBits };
    var tile = new TileCoder(mbCols, 1, ColorFormat.YOnly);
    for (var mbRow = 0; mbRow < mbRows; ++mbRow) {
      for (var mbColumn = 0; mbColumn < mbCols; ++mbColumn) {
        var block = new Macroblock(1);
        tile.DecodeMacroblock(
          context,
          block,
          mbColumn,
          mbRow,
          ref dcReader,
          ref lpReader,
          ref acReader,
          ref flexReader,
          qHp.Qp
        );

        var baseOffset = OverlapTransform.MbBase(mbCols, mbRow, mbColumn);
        SignalTransform.DequantizeRestore(block, 0, planes[0], baseOffset, qDc.Qp, qLp.Qp);
      }
      tile.AdvanceRow();
    }

    OverlapTransform.Inverse(
      planes,
      mbCols,
      mbRows,
      imageHeader.OverlapMode,
      planeHeader.ScaledFlag
    );

    var pixels = new int[checked(width * height)];
    var macroblock = new int[256];
    var outputShift = planeHeader.ScaledFlag ? SignalTransform.ScaledShift : 0;
    for (var mbRow = 0; mbRow < mbRows; ++mbRow)
    for (var mbColumn = 0; mbColumn < mbCols; ++mbColumn) {
      var baseOffset = OverlapTransform.MbBase(mbCols, mbRow, mbColumn);
      SignalTransform.StoreGray(planes[0], baseOffset, macroblock, 128, 255, outputShift);
      _StoreMacroblock(pixels, width, height, mbRow, mbColumn, macroblock);
    }

    return (width, height, pixels);
  }

  private static void _StoreMacroblock(
    int[] destination,
    int width,
    int height,
    int mbRow,
    int mbColumn,
    ReadOnlySpan<int> source
  ) {
    var startX = mbColumn * 16;
    var startY = mbRow * 16;
    var copyWidth = Math.Min(16, width - startX);
    var copyHeight = Math.Min(16, height - startY);
    for (var y = 0; y < copyHeight; ++y)
      for (var x = 0; x < copyWidth; ++x)
        destination[(startY + y) * width + startX + x] = source[y * 16 + x];
  }

  private static void _AlignToByte(ref BitReader reader) {
    var slack = (8 - (reader.BitPosition & 7)) & 7;
    if (slack != 0)
      reader.SkipBits(slack);
  }

  private static long _ReadVlwEsc(ref BitReader reader) {
    var first = reader.ReadBits(8);
    if (first < 0xFB)
      return (first << 8) | reader.ReadBits(8);
    if (first == 0xFB)
      return reader.ReadBits(32);
    if (first == 0xFC) {
      var high = reader.ReadBits(32);
      var low = reader.ReadBits(32);
      return ((long)high << 32) | low;
    }
    return 0;
  }

  private static long _ReadFrequencyIndexEntry(ref BitReader reader, out bool empty) {
    var first = reader.ReadBits(8);
    if (first == 0xFF) {
      empty = true;
      return 0;
    }

    empty = false;
    if (first < 0xFB)
      return (first << 8) | reader.ReadBits(8);
    if (first == 0xFB)
      return reader.ReadBits(32);
    if (first == 0xFC) {
      var high = reader.ReadBits(32);
      var low = reader.ReadBits(32);
      return ((long)high << 32) | low;
    }
    return 0;
  }
}
