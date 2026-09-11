using System;
using System.Buffers.Binary;

namespace FileFormat.Codecs.Indeo;

/// <summary>Builds independently decodable Intel Indeo 3 intra frames.</summary>
/// <remarks>
/// This is deliberately the smallest useful writer for the format rather than a second rate-control
/// experiment. Each of the three planes is one intra cell, mode 0, quantisation table 0, with no
/// motion vectors. That is a complete legal path through the bitstream grammar and makes every packet
/// a key frame; a later encoder can split cells, search motion and choose coarser tables without
/// changing the packet contract.
/// <para/>
/// The wire layout is derived from the decoder beside this file and checked against FFmpeg's
/// LGPL-2.1-or-later <c>libavcodec/indeo3.c</c>. FFmpeg contains no Indeo 3 encoder, so no encoder
/// implementation code exists there to copy: this writes the inverse of the documented/read grammar
/// and reuses only <see cref="Indeo3Tables"/>, whose licence and provenance are already recorded in
/// <c>THIRD-PARTY-NOTICE.FFmpeg.txt</c>.
/// </remarks>
internal static class Indeo3FrameEncoder {

  private const uint _OS_HEADER_ID = 0x46524D48; // MKBETAG('F', 'R', 'M', 'H')
  private const ushort _CODEC_VERSION = 32;
  private const ushort _KEY_FRAME = 1 << 2;

  private const int _OS_HEADER_LENGTH = 16;
  private const int _BITSTREAM_HEADER_LENGTH = 48;
  private const byte _WHOLE_PLANE_INTRA_CELL = 0b1011_0000;
  private const byte _MODE_ZERO_TABLE_ZERO = 0x00;
  private const byte _NEUTRAL_SAMPLE = 0x40;

  /// <summary>Encodes one RGB picture as an IV32 intra frame.</summary>
  internal static byte[] Encode(ReadOnlySpan<byte> rgb, int width, int height, uint frameNumber) {
    var chromaWidth = Indeo3Plane.Align(width >> 2, 4);
    var chromaHeight = Indeo3Plane.Align(height >> 2, 4);

    var luma = new byte[checked(width * height)];
    var blueDifference = new byte[checked(chromaWidth * chromaHeight)];
    var redDifference = new byte[blueDifference.Length];
    _ToPlanes(rgb, width, height, luma, blueDifference, redDifference, chromaWidth);

    var y = _EncodePlane(luma, width, height);
    var u = _EncodePlane(blueDifference, chromaWidth, chromaHeight);
    var v = _EncodePlane(redDifference, chromaWidth, chromaHeight);

    var yOffset = _BITSTREAM_HEADER_LENGTH;
    var vOffset = checked(yOffset + y.Length);
    var uOffset = checked(vOffset + v.Length);
    var dataSize = checked(uOffset + u.Length);
    var frame = new byte[checked(_OS_HEADER_LENGTH + dataSize)];

    var osHeader = frame.AsSpan(0, _OS_HEADER_LENGTH);
    BinaryPrimitives.WriteUInt32LittleEndian(osHeader, frameNumber);
    BinaryPrimitives.WriteUInt32LittleEndian(osHeader[4..], 0);
    BinaryPrimitives.WriteUInt32LittleEndian(osHeader[8..], frameNumber ^ (uint)dataSize ^ _OS_HEADER_ID);
    BinaryPrimitives.WriteUInt32LittleEndian(osHeader[12..], (uint)dataSize);

    var bitstreamHeader = frame.AsSpan(_OS_HEADER_LENGTH, _BITSTREAM_HEADER_LENGTH);
    BinaryPrimitives.WriteUInt16LittleEndian(bitstreamHeader, _CODEC_VERSION);
    BinaryPrimitives.WriteUInt16LittleEndian(bitstreamHeader[2..], _KEY_FRAME);
    BinaryPrimitives.WriteUInt32LittleEndian(bitstreamHeader[4..], checked((uint)dataSize * 8));
    bitstreamHeader[8] = 0; // codebook offset; table zero is used in every plane.
    BinaryPrimitives.WriteUInt16LittleEndian(bitstreamHeader[12..], (ushort)height);
    BinaryPrimitives.WriteUInt16LittleEndian(bitstreamHeader[14..], (ushort)width);
    BinaryPrimitives.WriteUInt32LittleEndian(bitstreamHeader[16..], (uint)yOffset);
    BinaryPrimitives.WriteUInt32LittleEndian(bitstreamHeader[20..], (uint)vOffset);
    BinaryPrimitives.WriteUInt32LittleEndian(bitstreamHeader[24..], (uint)uOffset);
    // Bytes 28..31 are reserved. Bytes 32..47 are the alternate quantisation selectors used only by
    // modes 1 and 4. The array is zero-filled and this encoder writes mode 0, so both stay zero.

    var payload = frame.AsSpan(_OS_HEADER_LENGTH);
    y.CopyTo(payload[yOffset..]);
    v.CopyTo(payload[vOffset..]);
    u.CopyTo(payload[uOffset..]);
    return frame;
  }

