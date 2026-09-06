using System;

namespace FileFormat.JpegXl.Codec;

/// <summary>
/// Writes a JPEG XL codestream (ISO/IEC 18181-1): one lossless modular frame,
/// in a single group where the picture fits one and in as many as it takes
/// otherwise.
/// </summary>
/// <remarks>
/// The layout is the one the decoder in this folder reads, field for field:
/// signature, <c>SizeHeader</c>, <c>ImageMetadata</c>, <c>CustomTransformData</c>,
/// byte alignment, frame header, table of contents, then the frame's sections.
/// The first section opens with the DC quantisation bundle every frame carries
/// whether or not it has any DC, then the frame's global modular setup — a
/// one-leaf decision tree and the code its residuals are stated in — then the
/// group header of the global stream.
///
/// <para>A picture that fits one group is one section and the residuals follow
/// that group header directly. A larger one is cut into groups of a thousand and
/// twenty-four pixels a side, and then the global stream carries no samples at
/// all — every channel is bigger than a group — while each group states its own
/// group header and its own run of residuals at its own offset in the frame. The
/// sections between the two, one per low-frequency group and one for the
/// high-frequency global data, belong to frames coded in the other mode and are
/// empty here, but the table of contents states them all the same because that
/// is what fixes where the groups begin.</para>
///
/// <para>The samples go in as they are, with no colour transform and no
/// wavelet: each channel is predicted from its neighbours and only the
/// difference is coded, so what comes back out is what went in. Prediction
/// starts afresh in every group, because a group is decodable on its own and its
/// neighbours are not there to be predicted from.</para>
/// </remarks>
internal static class JxlCodestreamEncoder {

  /// <summary>
  /// The largest group the format has, which is what a picture too big for one
  /// group is cut into: fewer groups means fewer sections and fewer places the
  /// prediction restarts.
  /// </summary>
  private const uint _LargestGroupSizeShift = 3;

  /// <summary>How many groups a low-frequency group is across (libjxl <c>kLfGroupDim</c>).</summary>
  private const int _GroupsPerLfGroupSide = 8;

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

    var gray = componentCount is 1 or 2;
    var hasAlpha = componentCount is 2 or 4;
    var channels = _Deinterleave(pixelData, width, height, componentCount, bitsPerSample);

    var groupSizeShift = _GroupSizeShift(width, height);
    var groupDim = 128 << (int)groupSizeShift;
    var groupsX = (width + groupDim - 1) / groupDim;
    var groupsY = (height + groupDim - 1) / groupDim;
    var lfGroupDim = groupDim * _GroupsPerLfGroupSide;
    var lfGroups = ((width + lfGroupDim - 1) / lfGroupDim) * ((height + lfGroupDim - 1) / lfGroupDim);

    var sections = groupsX * groupsY == 1
      ? [_EncodeSingleGroup(channels, width, height)]
      : _EncodeGroups(channels, width, height, groupDim, groupsX, groupsY, lfGroups);

    var bodyLength = 0;
    foreach (var section in sections)
      bodyLength = checked(bodyLength + section.Length);

    var writer = new JxlBitWriter(64 + 4 * sections.Length);
    writer.WriteBits(0xFF, 8);
    writer.WriteBits(0x0A, 8);
    _WriteSizeHeader(writer, width, height);
    _WriteImageMetadata(writer, gray, hasAlpha, bitsPerSample);
    writer.WriteBool(true); // CustomTransformData: all default
    writer.ZeroPadToByte();
    _WriteFrameHeader(writer, hasAlpha ? 1 : 0, groupSizeShift);

    // Table of contents: every section's length, in canonical order, which is
    // what tells the decoder where each group begins.
    writer.WriteBool(false); // not permuted
    writer.ZeroPadToByte();
    foreach (var section in sections)
      _WriteU32(writer, (uint)section.Length, 0, 10, 1024, 14, 17408, 22, 4211712, 30);
    writer.ZeroPadToByte();

