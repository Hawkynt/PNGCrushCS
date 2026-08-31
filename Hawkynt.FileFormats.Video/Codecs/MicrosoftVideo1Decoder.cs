using System;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Microsoft Video 1 (<c>CRAM</c>, <c>MSVC</c>, <c>WHAM</c>): vector quantisation over 4x4
/// blocks, palettised at eight bits a pixel and 5-5-5 at sixteen.
/// </summary>
/// <remarks>
/// A block is coded as one colour, as two colours chosen per pixel by a sixteen-bit mask, or as eight
/// colours — the block split into four 2x2 quads with two colours each, chosen by the same mask. A
/// fourth code skips a run of blocks, which is the whole of the inter-frame coding: a skipped block
/// is one that did not change, so the frame before has to still be there to be left alone.
/// <para/>
/// Both depths are one decoder because they are one algorithm. The block order, the mask layout and
/// the skip runs are identical; what differs is how wide a colour is and, at sixteen bits, that the
/// choice between two colours and eight is made by the top bit of the first colour rather than by the
/// second flag byte. Splitting them would have duplicated the traversal to vary the width of a
/// literal.
/// <para/>
/// Blocks run bottom to top, as a Windows bitmap's rows do — the first block coded is the bottom
/// left one — and within a row left to right.
/// <para/>
/// <b>Measured.</b> The quantisation is the encoder's; a decoder reading the same bitstream has
/// nothing to round. Every frame of every stream this was measured on came out identical to ffmpeg's
/// decode of the same file, sample for sample, on key frames and on frames made mostly of skip runs
/// alike.
/// <para/>
/// <b>What it does not read refuses by name.</b> A depth other than eight or sixteen, a picture whose
/// sides are not whole blocks, an eight-bit stream with no palette, a skip run reaching past the last
/// block, a frame that stops before every block is accounted for, and an opcode wanting more bytes
/// than the packet holds. None of them is caught and turned into a blank or a repeated frame: a
/// repeated frame is exactly what a still passage of this codec looks like, so returning one on
/// failure would be indistinguishable from working.
/// </remarks>
public sealed class MicrosoftVideo1Decoder : IVideoCodecDecoder<MicrosoftVideo1Decoder> {

  /// <summary>The three four-character codes this codec has been shipped under.</summary>
  /// <remarks>
  /// One codec and three names: Microsoft's own <c>MSVC</c>, the <c>CRAM</c> of the Video 1
  /// compressor, and the <c>WHAM</c> of Media Vision's card, which is where the algorithm came from.
  /// The bitstream behind all three is the same.
  /// </remarks>
  private static readonly CodecTag[] _Tags = [
    CodecTag.FromCharacters("CRAM"),
    CodecTag.FromCharacters("MSVC"),
    CodecTag.FromCharacters("WHAM"),
  ];

  /// <summary>The side of a coded block, in pixels.</summary>
  private const int _BLOCK = 4;

  /// <summary>The smallest second byte that introduces a run of skipped blocks.</summary>
  private const byte _FIRST_SKIP = 0x84;

  /// <summary>One past the largest second byte that introduces a run of skipped blocks.</summary>
  private const byte _AFTER_SKIP = 0x88;

  /// <summary>One past the largest second byte that introduces a block of literal colours.</summary>
  private const byte _AFTER_LITERAL = 0x80;

  /// <summary>The smallest second byte that makes an eight-bit block an eight-colour one.</summary>
  private const byte _FIRST_EIGHT_COLOUR = 0x90;

  private readonly int _width;
  private readonly int _height;
  private readonly int _bitsPerPixel;
  private readonly int _blocksAcross;
  private readonly int _blockRows;
  private readonly byte[]? _palette;
  private readonly int _paletteCount;

  /// <summary>
  /// The picture as coded values — a palette index at eight bits, a packed 5-5-5 colour at sixteen —
  /// one per pixel, the right way up.
  /// </summary>
  /// <remarks>
  /// Kept between packets and never cleared, because a skipped block means "as the frame before left
  /// it" and there is nowhere else for that frame to be. Held the right way up rather than in coded
  /// order so that only the block arithmetic has to know the rows run upwards, and the pixels come
  /// out ready to hand over.
  /// </remarks>
  private readonly ushort[] _canvas;

