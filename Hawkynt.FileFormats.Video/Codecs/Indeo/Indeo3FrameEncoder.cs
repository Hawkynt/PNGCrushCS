using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace FileFormat.Codecs.Indeo;

/// <summary>Builds Intel Indeo 3 intra and forward-predicted frames.</summary>
/// <remarks>
/// Indeo 3 has two codec-owned picture buffers. A frame selects one as its destination and an inter
/// cell predicts from the other one, optionally displaced by a motion vector. This encoder alternates
/// those buffers, writes one intra picture every twelve frames and uses full-plane zero-vector inter
/// cells between them. Unchanged blocks are represented by the format's copy escapes, and a wholly
/// unchanged plane collapses to a VQ-tree copy leaf.
/// <para/>
/// The encoder keeps its own reconstructed pictures. Indeo 3 is lossy, so an inter frame must predict
/// from what the decoder actually holds rather than from the source picture that produced it; otherwise
/// quantisation error becomes prediction drift on every following frame.
/// <para/>
/// The wire layout is derived independently from the public bitstream description and checked against
/// FFmpeg's LGPL-2.1-or-later <c>libavcodec/indeo3.c</c>. FFmpeg contains no Indeo 3 encoder. The fixed
/// VQ tables are shared with the decoder and retain their existing attribution in
/// <c>THIRD-PARTY-NOTICE.FFmpeg.txt</c>.
/// </remarks>
internal sealed class Indeo3FrameEncoder {

  private const uint _OS_HEADER_ID = 0x46524D48; // MKBETAG('F', 'R', 'M', 'H')
  private const ushort _CODEC_VERSION = 32;

  private const int _PERIODIC_KEY_FRAME = 1 << 0;
  private const int _KEY_FRAME = 1 << 2;
  private const int _NEXT_FRAME_IS_KEY = 1 << 3;
  private const int _BUFFER_SHIFT = 9;

  private const int _OS_HEADER_LENGTH = 16;
  private const int _BITSTREAM_HEADER_LENGTH = 48;
  private const int _TRAILING_SLACK = 16;

  private const byte _WHOLE_PLANE_INTRA_CELL = 0b1011_0000;
  private const byte _WHOLE_PLANE_INTER_COPY = 0b1110_0000;
  private const byte _WHOLE_PLANE_INTER_DATA = 0b1111_0000;
  private const byte _MODE_ZERO_TABLE_ZERO = 0x00;
  private const byte _RLE_BLOCK_RUN = 0xFB;
  private const byte _RLE_SKIP_BLOCK = 0xFA;
  private const byte _NEUTRAL_SAMPLE = 0x40;
  private const int _MAX_BLOCK_RUN = 31;

  private readonly int _width;
  private readonly int _height;
  private readonly int _chromaWidth;
  private readonly int _chromaHeight;

  private readonly byte[]?[] _sourceLuma = new byte[]?[2];
  private readonly byte[]?[] _sourceBlueDifference = new byte[]?[2];
  private readonly byte[]?[] _sourceRedDifference = new byte[]?[2];
  private readonly byte[]?[] _reconstructedLuma = new byte[]?[2];
  private readonly byte[]?[] _reconstructedBlueDifference = new byte[]?[2];
  private readonly byte[]?[] _reconstructedRedDifference = new byte[]?[2];

  private int _nextBuffer;

  internal Indeo3FrameEncoder(int width, int height) {
    this._width = width;
    this._height = height;
    this._chromaWidth = Indeo3Plane.Align(width >> 2, 4);
    this._chromaHeight = Indeo3Plane.Align(height >> 2, 4);
  }

