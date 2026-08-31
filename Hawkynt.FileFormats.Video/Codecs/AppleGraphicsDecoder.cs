using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Apple Graphics, the codec every QuickTime file calls <c>smc </c> (trailing space and
/// all): a vector quantizer over 4x4 blocks of eight-bit palettised pixels, sometimes called SMC
/// after its author's initials.
/// </summary>
/// <remarks>
/// Blocks run left to right, top to bottom, and a block is coded one of nine ways: skipped, so the
/// frame before it is left alone; the last block, or the last two blocks together, repeated forward;
/// one colour; two, four or eight colours chosen per pixel by packed indices, each set of colours
/// either given in the stream or referred to by number in one of three small caches the decoder
/// keeps; or sixteen raw palette indices with nothing shared between them at all.
/// <para/>
/// <b>Two kinds of state, on two different clocks.</b> The picture persists between packets — a
/// skipped or repeated block means the frame before is still there to draw from — but the three
/// colour caches do not: they are reset empty at the start of every packet, so a cached-colour opcode
/// can only refer to a set of colours the same packet has already stated, never to one from the frame
/// before. A byte naming an entry nothing in this packet has written yet is not refused; it reads as
/// the caches' reset value, because the format states no bound on which entries a stream may name and
/// a decoder that guessed at a bound would be inventing one.
/// <para/>
/// <b>An absent colour table is usually not a missing one.</b> Most real Apple Graphics streams carry
/// no <c>ctab</c> atom at all — the sample description ends exactly where one would begin — because
/// QuickTime defines a standard colour table for every indexed depth, the classic Macintosh system
/// palette, and a description naming it by its own depth rather than embedding it is stating that
/// palette rather than stating nothing. This decoder builds that table rather than refusing the
/// stream; see the remark on <see cref="_ReadPalette"/> for which descriptions this applies to and
/// which are refused for naming a colour resource genuinely outside the file.
/// <para/>
/// <b>What it does not read refuses by name.</b> A depth other than the two this format's visual
/// sample entries state — eight bits with a colour table, or forty for the greyscale convention the
/// entry uses for the same eight-bit depth — a colour table naming a system resource this library
/// cannot look up, a chunk shorter than its four-byte header, an opcode's run reaching past the last
/// block, a chunk that stops before every block is accounted for, and a repeat opcode with nothing
/// before it to repeat. A skip opcode is not refused on the very first frame, for the same reason it
/// is not refused in Apple Video: the canvas a freshly built decoder starts with is black, which is
/// exactly what a skip paints when nothing has been decoded yet.
/// </remarks>
public sealed class AppleGraphicsDecoder : IVideoCodecDecoder<AppleGraphicsDecoder> {

  private static readonly CodecTag _SMC = CodecTag.FromCharacters("smc ");

  private const int _BLOCK = 4;
  private const int _CACHE_SIZE = 256;

  private const byte _SKIP_INLINE = 0x00;
  private const byte _SKIP_BYTE = 0x10;
  private const byte _REPEAT_ONE_INLINE = 0x20;
  private const byte _REPEAT_ONE_BYTE = 0x30;
  private const byte _REPEAT_TWO_INLINE = 0x40;
  private const byte _REPEAT_TWO_BYTE = 0x50;
  private const byte _ONE_COLOUR_INLINE = 0x60;
  private const byte _ONE_COLOUR_BYTE = 0x70;
  private const byte _TWO_COLOUR_NEW = 0x80;
  private const byte _TWO_COLOUR_CACHED = 0x90;
  private const byte _FOUR_COLOUR_NEW = 0xA0;
  private const byte _FOUR_COLOUR_CACHED = 0xB0;
  private const byte _EIGHT_COLOUR_NEW = 0xC0;
  private const byte _EIGHT_COLOUR_CACHED = 0xD0;
  private const byte _SIXTEEN_COLOUR = 0xE0;

  private readonly int _width;
  private readonly int _height;
  private readonly int _codedWidth;
  private readonly int _codedHeight;
  private readonly int _blocksAcross;
  private readonly int _blockRows;
  private readonly byte[] _palette;
  private readonly int _paletteCount;

  /// <summary>
  /// The picture as palette indices, one byte a pixel, over the padded block grid and the right way
  /// up.
  /// </summary>
  /// <remarks>
  /// Kept between packets and never cleared, because a skipped or repeated block means "as the frame
  /// before left it" and there is nowhere else for that frame to be. Sized to the padded grid rather
  /// than the visible picture so a width or height that is not a whole number of blocks still has
  /// whole blocks to decode into; the padding is cropped off only when a picture is handed back.
  /// </remarks>
  private readonly byte[] _canvas;

  /// <summary>The colour pair, quad and octet caches, each 256 entries, reset before every packet.</summary>
  private readonly byte[] _pairCache = new byte[_CACHE_SIZE * 2];
  private readonly byte[] _quadCache = new byte[_CACHE_SIZE * 4];
  private readonly byte[] _octetCache = new byte[_CACHE_SIZE * 8];
  private int _pairNext;
  private int _quadNext;
  private int _octetNext;

  private AppleGraphicsDecoder(int width, int height, byte[] palette, int paletteCount) {
    this._width = width;
    this._height = height;
    this._palette = palette;
    this._paletteCount = paletteCount;
    this._codedWidth = (width + _BLOCK - 1) / _BLOCK * _BLOCK;
    this._codedHeight = (height + _BLOCK - 1) / _BLOCK * _BLOCK;
    this._blocksAcross = this._codedWidth / _BLOCK;
    this._blockRows = this._codedHeight / _BLOCK;
    this._canvas = new byte[this._codedWidth * this._codedHeight];
  }

  /// <summary>Gets the codec name.</summary>
  public static string CodecName => "Apple Graphics (SMC)";

  /// <summary>Determines whether the specified media stream is supported.</summary>
  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_SMC);
  }

  /// <summary>
  /// Builds a decoder for one Apple Graphics stream, reading its colour table from the sample
  /// description QuickTime carries beside it — or, where the description states none of its own,
  /// filling in the standard table QuickTime defines for the depth.
  /// </summary>
  /// <remarks>
  /// The codec is defined at eight bits a pixel only, whether the sample entry states that directly
  /// or, for a greyscale capture, states it as depth forty — so a stream naming any other depth is
  /// refused rather than read as whichever width of index looks plausible.
  /// </remarks>
  public static AppleGraphicsDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    var width = stream.Width;
    var height = stream.Height;
    if (width <= 0 || height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {width}x{height}, which is not a size an Apple Graphics frame can be decoded into.");

    if (stream.BitsPerPixel is not (0 or _COLOUR_DEPTH or _GREYSCALE_DEPTH))
      throw new NotSupportedException(
        $"Video stream {stream.Index} states {stream.BitsPerPixel} bits per pixel. Apple Graphics is defined at "
        + "eight bits a pixel, where the values are palette indices — stated as depth 8 for a colour table or "
        + "depth 40 for the greyscale convention QuickTime's visual sample entry uses for an eight-bit depth — "
        + "and nothing else is read.");

    var (palette, paletteCount) = _ReadPalette(stream);
    return new(width, height, palette, paletteCount);
  }

  /// <summary>Decodes one packet, which for this codec is always exactly one whole frame.</summary>
  /// <remarks>
  /// The three colour caches are reset here, before the canvas is touched, because they belong to the
  /// packet and not to the picture: a cached-colour opcode of one frame must never reach a colour set
  /// only the frame before it stated.
  /// </remarks>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    Array.Clear(this._pairCache);
    Array.Clear(this._quadCache);
    Array.Clear(this._octetCache);
    this._pairNext = this._quadNext = this._octetNext = 0;

    this._DecodeFrame(packet.Data.Span);

    frame = new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Indexed8,
      PixelData = this._Picture(),
      Palette = this._palette,
      PaletteCount = this._paletteCount,
    };

    return true;
  }

  // ============================================================================================
  // The block walk
  // ============================================================================================

  /// <summary>Walks one chunk's opcodes, block by block, from the top left.</summary>
  /// <remarks>
  /// The first four bytes — an unexplained flags byte and a three-byte chunk length, the same shape
  /// Apple Video's chunks open with — are read past rather than checked, for the same reason: the
  /// container has already said how long this packet is, and that is what decides where the data
  /// ends.
  /// </remarks>
  private void _DecodeFrame(ReadOnlySpan<byte> data) {
    if (data.Length < 4)
      throw new InvalidDataException(
        $"An Apple Graphics chunk is {data.Length} byte(s), where the four-byte header alone is 4.");

    var total = this._blocksAcross * this._blockRows;
    var position = 4;
    var block = 0;

    while (block < total) {
      var opcodeByte = _NextByte(data, ref position, block, "an opcode byte");
      var opcode = (byte)(opcodeByte & 0xF0);
      var inlineCount = (opcodeByte & 0x0F) + 1;

      switch (opcode) {
        case _SKIP_INLINE:
          this._RefuseRunPastEnd(block, inlineCount, total);
          block += inlineCount;
          break;

        case _SKIP_BYTE: {
          var count = _NextByte(data, ref position, block, "a skip run's block count") + 1;
          this._RefuseRunPastEnd(block, count, total);
          block += count;
          break;
        }

        case _REPEAT_ONE_INLINE:
          block = this._RepeatOneBlock(block, total, inlineCount);
          break;

        case _REPEAT_ONE_BYTE: {
          var count = _NextByte(data, ref position, block, "a repeat run's block count") + 1;
          block = this._RepeatOneBlock(block, total, count);
          break;
        }

        case _REPEAT_TWO_INLINE:
          block = this._RepeatTwoBlocks(block, total, inlineCount);
          break;

        case _REPEAT_TWO_BYTE: {
          var count = _NextByte(data, ref position, block, "a repeat run's pair count") + 1;
          block = this._RepeatTwoBlocks(block, total, count);
          break;
        }

        case _ONE_COLOUR_INLINE:
          block = this._PaintOneColourRun(data, ref position, block, total, inlineCount);
          break;

        case _ONE_COLOUR_BYTE: {
          var count = _NextByte(data, ref position, block, "a one-colour run's block count") + 1;
          block = this._PaintOneColourRun(data, ref position, block, total, count);
          break;
        }

        case _TWO_COLOUR_NEW:
          block = this._PaintTwoColourRun(data, ref position, block, total, inlineCount, newEntry: true);
          break;

        case _TWO_COLOUR_CACHED:
          block = this._PaintTwoColourRun(data, ref position, block, total, inlineCount, newEntry: false);
          break;

        case _FOUR_COLOUR_NEW:
          block = this._PaintFourColourRun(data, ref position, block, total, inlineCount, newEntry: true);
          break;

        case _FOUR_COLOUR_CACHED:
          block = this._PaintFourColourRun(data, ref position, block, total, inlineCount, newEntry: false);
          break;

        case _EIGHT_COLOUR_NEW:
          block = this._PaintEightColourRun(data, ref position, block, total, inlineCount, newEntry: true);
          break;

        case _EIGHT_COLOUR_CACHED:
          block = this._PaintEightColourRun(data, ref position, block, total, inlineCount, newEntry: false);
          break;

        case _SIXTEEN_COLOUR:
          block = this._PaintSixteenColourRun(data, ref position, block, total, inlineCount);
          break;

        default:
          throw new NotSupportedException(
            $"An Apple Graphics opcode byte 0x{opcodeByte:X2} at block {block} names opcode 0x{opcode:X2}, which "
            + "the format does not define.");
      }
    }
  }

  private void _RefuseRunPastEnd(int block, int count, int total) {
    if (block + count <= total)
      return;

    throw new InvalidDataException(
      $"An Apple Graphics opcode at block {block} runs {count} block(s), which reaches past the last of the "
      + $"{total} blocks of a {this._width}x{this._height} picture.");
  }

  // ============================================================================================
  // Skip's siblings: repeating the block or the pair of blocks already on the canvas
  // ============================================================================================

  /// <summary>
  /// Repeats the block immediately before this run, the same one, into each of the run's positions.
  /// </summary>
  private int _RepeatOneBlock(int block, int total, int count) {
    this._RefuseRunPastEnd(block, count, total);
    this._RefuseNothingToRepeat(block);

    var source = block - 1;
    for (var i = 0; i < count; ++i)
      this._CopyBlock(source, block + i);

    return block + count;
  }

  private void _RefuseNothingToRepeat(int block) {
    if (block >= 1)
      return;

    throw new InvalidDataException(
      $"An Apple Graphics repeat opcode at block {block} names the block before it, but there is none there.");
  }

  /// <summary>
  /// Repeats the two blocks immediately before this run, the same two, into each pair of the run's
  /// positions.
  /// </summary>
  /// <remarks>
  /// Earlier hand-built chunks exercising this opcode disagreed with ffmpeg's decode of them in ways
  /// that did not resolve into a rule, which is what this decoder refused the opcode over at first.
  /// Two real streams that carry it settled the question instead: both decode byte for byte identical
  /// to ffmpeg's output against exactly the reading here — the source is always the two blocks
  /// immediately before the run in canvas order, whatever opcode wrote them and whether or not that
  /// pair straddles a row of blocks — across 671 frames between them and every occurrence of the
  /// opcode either stream contains. The earlier disagreement was in the hand-built chunks, not in the
  /// format or in ffmpeg's reading of it.
  /// </remarks>
  private int _RepeatTwoBlocks(int block, int total, int pairs) {
    var count = pairs * 2;
    this._RefuseRunPastEnd(block, count, total);
    this._RefuseNothingToRepeatTwo(block);

    var firstSource = block - 2;
    var secondSource = block - 1;
    for (var i = 0; i < pairs; ++i) {
      this._CopyBlock(firstSource, block + i * 2);
      this._CopyBlock(secondSource, block + i * 2 + 1);
    }

    return block + count;
  }

  private void _RefuseNothingToRepeatTwo(int block) {
    if (block >= 2)
      return;

    throw new InvalidDataException(
      $"An Apple Graphics repeat opcode at block {block} names the two blocks before it, but there {(block == 0 ? "are none" : "is only one")} there.");
  }

  private void _CopyBlock(int from, int to) {
    var (fromLeft, fromTop) = this._Corner(from);
    var (toLeft, toTop) = this._Corner(to);
    for (var row = 0; row < _BLOCK; ++row) {
      var fromOffset = (fromTop + row) * this._codedWidth + fromLeft;
      var toOffset = (toTop + row) * this._codedWidth + toLeft;
      this._canvas.AsSpan(fromOffset, _BLOCK).CopyTo(this._canvas.AsSpan(toOffset, _BLOCK));
    }
  }

  // ============================================================================================
  // One, two, four and eight colours
  // ============================================================================================

  private int _PaintOneColourRun(ReadOnlySpan<byte> data, ref int position, int block, int total, int count) {
    this._RefuseRunPastEnd(block, count, total);
    var colour = _NextByte(data, ref position, block, "a one-colour block's palette index");

    for (var i = 0; i < count; ++i)
      this._PaintSolid(block + i, colour);

    return block + count;
  }

  private int _PaintTwoColourRun(ReadOnlySpan<byte> data, ref int position, int block, int total, int count, bool newEntry) {
    this._RefuseRunPastEnd(block, count, total);
    var pair = newEntry
      ? this._StoreNewEntry(data, ref position, block, this._pairCache, 2, ref this._pairNext, "a two-colour block's two palette indices")
      : this._CachedEntry(data, ref position, block, this._pairCache, 2, "a two-colour block's cache index");

    for (var i = 0; i < count; ++i) {
      var flags = _Read(data, ref position, 2, block + i, "a two-colour block's two flag bytes");
      this._PaintTwoColour(block + i, data[flags], data[flags + 1], pair);
    }

    return block + count;
  }

  private int _PaintFourColourRun(ReadOnlySpan<byte> data, ref int position, int block, int total, int count, bool newEntry) {
    this._RefuseRunPastEnd(block, count, total);
    var quad = newEntry
      ? this._StoreNewEntry(data, ref position, block, this._quadCache, 4, ref this._quadNext, "a four-colour block's four palette indices")
      : this._CachedEntry(data, ref position, block, this._quadCache, 4, "a four-colour block's cache index");

    for (var i = 0; i < count; ++i) {
      var flags = _Read(data, ref position, 4, block + i, "a four-colour block's four flag bytes");
      this._PaintFourColour(block + i, data.Slice(flags, 4), quad);
    }

    return block + count;
  }

  private int _PaintEightColourRun(ReadOnlySpan<byte> data, ref int position, int block, int total, int count, bool newEntry) {
    this._RefuseRunPastEnd(block, count, total);
    var octet = newEntry
      ? this._StoreNewEntry(data, ref position, block, this._octetCache, 8, ref this._octetNext, "an eight-colour block's eight palette indices")
      : this._CachedEntry(data, ref position, block, this._octetCache, 8, "an eight-colour block's cache index");

    for (var i = 0; i < count; ++i) {
      var flags = _Read(data, ref position, 6, block + i, "an eight-colour block's six flag bytes");
      this._PaintEightColour(block + i, data.Slice(flags, 6), octet);
    }

    return block + count;
  }

  private int _PaintSixteenColourRun(ReadOnlySpan<byte> data, ref int position, int block, int total, int count) {
    this._RefuseRunPastEnd(block, count, total);

    for (var i = 0; i < count; ++i) {
      var raw = _Read(data, ref position, 16, block + i, "a sixteen-colour block's sixteen palette indices");
      this._PaintSixteenColour(block + i, data.Slice(raw, 16));
    }

    return block + count;
  }

  /// <summary>
  /// Reads a fresh set of colours off the stream and stores it in the next slot of a circular cache.
  /// </summary>
  private ReadOnlySpan<byte> _StoreNewEntry(
    ReadOnlySpan<byte> data, ref int position, int block, byte[] cache, int width, ref int next, string what) {
    var at = _Read(data, ref position, width, block, what);
    var slot = next * width;
    data.Slice(at, width).CopyTo(cache.AsSpan(slot, width));
    next = (next + 1) % _CACHE_SIZE;
    return cache.AsSpan(slot, width);
  }

  /// <summary>
  /// Reads a cache index off the stream and answers the entry it names, which may be one this packet
  /// has not written yet — the format states no bound on the index a stream may give, so an
  /// unwritten, freshly reset entry is what such an index reads as.
  /// </summary>
  private ReadOnlySpan<byte> _CachedEntry(
    ReadOnlySpan<byte> data, ref int position, int block, byte[] cache, int width, string what) {
    var index = _NextByte(data, ref position, block, what);
    return cache.AsSpan(index * width, width);
  }

  // ============================================================================================
  // Reading bytes
  // ============================================================================================

  private static byte _NextByte(ReadOnlySpan<byte> data, ref int position, int block, string what) {
    if (position >= data.Length)
      throw new InvalidDataException(
        $"An Apple Graphics chunk of {data.Length} byte(s) ran out before block {block} was accounted for; {what} "
        + "was expected next.");

    return data[position++];
  }

  private static int _Read(ReadOnlySpan<byte> data, ref int position, int count, int block, string what) {
    if (position + count > data.Length)
      throw new InvalidDataException(
        $"An Apple Graphics chunk of {data.Length} byte(s) ends {count - (data.Length - position)} byte(s) short "
        + $"of {what} at block {block}.");

    var at = position;
    position += count;
    return at;
  }

  // ============================================================================================
  // Painting a block
  // ============================================================================================

  private void _PaintSolid(int block, byte colour) {
    var (left, top) = this._Corner(block);
    for (var row = 0; row < _BLOCK; ++row) {
      var offset = (top + row) * this._codedWidth + left;
      for (var column = 0; column < _BLOCK; ++column)
        this._canvas[offset + column] = colour;
    }
  }

  /// <summary>
  /// Paints a two-colour block from its two flag bytes: one bit a pixel, byte <paramref name="a"/>
  /// covering the top two rows and <paramref name="b"/> the bottom two, the high nibble of each byte
  /// the row above its low nibble, most significant bit leftmost.
  /// </summary>
  private void _PaintTwoColour(int block, byte a, byte b, ReadOnlySpan<byte> pair) {
    var (left, top) = this._Corner(block);
    Span<byte> rows = [(byte)(a >> 4), (byte)(a & 0xF), (byte)(b >> 4), (byte)(b & 0xF)];
    for (var row = 0; row < _BLOCK; ++row) {
      var offset = (top + row) * this._codedWidth + left;
      var nibble = rows[row];
      for (var column = 0; column < _BLOCK; ++column)
        this._canvas[offset + column] = pair[(nibble >> (3 - column)) & 1];
    }
  }

  /// <summary>
  /// Paints a four-colour block from its four index bytes, one byte a row, two bits a pixel, most
  /// significant pair first — the same layout Apple Video's four-colour block uses, indexing directly
  /// into the given colours rather than a computed quad.
  /// </summary>
  private void _PaintFourColour(int block, ReadOnlySpan<byte> flags, ReadOnlySpan<byte> quad) {
    var (left, top) = this._Corner(block);
    for (var row = 0; row < _BLOCK; ++row) {
      var flagByte = flags[row];
      var offset = (top + row) * this._codedWidth + left;
      for (var column = 0; column < _BLOCK; ++column) {
        var index = (flagByte >> ((3 - column) * 2)) & 0x3;
        this._canvas[offset + column] = quad[index];
      }
    }
  }

  /// <summary>
  /// Paints an eight-colour block from its six flag bytes, which are not six bytes of four pixels
  /// apiece but two 24-bit numbers built by picking nibbles out of all six at once.
  /// </summary>
  /// <remarks>
  /// Twelve nibbles come out of the six bytes: <c>n0 n1</c> from the first, <c>n2 n3</c> the second,
  /// and so on to <c>nA nB</c> from the sixth. The first 24-bit number, covering the block's top two
  /// rows, is <c>n0 n1 n2 n4 n5 n6</c> in that order most significant first; the second, covering the
  /// bottom two rows, is <c>n8 n9 nA n3 n7 nB</c>. Where that comes from is not guessable from the
  /// format's shape the way every other block's layout here is — it was recovered by decoding a
  /// stream with byte values <c>01 23 45 67 89 AB</c> and reading back which nibble of which byte
  /// produced which of the eight index positions, which is what the two orderings above are. Each
  /// 24-bit number then splits into eight three-bit fields, most significant first, one a pixel.
  /// </remarks>
  private void _PaintEightColour(int block, ReadOnlySpan<byte> raw, ReadOnlySpan<byte> octet) {
    Span<byte> nibbles = stackalloc byte[12];
    for (var i = 0; i < 6; ++i) {
      nibbles[i * 2] = (byte)(raw[i] >> 4);
      nibbles[i * 2 + 1] = (byte)(raw[i] & 0xF);
    }

    var flagsA = (nibbles[0] << 20) | (nibbles[1] << 16) | (nibbles[2] << 12) | (nibbles[4] << 8) | (nibbles[5] << 4) | nibbles[6];
    var flagsB = (nibbles[8] << 20) | (nibbles[9] << 16) | (nibbles[10] << 12) | (nibbles[3] << 8) | (nibbles[7] << 4) | nibbles[11];

    var (left, top) = this._Corner(block);
    for (var row = 0; row < _BLOCK; ++row) {
      var flags = row < 2 ? flagsA : flagsB;
      var localRow = row < 2 ? row : row - 2;
      var offset = (top + row) * this._codedWidth + left;
      for (var column = 0; column < _BLOCK; ++column) {
        var position = localRow * _BLOCK + column;
        var index = (flags >> ((7 - position) * 3)) & 0x7;
        this._canvas[offset + column] = octet[index];
      }
    }
  }

  /// <summary>Paints a block whose sixteen pixels are given as raw palette indices, in raster order.</summary>
  private void _PaintSixteenColour(int block, ReadOnlySpan<byte> raw) {
    var (left, top) = this._Corner(block);
    for (var row = 0; row < _BLOCK; ++row) {
      var offset = (top + row) * this._codedWidth + left;
      raw.Slice(row * _BLOCK, _BLOCK).CopyTo(this._canvas.AsSpan(offset, _BLOCK));
    }
  }

  /// <summary>Where a block's top-left pixel is, given that blocks are coded from the top of the picture.</summary>
  private (int Left, int Top) _Corner(int block) {
    var blockRow = block / this._blocksAcross;
    var blockColumn = block % this._blocksAcross;

    return (blockColumn * _BLOCK, blockRow * _BLOCK);
  }

  // ============================================================================================
  // Handing the picture over
  // ============================================================================================

  /// <summary>The visible picture, cropped from the padded canvas.</summary>
  private byte[] _Picture() {
    if (this._codedWidth == this._width)
      return (byte[])this._canvas.Clone();

    var picture = new byte[this._width * this._height];
    for (var y = 0; y < this._height; ++y)
      this._canvas.AsSpan(y * this._codedWidth, this._width).CopyTo(picture.AsSpan(y * this._width));

    return picture;
  }

  // ============================================================================================
  // The colour table
  // ============================================================================================

  /// <summary>The depth value a colour sample description states for an eight-bit colour table.</summary>
  private const int _COLOUR_DEPTH = 8;

  /// <summary>
  /// The depth value QuickTime's visual sample entry uses for "eight bits, greyscale" — the colour
  /// depth with 0x20 added, which is the same convention this library's QuickTime Animation decoder
  /// reads for its own greyscale depths and is a property of the sample entry's depth field rather
  /// than of any one codec.
  /// </summary>
  private const int _GREYSCALE_DEPTH = 40;

  /// <summary>Where the depth sits, counted from the first byte of the sample description's body.</summary>
  private const int _DEPTH_AT = 74;

  /// <summary>Where the colour table identifier sits, immediately after the depth.</summary>
  private const int _COLOUR_TABLE_ID_AT = 76;

  /// <summary>Where a table, when there is one, begins.</summary>
  private const int _COLOUR_TABLE_AT = 78;

  /// <summary>
  /// The identifier a description states when it carries no table of its own and none should be
  /// assumed — as distinct from the identifier equalling the depth, which is a request for the
  /// standard table of that depth rather than an assertion that no colours exist. See the remark on
  /// <see cref="_ReadPalette"/>.
  /// </summary>
  private const ushort _NO_COLOUR_TABLE = 0xFFFF;

  /// <summary>The length field's escape value, which means a sixty-four bit length follows it.</summary>
  private const uint _EXTENDED_LENGTH = 1;

  /// <summary>
  /// Reads the colour table out of the visual sample entry QuickTime carries beside the stream, or
  /// answers the standard table for the stream's depth when the entry carries none.
  /// </summary>
  /// <remarks>
  /// An absent table is not necessarily missing information. The QuickTime file format defines a
  /// standard colour table for each indexed depth — the classic Macintosh system palette — identified
  /// by a colour table ID equal to the depth itself, and a description naming that ID, or the generic
  /// "no table follows" identifier 0xFFFF, with no table bytes actually present is stating "use the
  /// standard table for my own depth" rather than "I have no colours". Measured on six real streams:
  /// four state 8, their own depth, as the colour table ID with nothing following it; one states the
  /// generic 0xFFFF; and one — the sole greyscale sample found — states 40, again its own depth. All
  /// six decode against ffmpeg once the standard table for their depth is filled in; see the remark on
  /// <see cref="_StandardColourTable"/> for how that table's colours were verified.
  /// <para/>
  /// A description naming some other identifier, with no table bytes present either, names a colour
  /// resource this library has no way to look up — a custom system CLUT living outside the file — and
  /// is refused rather than answered with the wrong standard table.
  /// </remarks>
  private static (byte[] Palette, int Count) _ReadPalette(MediaStreamInfo stream) {
    const int entries = 256;

    var body = _SampleDescriptionBody(stream.CodecPrivateData.Span);
    if (body.Length < _COLOUR_TABLE_AT)
      throw new InvalidDataException(
        $"Video stream {stream.Index} carries {body.Length} bytes of visual sample entry, which is too short to "
        + "reach the depth and colour table fields an Apple Graphics stream is described by.");

    var depth = BinaryPrimitives.ReadUInt16BigEndian(body.Slice(_DEPTH_AT, 2));
    var colourTableId = BinaryPrimitives.ReadUInt16BigEndian(body.Slice(_COLOUR_TABLE_ID_AT, 2));
    var table = body[_COLOUR_TABLE_AT..];

    if (table.Length == 0) {
      if (colourTableId != _NO_COLOUR_TABLE && colourTableId != depth)
        throw new NotSupportedException(
          $"Video stream {stream.Index} carries no colour table and names colour table identifier "
          + $"{colourTableId}, which is neither \"no table\" (0xFFFF) nor its own depth ({depth}) asking for the "
          + "standard table of that depth. That identifier names a system colour resource outside the file, "
          + "which this library has no way to look up.");

      return (_StandardColourTable(depth == _GREYSCALE_DEPTH), entries);
    }

    const int header = 8;
    const int entrySize = 8;

    if (table.Length < header)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a colour table of {table.Length} bytes, which is shorter than the "
        + "eight a table's own header is.");

    var stated = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(6, 2)) + 1;
    if (stated > entries)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a colour table of {stated} entries where an eight-bit depth has room "
        + $"for {entries}.");

    if (table.Length < header + stated * entrySize)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a colour table of {stated} entries but carries "
        + $"{table.Length - header} bytes for them, where {stated * entrySize} are needed.");

    var palette = new byte[entries * 3];
    for (var i = 0; i < stated; ++i) {
      var entry = table.Slice(header + i * entrySize, entrySize);
      palette[i * 3] = entry[2];
      palette[i * 3 + 1] = entry[4];
      palette[i * 3 + 2] = entry[6];
    }

    return (palette, entries);
  }

  private static ReadOnlySpan<byte> _SampleDescriptionBody(ReadOnlySpan<byte> sampleDescription) {
    if (sampleDescription.Length < 8)
      return default;

    var header = BinaryPrimitives.ReadUInt32BigEndian(sampleDescription) == _EXTENDED_LENGTH ? 16 : 8;
    return sampleDescription.Length < header ? default : sampleDescription[header..];
  }

  /// <summary>
  /// The classic Macintosh system palette for an eight-bit depth: the two hundred and fifty-six
  /// colours QuickDraw's default 'clut' resource of ID 8 holds, or — where <paramref name="grey"/> —
  /// the linear ramp from white to black the same depth's default greyscale resource holds instead.
  /// </summary>
  /// <remarks>
  /// The colour table is not two hundred and fifty-six arbitrary triples; it is generated, the way the
  /// system resource itself is. Two hundred and fifteen entries are every combination of six levels
  /// of red, green and blue — a red-major, green-middle, blue-minor count down from 5 to 0 — packed
  /// index <c>36r + 6g + b</c>. Index 255 is black. The remaining forty entries, 215 to 254, are ten
  /// extra shades apiece of red, green, blue and grey, each shade one of the sixteen levels 0 to 15
  /// that is not a multiple of three, in descending order. A six-level component is scaled to eight
  /// bits by <c>level * 51</c> and a sixteen-level one by <c>level * 17</c> — both exact, since 255 is
  /// divisible by 5 and by 15 — which is the same halving a 16-bit QuickDraw colour to eight bits by
  /// its high byte would give, and not a rounding this decoder introduces.
  /// <para/>
  /// The greyscale ramp is the simpler of the two: two hundred and fifty-six levels running from 255
  /// at index 0 to 0 at index 255, one level a step. White at index zero and black at the last index
  /// is the Macintosh convention this library's QuickTime Animation decoder measured the same way for
  /// its own greyscale depths.
  /// <para/>
  /// Both were checked against ffmpeg's decode of the real streams that reach them — not just that a
  /// picture came out, but that every sample of it matched — which is what distinguishes a table
  /// generated correctly from one that merely looks plausible.
  /// </remarks>
  private static byte[] _StandardColourTable(bool grey) {
    var palette = new byte[256 * 3];

    if (grey) {
      for (var i = 0; i < 256; ++i) {
        var level = (byte)(255 - i);
        palette[i * 3] = level;
        palette[i * 3 + 1] = level;
        palette[i * 3 + 2] = level;
      }

      return palette;
    }

    // The ten shades a supplementary entry picks from: levels 1 to 14 that are not multiples of
    // three, highest first.
    ReadOnlySpan<byte> shades = [14, 13, 11, 10, 8, 7, 5, 4, 2, 1];

    for (var i = 0; i < 256; ++i) {
      byte r, g, b;

      if (i < 215) {
        r = (byte)((5 - i / 36) * 51);
        g = (byte)((5 - i / 6 % 6) * 51);
        b = (byte)((5 - i % 6) * 51);
      } else if (i == 255) {
        r = g = b = 0;
      } else {
        var shade = (byte)(shades[(i - 215) % 10] * 17);
        switch ((i - 215) / 10) {
          case 0: r = shade; g = 0; b = 0; break;
          case 1: r = 0; g = shade; b = 0; break;
          case 2: r = 0; g = 0; b = shade; break;
          default: r = g = b = shade; break;
        }
      }

      palette[i * 3] = r;
      palette[i * 3 + 1] = g;
      palette[i * 3 + 2] = b;
    }

    return palette;
  }
}
