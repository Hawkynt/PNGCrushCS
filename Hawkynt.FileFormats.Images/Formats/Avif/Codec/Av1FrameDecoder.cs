using System;
using System.Collections.Generic;

namespace FileFormat.Avif.Codec;

/// <summary>
/// Decodes the single key frame an AVIF still picture carries: OBU parsing, sequence and frame
/// headers, tile decode, the three in-loop filters, and finally the colour conversion.
/// </summary>
internal static class Av1FrameDecoder {

  /// <summary>The decoded picture together with the headers that describe how to interpret it.</summary>
  internal sealed record Av1DecodeResult(Av1SequenceHeader Sequence, Av1FrameHeader Frame, Av1DecodedFrame Picture);

  /// <summary>Decodes AV1 OBUs into RGB24 pixel data.</summary>
  public static (int Width, int Height, byte[] RgbData) Decode(byte[] av1Data, int offset, int length) {
    var result = DecodeToPlanes(av1Data, offset, length);
    var rgb = ConvertToRgb(result);
    return (result.Frame.FrameWidth, result.Frame.FrameHeight, rgb);
  }

  /// <summary>Decodes AV1 OBUs and stops at the reconstructed YUV planes.</summary>
  public static Av1DecodeResult DecodeToPlanes(byte[] av1Data, int offset, int length) {
    ArgumentNullException.ThrowIfNull(av1Data);
    if (length == 0)
      throw new InvalidOperationException("AV1: empty bitstream data.");

    var obus = Av1ObuParser.ParseObus(av1Data, offset, length);

    Av1SequenceHeader? seq = null;
    Av1FrameHeader? fh = null;
    var tileGroups = new List<(int Offset, int Size)>();

    foreach (var obu in obus)
      switch (obu.Type) {
        case Av1ObuType.SequenceHeader:
          seq = Av1SequenceHeader.Parse(av1Data, obu.PayloadOffset, obu.PayloadSize);
          break;

        case Av1ObuType.Frame:
          if (seq == null)
            throw new InvalidOperationException("AV1: frame OBU before sequence header.");
          fh = Av1FrameHeader.Parse(av1Data, obu.PayloadOffset, obu.PayloadSize, seq);
          tileGroups.Add((obu.PayloadOffset + fh.TileDataOffset, obu.PayloadSize - fh.TileDataOffset));
          break;

        case Av1ObuType.FrameHeader:
          if (seq == null)
            throw new InvalidOperationException("AV1: frame header OBU before sequence header.");
          fh = Av1FrameHeader.Parse(av1Data, obu.PayloadOffset, obu.PayloadSize, seq);
          break;

        case Av1ObuType.TileGroup:
          tileGroups.Add((obu.PayloadOffset, obu.PayloadSize));
          break;
      }

    if (seq == null)
      throw new InvalidOperationException("AV1: no sequence header found in bitstream.");
    if (fh == null)
      throw new InvalidOperationException("AV1: no frame header found in bitstream.");
    if (tileGroups.Count == 0)
      throw new InvalidOperationException("AV1: no tile data found in bitstream.");

    var frame = new Av1DecodedFrame(seq, fh.FrameWidth, fh.FrameHeight);
    var restoration = _CreateRestorationUnits(seq, fh);
    var frameCdf = Av1CdfContext.CreateDefault(fh.BaseQIndex);
    var tileDecoder = new Av1TileDecoder(seq, fh, frame, frameCdf, restoration);

    foreach (var (groupOffset, groupSize) in tileGroups)
      _DecodeTileGroup(av1Data, groupOffset, groupSize, fh, tileDecoder);

    // The filters run over the whole frame in the order AV1 7.4 fixes: deblocking, then CDEF, then
    // loop restoration, which reads its stripe boundaries from the pre-CDEF picture.
    var deblocked = _RunPostFilters(frame, seq, fh, restoration);

    return new(seq, fh, frame);
  }

  private static short[][] _RunPostFilters(
    Av1DecodedFrame frame, Av1SequenceHeader seq, Av1FrameHeader fh, Av1LoopRestorationUnits? restoration
  ) {
    if (fh.LoopFilterLevel[0] != 0 || fh.LoopFilterLevel[1] != 0)
      Av1Deblocking.Apply(frame, seq, fh);

    var usesRestoration = restoration != null;
    var deblocked = new short[frame.NumPlanes][];
    if (usesRestoration)
      for (var plane = 0; plane < frame.NumPlanes; ++plane)
        deblocked[plane] = (short[])frame.Planes[plane].Clone();

    if (seq.EnableCdef && !fh.CodedLossless && !fh.AllowIntraBc)
      Av1CdefFilter.Apply(frame, seq, fh);

    if (usesRestoration)
      Av1LoopRestoration.Apply(frame, deblocked, seq, fh, restoration!);

    return deblocked;
  }