    var head = writer.ToArray();
    var result = new byte[checked(head.Length + bodyLength)];
    head.CopyTo(result, 0);
    var at = head.Length;
    foreach (var section in sections) {
      section.CopyTo(result, at);
      at += section.Length;
    }
    return result;
  }

  /// <summary>
  /// The frame's one section when the picture fits a single group: the DC
  /// quantisation bundle, the global modular setup, the group header and the
  /// residuals.
  /// </summary>
  private static byte[] _EncodeSingleGroup(int[][] channels, int width, int height) {
    var writer = new JxlBitWriter(width * height * channels.Length + 256);

    // Every frame carries this bundle, modular ones included, and leaving it out
    // puts the whole frame body one bit early.
    writer.WriteBool(true); // DC quantisation: all default

    writer.WriteBool(true); // the frame states a tree of its own
    _WriteTree(writer);

    // The residuals are gathered first because the code they are stated in
    // depends on which of them there are.
    var residuals = new JxlTokenStream(checked(width * height * channels.Length));
    foreach (var channel in channels)
      _CollectResiduals(residuals, channel, width, 0, 0, width, height);
    residuals.WriteHeader(writer, contextCount: 1);

    _WriteGroupHeader(writer);
    residuals.WriteTokens(writer);
    writer.ZeroPadToByte();
    return writer.ToArray();
  }

  /// <summary>
  /// The frame's sections when the picture takes more than one group.
  /// </summary>
  /// <remarks>
  /// Section zero carries what the whole frame shares — the DC quantisation
  /// bundle, the tree, the one code every group's residuals are stated in, and
  /// the group header of a global stream that carries no samples, because at this
  /// point every channel is larger than a group and the decoder stops at the
  /// first that is. The low-frequency group sections and the high-frequency
  /// global one after it hold what a frame coded in the other mode would put
  /// there and are empty; they are still stated, because the offsets of the
  /// group sections are the sum of everything before them.
  ///
  /// <para>Then one section per group, each a group header of its own followed by
  /// that group's residuals — channel by channel, and within a channel row by row
  /// across the part of it the group covers.</para>
  /// </remarks>
  private static byte[][] _EncodeGroups(
    int[][] channels,
    int width,
    int height,
    int groupDim,
    int groupsX,
    int groupsY,
    int lfGroups
  ) {
    var groups = groupsX * groupsY;

    // One block of residuals for the whole frame, in one run per group: the code
    // is stated once and each run is written where its group's section is.
    var residuals = new JxlTokenStream(checked(width * height * channels.Length));
    for (var group = 0; group < groups; ++group) {
      residuals.BeginRun();
      var originX = group % groupsX * groupDim;
      var originY = group / groupsX * groupDim;
      var rectWidth = Math.Min(groupDim, width - originX);
      var rectHeight = Math.Min(groupDim, height - originY);
      foreach (var channel in channels)
        _CollectResiduals(residuals, channel, width, originX, originY, rectWidth, rectHeight);
    }

    var sections = new byte[2 + lfGroups + groups][];

    var global = new JxlBitWriter(1024);
    global.WriteBool(true); // DC quantisation: all default
    global.WriteBool(true); // the frame states a tree of its own
    _WriteTree(global);
    residuals.WriteHeader(global, contextCount: 1);
    _WriteGroupHeader(global);
    global.ZeroPadToByte();
    sections[0] = global.ToArray();

    for (var section = 1; section < 2 + lfGroups; ++section)
      sections[section] = [];

    for (var group = 0; group < groups; ++group) {
      var originX = group % groupsX * groupDim;
      var originY = group / groupsX * groupDim;
      var rectWidth = Math.Min(groupDim, width - originX);
      var rectHeight = Math.Min(groupDim, height - originY);

      var writer = new JxlBitWriter(rectWidth * rectHeight * channels.Length + 64);
      _WriteGroupHeader(writer);
      residuals.WriteRun(writer, group);
      writer.ZeroPadToByte();
      sections[2 + lfGroups + group] = writer.ToArray();
    }

    return sections;
  }

  /// <summary>
  /// The header every modular stream opens with, which says the stream uses the
  /// frame's tree, leaves the weighted predictor at its defaults and applies no
  /// transform.
  /// </summary>
  private static void _WriteGroupHeader(JxlBitWriter writer) {
    writer.WriteBool(true); // use the frame's tree
    writer.WriteBool(true); // weighted-predictor parameters: all default
    writer.WriteBits(0, 2); // no transforms
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
  /// Walk one group's worth of a channel in the order the decoder reads it,
  /// predicting each sample from the ones already written and handing the
  /// difference to the block.
  /// </summary>
  /// <remarks>
  /// The neighbourhood is the format's, not a choice: the pixel to the left
  /// stands in for the one above at the start of a row and the other way round
  /// at the start of the picture, and a single neighbour taken differently here
  /// than in the reader puts every sample after it out.
  ///
  /// <para>The rectangle is the group's, and the neighbourhood stops at its
  /// edges: a group is decoded on its own into a buffer that holds nothing but
  /// itself, so the row above the group's first row is not the picture's, it is
  /// absent, and the first sample of a group is predicted from nothing however
  /// far into the picture the group sits.</para>
  /// </remarks>
  private static void _CollectResiduals(
    JxlTokenStream stream,
    int[] pixels,
    int stride,
    int originX,
    int originY,
    int rectWidth,
    int rectHeight
  ) {
    for (var y = 0; y < rectHeight; ++y) {
      var row = (originY + y) * stride + originX;
      var above = row - stride;
      for (var x = 0; x < rectWidth; ++x) {
        var west = x > 0
          ? pixels[row + x - 1]
          : y > 0
            ? pixels[above + x]
            : 0;
        var north = y > 0 ? pixels[above + x] : west;
        var northWest = x > 0 && y > 0 ? pixels[above + x - 1] : west;
        stream.Add(JxlTokenStream.PackSigned(pixels[row + x] - _ClampedGradient(north, west, northWest)));
      }
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
    var count = checked(width * height);
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

  /// <summary>
  /// The smallest group size that still holds the whole picture, or the largest
  /// the format has when none does.
  /// </summary>
  private static uint _GroupSizeShift(int width, int height) {
    var longest = Math.Max(width, height);
    for (var shift = 0u; shift < _LargestGroupSizeShift; ++shift)
      if (128 << (int)shift >= longest)
        return shift;
    return _LargestGroupSizeShift;
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
      _WriteAlphaChannelInfo(writer, bitsPerSample);

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

  /// <summary>
  /// The one extra channel a picture with alpha carries.
  /// </summary>
  /// <remarks>
  /// An extra channel keeps its own sample depth, and the depth it defaults to is
  /// eight bits whatever the colour channels are. Stating nothing therefore reads
  /// a sixteen-bit alpha plane back as an eight-bit one: the samples decode as
  /// they were written and are then scaled from the wrong range, so half of a
  /// grey-and-alpha picture comes out wrong while the grey half is exact. Eight
  /// bits is still stated by saying nothing, which is what libjxl writes and what
  /// keeps an eight-bit file to the shortest header.
  /// </remarks>
  private static void _WriteAlphaChannelInfo(JxlBitWriter writer, int bitsPerSample) {
    if (bitsPerSample <= 8) {
      writer.WriteBool(true); // all default: a plain eight-bit alpha
      return;
    }

    writer.WriteBool(false); // not all default
    _WriteEnum(writer, 0);   // alpha
    writer.WriteBool(false); // integer samples
    _WriteU32(writer, (uint)bitsPerSample, 8, 0, 10, 0, 12, 0, 1, 6);
    _WriteU32(writer, 0, 0, 0, 3, 0, 4, 0, 1, 3);   // no dimension shift
    _WriteU32(writer, 0, 0, 0, 0, 4, 16, 5, 48, 10); // no name
    writer.WriteBool(false); // not premultiplied
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