  /// <summary>Encodes one RGB picture and its frame-group signalling.</summary>
  internal EncodedFrame Encode(
    ReadOnlySpan<byte> rgb,
    uint frameNumber,
    bool periodicKeyFrame,
    bool nextFrameIsKeyFrame) {
    var luma = new byte[checked(this._width * this._height)];
    var blueDifference = new byte[checked(this._chromaWidth * this._chromaHeight)];
    var redDifference = new byte[blueDifference.Length];
    _ToPlanes(
      rgb, this._width, this._height, luma, blueDifference, redDifference, this._chromaWidth);

    var bufferSelect = this._nextBuffer;
    var referenceBuffer = bufferSelect ^ 1;
    var isKeyFrame = periodicKeyFrame
      || this._reconstructedLuma[referenceBuffer] is null
      || this._reconstructedBlueDifference[referenceBuffer] is null
      || this._reconstructedRedDifference[referenceBuffer] is null
      || this._sourceLuma[referenceBuffer] is null
      || this._sourceBlueDifference[referenceBuffer] is null
      || this._sourceRedDifference[referenceBuffer] is null;

    PlaneEncoding y;
    PlaneEncoding u;
    PlaneEncoding v;
    if (isKeyFrame) {
      y = _EncodeIntraPlane(luma, this._width, this._height);
      u = _EncodeIntraPlane(blueDifference, this._chromaWidth, this._chromaHeight);
      v = _EncodeIntraPlane(redDifference, this._chromaWidth, this._chromaHeight);
    } else {
      y = _EncodeInterPlane(
        luma, this._sourceLuma[referenceBuffer]!, this._reconstructedLuma[referenceBuffer]!,
        this._width, this._height);
      u = _EncodeInterPlane(
        blueDifference, this._sourceBlueDifference[referenceBuffer]!, this._reconstructedBlueDifference[referenceBuffer]!,
        this._chromaWidth, this._chromaHeight);
      v = _EncodeInterPlane(
        redDifference, this._sourceRedDifference[referenceBuffer]!, this._reconstructedRedDifference[referenceBuffer]!,
        this._chromaWidth, this._chromaHeight);
    }

    this._sourceLuma[bufferSelect] = luma;
    this._sourceBlueDifference[bufferSelect] = blueDifference;
    this._sourceRedDifference[bufferSelect] = redDifference;
    this._reconstructedLuma[bufferSelect] = y.Reconstructed;
    this._reconstructedBlueDifference[bufferSelect] = u.Reconstructed;
    this._reconstructedRedDifference[bufferSelect] = v.Reconstructed;
    this._nextBuffer ^= 1;

    return new(
      _BuildFrame(
        y.Data, u.Data, v.Data,
        this._width, this._height,
        frameNumber, bufferSelect,
        isKeyFrame,
        periodicKeyFrame && isKeyFrame,
        nextFrameIsKeyFrame),
      isKeyFrame);
  }

  private static byte[] _BuildFrame(
    byte[] y,
    byte[] u,
    byte[] v,
    int width,
    int height,
    uint frameNumber,
    int bufferSelect,
    bool isKeyFrame,
    bool isPeriodicKeyFrame,
    bool nextFrameIsKeyFrame) {
    var yOffset = _BITSTREAM_HEADER_LENGTH;
    var vOffset = checked(yOffset + y.Length);
    var uOffset = checked(vOffset + v.Length);

    // Sixteen bytes of slack follow the last plane. The decoder's cell reader may run that far past
    // the final cell it consumes, so the highest plane start has to remain more than sixteen bytes
    // before the stated end of frame data. Real IV32 packets carry the same tail room.
    var dataSize = checked(uOffset + u.Length + _TRAILING_SLACK);
    var frame = new byte[checked(_OS_HEADER_LENGTH + dataSize)];

    var osHeader = frame.AsSpan(0, _OS_HEADER_LENGTH);
    BinaryPrimitives.WriteUInt32LittleEndian(osHeader, frameNumber);
    BinaryPrimitives.WriteUInt32LittleEndian(osHeader[4..], 0);
    BinaryPrimitives.WriteUInt32LittleEndian(osHeader[8..], frameNumber ^ (uint)dataSize ^ _OS_HEADER_ID);
    BinaryPrimitives.WriteUInt32LittleEndian(osHeader[12..], (uint)dataSize);

    var flags = (isPeriodicKeyFrame ? _PERIODIC_KEY_FRAME : 0)
      | (isKeyFrame ? _KEY_FRAME : 0)
      | (nextFrameIsKeyFrame ? _NEXT_FRAME_IS_KEY : 0)
      | (bufferSelect << _BUFFER_SHIFT);
    var bitstreamHeader = frame.AsSpan(_OS_HEADER_LENGTH, _BITSTREAM_HEADER_LENGTH);
    BinaryPrimitives.WriteUInt16LittleEndian(bitstreamHeader, _CODEC_VERSION);
    BinaryPrimitives.WriteUInt16LittleEndian(bitstreamHeader[2..], (ushort)flags);
    BinaryPrimitives.WriteUInt32LittleEndian(bitstreamHeader[4..], checked((uint)dataSize * 8));
    bitstreamHeader[8] = 0; // codebook offset; table zero is used in every coded cell.
    BinaryPrimitives.WriteUInt16LittleEndian(bitstreamHeader[12..], (ushort)height);
    BinaryPrimitives.WriteUInt16LittleEndian(bitstreamHeader[14..], (ushort)width);
    BinaryPrimitives.WriteUInt32LittleEndian(bitstreamHeader[16..], (uint)yOffset);
    BinaryPrimitives.WriteUInt32LittleEndian(bitstreamHeader[20..], (uint)vOffset);
    BinaryPrimitives.WriteUInt32LittleEndian(bitstreamHeader[24..], (uint)uOffset);
    // Bytes 28..31 are reserved. Bytes 32..47 are alternate quantisation selectors for modes 1/4.

    var payload = frame.AsSpan(_OS_HEADER_LENGTH);
    y.CopyTo(payload[yOffset..]);
    v.CopyTo(payload[vOffset..]);
    u.CopyTo(payload[uOffset..]);
    return frame;
  }

  /// <summary>
  /// Converts packed RGB to the seven-bit YUV 4:1:0 samples Indeo 3 actually codes.
  /// </summary>
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