  /// <summary>
  /// Converts packed RGB to the seven-bit YUV 4:1:0 samples Indeo 3 actually codes.
  /// </summary>
  /// <remarks>
  /// The decoder exposes the coded seven-bit samples by doubling them and displays them using BT.601
  /// studio swing. This is that display convention inverted: luma is converted per pixel and each
  /// chroma value is the average of the sixteen pixels in its 4x4 square. The padded chroma fringe is
  /// neutral and is never visible in the decoded picture.
  /// </remarks>
  private static void _ToPlanes(
    ReadOnlySpan<byte> rgb,
    int width,
    int height,
    Span<byte> luma,
    Span<byte> blueDifference,
    Span<byte> redDifference,
    int chromaWidth) {
    blueDifference.Fill(_NEUTRAL_SAMPLE);
    redDifference.Fill(_NEUTRAL_SAMPLE);

    var visibleChromaWidth = width >> 2;
    var visibleChromaHeight = height >> 2;
    var chromaSamples = checked(visibleChromaWidth * visibleChromaHeight);
    var blueSums = new int[chromaSamples];
    var redSums = new int[chromaSamples];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var source = (y * width + x) * 3;
      var red = rgb[source];
      var green = rgb[source + 1];
      var blue = rgb[source + 2];

      // ITU-R BT.601 studio-swing integer conversion, the inverse convention of IndeoColorConversion.
      var y8 = ((66 * red + 129 * green + 25 * blue + 128) >> 8) + 16;
      var cb8 = ((-38 * red - 74 * green + 112 * blue + 128) >> 8) + 128;
      var cr8 = ((112 * red - 94 * green - 18 * blue + 128) >> 8) + 128;
      luma[y * width + x] = _ToSevenBits(y8);

      var chroma = (y >> 2) * visibleChromaWidth + (x >> 2);
      blueSums[chroma] += cb8;
      redSums[chroma] += cr8;
    }

    for (var y = 0; y < visibleChromaHeight; ++y)
    for (var x = 0; x < visibleChromaWidth; ++x) {
      var source = y * visibleChromaWidth + x;
      var target = y * chromaWidth + x;
      blueDifference[target] = _ToSevenBits((blueSums[source] + 8) >> 4);
      redDifference[target] = _ToSevenBits((redSums[source] + 8) >> 4);
    }
  }

  /// <summary>
  /// Writes one plane: no vectors, the two tree codes “intra” then “coded”, and one mode-0 cell.
  /// </summary>
  private static byte[] _EncodePlane(ReadOnlySpan<byte> samples, int width, int height) {
    var table = Indeo3Tables.Tables[0];
    var output = new byte[checked(6 + width * height / 2)];
    // The motion-vector count at bytes 0..3 is already zero.
    output[4] = _WHOLE_PLANE_INTRA_CELL;
    output[5] = _MODE_ZERO_TABLE_ZERO;

    var reconstructed = new byte[checked(width * height)];
    var at = 6;

    // The decoder walks 4x4 blocks, and within a block walks four lines. Two bytes describe each line:
    // the first byte read from the stream is the table index for the right dyad and the second byte is
    // the table index for the left one. We solve each dyad against the decoder's packed 16-bit add so
    // byte carry/borrow has exactly the same effect here as it has while decoding.
    for (var blockY = 0; blockY < height; blockY += 4)
    for (var blockX = 0; blockX < width; blockX += 4)
    for (var line = 0; line < 4; ++line) {
      var y = blockY + line;
      var target = y * width + blockX;
      var reference = y == 0 ? -1 : target - width;

      var left = _BestDyad(
        table,
        reference < 0 ? _NEUTRAL_SAMPLE : reconstructed[reference],
        reference < 0 ? _NEUTRAL_SAMPLE : reconstructed[reference + 1],
        samples[target], samples[target + 1],
        out reconstructed[target], out reconstructed[target + 1]);
      var right = _BestDyad(
        table,
        reference < 0 ? _NEUTRAL_SAMPLE : reconstructed[reference + 2],
        reference < 0 ? _NEUTRAL_SAMPLE : reconstructed[reference + 3],
        samples[target + 2], samples[target + 3],
        out reconstructed[target + 2], out reconstructed[target + 3]);

      output[at++] = (byte)right;
      output[at++] = (byte)left;
    }

    return output;
  }

  /// <summary>Chooses the dyad whose decoded pair is nearest to the requested pair.</summary>
  private static int _BestDyad(
    Indeo3Tables.VqTable table,
    byte reference0,
    byte reference1,
    byte wanted0,
    byte wanted1,
    out byte decoded0,
    out byte decoded1) {
    var reference = reference0 | (reference1 << 8);
    var bestIndex = 0;
    var bestValue = reference;
    var bestError = int.MaxValue;

    for (var index = 0; index < table.DyadCount; ++index) {
      var value = (reference + table.Deltas[index]) & 0x7F7F;
      var error0 = (value & 0x7F) - wanted0;
      var error1 = ((value >> 8) & 0x7F) - wanted1;
      var error = error0 * error0 + error1 * error1;
      if (error >= bestError)
        continue;

      bestError = error;
      bestIndex = index;
      bestValue = value;
      if (error == 0)
        break;
    }

    decoded0 = (byte)(bestValue & 0x7F);
    decoded1 = (byte)((bestValue >> 8) & 0x7F);
    return bestIndex;
  }

  private static byte _ToSevenBits(int value) => (byte)(Math.Clamp(value, 0, 255) + 1 >> 1);
}