  private MicrosoftVideo1Decoder(int width, int height, int bitsPerPixel, byte[]? palette, int paletteCount) {
    this._width = width;
    this._height = height;
    this._bitsPerPixel = bitsPerPixel;
    this._palette = palette;
    this._paletteCount = paletteCount;
    this._blocksAcross = width / _BLOCK;
    this._blockRows = height / _BLOCK;
    this._canvas = new ushort[width * height];
  }

  /// <summary>Gets the codec name.</summary>
  public static string CodecName => "Microsoft Video 1";

  /// <summary>Determines whether the specified media stream is supported.</summary>
  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      return false;

    foreach (var tag in _Tags)
      if (stream.Codec.EqualsIgnoringCase(tag))
        return true;

    return false;
  }

  /// <summary>
  /// Builds a decoder from the stream's <c>BITMAPINFOHEADER</c>.
  /// </summary>
  /// <remarks>
  /// The depth is the one thing the packets do not say. Eight bits means the values are palette
  /// indices and sixteen means they are colours, and the two bitstreams are not distinguishable from
  /// each other by inspection — the same bytes are a valid frame under both readings and produce
  /// different pictures. So the header decides, and a stream that states any other depth is refused
  /// rather than read as whichever looks more likely.
  /// </remarks>
  public static MicrosoftVideo1Decoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    var format = stream.CodecPrivateData;
    if (format.Length < BitmapInfoHeader.StructSize)
      throw new InvalidOperationException(
        $"Microsoft Video 1 stream {stream.Index} carries {format.Length} bytes of stream format where a "
        + $"BITMAPINFOHEADER is {BitmapInfoHeader.StructSize}.");

    var info = BitmapInfoHeader.ReadFrom(format.Span);
    var bitsPerPixel = (int)info.BitsPerPixel;
    if (bitsPerPixel is not (8 or 16))
      throw new NotSupportedException(
        $"Video stream {stream.Index} states {bitsPerPixel} bits per pixel. Microsoft Video 1 is defined at eight "
        + "bits a pixel, where the values are palette indices, and at sixteen, where they are 5-5-5 colours. "
        + "Nothing else is read.");

    var width = info.Width;
    var height = Math.Abs(info.Height);
    if (width <= 0 || height <= 0)
      throw new InvalidOperationException(
        $"Microsoft Video 1 stream {stream.Index} states a picture of {width}x{height}, which has no pixels.");

    // The codec has no way of saying what to do with a part of a block, and the reference encoder
    // will not write one: it refuses a size that is not a whole number of blocks rather than padding.
    // Reading such a file would mean choosing which edge the leftover pixels fall off, and any choice
    // would be this decoder's invention rather than the file's meaning.
    if (width % _BLOCK != 0 || height % _BLOCK != 0)
      throw new NotSupportedException(
        $"Video stream {stream.Index} is {width}x{height}, which is not a whole number of 4x4 blocks. Microsoft "
        + "Video 1 codes nothing but whole blocks and states nowhere what a partial one covers.");

    byte[]? palette = null;
    var paletteCount = 0;
    if (bitsPerPixel == 8)
      (palette, paletteCount) = _ReadPalette(stream, info);

    return new(width, height, bitsPerPixel, palette, paletteCount);
  }

  /// <summary>Decodes one packet, which for this codec is always exactly one whole frame.</summary>
  /// <remarks>
  /// The canvas is not cleared first, and what comes out is a fresh picture rather than the canvas
  /// itself — the canvas is about to be painted on by the next frame, and a caller holding several
  /// frames would otherwise find every one of them showing the last.
  /// </remarks>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    this._DecodeFrame(packet.Data.Span);

    frame = this._bitsPerPixel == 8
      ? new() {
        Width = this._width,
        Height = this._height,
        Format = PixelFormat.Indexed8,
        PixelData = this._ToIndices(),
        Palette = this._palette,
        PaletteCount = this._paletteCount,
      }
      : new() {
        Width = this._width,
        Height = this._height,
        Format = PixelFormat.Rgb24,
        PixelData = this._ToRgb24(),
      };

    return true;
  }

  // ============================================================================================
  // The block walk
  // ============================================================================================

  /// <summary>Walks one frame's opcodes, block by block, from the bottom left.</summary>
  private void _DecodeFrame(ReadOnlySpan<byte> data) {
    var total = this._blocksAcross * this._blockRows;
    var at = 0;
    var block = 0;

    while (block < total) {
      var flags = _Read(data, ref at, 2, block, "a block's two flag bytes");
      var low = data[flags];
      var high = data[flags + 1];

      if (high is >= _FIRST_SKIP and < _AFTER_SKIP) {
        var skipped = (high - _FIRST_SKIP) * 256 + low;
        _RefuseEmptySkipRun(skipped, block);

        if (block + skipped > total)
          throw new InvalidDataException(
            $"A Microsoft Video 1 skip run of {skipped} block(s) at block {block} reaches past the last of the "
            + $"{total} blocks of a {this._width}x{this._height} picture.");

        block += skipped;
        continue;
      }

      if (this._bitsPerPixel == 8)
        this._DecodeIndexedBlock(data, ref at, block, low, high);
      else
        this._DecodeColourBlock(data, ref at, block, low, high);

      ++block;
    }
  }

  /// <summary>
  /// Refuses a skip run that skips nothing, because nobody agrees what one means.
  /// </summary>
  /// <remarks>
  /// Read as the format describes it, a run of zero blocks is a two-byte no-op and decoding carries
  /// on with the next block. ffmpeg does something else entirely: it abandons the rest of the frame,
  /// so every block after the run comes out as the frame before left it. Both readings produce a
  /// picture and neither produces an error, which is exactly the case where picking one silently is
  /// worst — the two differ across the whole rest of the frame, and nothing in the file says which
  /// its writer meant.
  /// <para/>
  /// The disagreement is specifically about a count of zero and not about the two-byte form of the
  /// count: runs of 1, 256 and 512 blocks all decode identically here and in ffmpeg, including the
  /// ones whose first byte is zero. And a run of zero blocks is a construct no encoder has a reason
  /// to write, since it costs two bytes and does nothing.
  /// </remarks>
  private static void _RefuseEmptySkipRun(int skipped, int block) {
    if (skipped != 0)
      return;

    throw new NotSupportedException(
      $"A Microsoft Video 1 skip run at block {block} skips no blocks at all. Read as the format describes it that "
      + "is a no-op and the next block follows; ffmpeg instead abandons the rest of the frame there, leaving every "
      + "later block as the previous frame left it. The two readings disagree about the whole rest of the picture "
      + "and the file says nothing about which was meant, so neither is chosen.");
  }

  /// <summary>One block of an eight-bit stream, whose second flag byte says which of the three codings it is.</summary>
  private void _DecodeIndexedBlock(ReadOnlySpan<byte> data, ref int at, int block, byte low, byte high) {
    var mask = low | (high << 8);

    if (high < _AFTER_LITERAL) {
      var colours = _Read(data, ref at, 2, block, "a two-colour block's colours");
      this._PaintTwoColour(block, mask, data[colours], data[colours + 1]);
      return;
    }

    if (high >= _FIRST_EIGHT_COLOUR) {
      var quads = _Read(data, ref at, 8, block, "an eight-colour block's colours");
      Span<ushort> colours = stackalloc ushort[8];
      for (var i = 0; i < 8; ++i)
        colours[i] = data[quads + i];

      this._PaintEightColour(block, mask, colours);
      return;
    }

    // The gaps either side of the skip codes, which carry no colours of their own: the whole block is
    // the first flag byte.
    this._PaintSolid(block, low);
  }

  /// <summary>
  /// One block of a sixteen-bit stream, where the choice between two colours and eight is in the
  /// first colour rather than in the flags.
  /// </summary>
  /// <remarks>
  /// Bit 15 of a 5-5-5 colour is spare, and this is what the format spends it on. It is only ever
  /// looked at on the first colour of a block whose flag byte allows literals, and it is not masked
  /// off afterwards because none of the three channels reaches it — a colour is read through its
  /// masks, so the marker cannot leak into a sample.
  /// </remarks>
  private void _DecodeColourBlock(ReadOnlySpan<byte> data, ref int at, int block, byte low, byte high) {
    if (high >= _AFTER_LITERAL) {
      this._PaintSolid(block, (ushort)(low | (high << 8)));
      return;
    }

    var mask = low | (high << 8);
    var first = _Read(data, ref at, 4, block, "a two-colour block's colours");
    var eightColour = (data[first + 1] & 0x80) != 0;

    if (!eightColour) {
      this._PaintTwoColour(
        block, mask,
        (ushort)(data[first] | (data[first + 1] << 8)),
        (ushort)(data[first + 2] | (data[first + 3] << 8)));
      return;
    }

    // The first quad's two colours have already been read; the other three follow.
    var rest = _Read(data, ref at, 12, block, "an eight-colour block's remaining colours");
    Span<ushort> colours = stackalloc ushort[8];
    colours[0] = (ushort)(data[first] | (data[first + 1] << 8));
    colours[1] = (ushort)(data[first + 2] | (data[first + 3] << 8));
    for (var i = 0; i < 6; ++i)
      colours[i + 2] = (ushort)(data[rest + i * 2] | (data[rest + i * 2 + 1] << 8));

    this._PaintEightColour(block, mask, colours);
  }

  /// <summary>Takes the next <paramref name="count"/> bytes of the packet and answers where they start.</summary>
  private static int _Read(ReadOnlySpan<byte> data, ref int at, int count, int block, string what) {
    if (at + count > data.Length)
      throw new InvalidDataException(
        $"A Microsoft Video 1 frame ends after {data.Length} bytes, {count - (data.Length - at)} short of {what} at "
        + $"block {block}.");

    var start = at;
    at += count;
    return start;
  }

  // ============================================================================================
  // Painting a block
  // ============================================================================================

  /// <summary>
  /// Which of a block's sixteen mask bits belongs to a pixel.
  /// </summary>
  /// <remarks>
  /// The mask is laid out bottom row first, four bits at a time, because the picture is: the low
  /// nibble of the first flag byte is the block's bottom row and the high nibble of the second is its
  /// top. Counting rows downwards from the top, that makes the row's nibble <c>3 - row</c>.
  /// </remarks>
  private static bool _IsFirstColour(int mask, int row, int column) => ((mask >> ((3 - row) * _BLOCK + column)) & 1) != 0;

  private void _PaintSolid(int block, ushort colour) {
    var (left, top) = this._Corner(block);
    for (var row = 0; row < _BLOCK; ++row) {
      var offset = (top + row) * this._width + left;
      for (var column = 0; column < _BLOCK; ++column)
        this._canvas[offset + column] = colour;
    }
  }

  private void _PaintTwoColour(int block, int mask, ushort first, ushort second) {
    var (left, top) = this._Corner(block);
    for (var row = 0; row < _BLOCK; ++row) {
      var offset = (top + row) * this._width + left;
      for (var column = 0; column < _BLOCK; ++column)
        this._canvas[offset + column] = _IsFirstColour(mask, row, column) ? first : second;
    }
  }

  /// <summary>
  /// Paints the four 2x2 quads, each choosing between its own pair of colours.
  /// </summary>
  /// <remarks>
  /// The quads are numbered from the bottom left the way the blocks themselves are — quad 1 is bottom
  /// left, 2 bottom right, 3 top left, 4 top right — and their colours arrive in that order. Counting
  /// rows downwards, the top half is quads 3 and 4.
  /// </remarks>
  private void _PaintEightColour(int block, int mask, ReadOnlySpan<ushort> colours) {
    var (left, top) = this._Corner(block);
    for (var row = 0; row < _BLOCK; ++row) {
      var offset = (top + row) * this._width + left;
      for (var column = 0; column < _BLOCK; ++column) {
        var quad = (row < 2 ? 2 : 0) + (column < 2 ? 0 : 1);
        this._canvas[offset + column] = colours[quad * 2 + (_IsFirstColour(mask, row, column) ? 0 : 1)];
      }
    }
  }

  /// <summary>
  /// Where a block's top-left pixel is, given that blocks are coded from the bottom of the picture.
  /// </summary>
  private (int Left, int Top) _Corner(int block) {
    var blockRow = block / this._blocksAcross;
    var blockColumn = block % this._blocksAcross;

    return (blockColumn * _BLOCK, this._height - (blockRow + 1) * _BLOCK);
  }

  // ============================================================================================
  // Handing the picture over
  // ============================================================================================

  private byte[] _ToIndices() {
    var picture = new byte[this._canvas.Length];
    for (var i = 0; i < picture.Length; ++i)
      picture[i] = (byte)this._canvas[i];

    return picture;
  }

  /// <summary>
  /// Widens the packed 5-5-5 canvas to eight bits a channel.
  /// </summary>
  /// <remarks>
  /// By repeating each channel's five bits rather than shifting them up by three, which is the rule a
  /// sweep of all thirty-two five-bit values through ffmpeg settled for this library's bitmap reader
  /// and the same rule the same tool applies here. It is also the only one of the two that reaches
  /// the end of the range: a full-scale 31 becomes 255 where a plain shift gives 248, so a white
  /// pixel stays white.
  /// <para/>
  /// The layout is R in bits 14 to 10, G in 9 to 5 and B in 4 to 0 — the same 5-5-5 a
  /// <c>BI_RGB</c> bitmap of sixteen bits holds, and what ffprobe calls this codec's pixel format,
  /// <c>rgb555le</c>. The description of the format on multimedia.cx names the channels the other way
  /// round; decoding a frame each way and comparing both with ffmpeg's own settles it, since red and
  /// blue read the wrong way round put 1733 of 3072 pixels of the first frame wrong and this puts
  /// none.
  /// <para/>
  /// Bit 15 is not a channel — it is the marker that told the block walk this was an eight-colour
  /// block — and reading through the masks is what keeps it out of the samples.
  /// </remarks>
  private byte[] _ToRgb24() {
    var picture = new byte[this._canvas.Length * 3];
    for (var i = 0; i < this._canvas.Length; ++i) {
      var colour = this._canvas[i];
      picture[i * 3] = _Widen((colour >> 10) & 0x1F);
      picture[i * 3 + 1] = _Widen((colour >> 5) & 0x1F);
      picture[i * 3 + 2] = _Widen(colour & 0x1F);
    }

    return picture;
  }

  private static byte _Widen(int channel) => (byte)((channel << 3) | (channel >> 2));

  /// <summary>
  /// Lifts the palette out of the stream format, where it sits directly behind the header.
  /// </summary>
  /// <remarks>
  /// The entries are <c>RGBQUAD</c>s — blue, green, red and a byte that is not an alpha — where a
  /// <see cref="RawImage"/> wants red, green and blue, so the outer two swap. Only the eight-bit
  /// variant has one: a sixteen-bit frame carries its colours in the blocks.
  /// </remarks>
  private static (byte[] Palette, int Count) _ReadPalette(MediaStreamInfo stream, BitmapInfoHeader info) {
    var headerSize = info.HeaderSize >= BitmapInfoHeader.StructSize ? info.HeaderSize : BitmapInfoHeader.StructSize;
    var format = stream.CodecPrivateData.Span;
    if (headerSize >= format.Length)
      throw new InvalidOperationException(
        $"Microsoft Video 1 stream {stream.Index} is eight bits a pixel and carries no palette behind its "
        + $"{headerSize}-byte stream format header. Those pixels are palette indices, so there are no colours to "
        + "decode them to.");

    var entries = info.ColorsUsed > 0 ? info.ColorsUsed : 256;
    var available = (format.Length - headerSize) / 4;
    if (available < entries)
      throw new InvalidDataException(
        $"Microsoft Video 1 stream {stream.Index} states {entries} palette entries and carries {available}.");

    var palette = new byte[entries * 3];
    for (var entry = 0; entry < entries; ++entry) {
      var at = headerSize + entry * 4;
      palette[entry * 3] = format[at + 2];
      palette[entry * 3 + 1] = format[at + 1];
      palette[entry * 3 + 2] = format[at];
    }

    return (palette, entries);
  }
}