  /// <summary>Writes one whole-plane intra mode-0 cell and records its decoder-side reconstruction.</summary>
  private static PlaneEncoding _EncodeIntraPlane(ReadOnlySpan<byte> samples, int width, int height) {
    var table = Indeo3Tables.Tables[0];
    var output = new byte[checked(6 + width * height / 2)];
    // Motion-vector count at bytes 0..3 is zero.
    output[4] = _WHOLE_PLANE_INTRA_CELL;
    output[5] = _MODE_ZERO_TABLE_ZERO;

    var reconstructed = new byte[checked(width * height)];
    var at = 6;

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

    return new(output, reconstructed);
  }

  /// <summary>
  /// Writes one whole-plane inter cell using motion vector 0,0 and mode 0 residuals.
  /// </summary>
  /// <remarks>
  /// The source plane from the preceding frame is kept separately from its reconstruction. If a 4x4
  /// block's source samples did not change, copying the decoder's old reconstruction is preferable to
  /// re-quantising the same desired values and making a still region shimmer. Otherwise the same VQ
  /// search as intra coding is performed, but against the previous decoded samples. Runs of copied
  /// blocks use the format's FB/FA escapes; a completely copied plane uses the VQ-tree copy leaf.
  /// </remarks>
  private static PlaneEncoding _EncodeInterPlane(
    ReadOnlySpan<byte> samples,
    ReadOnlySpan<byte> previousSource,
    ReadOnlySpan<byte> reference,
    int width,
    int height) {
    var table = Indeo3Tables.Tables[0];
    var reconstructed = new byte[checked(width * height)];
    var blockCount = checked(width / 4 * (height / 4));
    var copied = new bool[blockCount];
    var blockData = new byte[checked(blockCount * 8)];
    var blockIndex = 0;

    for (var blockY = 0; blockY < height; blockY += 4)
    for (var blockX = 0; blockX < width; blockX += 4, ++blockIndex) {
      if (_BlockEquals(samples, previousSource, width, blockX, blockY)) {
        copied[blockIndex] = true;
        _CopyBlock(reference, reconstructed, width, blockX, blockY);
        continue;
      }

      var dataAt = blockIndex * 8;
      var allZero = true;
      for (var line = 0; line < 4; ++line) {
        var target = (blockY + line) * width + blockX;
        var left = _BestDyad(
          table,
          reference[target], reference[target + 1],
          samples[target], samples[target + 1],
          out reconstructed[target], out reconstructed[target + 1]);
        var right = _BestDyad(
          table,
          reference[target + 2], reference[target + 3],
          samples[target + 2], samples[target + 3],
          out reconstructed[target + 2], out reconstructed[target + 3]);

        blockData[dataAt++] = (byte)right;
        blockData[dataAt++] = (byte)left;
        allZero &= left == 0 && right == 0;
      }

      copied[blockIndex] = allZero;
    }

    var allCopied = true;
    for (var i = 0; i < copied.Length; ++i)
      allCopied &= copied[i];

    if (allCopied)
      return new([1, 0, 0, 0, 0, 0, _WHOLE_PLANE_INTER_COPY, 0], reconstructed);

    var output = new List<byte>(checked(9 + blockCount * 8)) {
      1, 0, 0, 0, // one motion vector
      0, 0,       // vector 0 = (y: 0, x: 0)
      _WHOLE_PLANE_INTER_DATA,
      0, // vector index
      _MODE_ZERO_TABLE_ZERO,
    };

    for (var block = 0; block < blockCount;) {
      if (!copied[block]) {
        for (var i = 0; i < 8; ++i)
          output.Add(blockData[block * 8 + i]);
        ++block;
        continue;
      }

      var run = 1;
      while (run < _MAX_BLOCK_RUN && block + run < blockCount && copied[block + run])
        ++run;

      if (run == 1) {
        output.Add(_RLE_SKIP_BLOCK);
      } else {
        output.Add(_RLE_BLOCK_RUN);
        output.Add((byte)run);
      }

      block += run;
    }

    return new(output.ToArray(), reconstructed);
  }

  private static bool _BlockEquals(
    ReadOnlySpan<byte> left, ReadOnlySpan<byte> right, int width, int blockX, int blockY) {
    for (var y = 0; y < 4; ++y) {
      var at = (blockY + y) * width + blockX;
      if (!left.Slice(at, 4).SequenceEqual(right.Slice(at, 4)))
        return false;
    }

    return true;
  }

  private static void _CopyBlock(
    ReadOnlySpan<byte> source, Span<byte> destination, int width, int blockX, int blockY) {
    for (var y = 0; y < 4; ++y) {
      var at = (blockY + y) * width + blockX;
      source.Slice(at, 4).CopyTo(destination.Slice(at, 4));
    }
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

  internal readonly record struct EncodedFrame(byte[] Data, bool IsKeyFrame);

  private readonly record struct PlaneEncoding(byte[] Data, byte[] Reconstructed);
}
