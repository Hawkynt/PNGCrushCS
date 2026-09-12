using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Codecs.Theora;

/// <summary>Builds the three Theora I header packets and their Xiph-laced private-data form.</summary>
internal static class TheoraEncoderHeaders {

  internal const int GranuleShift = 6;
  internal const int QuantiserMatrixValue = 16;
  internal const int QuantiserScaleValue = 100;

  internal static (TheoraIdentificationHeader Header, byte[] CodecPrivateData) Build(MediaStreamInfo stream) {
    var macroBlocksWide = checked((stream.Width + 15) / 16);
    var macroBlocksHigh = checked((stream.Height + 15) / 16);
    var frameRate = stream.FrameRate.IsKnown ? stream.FrameRate : new Rational(25, 1);

    if (frameRate.Numerator <= 0 || frameRate.Numerator > uint.MaxValue
        || frameRate.Denominator <= 0 || frameRate.Denominator > uint.MaxValue)
      throw new NotSupportedException(
        $"Theora stores its frame-rate numerator and denominator in 32 bits; {frameRate} cannot be represented there.");

    var header = new TheoraIdentificationHeader {
      VersionMajor = 3,
      VersionMinor = 2,
      VersionRevision = 1,
      FrameMacroBlocksWide = macroBlocksWide,
      FrameMacroBlocksHigh = macroBlocksHigh,
      PictureWidth = stream.Width,
      PictureHeight = stream.Height,
      PictureX = 0,
      PictureY = 0,
      FrameRateNumerator = (uint)frameRate.Numerator,
      FrameRateDenominator = (uint)frameRate.Denominator,
      AspectNumerator = 1,
      AspectDenominator = 1,
      ColorSpace = 0,
      NominalBitrate = 0,
      Quality = 32,
      KeyFrameGranuleShift = GranuleShift,
      PixelFormat = TheoraPixelFormat.Yuv444,
    };

    var identification = _Identification(header);
    var comment = _Comment();
    var setup = _Setup();
    return (header, _Lace([identification, comment, setup]));
  }

  private static byte[] _Identification(TheoraIdentificationHeader header) {
    var bits = new TheoraBitWriter();
    bits.WriteBits(8, 0x80);
    bits.WriteBytes("theora"u8);
    bits.WriteBits(8, (uint)header.VersionMajor);
    bits.WriteBits(8, (uint)header.VersionMinor);
    bits.WriteBits(8, (uint)header.VersionRevision);
    bits.WriteBits(16, (uint)header.FrameMacroBlocksWide);
    bits.WriteBits(16, (uint)header.FrameMacroBlocksHigh);
    bits.WriteBits(24, (uint)header.PictureWidth);
    bits.WriteBits(24, (uint)header.PictureHeight);
    bits.WriteBits(8, (uint)header.PictureX);
    bits.WriteBits(8, (uint)header.PictureY);
    bits.WriteBits(32, header.FrameRateNumerator);
    bits.WriteBits(32, header.FrameRateDenominator);
    bits.WriteBits(24, header.AspectNumerator);
    bits.WriteBits(24, header.AspectDenominator);
    bits.WriteBits(8, (uint)header.ColorSpace);
    bits.WriteBits(24, header.NominalBitrate);
    bits.WriteBits(6, (uint)header.Quality);
    bits.WriteBits(5, (uint)header.KeyFrameGranuleShift);
    bits.WriteBits(2, (uint)header.PixelFormat);
    bits.WriteBits(3, 0);
    return bits.Finish();
  }

  private static byte[] _Comment() {
    var bits = new TheoraBitWriter();
    bits.WriteBits(8, 0x81);
    bits.WriteBytes("theora"u8);
    bits.WriteBits(32, 0);
    bits.WriteBits(32, 0);
    return bits.Finish();
  }

  /// <summary>
  /// A deliberately simple setup: no loop filter, one constant quantisation matrix declared twice,
  /// and eighty identical balanced Huffman trees whose five-bit code is the token number itself.
  /// </summary>
  private static byte[] _Setup() {
    var bits = new TheoraBitWriter();
    bits.WriteBits(8, 0x82);
    bits.WriteBytes("theora"u8);

    // Loop-filter limit bit width zero: all sixty-four limits are zero and no values follow.
    bits.WriteBits(3, 0);

    // AC and DC scale tables. A stored width of six means seven bits per value.
    for (var table = 0; table < 2; ++table) {
      bits.WriteBits(4, 6);
      for (var index = 0; index < 64; ++index)
        bits.WriteBits(7, QuantiserScaleValue);
    }

    // Two identical base matrices, where one would do. The field a quant range names its matrix
    // with is ilog(NBMS - 1) bits wide, which is no field at all when a header declares one matrix;
    // FFmpeg's reader computes the same width as log2(NBMS - 1) + 1, which is one bit there, and
    // then refuses the index it reads out of the size field behind it. The two readings disagree
    // for NBMS = 1 alone and agree from two upwards, so the duplicate matrix costs 64 bytes and
    // buys a header both of them read alike.
    bits.WriteBits(9, 1);
    for (var matrix = 0; matrix < 2; ++matrix)
      for (var coefficient = 0; coefficient < 64; ++coefficient)
        bits.WriteBits(8, QuantiserMatrixValue);

    // One quantisation range covering indices 0..63: the matrix it opens on, its size, and the
    // matrix the scale ends on. The other five type/plane combinations copy an earlier range
    // exactly as section 6.4.3 permits.
    for (var type = 0; type < 2; ++type)
    for (var plane = 0; plane < 3; ++plane) {
      if (type == 0 && plane == 0) {
        bits.WriteBits(1, 0);
        bits.WriteBits(6, 62);
        bits.WriteBits(1, 0);
        continue;
      }

      bits.WriteBit(0);
      if (type > 0)
        bits.WriteBit(1);
    }

    for (var table = 0; table < 80; ++table)
      _HuffmanTree(bits, 0, 0);

    return bits.Finish();
  }

  private static void _HuffmanTree(TheoraBitWriter bits, int depth, int code) {
    if (depth == 5) {
      bits.WriteBit(1);
      bits.WriteBits(5, (uint)code);
      return;
    }

    bits.WriteBit(0);
    _HuffmanTree(bits, depth + 1, code << 1);
    _HuffmanTree(bits, depth + 1, (code << 1) | 1);
  }

  private static byte[] _Lace(IReadOnlyList<byte[]> packets) {
    var result = new List<byte> { checked((byte)(packets.Count - 1)) };

    for (var packet = 0; packet < packets.Count - 1; ++packet) {
      var remaining = packets[packet].Length;
      while (remaining >= 255) {
        result.Add(255);
        remaining -= 255;
      }
      result.Add((byte)remaining);
    }

    foreach (var packet in packets)
      result.AddRange(packet);

    return result.ToArray();
  }
}
