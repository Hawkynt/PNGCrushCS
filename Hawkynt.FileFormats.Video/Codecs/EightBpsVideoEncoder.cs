using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes 8BPS: Apple's Planar RGB, the picture taken apart into one plane a channel and each row of
/// each plane run-length coded on its own.
/// </summary>
/// <remarks>
/// The bitstream is the one <see cref="EightBpsVideoDecoder"/> documents and reads, and this writes
/// nothing the decoder beside it does not already have a measured reading for: the tables of
/// sixteen-bit row lengths, one plane after another, then the coded rows in the same order, every row
/// PackBits with a literal run of <c>control + 1</c> bytes at 127 and below and <c>257 - control</c>
/// copies of the following byte above it. There is no inter-frame coding in the format and none is
/// invented here, so every frame is whole and every frame is a key frame.
/// <para/>
/// <b>Each row is coded at its shortest.</b> Which of a literal copy and a repeat is cheaper at a
/// given byte is not a local decision — a repeat taken here may leave the bytes after it needing a
/// literal of their own, whose one control byte the longer literal would not have paid — so the row
/// is walked from its last byte to its first, the cost of the best coding from every position to the
/// end is kept, and the opcodes are read back off that table from the front. A repeat is only ever
/// taken over the whole run it sits at the head of, capped at the 128 one opcode can state: the cost
/// of coding a suffix never rises as the suffix shortens, so stopping a repeat early can never pay.
/// The literal is the case that needs looking further ahead, and the cheapest byte to end one at is
/// found with a sliding minimum over the 128 positions in reach, the same way
/// <see cref="QuickTimeRleEncoder"/> finds its own.
/// <para/>
/// <b>Lossless, at the three depths the decoder reads.</b> <see cref="PixelFormat.Rgb24"/> is written
/// as three planes and <see cref="PixelFormat.Rgba32"/> as four, red, green, blue and then alpha —
/// which is the byte order those pictures already have, so a plane is every <i>n</i>th byte of the
/// picture and nothing is reordered. <see cref="PixelFormat.Indexed8"/> is written as the single
/// plane of indices its depth has, with the colours in the sample description's colour table where
/// the decoder looks for them. Everything else is refused by name rather than converted, because
/// whether a conversion may lose something is not this codec's decision to make.
/// <para/>
/// <b>A row's coded length is sixteen bits</b>, which is what puts a ceiling on the width: a row of
/// pure noise costs its own bytes plus one control byte per 128 of them, so a picture wider than
/// 65020 pixels has rows this format cannot state the length of. That is refused when the encoder is
/// built rather than discovered on whichever frame first happens to be noisy.
/// </remarks>
public sealed class EightBpsVideoEncoder : IVideoCodecEncoder<EightBpsVideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("8BPS");

  /// <summary>The most bytes one literal copy can carry.</summary>
  private const int _LONGEST_LITERAL = 128;

  /// <summary>The most times one repeat can state its byte.</summary>
  private const int _LONGEST_REPEAT = 128;

  /// <summary>The largest coded length a row's table entry can state.</summary>
  private const int _LONGEST_ROW = ushort.MaxValue;

  /// <summary>Where the depth sits in a QuickTime visual sample entry, counted from the entry's body.</summary>
  private const int _DEPTH_AT = 74;

  /// <summary>Where the colour table identifier sits, immediately after the depth.</summary>
  private const int _COLOUR_TABLE_ID_AT = 76;

  /// <summary>Where a colour table, when the identifier calls for one, begins.</summary>
  private const int _COLOUR_TABLE_AT = 78;

  /// <summary>The length of a visual sample entry's body up to where a colour table would start.</summary>
  private const int _BODY_LENGTH = 78;

  /// <summary>A colour table's seed, flags and one-less-than-count, before its entries.</summary>
  private const int _TABLE_HEADER = 8;

  /// <summary>One colour table entry: an index and three channels, each sixteen bits.</summary>
  private const int _TABLE_ENTRY = 8;

  /// <summary>The identifier a description states when it carries no colour table.</summary>
  private const ushort _NO_COLOUR_TABLE = 0xFFFF;

  private readonly MediaStreamInfo _requested;
  private readonly int _width;
  private readonly int _height;

  private int _depth;
  private int _planes;
  private byte[]? _palette;
  private int _paletteCount;
  private MediaStreamInfo? _stream;

  /// <summary>One row of one plane, gathered out of the picture's interleaved bytes.</summary>
  private readonly byte[] _row;

  /// <summary>The cost, in coded bytes, of the best coding from each byte of a row to its end.</summary>
  private readonly int[] _cost;

  /// <summary>The opcode chosen at each byte: positive to copy that many, negative to repeat that many.</summary>
  private readonly short[] _opcode;

  /// <summary>A sliding window over the bytes a literal copy could run to, cheapest first.</summary>
  private readonly int[] _window;

  private byte[] _buffer = new byte[4096];
  private int _length;

  private EightBpsVideoEncoder(MediaStreamInfo stream, int depth) {
    this._requested = stream;
    this._width = stream.Width;
    this._height = stream.Height;

    this._row = new byte[this._width];
    this._cost = new int[this._width + 1];
    this._opcode = new short[this._width];
    this._window = new int[this._width + 2];

    if (depth != 0)
      this._TakeLayout(depth);
    if (depth == 8)
      this._AdoptPaletteFrom(stream.CodecPrivateData.Span);
  }

  public static string CodecName => "Apple Planar RGB (8BPS)";

  public static CodecTag Codec => _Tag;

  /// <summary>
  /// Builds an encoder for the stream described, refusing a picture or a depth the format cannot state.
  /// </summary>
  /// <remarks>
  /// The depth is taken from the description where it states one and from the first picture where it
  /// does not. An eight-bit description that already carries a sample entry with a colour table —
  /// one that came back out of a demuxer, say — lends its palette, so that the stream can be
  /// described before a single picture has been seen.
  /// </remarks>
  public static EightBpsVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("8BPS can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"An 8BPS encoder needs the picture size up front; {stream.Width}x{stream.Height} was supplied.");
    if (stream.Width > ushort.MaxValue || stream.Height > ushort.MaxValue)
      throw new NotSupportedException(
        $"A picture of {stream.Width}x{stream.Height} does not fit the sixteen-bit size fields of a QuickTime sample description.");
    if (_WorstCaseRow(stream.Width) > _LONGEST_ROW)
      throw new NotSupportedException(
        $"A row of {stream.Width} pixels can cost {_WorstCaseRow(stream.Width)} coded bytes, and an 8BPS row length is "
        + $"sixteen bits, so no picture wider than {_WidestRow()} pixels can be written.");
    if (stream.BitsPerPixel is not (0 or 8 or 24 or 32))
      throw new NotSupportedException(
        $"Video stream {stream.Index} asks for {stream.BitsPerPixel} bits per pixel. 8BPS defines a plane layout for "
        + "eight bits through a colour table, for twenty-four and for thirty-two, and for nothing else.");

    return new(stream, stream.BitsPerPixel);
  }

  /// <summary>
  /// Codes one picture whole.
  /// </summary>
  /// <remarks>
  /// Always produces a packet, and always a key frame: the format carries no reference to the frame
  /// before, so every packet stands on its own and a decoder may start at any of them.
  /// </remarks>
  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"8BPS geometry is fixed at {this._width}x{this._height}; received {frame.Width}x{frame.Height}.");

    this._TakeDepthFrom(frame);
    if (this._depth == 8)
      this._TakePaletteFrom(frame);
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException(
        "The source RawImage does not contain enough pixel data for its declared format and dimensions.");
    if (this._depth == 8)
      this._RefuseIndicesOutsideTheTable(frame.PixelData);

    this._EncodeFrame(frame.PixelData);

    packet = new(
      this._requested.Index,
      this._buffer.AsSpan(0, this._length).ToArray(),
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: true);
    return true;
  }

  /// <summary>
  /// The stream as a muxer needs it: a whole <c>8BPS</c> visual sample entry, with the colour table
  /// inside it at eight bits.
  /// </summary>
  /// <remarks>
  /// That is what a QuickTime <c>stsd</c> carries and what <see cref="EightBpsVideoDecoder"/> reads
  /// its depth and its palette out of. At eight bits the palette is not known until the first picture
  /// has been seen unless the description handed in already carried one, so asking before then is
  /// refused rather than answered with an entry naming no colours.
  /// </remarks>
  public MediaStreamInfo DescribeStream() {
    if (this._stream != null)
      return this._stream;

    if (this._depth == 0)
      throw new InvalidOperationException(
        "An 8BPS stream cannot be described before its depth is known. Encode the first picture first, or hand Create "
        + "a stream that states BitsPerPixel.");
    if (this._depth == 8 && this._palette == null)
      throw new InvalidOperationException(
        "An eight-bit 8BPS stream cannot be described before its palette is known. Encode the first picture first, or "
        + "hand Create a stream whose CodecPrivateData is a sample entry with a colour table.");

    return this._stream = new() {
      Index = this._requested.Index,
      Kind = MediaStreamKind.Video,
      Codec = _Tag,
      Handler = _Tag,
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

  /// <summary>The most coded bytes a row of this width can cost: every byte a literal, one control
  /// byte for every 128 of them.</summary>
  private static int _WorstCaseRow(int width) => width + (width + _LONGEST_LITERAL - 1) / _LONGEST_LITERAL;

  /// <summary>The widest row whose worst case still fits a sixteen-bit length.</summary>
  private static int _WidestRow() {
    var width = _LONGEST_ROW;
    while (_WorstCaseRow(width) > _LONGEST_ROW)
      --width;

    return width;
  }

  /// <summary>Fixes the depth from the first picture, or checks a later one against it.</summary>
  private void _TakeDepthFrom(RawImage frame) {
    var depth = frame.Format switch {
      PixelFormat.Indexed8 => 8,
      PixelFormat.Rgb24 => 24,
      PixelFormat.Rgba32 => 32,
      _ => throw new NotSupportedException(
        $"8BPS is written here from Rgb24, Rgba32 and Indexed8 pictures only; a {frame.Format} picture would have to "
        + "be converted first, and whether that conversion may lose anything is not this codec's decision."),
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

  /// <summary>Settles how many planes a depth is taken apart into.</summary>
  private void _TakeLayout(int depth) {
    this._depth = depth;
    this._planes = depth / 8;
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
      "The picture carries a different palette from the one the stream was described with. The colour table is stated "
      + "once in the sample description, so it cannot change between frames.");
  }

  /// <summary>Takes a palette out of a sample description that already carries a colour table.</summary>
  /// <remarks>
  /// Read exactly the way the decoder reads it — a seed, flags, one less than the entry count, and
  /// then entries of an index and three sixteen-bit channels of which the high byte is kept — so that
  /// a description handed back in names the same colours it named going out. A description that
  /// carries no table, or is too short to reach one, simply lends nothing and leaves the palette to
  /// the first picture.
  /// </remarks>
  private void _AdoptPaletteFrom(ReadOnlySpan<byte> description) {
    if (description.Length < 8)
      return;

    var header = BinaryPrimitives.ReadUInt32BigEndian(description) == 1 ? 16 : 8;
    if (description.Length < header)
      return;

    var body = description[header..];
    if (body.Length < _COLOUR_TABLE_AT)
      return;
    if (BinaryPrimitives.ReadUInt16BigEndian(body.Slice(_COLOUR_TABLE_ID_AT, 2)) != 0)
      return;

    var table = body[_COLOUR_TABLE_AT..];
    if (table.Length < _TABLE_HEADER)
      return;

    var entries = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(6, 2)) + 1;
    if (entries > 256 || table.Length < _TABLE_HEADER + entries * _TABLE_ENTRY)
      return;

    var palette = new byte[entries * 3];
    for (var i = 0; i < entries; ++i) {
      var entry = table.Slice(_TABLE_HEADER + i * _TABLE_ENTRY, _TABLE_ENTRY);
      var index = BinaryPrimitives.ReadUInt16BigEndian(entry);
      if (index >= entries)
        return;

      palette[index * 3] = entry[2];
      palette[index * 3 + 1] = entry[4];
      palette[index * 3 + 2] = entry[6];
    }

    this._palette = palette;
    this._paletteCount = entries;
  }

  /// <summary>
  /// Refuses a picture naming a colour the table does not hold.
  /// </summary>
  /// <remarks>
  /// The colour table states how many colours there are and no more, so an index past its end
  /// decodes to whatever a reader happens to leave there — black, for the decoder beside this one.
  /// Written out, that is a picture nothing in the file says is wrong.
  /// </remarks>
  private void _RefuseIndicesOutsideTheTable(byte[] picture) {
    var pixels = this._width * this._height;
    for (var i = 0; i < pixels; ++i) {
      var index = picture[i];
      if (index >= this._paletteCount)
        throw new InvalidDataException(
          $"Pixel {i % this._width},{i / this._width} is palette index {index} and the palette has "
          + $"{this._paletteCount} entries; the colour table names that many colours and no more.");
    }
  }

  // ============================================================================================
  // The frame
  // ============================================================================================

  /// <summary>
  /// Writes one picture: the tables of row lengths for every plane, then every plane's coded rows.
  /// </summary>
  /// <remarks>
  /// The tables come first in the packet and their contents are only known once the rows have been
  /// coded, so the room for them is taken at the front and each entry filled in as its row is
  /// finished. Coding straight into the same buffer keeps the rows where they belong and spares the
  /// packet a second copy.
  /// </remarks>
  private void _EncodeFrame(byte[] picture) {
    var tableBytes = 2 * this._planes * this._height;
    this._Reserve(tableBytes);
    this._length = tableBytes;

    for (var plane = 0; plane < this._planes; ++plane)
      for (var row = 0; row < this._height; ++row) {
        this._GatherRow(picture, plane, row);
        var start = this._length;
        this._PackRow();
        var coded = this._length - start;
        BinaryPrimitives.WriteUInt16BigEndian(this._buffer.AsSpan((plane * this._height + row) * 2, 2), (ushort)coded);
      }
  }

  /// <summary>Takes one row of one plane out of the picture's interleaved bytes.</summary>
  /// <remarks>
  /// A plane is every <i>n</i>th byte of the picture, because the channel order the planes are
  /// written in — red, green, blue, alpha — is the byte order the pictures this encoder takes already
  /// have. At eight bits there is one plane and the row is the picture's own bytes.
  /// </remarks>
  private void _GatherRow(byte[] picture, int plane, int row) {
    var width = this._width;
    var planes = this._planes;
    var start = row * width * planes;
    if (planes == 1) {
      picture.AsSpan(start, width).CopyTo(this._row);
      return;
    }

    var destination = this._row;
    for (var x = 0; x < width; ++x)
      destination[x] = picture[start + x * planes + plane];
  }

  /// <summary>
  /// Writes the row gathered in <see cref="_row"/> as the shortest PackBits coding it has.
  /// </summary>
  /// <remarks>
  /// Walked from the last byte to the first. At each byte the cost of the best coding from there to
  /// the end is the lesser of two: a repeat over the whole run of equal bytes starting here, costing
  /// a control byte and the byte itself; or a literal copy ending at whichever byte within 128 makes
  /// the rest cheapest, costing a control byte and the bytes copied. Only the whole run is ever
  /// considered for a repeat — a shorter one leaves a longer suffix, whose coding can never be
  /// cheaper — while the literal's end is the one genuine choice, found with a sliding minimum over
  /// the positions still in reach.
  /// </remarks>
  private void _PackRow() {
    var width = this._width;
    var row = this._row.AsSpan(0, width);
    var cost = this._cost;
    var opcode = this._opcode;
    var window = this._window;
    cost[width] = 0;

    var head = 0;
    var tail = 0;
    var repeat = 0;

    for (var i = width - 1; i >= 0; --i) {
      // The byte after this one joins the window as a place a literal could end; anything it beats
      // leaves, and anything now out of reach leaves from the other end.
      var joining = i + 1;
      var joiningKey = cost[joining] + joining;
      while (tail > head && cost[window[tail - 1]] + window[tail - 1] >= joiningKey)
        --tail;
      window[tail++] = joining;
      while (window[head] > i + _LONGEST_LITERAL)
        ++head;

      var literalEnd = window[head];
      var literalCost = 1 + (literalEnd - i) + cost[literalEnd];

      repeat = i < width - 1 && row[i] == row[i + 1] ? Math.Min(repeat + 1, _LONGEST_REPEAT) : 1;
      var repeatCost = repeat > 1 ? 2 + cost[i + repeat] : int.MaxValue;

      if (repeatCost <= literalCost) {
        cost[i] = repeatCost;
        opcode[i] = (short)-repeat;
      } else {
        cost[i] = literalCost;
        opcode[i] = (short)(literalEnd - i);
      }
    }

    this._Reserve(this._length + cost[0]);
    for (var at = 0; at < width;) {
      var code = opcode[at];
      if (code > 0) {
        this._Put((byte)(code - 1));
        this._Put(row.Slice(at, code));
        at += code;
      } else {
        var count = -code;
        this._Put((byte)(257 - count));
        this._Put(row[at]);
        at += count;
      }
    }
  }

  // ============================================================================================
  // The sample description
  // ============================================================================================

  /// <summary>
  /// A whole <c>8BPS</c> visual sample entry, box header and all, with a colour table at eight bits.
  /// </summary>
  /// <remarks>
  /// The fields are the ones every visual sample entry has, at the places every container reads them;
  /// the colour table follows the depth as a QuickTime <c>ColorTable</c> — a seed, flags, one less
  /// than the entry count, and the entries as four sixteen-bit values apiece with each colour's eight
  /// bits repeated into both halves.
  /// </remarks>
  private byte[] _SampleEntry() {
    var tableLength = this._depth == 8 ? _TABLE_HEADER + this._paletteCount * _TABLE_ENTRY : 0;
    var entry = new byte[8 + _BODY_LENGTH + tableLength];
    var span = entry.AsSpan();

    BinaryPrimitives.WriteInt32BigEndian(span, entry.Length);
    "8BPS"u8.CopyTo(span[4..]);
    var body = span[8..];
    BinaryPrimitives.WriteUInt16BigEndian(body[6..], 1);                   // data reference index
    BinaryPrimitives.WriteUInt16BigEndian(body[24..], (ushort)this._width);
    BinaryPrimitives.WriteUInt16BigEndian(body[26..], (ushort)this._height);
    BinaryPrimitives.WriteUInt32BigEndian(body[28..], 0x00480000);         // 72 dpi
    BinaryPrimitives.WriteUInt32BigEndian(body[32..], 0x00480000);
    BinaryPrimitives.WriteUInt16BigEndian(body[40..], 1);                  // frames per sample
    var name = "Planar RGB"u8;
    body[42] = (byte)name.Length;
    name.CopyTo(body[43..]);
    BinaryPrimitives.WriteUInt16BigEndian(body[_DEPTH_AT..], (ushort)this._depth);

    if (this._depth != 8) {
      BinaryPrimitives.WriteUInt16BigEndian(body[_COLOUR_TABLE_ID_AT..], _NO_COLOUR_TABLE);
      return entry;
    }

    BinaryPrimitives.WriteUInt16BigEndian(body[_COLOUR_TABLE_ID_AT..], 0);
    var table = body[_COLOUR_TABLE_AT..];
    BinaryPrimitives.WriteUInt16BigEndian(table[6..], (ushort)(this._paletteCount - 1));
    var palette = this._palette!;
    for (var i = 0; i < this._paletteCount; ++i) {
      var colour = table.Slice(_TABLE_HEADER + i * _TABLE_ENTRY, _TABLE_ENTRY);
      BinaryPrimitives.WriteUInt16BigEndian(colour, (ushort)i);
      colour[2] = colour[3] = palette[i * 3];
      colour[4] = colour[5] = palette[i * 3 + 1];
      colour[6] = colour[7] = palette[i * 3 + 2];
    }

    return entry;
  }

  // ============================================================================================
  // The output buffer
  // ============================================================================================

  private void _Reserve(int length) {
    if (length <= this._buffer.Length)
      return;

    var grown = this._buffer.Length;
    while (grown < length)
      grown *= 2;

    Array.Resize(ref this._buffer, grown);
  }

  private void _Put(byte value) {
    this._Reserve(this._length + 1);
    this._buffer[this._length++] = value;
  }

  private void _Put(ReadOnlySpan<byte> bytes) {
    this._Reserve(this._length + bytes.Length);
    bytes.CopyTo(this._buffer.AsSpan(this._length));
    this._length += bytes.Length;
  }
}
