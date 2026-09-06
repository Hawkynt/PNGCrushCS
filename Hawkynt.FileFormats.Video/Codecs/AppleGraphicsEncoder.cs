using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes Apple Graphics, the codec every QuickTime file calls <c>smc </c>: eight-bit palette
/// indices as 4x4 blocks of one, two, four, eight or sixteen colours, with runs of blocks skipped or
/// repeated where the picture lets them be.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>libavcodec/smcenc.c</c>, copyright (c) 2021 The FFmpeg project, written
/// by Paul B. Mahol and distributed there under LGPL-2.1-or-later. This conversion is distributed with
/// PNGCrushCS under LGPL-3.0-or-later; see <c>Codecs/AppleGraphics/THIRD-PARTY-NOTICE.FFmpeg.txt</c>.
/// What is taken is the shape of the block walk: at every block the encoder measures four runs — how
/// many blocks from here are unchanged since the frame before, how many repeat the block just written,
/// how many share one set of distinct colours, and how many colours that set holds — and then spends
/// whichever of them covers the most blocks.
/// <para/>
/// <b>Lossless, and only from indices.</b> Every block of an eight-bit palettised picture is
/// representable: one distinct colour is the one-colour opcode, two the two-colour, three or four the
/// four-colour, five to eight the eight-colour, and anything above that the sixteen raw indices, which
/// can hold any block at all. So this encoder never rounds and never quantises, and the decoder gets
/// back the indices that went in, byte for byte. The other side of that is that
/// <see cref="PixelFormat.Indexed8"/> is the only picture it takes: a true-colour picture would have
/// to be reduced to 256 colours first, and which 256 is not a decision a codec should make silently,
/// so it is refused by name instead.
/// <para/>
/// <b>The colour caches are reset with every packet, and this encoder never reaches across one.</b>
/// The format's two-, four- and eight-colour opcodes each have a second spelling that names one of
/// three 256-entry caches of recently written colour sets rather than restating the colours. Whether
/// those caches survive from one chunk to the next is the one place where the readings of this format
/// disagree: <see cref="AppleGraphicsDecoder"/> resets them at every packet, because a chunk is a
/// whole frame and nothing in the format's description carries them across, while FFmpeg's decoder
/// leaves the contents of the previous chunk in place and only restarts the write pointer — and
/// FFmpeg's encoder relies on that, matching against entries an earlier frame wrote. A stream doing
/// that decodes differently under the two readings, so nothing here writes one: the caches are reset
/// at the start of every packet and a cached-colour opcode is only ever emitted for an entry this
/// packet has already written. Such a stream reads identically under either reading, which is the
/// only kind worth writing while the question is open.
/// <para/>
/// The picture is coded over a canvas padded out to whole 4x4 blocks, with the last row and column
/// repeated into the padding; <see cref="AppleGraphicsDecoder"/> crops the padding back off, so what
/// is in it changes only how well the edge blocks compress and never what comes out.
/// <para/>
/// The first frame is written whole. Every frame after it is written against the one before, and is
/// flagged as a key frame only when it happened to reach the end without a single skip — in which
/// case a decoder really can start there.
/// </remarks>
public sealed class AppleGraphicsEncoder : IVideoCodecEncoder<AppleGraphicsEncoder> {

  /// <summary>The code every container names this codec with, trailing space and all.</summary>
  private static readonly CodecTag _SMC = CodecTag.FromCharacters("smc ");

  private const int _BLOCK = 4;
  private const int _BLOCK_PIXELS = _BLOCK * _BLOCK;

  /// <summary>How many sets of colours each of the three caches holds.</summary>
  private const int _CACHE_SIZE = 256;

  private const byte _SKIP_INLINE = 0x00;
  private const byte _SKIP_BYTE = 0x10;
  private const byte _REPEAT_ONE_INLINE = 0x20;
  private const byte _REPEAT_ONE_BYTE = 0x30;
  private const byte _ONE_COLOUR_INLINE = 0x60;
  private const byte _ONE_COLOUR_BYTE = 0x70;
  private const byte _TWO_COLOUR_NEW = 0x80;
  private const byte _TWO_COLOUR_CACHED = 0x90;
  private const byte _FOUR_COLOUR_NEW = 0xA0;
  private const byte _FOUR_COLOUR_CACHED = 0xB0;
  private const byte _EIGHT_COLOUR_NEW = 0xC0;
  private const byte _EIGHT_COLOUR_CACHED = 0xD0;
  private const byte _SIXTEEN_COLOUR = 0xE0;

