using System;

namespace FileFormat.JpegXl.Codec;

/// <summary>
/// Writes a JPEG XL codestream (ISO/IEC 18181-1): a single lossless modular
/// frame in one group, which is the smallest arrangement the format defines
/// that carries a picture back sample for sample.
/// </summary>
/// <remarks>
/// The layout is the one the decoder in this folder reads, field for field:
/// signature, <c>SizeHeader</c>, <c>ImageMetadata</c>, <c>CustomTransformData</c>,
/// byte alignment, frame header, table of contents, then the frame body. The
/// body opens with the DC quantisation bundle every frame carries whether or not
/// it has any DC, then the frame's global modular setup — a one-leaf decision
/// tree and the code its residuals are stated in — then the group header and the
/// residuals themselves.
///
/// <para>The samples go in as they are, with no colour transform and no
/// wavelet: each channel is predicted from its neighbours and only the
/// difference is coded, so what comes back out is what went in.</para>
/// </remarks>
internal static class JxlCodestreamEncoder {

  /// <summary>
  /// The largest picture this writes. A frame is cut into groups of at most a
  /// thousand and twenty-four pixels a side, and a picture that needs more than
  /// one of them has to state each separately.
  /// </summary>
  public const int MaxDimension = 1024;

  /// <summary>
  /// The predictor every leaf of the tree names: the gradient of the pixel
  /// above, the pixel to the left and the corner between them, clamped to lie
  /// between the first two (libjxl <c>Predictor::Gradient</c>).
  /// </summary>
  private const int _GradientPredictor = 5;

  /// <summary>libjxl <c>MATreeContext</c>: the six contexts a tree is stated in.</summary>
  private const int _TreeContextCount = 6;

  /// <summary>
  /// Assemble a complete bare codestream.
  /// </summary>
  /// <param name="pixelData">Interleaved samples, one or two bytes each.</param>
  /// <param name="width">Picture width in pixels.</param>
  /// <param name="height">Picture height in pixels.</param>
  /// <param name="componentCount">1 grey, 2 grey and alpha, 3 colour, 4 colour and alpha.</param>
  /// <param name="bitsPerSample">8, or 16 for two big-endian bytes per sample.</param>
  public static byte[] Encode(byte[] pixelData, int width, int height, int componentCount, int bitsPerSample) {
    ArgumentNullException.ThrowIfNull(pixelData);
    if (width <= 0 || height <= 0)
      throw new ArgumentOutOfRangeException(nameof(width), "A picture needs a positive width and height.");
    if (componentCount is < 1 or > 4)
      throw new ArgumentOutOfRangeException(nameof(componentCount), "A picture has one to four components.");
    if (bitsPerSample is not (8 or 16))
      throw new ArgumentOutOfRangeException(nameof(bitsPerSample), "Only eight and sixteen bits per sample are written.");
    if (width > MaxDimension || height > MaxDimension)
      throw new NotSupportedException(
        $"This JPEG XL writer states a picture in one group, so it goes up to {MaxDimension} by {MaxDimension}; this one is {width} by {height}.");

    var gray = componentCount is 1 or 2;
    var hasAlpha = componentCount is 2 or 4;
    var channels = _Deinterleave(pixelData, width, height, componentCount, bitsPerSample);
    var body = _EncodeFrameBody(channels, width, height);

    var writer = new JxlBitWriter(body.Length + 64);
    writer.WriteBits(0xFF, 8);
    writer.WriteBits(0x0A, 8);
    _WriteSizeHeader(writer, width, height);
    _WriteImageMetadata(writer, gray, hasAlpha, bitsPerSample);
    writer.WriteBool(true); // CustomTransformData: all default
    writer.ZeroPadToByte();
    _WriteFrameHeader(writer, hasAlpha ? 1 : 0, _GroupSizeShift(width, height));

    // Table of contents: one section, stated in canonical order.
    writer.WriteBool(false); // not permuted
    writer.ZeroPadToByte();
    _WriteU32(writer, (uint)body.Length, 0, 10, 1024, 14, 17408, 22, 0, 30);
    writer.ZeroPadToByte();

    var head = writer.ToArray();
    var result = new byte[head.Length + body.Length];
    head.CopyTo(result, 0);
    body.CopyTo(result, head.Length);
    return result;
  }

  /// <summary>
  /// The frame's one section: the DC quantisation bundle, the global modular
  /// setup, the group header and the residuals.
  /// </summary>
  private static byte[] _EncodeFrameBody(int[][] channels, int width, int height) {
    var writer = new JxlBitWriter(width * height * channels.Length + 256);

    // Every frame carries this bundle, modular ones included, and leaving it out
    // puts the whole frame body one bit early.
    writer.WriteBool(true); // DC quantisation: all default

    writer.WriteBool(true); // the frame states a tree of its own
    _WriteTree(writer);

    // The residuals are gathered first because the code they are stated in
    // depends on which of them there are.
    var residuals = new JxlTokenStream();
    foreach (var channel in channels)
      _CollectResiduals(residuals, channel, width, height);
    residuals.WriteHeader(writer, contextCount: 1);

    // GroupHeader, which sits between the frame's global setup and its samples.
    writer.WriteBool(true); // use the frame's tree
    writer.WriteBool(true); // weighted-predictor parameters: all default
    writer.WriteBits(0, 2); // no transforms

    residuals.WriteTokens(writer);
    writer.ZeroPadToByte();
    return writer.ToArray();
  }

