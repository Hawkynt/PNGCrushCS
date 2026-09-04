using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Codecs.QuickTimeRle;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes QuickTime Animation, the codec every container calls <c>rle </c>: each line as runs,
/// literal pixels and skips over whatever the frame before left there.
/// </summary>
/// <remarks>
/// Adapted from FFmpeg's <c>libavcodec/qtrleenc.c</c>, copyright (c) 2007 Clemens Fruhwirth and
/// Alexis Ballier, distributed there under LGPL-2.1-or-later. This adaptation is distributed with
/// PNGCrushCS under LGPL-3.0-or-later.
/// <para/>
/// A frame first says which band of lines it touches — the lines from the first that differs from
/// the frame before to the last that does — and then writes each of those lines as the cheapest
/// sequence of three opcodes: a literal copy of up to 127 units, one unit repeated up to 128 times,
/// or a skip of up to 254 units that were the same in the frame before. Which of the three is
/// cheapest at each position is not a local decision, because a run worth taking at one pixel may
/// leave the next few pixels more expensive to code; so each line is walked from its end to its
/// start, the cost of the best coding from every position to the end is kept, and the opcodes are
/// read off that table from the front. That is the reference encoder's dynamic programme and it is
/// used unchanged, save that the best literal copy is found with an exact sliding minimum where the
/// reference keeps a best and a second-best candidate.
/// <para/>
/// <b>Every count is in coded units, not pixels</b>, as <see cref="QuickTimeRleDecoder"/> reads them:
/// a unit is one pixel at twenty-four and thirty-two bits and four pixels at eight. A picture whose
/// width is not a whole number of units is coded with padding on the right of every line, which the
/// decoder crops back off; the padding is index zero and never changes, so it costs a skip and no more.
/// <para/>
/// <b>Lossless, at three depths.</b> <see cref="PixelFormat.Rgb24"/> is written at twenty-four bits,
/// <see cref="PixelFormat.Rgba32"/> at thirty-two — QuickTime's pixel is ARGB, so the alpha moves to
/// the front — and <see cref="PixelFormat.Indexed8"/> with its palette at eight bits, with the
/// palette in the sample description's colour table where the decoder reads it. Everything else is
/// refused by name. Sixteen bits in particular is not written: the format stores five bits a channel
/// and there is no sixteen-bit RGB picture here to take those from, so the only way to write it
/// would be to round, and a codec that says "lossless" must not.
/// <para/>
/// The first frame is written whole and flagged as a key frame. Every frame after it is written
/// against the one before, and flagged as a key frame only when it happened to cover every line
/// without a single skip — in which case a decoder really can start there. A frame identical to the
/// one before it is the format's seven-byte "nothing changed" frame.
/// </remarks>
public sealed class QuickTimeRleEncoder : IVideoCodecEncoder<QuickTimeRleEncoder> {

  /// <summary>The code every container names this codec with, trailing space and all.</summary>
  private static readonly CodecTag _RLE = CodecTag.FromCharacters("rle ");

  /// <summary>The flag in a frame's header that says a band of lines follows.</summary>
  private const ushort _HAS_LINE_RANGE = 0x0008;

  /// <summary>The most units one literal copy can state.</summary>
  private const int _LONGEST_COPY = 127;

  /// <summary>The most times one run can repeat its unit.</summary>
  private const int _LONGEST_RUN = 128;

  /// <summary>The furthest one skip can step.</summary>
  private const int _LONGEST_SKIP = 254;

  /// <summary>The opcode that ends a line.</summary>
  private const byte _END_OF_LINE = 0xFF;

  /// <summary>The identifier a description states when it carries no colour table.</summary>
  private const ushort _NO_COLOUR_TABLE = 0xFFFF;

  private readonly MediaStreamInfo _requested;
  private readonly int _width;
  private readonly int _height;

  private int _depth;
  private int _unitBytes;
  private int _unitPixels;
  private int _bytesPerPixel;
  private int _codedWidth;
  private int _lineUnits;
  private int _lineBytes;
  private byte[]? _palette;
  private int _paletteCount;
  private MediaStreamInfo? _stream;