  /// <summary>The most blocks an opcode can state in the low nibble of its own byte.</summary>
  private const int _INLINE_RUN = 16;

  /// <summary>The most blocks an opcode can state in a count byte of its own.</summary>
  private const int _COUNTED_RUN = 256;

  /// <summary>The depth an Apple Graphics sample description states.</summary>
  private const int _DEPTH = 8;

  /// <summary>The most colours an eight-bit colour table can hold.</summary>
  private const int _COLOURS = 256;

  /// <summary>The longest chunk the three-byte length in a chunk's own header can state.</summary>
  private const int _LONGEST_CHUNK = 0xFFFFFF;

  private readonly MediaStreamInfo _requested;
  private readonly int _width;
  private readonly int _height;
  private readonly int _codedWidth;
  private readonly int _codedHeight;
  private readonly int _blocksAcross;
  private readonly int _totalBlocks;

  private byte[]? _palette;
  private int _paletteCount;
  private MediaStreamInfo? _stream;

  /// <summary>The picture before, over the padded block grid, or null before the first.</summary>
  private byte[]? _previous;

  /// <summary>The colour pair, quad and octet caches, reset before every packet.</summary>
  private readonly byte[] _pairCache = new byte[_CACHE_SIZE * 2];
  private readonly byte[] _quadCache = new byte[_CACHE_SIZE * 4];
  private readonly byte[] _octetCache = new byte[_CACHE_SIZE * 8];

  /// <summary>How many entries of each cache this packet has written, and where the next one goes.</summary>
  private int _pairFilled;
  private int _pairNext;
  private int _quadFilled;
  private int _quadNext;
  private int _octetFilled;
  private int _octetNext;

  private byte[] _buffer = new byte[4096];
  private int _length;

  private AppleGraphicsEncoder(MediaStreamInfo stream) {
    this._requested = stream;
    this._width = stream.Width;
    this._height = stream.Height;
    this._codedWidth = (stream.Width + _BLOCK - 1) / _BLOCK * _BLOCK;
    this._codedHeight = (stream.Height + _BLOCK - 1) / _BLOCK * _BLOCK;
    this._blocksAcross = this._codedWidth / _BLOCK;
    this._totalBlocks = this._blocksAcross * (this._codedHeight / _BLOCK);

    this._AdoptPaletteFrom(stream.CodecPrivateData.Span);
  }

  public static string CodecName => "Apple Graphics (SMC)";

  public static CodecTag Codec => _SMC;