  /// <summary>
  /// Write the decision tree the residuals are read through: one leaf, so every
  /// sample of every channel shares a single context and a single predictor.
  /// </summary>
  private static void _WriteTree(JxlBitWriter writer) {
    var tree = new JxlTokenStream();
    tree.Add(0);                                   // property index + 1 = 0, i.e. this node is a leaf
    tree.Add(_GradientPredictor);                  // the leaf's predictor
    tree.Add(JxlTokenStream.PackSigned(0));        // the offset it adds
    tree.Add(0);                                   // the multiplier's exponent
    tree.Add(0);                                   // and its mantissa, so the multiplier is one
    tree.WriteHeader(writer, _TreeContextCount);
    tree.WriteTokens(writer);
  }

  /// <summary>
  /// Walk one channel in the order the decoder reads it, predicting each sample
  /// from the ones already written and handing the difference to the block.
  /// </summary>
  /// <remarks>
  /// The neighbourhood is the format's, not a choice: the pixel to the left
  /// stands in for the one above at the start of a row and the other way round
  /// at the start of the picture, and a single neighbour taken differently here
  /// than in the reader puts every sample after it out.
  /// </remarks>
  private static void _CollectResiduals(JxlTokenStream stream, int[] pixels, int width, int height) {
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var west = x > 0
        ? pixels[y * width + x - 1]
        : y > 0
          ? pixels[(y - 1) * width + x]
          : 0;
      var north = y > 0 ? pixels[(y - 1) * width + x] : west;
      var northWest = x > 0 && y > 0 ? pixels[(y - 1) * width + x - 1] : west;
      stream.Add(JxlTokenStream.PackSigned(pixels[y * width + x] - _ClampedGradient(north, west, northWest)));
    }
  }

  /// <summary>libjxl <c>ClampedGradient</c>: the gradient held between the two
  /// neighbours it was built from.</summary>
  private static int _ClampedGradient(int north, int west, int northWest) {
    var low = Math.Min(north, west);
    var high = Math.Max(north, west);
    return Math.Clamp(north + west - northWest, low, high);
  }

  /// <summary>Split interleaved samples into one array per channel.</summary>
  private static int[][] _Deinterleave(byte[] pixelData, int width, int height, int componentCount, int bitsPerSample) {
    var count = width * height;
    var deep = bitsPerSample > 8;
    var stride = componentCount * (deep ? 2 : 1);
    var needed = checked((long)count * stride);
    if (pixelData.LongLength < needed)
      throw new ArgumentException(
        $"A {width} by {height} picture of {componentCount} components at {bitsPerSample} bits needs {needed} bytes, and {pixelData.LongLength} were given.",
        nameof(pixelData));

    var channels = new int[componentCount][];
    for (var c = 0; c < componentCount; ++c) {
      var samples = new int[count];
      for (var i = 0; i < count; ++i) {
        var at = i * stride + c * (deep ? 2 : 1);
        samples[i] = deep ? (pixelData[at] << 8) | pixelData[at + 1] : pixelData[at];
      }
      channels[c] = samples;
    }
    return channels;
  }

  /// <summary>The smallest group size that still holds the whole picture.</summary>
  private static uint _GroupSizeShift(int width, int height) {
    var longest = Math.Max(width, height);
    for (var shift = 0u; shift < 4u; ++shift)
      if (128 << (int)shift >= longest)
        return shift;
    throw new NotSupportedException($"A picture {longest} pixels along does not fit in one group.");
  }

  /// <summary>
  /// The picture's size, either in whole eighths when both sides allow it or
  /// spelled out.
  /// </summary>
  /// <remarks>
  /// The width may also be left out and derived from one of seven aspect ratios,
  /// which is what makes the shortest headers libjxl writes. Stating it outright
  /// costs a handful of bits and is right for every size.
  /// </remarks>
  private static void _WriteSizeHeader(JxlBitWriter writer, int width, int height) {
    if (_FitsSmall(width) && _FitsSmall(height)) {
      writer.WriteBool(true);
      writer.WriteBits((uint)(height / 8 - 1), 5);
      writer.WriteBits(0, 3); // no aspect ratio, so the width follows
      writer.WriteBits((uint)(width / 8 - 1), 5);
      return;
    }

    writer.WriteBool(false);
    _WriteDimension(writer, height);
    writer.WriteBits(0, 3);
    _WriteDimension(writer, width);
  }

  private static bool _FitsSmall(int value) => value % 8 == 0 && value is >= 8 and <= 256;

  private static void _WriteDimension(JxlBitWriter writer, int value)
    => _WriteU32(writer, (uint)value, 1, 9, 1, 13, 1, 18, 1, 30);

  private static void _WriteImageMetadata(JxlBitWriter writer, bool gray, bool hasAlpha, int bitsPerSample) {
    writer.WriteBool(false); // not all default
    writer.WriteBool(false); // no orientation, preview, animation or intrinsic size

    writer.WriteBool(false); // integer samples
    _WriteU32(writer, (uint)bitsPerSample, 8, 0, 10, 0, 12, 0, 1, 6);
    writer.WriteBool(bitsPerSample <= 8); // sixteen-bit modular buffers suffice at eight bits

    _WriteU32(writer, hasAlpha ? 1u : 0u, 0, 0, 1, 0, 2, 4, 1, 12);
    if (hasAlpha)
      writer.WriteBool(true); // the extra channel is a plain eight-bit alpha

    writer.WriteBool(false); // samples are stated as they are, not in XYB

    if (gray) {
      writer.WriteBool(false); // the colour encoding is stated
      writer.WriteBool(false); // no embedded profile
      _WriteEnum(writer, 1);   // grey
      _WriteEnum(writer, 1);   // D65
      writer.WriteBool(false); // the transfer function is named, not a gamma
      _WriteEnum(writer, 13);  // sRGB
      _WriteEnum(writer, 1);   // relative colorimetric
    } else
      writer.WriteBool(true); // the colour encoding is sRGB, which is the default

    writer.WriteBits(0, 2); // no extensions
  }

  private static void _WriteFrameHeader(JxlBitWriter writer, int extraChannels, uint groupSizeShift) {
    writer.WriteBool(false); // not all default
    writer.WriteBits(0, 2);  // a regular frame
    writer.WriteBool(true);  // coded in modular mode
    writer.WriteBits(0, 2);  // no flags
    writer.WriteBool(false); // no colour transform

    _WriteU32(writer, 1, 1, 0, 2, 0, 4, 0, 8, 0); // not upsampled
    for (var i = 0; i < extraChannels; ++i)
      _WriteU32(writer, 1, 1, 0, 2, 0, 4, 0, 8, 0);

    writer.WriteBits(groupSizeShift, 2);
    _WriteU32(writer, 1, 1, 0, 2, 0, 3, 0, 4, 3); // one pass
    writer.WriteBool(false);                      // the frame covers the picture

    _WriteBlendingInfo(writer);
    for (var i = 0; i < extraChannels; ++i)
      _WriteBlendingInfo(writer);

    writer.WriteBool(true); // the last frame
    _WriteU32(writer, 0, 0, 0, 0, 4, 16, 5, 48, 10); // no name

    // The loop filter is stated rather than defaulted, because its defaults turn
    // on smoothing and two passes of edge preservation, and a lossless frame
    // wants neither.
    writer.WriteBool(false); // not all default
    writer.WriteBool(false); // no smoothing filter
    writer.WriteBits(0, 2);  // no edge-preserving passes
    writer.WriteBits(0, 2);  // no loop-filter extensions

    writer.WriteBits(0, 2); // no frame extensions
  }

  /// <summary>The frame replaces what is under it, which needs nothing else stated.</summary>
  private static void _WriteBlendingInfo(JxlBitWriter writer)
    => _WriteU32(writer, 0, 0, 0, 1, 0, 2, 0, 3, 2);

  /// <summary>libjxl <c>Visitor::Enum</c>: zero, one, two to seventeen, or
  /// eighteen to eighty-one.</summary>
  private static void _WriteEnum(JxlBitWriter writer, uint value)
    => _WriteU32(writer, value, 0, 0, 1, 0, 2, 4, 18, 6);

  /// <summary>
  /// Write a value in the format's four-way variable-length form, taking the
  /// first of the four shapes that holds it.
  /// </summary>
  private static void _WriteU32(
    JxlBitWriter writer,
    uint value,
    uint c0, int u0,
    uint c1, int u1,
    uint c2, int u2,
    uint c3, int u3
  ) {
    Span<uint> constants = [c0, c1, c2, c3];
    Span<int> widths = [u0, u1, u2, u3];
    for (var selector = 0; selector < 4; ++selector) {
      var constant = constants[selector];
      if (value < constant)
        continue;
      var width = widths[selector];
      var span = width >= 32 ? uint.MaxValue : (1u << width) - 1u;
      if (value - constant > span)
        continue;

      writer.WriteBits((uint)selector, 2);
      if (width > 0)
        writer.WriteBits(value - constant, width);
      return;
    }

    throw new ArgumentOutOfRangeException(nameof(value), $"The value {value} does not fit any of this field's four shapes.");
  }
}