  /// <summary>The picture before, in coded layout — padded lines of coded units — or null before the first.</summary>
  private byte[]? _previous;

  /// <summary>The cost, in bytes, of the best coding from each unit of a line to its end.</summary>
  private int[] _cost = [];

  /// <summary>The opcode chosen at each unit: zero to skip, negative to repeat, positive to copy.</summary>
  private sbyte[] _opcode = [];

  /// <summary>How many units from each one onwards are the same as the frame before.</summary>
  private byte[] _skip = [];

  /// <summary>A sliding window over the units a literal copy could run to, cheapest first.</summary>
  private int[] _window = [];

  private byte[] _buffer = new byte[4096];
  private int _length;

  private QuickTimeRleEncoder(MediaStreamInfo stream, int depth) {
    this._requested = stream;
    this._width = stream.Width;
    this._height = stream.Height;

    if (depth != 0)
      this._TakeLayout(depth);
    if (depth == 8)
      this._AdoptPaletteFrom(stream.CodecPrivateData.Span);
  }

  public static string CodecName => "QuickTime Animation (RLE)";

  public static CodecTag Codec => _RLE;

  /// <summary>
  /// Builds an encoder for the stream described, refusing a depth the encoder does not write.
  /// </summary>
  /// <remarks>
  /// The depth is taken from the description where it states one and from the first picture where
  /// it does not. An eight-bit description that already carries a sample entry with a colour table
  /// — one that came out of a demuxer, say — lends its palette, so that the stream can be described
  /// before a single picture has been seen.
  /// </remarks>
  public static QuickTimeRleEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("QuickTime Animation can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"A QuickTime Animation encoder needs the picture size up front; {stream.Width}x{stream.Height} was supplied.");
    if (stream.Width > ushort.MaxValue || stream.Height > ushort.MaxValue)
      throw new NotSupportedException(
        $"A picture of {stream.Width}x{stream.Height} does not fit the sixteen-bit size fields of a QuickTime sample description.");
    if (stream.BitsPerPixel is not (0 or 8 or 24 or 32))
      throw new NotSupportedException(
        $"Video stream {stream.Index} asks for {stream.BitsPerPixel} bits per pixel. This encoder writes QuickTime "
        + "Animation at eight bits through a colour table, at twenty-four and at thirty-two, and nothing else.");

    return new(stream, stream.BitsPerPixel);
  }