  /// <summary>
  /// Builds an encoder for the stream described, refusing a depth this format is not defined at.
  /// </summary>
  /// <remarks>
  /// A description that already carries an <c>smc </c> sample entry with a colour table — one that
  /// came out of a demuxer, say — lends its palette, so that the stream can be described before a
  /// single picture has been seen.
  /// </remarks>
  public static AppleGraphicsEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Apple Graphics can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"An Apple Graphics encoder needs the picture size up front; {stream.Width}x{stream.Height} was supplied.");
    if (stream.Width > ushort.MaxValue || stream.Height > ushort.MaxValue)
      throw new NotSupportedException(
        $"A picture of {stream.Width}x{stream.Height} does not fit the sixteen-bit size fields of a QuickTime sample description.");
    if (stream.BitsPerPixel is not (0 or _DEPTH))
      throw new NotSupportedException(
        $"Video stream {stream.Index} asks for {stream.BitsPerPixel} bits per pixel. Apple Graphics is written here at "
        + "eight bits through a colour table of its own and nothing else — depth 40, the greyscale convention a "
        + "QuickTime visual sample entry states for the same eight bits, is not written, because a greyscale picture "
        + "is written back as those same eight bits with the ramp stated as an ordinary colour table.");

    return new(stream);
  }

  /// <summary>Codes one picture against the one before it, or whole when there is none.</summary>
  /// <remarks>
  /// Always produces a packet: this codec has no frame it holds back, and a picture identical to the
  /// one before it is written as skip opcodes covering every block.
  /// </remarks>
  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"Apple Graphics geometry is fixed at {this._width}x{this._height}; received {frame.Width}x{frame.Height}.");
    if (frame.Format != PixelFormat.Indexed8)
      throw new NotSupportedException(
        $"Apple Graphics codes palette indices and takes only Indexed8 pictures; a {frame.Format} picture would have "
        + "to be reduced to 256 colours first, and which colours those are is not this codec's decision.");

    this._TakePaletteFrom(frame);
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException(
        "The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var coded = this._Coded(frame);
    var keyFrame = this._previous == null;

    Array.Clear(this._pairCache);
    Array.Clear(this._quadCache);
    Array.Clear(this._octetCache);
    this._pairFilled = this._pairNext = 0;
    this._quadFilled = this._quadNext = 0;
    this._octetFilled = this._octetNext = 0;

    this._length = 0;
    this._Put(0, 0, 0, 0);
    var wholePicture = this._EncodeFrame(coded, keyFrame);
    this._previous = coded;

    // The chunk header: an unexplained flags byte the format leaves at zero, and the chunk's own
    // length as a 24-bit number, which is what a decoder reading a chunk out of a larger buffer needs.
    // A chunk too long to state its own length is refused rather than written with the length wrapped,
    // which would read as a chunk of some other size and decode to something nobody wrote.
    if (this._length > _LONGEST_CHUNK)
      throw new InvalidDataException(
        $"This picture codes to {this._length} bytes, and an Apple Graphics chunk states its own length in three "
        + $"bytes, so it can be at most {_LONGEST_CHUNK}.");

    this._buffer[0] = 0;
    this._buffer[1] = (byte)(this._length >> 16);
    this._buffer[2] = (byte)(this._length >> 8);
    this._buffer[3] = (byte)this._length;

    packet = new(
      this._requested.Index,
      this._buffer.AsSpan(0, this._length).ToArray(),
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: wholePicture);
    return true;
  }

  /// <summary>
  /// The stream as a muxer needs it: a whole <c>smc </c> visual sample entry with the colour table
  /// inside it.
  /// </summary>
  /// <remarks>
  /// That is what a QuickTime or MP4 <c>stsd</c> carries and what <see cref="AppleGraphicsDecoder"/>
  /// reads its depth and palette out of. The palette is not known until the first picture has been
  /// seen unless the description handed in carried one, so asking before then is refused rather than
  /// answered with an entry naming no colours — which for this codec would not read as "no colours"
  /// at all but as a request for the classic Macintosh system palette.
  /// </remarks>
  public MediaStreamInfo DescribeStream() {
    if (this._stream != null)
      return this._stream;

    if (this._palette == null)
      throw new InvalidOperationException(
        "An Apple Graphics stream cannot be described before its palette is known. Encode the first picture first, or "
        + "hand Create a stream whose CodecPrivateData is a sample entry with a colour table.");

    return this._stream = new() {
      Index = this._requested.Index,
      Kind = MediaStreamKind.Video,
      Codec = _SMC,
      Handler = _SMC,
      TimeBase = this._requested.TimeBase,
      FrameRate = this._requested.FrameRate,
      DeclaredFrameCount = this._requested.DeclaredFrameCount,
      Width = this._width,
      Height = this._height,
      BitsPerPixel = _DEPTH,
      CodecPrivateData = this._SampleEntry(),
      Language = this._requested.Language,
      Name = this._requested.Name,
    };
  }

  // ============================================================================================
  // What goes in
  // ============================================================================================

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

    var entries = Math.Min(frame.PaletteCount, _COLOURS);
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

  /// <summary>Takes a palette out of an <c>smc </c> sample description that already carries a colour table.</summary>
  /// <remarks>
  /// Only out of this codec's own entry. A description naming some other codec has its own idea of
  /// what follows the depth, and reading a colour table out of that would be reading whatever happened
  /// to be there.
  /// </remarks>
  private void _AdoptPaletteFrom(ReadOnlySpan<byte> description) {
    const int bodyLength = 78;
    const int tableHeader = 8;
    const int tableEntry = 8;
    const uint extendedLength = 1;

    if (description.Length < 8 || !description.Slice(4, 4).SequenceEqual("smc "u8))
      return;

    var header = BinaryPrimitives.ReadUInt32BigEndian(description) == extendedLength ? 16 : 8;
    if (description.Length < header + bodyLength + tableHeader)
      return;

    var table = description[(header + bodyLength)..];
    var stated = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(6, 2)) + 1;
    if (stated > _COLOURS || table.Length < tableHeader + stated * tableEntry)
      return;

    var palette = new byte[stated * 3];
    for (var i = 0; i < stated; ++i) {
      var colour = table.Slice(tableHeader + i * tableEntry, tableEntry);
      palette[i * 3] = colour[2];
      palette[i * 3 + 1] = colour[4];
      palette[i * 3 + 2] = colour[6];
    }

    this._palette = palette;
    this._paletteCount = stated;
  }

  /// <summary>
  /// The picture over the padded block grid, with the last visible row and column repeated into the
  /// padding.
  /// </summary>
  /// <remarks>
  /// Repeated rather than left at zero so that an edge block holds no colour the picture does not
  /// already have there, which is what keeps a picture whose width or height is not a whole number of
  /// blocks from paying for a colour of its own along two of its sides. The decoder crops the padding
  /// off, so nothing in it can reach the output either way.
  /// <para/>
  /// An index past the end of the palette is refused here: the colour table states how many colours
  /// there are, so such an index would decode to a colour nothing chose.
  /// </remarks>
  private byte[] _Coded(RawImage frame) {
    var coded = new byte[this._codedWidth * this._codedHeight];
    var source = frame.PixelData;

    for (var y = 0; y < this._height; ++y) {
      var row = coded.AsSpan(y * this._codedWidth, this._codedWidth);
      source.AsSpan(y * this._width, this._width).CopyTo(row);
      row[this._width..].Fill(row[this._width - 1]);
    }

    for (var y = this._height; y < this._codedHeight; ++y)
      coded.AsSpan((this._height - 1) * this._codedWidth, this._codedWidth).CopyTo(coded.AsSpan(y * this._codedWidth));

    for (var y = 0; y < this._height; ++y)
    for (var x = 0; x < this._width; ++x) {
      var index = coded[y * this._codedWidth + x];
      if (index >= this._paletteCount)
        throw new InvalidDataException(
          $"Pixel {x},{y} is palette index {index} and the palette has {this._paletteCount} entries; the colour table "
          + "names that many colours and no more.");
    }

    return coded;
  }

  // ============================================================================================
  // The block walk
  // ============================================================================================

  /// <summary>
  /// Writes every block of one frame and says whether it was written without a single skip.
  /// </summary>
  /// <remarks>
  /// At each block four runs are measured and the longest wins. Two of them cost almost nothing — a
  /// run of blocks unchanged since the frame before is one or two bytes whatever its length, and so is
  /// a run repeating the block just written — so they are preferred wherever they reach at least as
  /// far as a coded run would. Between the two, the repeat wins a tie: it needs no frame before it, so
  /// a frame that used only repeats can still be started at.
  /// </remarks>
  private bool _EncodeFrame(byte[] current, bool keyFrame) {
    Span<byte> distinct = stackalloc byte[_BLOCK_PIXELS];
    Span<byte> next = stackalloc byte[_BLOCK_PIXELS];

    var skipped = false;
    var block = 0;
    while (block < this._totalBlocks) {
      var interSkip = 0;
      if (!keyFrame)
        while (block + interSkip < this._totalBlocks
               && interSkip < _COUNTED_RUN
               && this._SameBlock(current, block + interSkip, this._previous!, block + interSkip))
          ++interSkip;

      var repeat = 0;
      if (block > 0)
        while (block + repeat < this._totalBlocks
               && repeat < _COUNTED_RUN
               && this._SameBlock(current, block + repeat, current, block - 1))
          ++repeat;

      // How many blocks from here share one set of distinct colours, and how many colours that is.
      // A run of one colour may reach 256 blocks because its opcode carries a count byte; every other
      // coding states its run in the low nibble of the opcode and stops at sixteen.
      var codedBlocks = 0;
      var codedDistinct = 0;
      while (block + codedBlocks < this._totalBlocks && codedBlocks < _COUNTED_RUN) {
        var count = this._Distinct(current, block + codedBlocks, next);
        if (codedBlocks == 0) {
          next[..count].CopyTo(distinct);
          codedDistinct = count;
        } else if (count != codedDistinct || !next[..count].SequenceEqual(distinct[..count]))
          break;

        ++codedBlocks;
        if (codedDistinct > 1 && codedBlocks >= _INLINE_RUN)
          break;
      }

      // A block of more than eight colours is written raw, which no run of blocks can share, so it
      // never outbids a skip or a repeat.
      var codedReach = codedDistinct <= 8 ? codedBlocks : 0;

      if (repeat > 0 && repeat >= codedReach && repeat >= interSkip) {
        this._PutRun(_REPEAT_ONE_INLINE, _REPEAT_ONE_BYTE, repeat);
        block += repeat;
        continue;
      }

      if (interSkip >= codedReach && interSkip > repeat) {
        this._PutRun(_SKIP_INLINE, _SKIP_BYTE, interSkip);
        block += interSkip;
        skipped = true;
        continue;
      }

      this._PutCodedRun(current, block, codedBlocks, distinct[..codedDistinct]);
      block += codedBlocks;
    }

    return !skipped;
  }

  /// <summary>Whether two blocks of two pictures over the padded grid hold the same sixteen indices.</summary>
  private bool _SameBlock(byte[] left, int leftBlock, byte[] right, int rightBlock) {
    var (leftAt, rightAt) = (this._Corner(leftBlock), this._Corner(rightBlock));
    for (var row = 0; row < _BLOCK; ++row)
      if (!left.AsSpan(leftAt + row * this._codedWidth, _BLOCK).SequenceEqual(right.AsSpan(rightAt + row * this._codedWidth, _BLOCK)))
        return false;

    return true;
  }

  /// <summary>The block's distinct indices in ascending order, and how many there are.</summary>
  private int _Distinct(byte[] picture, int block, Span<byte> distinct) {
    Span<byte> pixels = stackalloc byte[_BLOCK_PIXELS];
    var at = this._Corner(block);
    for (var row = 0; row < _BLOCK; ++row)
      picture.AsSpan(at + row * this._codedWidth, _BLOCK).CopyTo(pixels[(row * _BLOCK)..]);

    pixels.Sort();

    var count = 1;
    distinct[0] = pixels[0];
    for (var i = 1; i < _BLOCK_PIXELS; ++i)
      if (pixels[i] != pixels[i - 1])
        distinct[count++] = pixels[i];

    return count;
  }

  /// <summary>Where a block's top-left pixel is in the padded grid.</summary>
  private int _Corner(int block)
    => block / this._blocksAcross * _BLOCK * this._codedWidth + block % this._blocksAcross * _BLOCK;

  // ============================================================================================
  // The opcodes
  // ============================================================================================

  /// <summary>
  /// Writes an opcode that carries nothing but a block count, in whichever of its two spellings the
  /// count fits.
  /// </summary>
  private void _PutRun(byte inlineOpcode, byte countedOpcode, int blocks) {
    if (blocks <= _INLINE_RUN)
      this._Put((byte)(inlineOpcode | (blocks - 1)));
    else
      this._Put(countedOpcode, (byte)(blocks - 1));
  }

  /// <summary>Writes a run of blocks that share one set of distinct colours.</summary>
  private void _PutCodedRun(byte[] current, int block, int blocks, ReadOnlySpan<byte> distinct) {
    switch (distinct.Length) {
      case 1:
        this._PutRun(_ONE_COLOUR_INLINE, _ONE_COLOUR_BYTE, blocks);
        this._Put(distinct[0]);
        return;

      case 2: {
        var pair = this._Colours(_TWO_COLOUR_NEW, _TWO_COLOUR_CACHED, blocks, distinct, this._pairCache, 2, ref this._pairFilled, ref this._pairNext);
        for (var i = 0; i < blocks; ++i)
          this._PutTwoColour(current, block + i, pair);

        return;
      }

      case 3 or 4: {
        var quad = this._Colours(_FOUR_COLOUR_NEW, _FOUR_COLOUR_CACHED, blocks, distinct, this._quadCache, 4, ref this._quadFilled, ref this._quadNext);
        for (var i = 0; i < blocks; ++i)
          this._PutFourColour(current, block + i, quad);

        return;
      }

      case >= 5 and <= 8: {
        var octet = this._Colours(_EIGHT_COLOUR_NEW, _EIGHT_COLOUR_CACHED, blocks, distinct, this._octetCache, 8, ref this._octetFilled, ref this._octetNext);
        for (var i = 0; i < blocks; ++i)
          this._PutEightColour(current, block + i, octet);

        return;
      }

      default:
        this._Put((byte)(_SIXTEEN_COLOUR | (blocks - 1)));
        for (var i = 0; i < blocks; ++i) {
          var at = this._Corner(block + i);
          for (var row = 0; row < _BLOCK; ++row)
            this._Put(current.AsSpan(at + row * this._codedWidth, _BLOCK));
        }

        return;
    }
  }

  /// <summary>
  /// Writes the opcode of a several-colour run and answers the set of colours its blocks index into.
  /// </summary>
  /// <remarks>
  /// A set this packet has already written and which holds every colour the run needs is named by its
  /// cache index, two bytes in all; otherwise the colours are written out and take the next slot of
  /// the cache, which is circular and 256 entries long. Only entries this packet wrote are searched,
  /// which is what keeps the stream readable whether or not a decoder carries its caches across
  /// packets — see the remark on the class.
  /// <para/>
  /// A set shorter than the cache's width is padded by repeating its last colour. The padding is
  /// never indexed, since every pixel of every block in the run is one of the distinct colours and
  /// finds the first slot holding it.
  /// </remarks>
  private ReadOnlySpan<byte> _Colours(
    byte newOpcode, byte cachedOpcode, int blocks, ReadOnlySpan<byte> distinct, byte[] cache, int width,
    ref int filled, ref int next) {
    for (var entry = 0; entry < filled; ++entry) {
      var held = cache.AsSpan(entry * width, width);
      var complete = true;
      foreach (var colour in distinct)
        if (held.IndexOf(colour) < 0) {
          complete = false;
          break;
        }

      if (!complete)
        continue;

      this._Put((byte)(cachedOpcode | (blocks - 1)), (byte)entry);
      return held;
    }

    this._Put((byte)(newOpcode | (blocks - 1)));

    var slot = cache.AsSpan(next * width, width);
    distinct.CopyTo(slot);
    slot[distinct.Length..].Fill(distinct[^1]);
    this._Put(slot);

    next = (next + 1) % _CACHE_SIZE;
    filled = Math.Min(filled + 1, _CACHE_SIZE);
    return slot;
  }

  /// <summary>
  /// Writes a two-colour block: one bit a pixel, set where the pixel is the second of the pair, in
  /// raster order from the most significant bit of the first of two bytes.
  /// </summary>
  private void _PutTwoColour(byte[] current, int block, ReadOnlySpan<byte> pair) {
    var at = this._Corner(block);
    var flags = 0;
    for (var pixel = 0; pixel < _BLOCK_PIXELS; ++pixel)
      if (current[at + pixel / _BLOCK * this._codedWidth + pixel % _BLOCK] == pair[1])
        flags |= 1 << (15 - pixel);

    this._Put((byte)(flags >> 8), (byte)flags);
  }

  /// <summary>
  /// Writes a four-colour block: two bits a pixel indexing the quad, one byte a row, most significant
  /// pair leftmost.
  /// </summary>
  private void _PutFourColour(byte[] current, int block, ReadOnlySpan<byte> quad) {
    var at = this._Corner(block);
    var flags = 0u;
    for (var pixel = 0; pixel < _BLOCK_PIXELS; ++pixel) {
      var index = (uint)quad.IndexOf(current[at + pixel / _BLOCK * this._codedWidth + pixel % _BLOCK]);
      flags |= index << (30 - pixel * 2);
    }

    this._Put((byte)(flags >> 24), (byte)(flags >> 16), (byte)(flags >> 8), (byte)flags);
  }

  /// <summary>
  /// Writes an eight-colour block: three bits a pixel indexing the octet, in the permuted six-byte
  /// packing the format states.
  /// </summary>
  /// <remarks>
  /// The forty-eight index bits are not six bytes of four pixels apiece. They are cut into two
  /// twenty-four bit halves, one for the top two rows and one for the bottom two, and the six bytes
  /// interleave them: the first two and a half bytes of each half in order, then their last nibbles
  /// gathered into the low nibbles of the second, fourth and sixth byte.
  /// <see cref="AppleGraphicsDecoder"/> reads exactly this apart again.
  /// </remarks>
  private void _PutEightColour(byte[] current, int block, ReadOnlySpan<byte> octet) {
    var at = this._Corner(block);
    var flags = 0ul;
    for (var pixel = 0; pixel < _BLOCK_PIXELS; ++pixel) {
      var index = (ulong)octet.IndexOf(current[at + pixel / _BLOCK * this._codedWidth + pixel % _BLOCK]);
      flags |= index << (45 - pixel * 3);
    }

    this._Put((byte)(flags >> 40), (byte)(((flags >> 32) & 0xF0) | ((flags >> 8) & 0x0F)));
    this._Put((byte)(flags >> 28), (byte)(((flags >> 20) & 0xF0) | ((flags >> 4) & 0x0F)));
    this._Put((byte)(flags >> 16), (byte)(((flags >> 8) & 0xF0) | (flags & 0x0F)));
  }

  // ============================================================================================
  // The sample description
  // ============================================================================================

  /// <summary>A whole <c>smc </c> visual sample entry, box header and colour table and all.</summary>
  /// <remarks>
  /// The fields are the ones every visual sample entry has, at the places every container reads them;
  /// the colour table follows the depth as a QuickTime <c>ColorTable</c> — a seed, flags, one less
  /// than the entry count, and the entries as four sixteen-bit values apiece with each colour's eight
  /// bits repeated into both halves.
  /// </remarks>
  private byte[] _SampleEntry() {
    const int bodyLength = 78;
    const int tableHeader = 8;
    const int tableEntry = 8;

    var entry = new byte[8 + bodyLength + tableHeader + this._paletteCount * tableEntry];
    var span = entry.AsSpan();

    BinaryPrimitives.WriteInt32BigEndian(span, entry.Length);
    "smc "u8.CopyTo(span[4..]);
    var body = span[8..];
    BinaryPrimitives.WriteUInt16BigEndian(body[6..], 1);                   // data reference index
    BinaryPrimitives.WriteUInt16BigEndian(body[24..], (ushort)this._width);
    BinaryPrimitives.WriteUInt16BigEndian(body[26..], (ushort)this._height);
    BinaryPrimitives.WriteUInt32BigEndian(body[28..], 0x00480000);         // 72 dpi
    BinaryPrimitives.WriteUInt32BigEndian(body[32..], 0x00480000);
    BinaryPrimitives.WriteUInt16BigEndian(body[40..], 1);                  // frames per sample
    var name = "Graphics"u8;
    body[42] = (byte)name.Length;
    name.CopyTo(body[43..]);
    BinaryPrimitives.WriteUInt16BigEndian(body[74..], _DEPTH);
    BinaryPrimitives.WriteUInt16BigEndian(body[76..], 0);                  // a table of its own follows

    var table = body[bodyLength..];
    BinaryPrimitives.WriteUInt16BigEndian(table[6..], (ushort)(this._paletteCount - 1));
    var palette = this._palette!;
    for (var i = 0; i < this._paletteCount; ++i) {
      var colour = table.Slice(tableHeader + i * tableEntry, tableEntry);
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

  private void _Put(byte first, byte second) {
    this._Put(first);
    this._Put(second);
  }

  private void _Put(byte first, byte second, byte third, byte fourth) {
    this._Put(first);
    this._Put(second);
    this._Put(third);
    this._Put(fourth);
  }

  private void _Put(ReadOnlySpan<byte> bytes) {
    if (this._length + bytes.Length > this._buffer.Length)
      Array.Resize(ref this._buffer, Math.Max(this._buffer.Length * 2, this._length + bytes.Length));

    bytes.CopyTo(this._buffer.AsSpan(this._length));
    this._length += bytes.Length;
  }
}