  private static Av1LoopRestorationUnits? _CreateRestorationUnits(Av1SequenceHeader seq, Av1FrameHeader fh) {
    var uses = false;
    for (var plane = 0; plane < seq.NumPlanes; ++plane)
      uses |= fh.LrType[plane] != (int)Av1RestorationType.None;

    if (!uses)
      return null;

    var unitCols = new int[seq.NumPlanes];
    var unitRows = new int[seq.NumPlanes];
    for (var plane = 0; plane < seq.NumPlanes; ++plane) {
      var subX = plane > 0 ? seq.SubsamplingX : 0;
      var subY = plane > 0 ? seq.SubsamplingY : 0;
      var unitSize = fh.LoopRestorationSize[plane];
      if (unitSize <= 0) {
        unitCols[plane] = 0;
        unitRows[plane] = 0;
        continue;
      }

      unitCols[plane] = _CountUnitsInFrame(unitSize, _Round2(fh.UpscaledWidth, subX));
      unitRows[plane] = _CountUnitsInFrame(unitSize, _Round2(fh.FrameHeight, subY));
    }

    return new(seq.NumPlanes, unitCols, unitRows);
  }

  private static int _CountUnitsInFrame(int unitSize, int frameSize) =>
    Math.Max((frameSize + (unitSize >> 1)) / unitSize, 1);

  private static int _Round2(int value, int shift) => shift <= 0 ? value : (value + (1 << (shift - 1))) >> shift;

  private static void _DecodeTileGroup(
    byte[] data, int offset, int length, Av1FrameHeader fh, Av1TileDecoder tileDecoder
  ) {
    var numTiles = fh.TileRows * fh.TileCols;
    var reader = new Av1BitReader(data, offset, length);

    var tileStart = 0;
    var tileEnd = numTiles - 1;
    if (numTiles > 1 && reader.ReadBool()) {
      var bits = fh.TileColsLog2 + fh.TileRowsLog2;
      tileStart = (int)reader.ReadBits(bits);
      tileEnd = (int)reader.ReadBits(bits);
    }

    reader.ByteAlign();
    var current = reader.ByteOffset;
    var end = offset + length;

    for (var tile = tileStart; tile <= tileEnd; ++tile) {
      int tileSize;
      if (tile == tileEnd)
        tileSize = end - current;
      else {
        if (end - current < fh.TileSizeBytes)
          throw new InvalidOperationException("AV1: tile group ended inside a tile size field.");

        tileSize = 0;
        for (var i = 0; i < fh.TileSizeBytes; ++i)
          tileSize |= data[current + i] << (i * 8);
        ++tileSize;
        current += fh.TileSizeBytes;
      }

      if (tileSize <= 0 || tileSize > end - current)
        throw new InvalidOperationException("AV1: tile size runs past the end of the tile group.");

      tileDecoder.DecodeTile(data, current, tileSize, tile % fh.TileCols, tile / fh.TileCols);
      current += tileSize;
    }
  }

  /// <summary>Converts the decoded planes to interleaved RGB24.</summary>
  public static byte[] ConvertToRgb(Av1DecodeResult result) {
    var seq = result.Sequence;
    var frame = result.Picture;
    var width = result.Frame.FrameWidth;
    var height = result.Frame.FrameHeight;
    var planes = frame.Planes;
    var strides = frame.Strides;

    if (seq.MonoChrome)
      return Av1YuvToRgb.ConvertMonoToRgb(planes[0], strides[0], width, height, seq.BitDepth, seq.ColorRange);

    if (seq.MatrixCoefficients == Av1MatrixCoefficients.Identity)
      return Av1YuvToRgb.ConvertIdentityToRgb(
        planes[0], strides[0], planes[1], strides[1], planes[2], strides[2],
        width, height, seq.BitDepth);

    if (seq.SubsamplingX != 0 && seq.SubsamplingY != 0)
      return Av1YuvToRgb.ConvertYuv420ToRgb(
        planes[0], strides[0], planes[1], strides[1], planes[2], strides[2],
        width, height, seq.BitDepth, seq.MatrixCoefficients, seq.ColorRange);

    if (seq.SubsamplingX == 0 && seq.SubsamplingY == 0)
      return Av1YuvToRgb.ConvertYuv444ToRgb(
        planes[0], strides[0], planes[1], strides[1], planes[2], strides[2],
        width, height, seq.BitDepth, seq.MatrixCoefficients, seq.ColorRange);

    // 4:2:2 has full vertical chroma resolution, so only the horizontal axis needs replicating.
    var chromaWidth = (width + 1) >> 1;
    var upsampledU = _RepeatHorizontally(planes[1], strides[1], chromaWidth, height, width);
    var upsampledV = _RepeatHorizontally(planes[2], strides[2], chromaWidth, height, width);
    return Av1YuvToRgb.ConvertYuv444ToRgb(
      planes[0], strides[0], upsampledU, width, upsampledV, width,
      width, height, seq.BitDepth, seq.MatrixCoefficients, seq.ColorRange);
  }

  private static short[] _RepeatHorizontally(short[] source, int sourceStride, int sourceWidth, int height, int width) {
    var result = new short[width * height];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        result[y * width + x] = source[y * sourceStride + Math.Min(x >> 1, sourceWidth - 1)];
    return result;
  }
}