  /// <summary>
  /// Codes one picture against the one before it, or whole when there is none.
  /// </summary>
  /// <remarks>
  /// Always produces a packet: this codec has no frame it holds back, and a picture identical to
  /// the one before it is written as the format's own "nothing changed" frame.
  /// </remarks>
  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"QuickTime Animation geometry is fixed at {this._width}x{this._height}; received {frame.Width}x{frame.Height}.");

    this._TakeDepthFrom(frame);
    if (this._depth == 8)
      this._TakePaletteFrom(frame);
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException(
        "The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var coded = this._Coded(frame);
    var keyFrame = this._previous == null;
    this._length = 0;
    var wholePicture = this._EncodeFrame(coded, keyFrame);
    this._previous = coded;

    packet = new(
      this._requested.Index,
      this._buffer.AsSpan(0, this._length).ToArray(),
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: wholePicture);
    return true;
  }

  /// <summary>
  /// The stream as a muxer needs it: a whole <c>rle </c> visual sample entry, with the colour table
  /// inside it at eight bits.
  /// </summary>
  /// <remarks>
  /// That is what a QuickTime or MP4 <c>stsd</c> carries and what <see cref="QuickTimeRleDecoder"/>
  /// reads its depth and palette out of. At eight bits the palette is not known until the first
  /// picture has been seen unless the description handed in carried one, so asking before then is
  /// refused rather than answered with an entry naming no colours.
  /// </remarks>
  public MediaStreamInfo DescribeStream() {
    if (this._stream != null)
      return this._stream;

    if (this._depth == 0)
      throw new InvalidOperationException(
        "A QuickTime Animation stream cannot be described before its depth is known. Encode the first picture first, "
        + "or hand Create a stream that states BitsPerPixel.");
    if (this._depth == 8 && this._palette == null)
      throw new InvalidOperationException(
        "An eight-bit QuickTime Animation stream cannot be described before its palette is known. Encode the first "
        + "picture first, or hand Create a stream whose CodecPrivateData is a sample entry with a colour table.");

    return this._stream = new() {
      Index = this._requested.Index,
      Kind = MediaStreamKind.Video,
      Codec = _RLE,
      Handler = _RLE,
      TimeBase = this._requested.TimeBase,
      FrameRate = this._requested.FrameRate,
      DeclaredFrameCount = this._requested.DeclaredFrameCount,
      Width = this._width,
      Height = this._height,
      BitsPerPixel = this._depth,
      CodecPrivateData = this._SampleEntry(),
      Language = this._requested.Language,
      Name = this._requested.Name,
    };
  }

  // ============================================================================================
  // What goes in
  // ============================================================================================

  /// <summary>Fixes the depth from the first picture, or checks a later one against it.</summary>
  private void _TakeDepthFrom(RawImage frame) {
    var depth = frame.Format switch {
      PixelFormat.Indexed8 => 8,
      PixelFormat.Rgb24 => 24,
      PixelFormat.Rgba32 => 32,
      _ => throw new NotSupportedException(
        $"QuickTime Animation is written here from Rgb24, Rgba32 and Indexed8 pictures only; a {frame.Format} picture "
        + "would have to be converted first, and whether that conversion may lose anything is not this codec's decision."),
    };

    if (this._depth == 0) {
      this._TakeLayout(depth);
      return;
    }

    if (depth != this._depth)
      throw new InvalidDataException(
        $"The stream is coded at {this._depth} bits and a {frame.Format} picture is {depth}; the depth is stated in "
        + "the sample description and cannot change between frames.");
  }

  /// <summary>Settles the unit size and the padded line width a depth is coded with.</summary>
  private void _TakeLayout(int depth) {
    this._depth = depth;
    (this._unitBytes, this._unitPixels) = depth switch {
      8 => (4, 4),
      24 => (3, 1),
      _ => (4, 1),
    };
    this._bytesPerPixel = this._unitBytes / this._unitPixels;
    this._codedWidth = (this._width + this._unitPixels - 1) / this._unitPixels * this._unitPixels;
    this._lineUnits = this._codedWidth / this._unitPixels;
    this._lineBytes = this._lineUnits * this._unitBytes;

    this._cost = new int[this._lineUnits + 1];
    this._opcode = new sbyte[this._lineUnits];
    this._skip = new byte[this._lineUnits];
    this._window = new int[this._lineUnits + 1];
  }

  /// <summary>Fixes the palette from the first picture, or checks a later one against it.</summary>
  /// <remarks>
  /// The colour table is in the sample description, once, so every frame has to be drawn through the
  /// same one. A picture bringing another is refused rather than written through the first — its
  /// indices would decode to the wrong colours and nothing in the file would say so.
  /// </remarks>
  private void _TakePaletteFrom(RawImage frame) {
    if (frame.Palette == null || frame.PaletteCount <= 0)
      throw new InvalidDataException(
        "A palettised picture without a palette cannot be coded: the frames hold indices and the sample description "
        + "holds the colours, and there are none to put there.");

    var entries = Math.Min(frame.PaletteCount, 256);
    if (frame.Palette.Length < entries * 3)
      throw new InvalidDataException(
        $"The picture states a palette of {frame.PaletteCount} entries but carries {frame.Palette.Length / 3}.");

    if (this._palette == null) {
      this._palette = frame.Palette.AsSpan(0, entries * 3).ToArray();
      this._paletteCount = entries;
      return;
    }

    if (entries == this._paletteCount && frame.Palette.AsSpan(0, entries * 3).SequenceEqual(this._palette))
      return;

    throw new InvalidDataException(
      "The picture carries a different palette from the one the stream was described with. The colour table is "
      + "stated once in the sample description, so it cannot change between frames.");
  }

  /// <summary>Takes a palette out of a sample description that already carries a colour table.</summary>
  private void _AdoptPaletteFrom(ReadOnlySpan<byte> description) {
    if (description.IsEmpty)
      return;

    var table = QuickTimeRleSampleDescription.ColourTable(description);
    if (table.IsEmpty)
      return;

    // The layout reads the table into a full 256-entry palette; only the entries the table states
    // are kept, so that the description written back names the same colours and no more.
    var (palette, _) = QuickTimeRleLayout.ForDepth(8, this._requested.Index).BuildPalette(table, this._requested.Index);
    var stated = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(6, 2)) + 1;
    this._palette = palette.AsSpan(0, stated * 3).ToArray();
    this._paletteCount = stated;
  }

  /// <summary>
  /// The picture in the layout the lines are coded in: padded to whole units, with the channels in
  /// the order the stream stores them.
  /// </summary>
  /// <remarks>
  /// At eight bits an index past the end of the palette is refused here. The colour table states
  /// how many colours there are and the decoder fills the rest with black, so such an index would
  /// decode to a colour nothing chose.
  /// </remarks>
  private byte[] _Coded(RawImage frame) {
    var coded = new byte[this._lineBytes * this._height];
    var source = frame.PixelData;
    var visible = this._width * this._bytesPerPixel;

    switch (this._depth) {
      case 8:
        for (var y = 0; y < this._height; ++y)
          source.AsSpan(y * visible, visible).CopyTo(coded.AsSpan(y * this._lineBytes));

        for (var y = 0; y < this._height; ++y)
        for (var x = 0; x < this._width; ++x) {
          var index = coded[y * this._lineBytes + x];
          if (index >= this._paletteCount)
            throw new InvalidDataException(
              $"Pixel {x},{y} is palette index {index} and the palette has {this._paletteCount} entries; the colour "
              + "table names that many colours and no more.");
        }

        return coded;

      case 24:
        source.AsSpan(0, coded.Length).CopyTo(coded);
        return coded;

      default:
        // RGBA in, ARGB out: QuickTime's thirty-two bit pixel puts the alpha first.
        for (var i = 0; i < coded.Length; i += 4) {
          coded[i] = source[i + 3];
          coded[i + 1] = source[i];
          coded[i + 2] = source[i + 1];
          coded[i + 3] = source[i + 2];
        }

        return coded;
    }
  }

  // ============================================================================================
  // The frame
  // ============================================================================================

  /// <summary>
  /// Writes one frame and says whether it was written whole, without a single skip.
  /// </summary>
  /// <remarks>
  /// A delta frame first narrows itself to the band of lines that changed. When none did, the band
  /// is empty and the frame is a length, a bare header and the terminating zero — seven bytes, which
  /// the decoder reads as "nothing changed". Otherwise the band is stated in the header unless it is
  /// the whole picture, and each line of it is coded by <see cref="_EncodeLine"/>.
  /// </remarks>
  private bool _EncodeFrame(byte[] current, bool keyFrame) {
    var startLine = 0;
    var endLine = this._height;

    if (!keyFrame) {
      var previous = this._previous!;
      for (; startLine < this._height; ++startLine)
        if (!this._Line(current, startLine).SequenceEqual(this._Line(previous, startLine)))
          break;

      for (; endLine > startLine; --endLine)
        if (!this._Line(current, endLine - 1).SequenceEqual(this._Line(previous, endLine - 1)))
          break;
    }

    this._Put(0, 0, 0, 0);
    var wholeBand = startLine == 0 && endLine == this._height;
    if (wholeBand || startLine == this._height)
      this._PutBig16(0);
    else {
      this._PutBig16(_HAS_LINE_RANGE);
      this._PutBig16((ushort)startLine);
      this._PutBig16(0);
      this._PutBig16((ushort)(endLine - startLine));
      this._PutBig16(0);
    }

    var skipped = false;
    for (var line = startLine; line < endLine; ++line)
      skipped |= this._EncodeLine(current, line, keyFrame);

    this._Put(0);
    BinaryPrimitives.WriteInt32BigEndian(this._buffer.AsSpan(0, 4), this._length);
    return wholeBand && !skipped;
  }

  private Span<byte> _Line(byte[] picture, int line) => picture.AsSpan(line * this._lineBytes, this._lineBytes);

  /// <summary>
  /// Finds the cheapest opcode sequence for one line and writes it. Says whether a skip was used.
  /// </summary>
  /// <remarks>
  /// Walked from the last unit to the first. At each unit the cost of the best coding from there to
  /// the end is the least of three: a skip over the units that match the frame before, a run over
  /// the units that match this one, or a literal copy ending at whichever unit within reach makes
  /// the rest cheapest. A skip costs two bytes (an opcode of zero and a count), a run one byte and a
  /// unit, a copy one byte and its units. The first unit of a line is different: the line always
  /// opens with a skip byte, so a skip there costs one byte fewer and anything else costs one more.
  /// <para/>
  /// The best copy is found with a sliding-window minimum over the units a copy could end at: the
  /// cost of copying from <i>i</i> to <i>j</i> and coding the rest is the cost from <i>j</i> plus one
  /// plus <i>j-i</i> units, and only the <i>j</i>-dependent part need be kept in the window. The
  /// reference keeps a best and a second-best candidate instead, which is the same answer except
  /// where both slip out of reach at once.
  /// </remarks>
  private bool _EncodeLine(byte[] current, int line, bool keyFrame) {
    var units = this._lineUnits;
    var unitBytes = this._unitBytes;
    var thisLine = this._Line(current, line);
    var prevLine = keyFrame ? thisLine : this._Line(this._previous!, line);

    var cost = this._cost;
    var opcode = this._opcode;
    var skip = this._skip;
    var window = this._window;
    cost[units] = 0;

    var windowHead = 0;
    var windowTail = 0;
    var skipCount = 0;
    var repeatCount = 0;

    for (var i = units - 1; i >= 0; --i) {
      var first = i == 0 ? 1 : 0;

      // The unit after this one joins the window as a place a copy could end; anything it beats
      // leaves, and anything now out of reach leaves from the other end.
      var joining = i + 1;
      var joiningKey = cost[joining] + joining * unitBytes;
      while (windowTail > windowHead && cost[window[windowTail - 1]] + window[windowTail - 1] * unitBytes >= joiningKey)
        --windowTail;
      window[windowTail++] = joining;
      while (window[windowHead] > i + _LONGEST_COPY)
        ++windowHead;

      var copyEnd = window[windowHead];
      var copyCost = cost[copyEnd] + 1 + (copyEnd - i) * unitBytes + first;

      var unit = thisLine.Slice(i * unitBytes, unitBytes);
      skipCount = !keyFrame && unit.SequenceEqual(prevLine.Slice(i * unitBytes, unitBytes))
        ? Math.Min(skipCount + 1, _LONGEST_SKIP)
        : 0;
      var skipCost = cost[i + skipCount] + 2 - first;
      skip[i] = (byte)skipCount;

      repeatCount = i < units - 1 && unit.SequenceEqual(thisLine.Slice((i + 1) * unitBytes, unitBytes))
        ? Math.Min(repeatCount + 1, _LONGEST_RUN)
        : 1;
      var repeatCost = cost[i + repeatCount] + 1 + unitBytes + first;

      if (repeatCount > 1 && (skipCount == 0 || repeatCost < skipCost)) {
        cost[i] = repeatCost;
        opcode[i] = (sbyte)-repeatCount;
      } else if (skipCount > 0) {
        cost[i] = skipCost;
        opcode[i] = 0;
      } else {
        cost[i] = copyCost;
        opcode[i] = (sbyte)(copyEnd - i);
      }
    }

    // Now the opcodes are read off from the front. The line opens with its skip byte: one more than
    // the units to step past, or one when the first opcode is not a skip.
    var skipped = false;
    var at = 0;
    if (opcode[0] == 0) {
      this._Put((byte)(skip[0] + 1));
      at += skip[0];
      skipped = true;
    } else
      this._Put(1);

    while (at < units) {
      var code = opcode[at];
      this._Put((byte)code);
      if (code == 0) {
        this._Put((byte)(skip[at] + 1));
        at += skip[at];
        skipped = true;
      } else if (code > 0) {
        this._Put(thisLine.Slice(at * unitBytes, code * unitBytes));
        at += code;
      } else {
        this._Put(thisLine.Slice(at * unitBytes, unitBytes));
        at -= code;
      }
    }

    this._Put(_END_OF_LINE);
    return skipped;
  }

  // ============================================================================================
  // The sample description
  // ============================================================================================

  /// <summary>
  /// A whole <c>rle </c> visual sample entry, box header and all, with a colour table at eight bits.
  /// </summary>
  /// <remarks>
  /// The fields are the ones every visual sample entry has, at the places every container reads
  /// them; the colour table follows the depth as a QuickTime <c>ColorTable</c> — a seed, flags, one
  /// less than the entry count, and the entries as four sixteen-bit values apiece with each colour's
  /// eight bits repeated into both halves.
  /// </remarks>
  private byte[] _SampleEntry() {
    const int _BODY = 78;
    const int _TABLE_HEADER = 8;
    const int _ENTRY = 8;

    var tableLength = this._depth == 8 ? _TABLE_HEADER + this._paletteCount * _ENTRY : 0;
    var entry = new byte[8 + _BODY + tableLength];
    var span = entry.AsSpan();

    BinaryPrimitives.WriteInt32BigEndian(span, entry.Length);
    "rle "u8.CopyTo(span[4..]);
    var body = span[8..];
    BinaryPrimitives.WriteUInt16BigEndian(body[6..], 1);                   // data reference index
    BinaryPrimitives.WriteUInt16BigEndian(body[24..], (ushort)this._width);
    BinaryPrimitives.WriteUInt16BigEndian(body[26..], (ushort)this._height);
    BinaryPrimitives.WriteUInt32BigEndian(body[28..], 0x00480000);         // 72 dpi
    BinaryPrimitives.WriteUInt32BigEndian(body[32..], 0x00480000);
    BinaryPrimitives.WriteUInt16BigEndian(body[40..], 1);                  // frames per sample
    var name = "Animation"u8;
    body[42] = (byte)name.Length;
    name.CopyTo(body[43..]);
    BinaryPrimitives.WriteUInt16BigEndian(body[74..], (ushort)this._depth);

    if (this._depth != 8) {
      BinaryPrimitives.WriteUInt16BigEndian(body[76..], _NO_COLOUR_TABLE);
      return entry;
    }

    BinaryPrimitives.WriteUInt16BigEndian(body[76..], 0);
    var table = body[_BODY..];
    BinaryPrimitives.WriteUInt16BigEndian(table[6..], (ushort)(this._paletteCount - 1));
    var palette = this._palette!;
    for (var i = 0; i < this._paletteCount; ++i) {
      var colour = table.Slice(_TABLE_HEADER + i * _ENTRY, _ENTRY);
      colour[2] = colour[3] = palette[i * 3];
      colour[4] = colour[5] = palette[i * 3 + 1];
      colour[6] = colour[7] = palette[i * 3 + 2];
    }

    return entry;
  }

  // ============================================================================================
  // The output buffer
  // ============================================================================================

  private void _Put(byte value) {
    if (this._length == this._buffer.Length)
      Array.Resize(ref this._buffer, this._buffer.Length * 2);

    this._buffer[this._length++] = value;
  }

  private void _Put(byte first, byte second, byte third, byte fourth) {
    this._Put(first);
    this._Put(second);
    this._Put(third);
    this._Put(fourth);
  }

  private void _PutBig16(ushort value) {
    this._Put((byte)(value >> 8));
    this._Put((byte)value);
  }

  private void _Put(ReadOnlySpan<byte> bytes) {
    if (this._length + bytes.Length > this._buffer.Length)
      Array.Resize(ref this._buffer, Math.Max(this._buffer.Length * 2, this._length + bytes.Length));

    bytes.CopyTo(this._buffer.AsSpan(this._length));
    this._length += bytes.Length;
  }
}
