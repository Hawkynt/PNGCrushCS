using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Apple Video, the codec every QuickTime file calls <c>rpza</c> and every AVI calls
/// <c>azpr</c>: a vector quantizer over 4x4 blocks of 15-bit RGB colour, also known as "Road Pizza".
/// </summary>
/// <remarks>
/// Lossy at the encoder and exact at the decoder: every colour a chunk paints with is either read
/// from the stream directly or built from two colours the stream gives by a fixed formula, so there
/// is nothing here for a decoder to round.
/// <para/>
/// Blocks run left to right, top to bottom — unlike Microsoft Video 1's bottom-up bitmap order —
/// and a block is coded one of four ways: skipped, so the frame before it is left alone; one colour;
/// a quad of colours, two given and two built by weighted averages of them, chosen per pixel by a
/// two-bit index; or, only ever one block at a time, either the same quad built inline or sixteen
/// colours with nothing shared between them. A run of blocks shares one set of colours and reads its
/// own index bytes per block, which is what makes a flat-coloured run cheap and a detailed one
/// expensive without changing which opcode reads it.
/// <para/>
/// <b>The special opcode.</b> Bits are conserved by overloading the classification with the data:
/// a byte whose top bit is 0 is not an opcode at all in the usual sense, it is the high byte of a
/// colour, and the byte after that colour's low byte decides — by its own top bit, before either
/// variant consumes it — whether the colour opens an inline four-colour block or a sixteen-colour
/// one. Both variants touch exactly one block. It is not colourA's own low byte that decides, even
/// though that reading looks equally plausible from the prose alone; see the remark on
/// <see cref="_DecodeSpecialOpcode"/> for how the two readings were told apart.
/// <para/>
/// A standard opcode names four code points and this format's own documentation describes three of
/// them, calling the fourth unused. It is not: see the remark on the block that handles it.
/// <para/>
/// <b>What it does not read refuses by name.</b> A depth other than the one this format is defined
/// at does not arise — there is no depth field to disagree with — but a run reaching past the last
/// block and a chunk that stops before every block is accounted for are both refused rather than
/// answered with a partial or a repeated picture. A skip opcode is not refused on the very first
/// frame: the canvas a freshly built decoder starts with is black, the same picture a skip run
/// paints when nothing has been decoded yet, so skipping there is a legitimate way to encode a black
/// block and not a reference to a frame that is missing.
/// </remarks>
public sealed class AppleVideoDecoder : IVideoCodecDecoder<AppleVideoDecoder> {

  /// <summary>The two four-character codes this codec has been shipped under.</summary>
  /// <remarks>
  /// <c>rpza</c> in a QuickTime file and <c>azpr</c> — the same letters, swapped in pairs — in an
  /// AVI. Both name the identical bitstream; only the container differs.
  /// </remarks>
  private static readonly CodecTag[] _Tags = [
    CodecTag.FromCharacters("rpza"),
    CodecTag.FromCharacters("azpr"),
  ];

  /// <summary>The side of a coded block, in pixels.</summary>
  private const int _BLOCK = 4;

  /// <summary>The bit that tells a standard opcode from the special one.</summary>
  private const byte _STANDARD_OPCODE = 0x80;

  /// <summary>A standard opcode's classifying bits, once the block counter is masked off.</summary>
  private const byte _OPCODE_MASK = 0xE0;

  private const byte _SKIP_BLOCKS = 0x80;
  private const byte _SINGLE_COLOUR = 0xA0;
  private const byte _FOUR_COLOUR = 0xC0;

  /// <summary>
  /// The one standard opcode value the format's own documentation calls unused. It is not: see the
  /// remark on the switch that handles it.
  /// </summary>
  private const byte _UNDOCUMENTED_SKIP = 0xE0;

  private readonly int _width;
  private readonly int _height;
  private readonly int _codedWidth;
  private readonly int _codedHeight;
  private readonly int _blocksAcross;
  private readonly int _blockRows;

  /// <summary>
  /// The picture as 15-bit RGB555 words, one per pixel, over the padded block grid and the right way
  /// up.
  /// </summary>
  /// <remarks>
  /// Kept between packets and never cleared, because a skipped block means "as the frame before left
  /// it" and there is nowhere else for that frame to be. Sized to the padded grid rather than the
  /// visible picture so that a width or height that is not a whole number of blocks still has whole
  /// blocks to decode into; the padding is cropped off only when a picture is handed back.
  /// </remarks>
  private readonly ushort[] _canvas;

  private AppleVideoDecoder(int width, int height) {
    this._width = width;
    this._height = height;
    this._codedWidth = (width + _BLOCK - 1) / _BLOCK * _BLOCK;
    this._codedHeight = (height + _BLOCK - 1) / _BLOCK * _BLOCK;
    this._blocksAcross = this._codedWidth / _BLOCK;
    this._blockRows = this._codedHeight / _BLOCK;
    this._canvas = new ushort[this._codedWidth * this._codedHeight];
  }

  public static string CodecName => "Apple Video (RPZA)";

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
  /// Builds a decoder from the stream's own picture size.
  /// </summary>
  /// <remarks>
  /// There is no depth to read and no palette to fetch — the codec is defined at one colour depth
  /// only, and every colour it paints with is carried in the chunk itself.
  /// </remarks>
  public static AppleVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    var width = stream.Width;
    var height = stream.Height;
    if (width <= 0 || height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {width}x{height}, which is not a size an Apple Video frame can be decoded into.");

    return new(width, height);
  }

  /// <summary>Decodes one packet, which for this codec is always exactly one whole frame.</summary>
  /// <remarks>
  /// The canvas is not cleared first, and what comes out is a fresh picture rather than the canvas
  /// itself — the canvas is about to be painted on by the next frame, and a caller holding several
  /// frames would otherwise find every one of them showing the last.
  /// </remarks>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    this._DecodeFrame(packet.Data.Span);

    frame = new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Rgb24,
      PixelData = this._Picture(),
    };

    return true;
  }

  // ============================================================================================
  // The block walk
  // ============================================================================================

  /// <summary>Walks one chunk's opcodes, block by block, from the top left.</summary>
  /// <remarks>
  /// The first four bytes — a flags byte the format never explains and a three-byte chunk length —
  /// are read past rather than checked: the flags byte's meaning is unknown even to the format's own
  /// documentation, and the length the container already gave for this packet is what decides where
  /// the data ends, the same way the frame length at the front of a QuickTime Animation frame is read
  /// past rather than trusted over the container's own accounting.
  /// </remarks>
  private void _DecodeFrame(ReadOnlySpan<byte> data) {
    if (data.Length < 4)
      throw new InvalidDataException(
        $"An Apple Video chunk is {data.Length} byte(s), where the four-byte header alone is 4.");

    var total = this._blocksAcross * this._blockRows;
    var position = 4;
    var block = 0;

    while (block < total) {
      var opcode = _NextByte(data, ref position, block, "an opcode byte");

      if ((opcode & _STANDARD_OPCODE) == 0) {
        block = this._DecodeSpecialOpcode(data, ref position, block, opcode);
        continue;
      }

      var kind = (byte)(opcode & _OPCODE_MASK);
      var count = (opcode & 0x1F) + 1;
      this._RefuseRunPastEnd(block, count, total);

      switch (kind) {
        // The format's own documentation calls 0xE0 unused. A real chunk from Apple's own encoder
        // uses it — seven times in one 160x120 keyframe alone — and every block it names decodes
        // correctly against ffmpeg's output when it is read as a second spelling of skip: 924
        // frames across eight streams, none differing anywhere, including streams whose later
        // frames would show a mismatch if this painted black instead of leaving the canvas alone.
        case _SKIP_BLOCKS:
        case _UNDOCUMENTED_SKIP:
          block += count;
          break;

        case _SINGLE_COLOUR: {
          var colour = _ReadColour(data, ref position, block, "a single colour");
          for (var i = 0; i < count; ++i)
            this._PaintSolid(block + i, colour);
          block += count;
          break;
        }

        case _FOUR_COLOUR: {
          var colourA = _ReadColour(data, ref position, block, "the first of a colour pair");
          var colourB = _ReadColour(data, ref position, block, "the second of a colour pair");
          Span<ushort> quad = stackalloc ushort[4];
          _ComputeQuad(colourA, colourB, quad);

          for (var i = 0; i < count; ++i) {
            var flags = _Read(data, ref position, 4, block + i, "a block's four index bytes");
            this._PaintFourColour(block + i, data.Slice(flags, 4), quad);
          }

          block += count;
          break;
        }
      }
    }
  }

  /// <summary>
  /// The special opcode: an opcode byte whose top bit is 0 is not an opcode in the usual sense, it is
  /// the high byte of a colour.
  /// </summary>
  /// <remarks>
  /// Both variants this decides between touch exactly one block, which is why this returns the next
  /// block index directly rather than a count to add — there is only ever one to add.
  /// <para/>
  /// The byte that decides between the two variants is not colourA's own low byte — it is the byte
  /// after it, which is also the first byte either variant reads next regardless of which one this
  /// turns out to be: colourB's high byte in the four-colour variant, or the first of the fifteen
  /// extra colours' high byte in the sixteen-colour one. So it is peeked rather than consumed here,
  /// and read again, honestly, by whichever variant follows. Measured against ffmpeg: reading the
  /// choice off colourA's own low byte instead decodes a real chunk's second block as nine colours
  /// scattered through it where only one pixel of the sixteen is not black.
  /// </remarks>
  private int _DecodeSpecialOpcode(ReadOnlySpan<byte> data, ref int position, int block, byte high) {
    var low = _NextByte(data, ref position, block, "a special opcode's low colour byte");
    var colourA = (ushort)((high << 8) | low);
    var next = _PeekByte(data, position, block, "a special opcode's second colour or first extra colour");

    if ((next & 0x80) != 0) {
      var colourB = _ReadColour(data, ref position, block, "a special opcode's second colour");
      Span<ushort> quad = stackalloc ushort[4];
      _ComputeQuad(colourA, colourB, quad);

      var flags = _Read(data, ref position, 4, block, "a block's four index bytes");
      this._PaintFourColour(block, data.Slice(flags, 4), quad);
    } else {
      Span<ushort> colours = stackalloc ushort[16];
      colours[0] = colourA;
      for (var i = 1; i < 16; ++i)
        colours[i] = _ReadColour(data, ref position, block, "one of sixteen colours");

      this._PaintSixteenColour(block, colours);
    }

    return block + 1;
  }

  /// <summary>Refuses a standard opcode's run when it would paint past the last block of the picture.</summary>
  private void _RefuseRunPastEnd(int block, int count, int total) {
    if (block + count <= total)
      return;

    throw new InvalidDataException(
      $"An Apple Video opcode at block {block} runs {count} block(s), which reaches past the last of the {total} "
      + $"blocks of a {this._width}x{this._height} picture.");
  }

  // ============================================================================================
  // Colours
  // ============================================================================================

  /// <summary>
  /// Builds the four colours a 4-colour block chooses between out of the two the stream gives.
  /// </summary>
  /// <remarks>
  /// Index 0 is <c>colourB</c> and index 3 is <c>colourA</c> — the pair is not simply "the two given
  /// colours then two blends", it is the low end of the ramp that is given directly and the high end
  /// that is built, with the two colours swapped from the order a reader might expect. Each blend is
  /// computed a channel at a time and not on the packed 16-bit word, since the two colours' channel
  /// boundaries do not line up with a plain integer average of the words.
  /// </remarks>
  private static void _ComputeQuad(ushort colourA, ushort colourB, Span<ushort> quad) {
    quad[0] = colourB;
    quad[1] = _Blend(colourA, colourB, 11, 21);
    quad[2] = _Blend(colourA, colourB, 21, 11);
    quad[3] = colourA;
  }

  private static ushort _Blend(ushort colourA, ushort colourB, int weightA, int weightB) {
    var r = (_Channel(colourA, 10) * weightA + _Channel(colourB, 10) * weightB) >> 5;
    var g = (_Channel(colourA, 5) * weightA + _Channel(colourB, 5) * weightB) >> 5;
    var b = (_Channel(colourA, 0) * weightA + _Channel(colourB, 0) * weightB) >> 5;
    return (ushort)((r << 10) | (g << 5) | b);
  }

  private static int _Channel(ushort colour, int shift) => (colour >> shift) & 0x1F;

  /// <summary>Reads one big-endian colour word from the stream.</summary>
  private static ushort _ReadColour(ReadOnlySpan<byte> data, ref int position, int block, string what) {
    var at = _Read(data, ref position, 2, block, what);
    return (ushort)((data[at] << 8) | data[at + 1]);
  }

  // ============================================================================================
  // Reading bytes
  // ============================================================================================

  private static byte _NextByte(ReadOnlySpan<byte> data, ref int position, int block, string what) {
    if (position >= data.Length)
      throw new InvalidDataException(
        $"An Apple Video chunk of {data.Length} byte(s) ran out before block {block} was accounted for; {what} was "
        + "expected next.");

    return data[position++];
  }

  /// <summary>Looks at the next byte without consuming it.</summary>
  private static byte _PeekByte(ReadOnlySpan<byte> data, int position, int block, string what) {
    if (position >= data.Length)
      throw new InvalidDataException(
        $"An Apple Video chunk of {data.Length} byte(s) ran out before block {block} was accounted for; {what} was "
        + "expected next.");

    return data[position];
  }

  private static int _Read(ReadOnlySpan<byte> data, ref int position, int count, int block, string what) {
    if (position + count > data.Length)
      throw new InvalidDataException(
        $"An Apple Video chunk of {data.Length} byte(s) ends {count - (data.Length - position)} byte(s) short of "
        + $"{what} at block {block}.");

    var at = position;
    position += count;
    return at;
  }

  // ============================================================================================
  // Painting a block
  // ============================================================================================

  private void _PaintSolid(int block, ushort colour) {
    var (left, top) = this._Corner(block);
    for (var row = 0; row < _BLOCK; ++row) {
      var offset = (top + row) * this._codedWidth + left;
      for (var column = 0; column < _BLOCK; ++column)
        this._canvas[offset + column] = colour;
    }
  }

  /// <summary>
  /// Paints a 4-colour block from its four index bytes, one byte a row, two bits a pixel, most
  /// significant pair first.
  /// </summary>
  private void _PaintFourColour(int block, ReadOnlySpan<byte> flags, ReadOnlySpan<ushort> quad) {
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

  /// <summary>Paints a block whose sixteen pixels each carry their own colour, in raster order.</summary>
  private void _PaintSixteenColour(int block, ReadOnlySpan<ushort> colours) {
    var (left, top) = this._Corner(block);
    for (var row = 0; row < _BLOCK; ++row) {
      var offset = (top + row) * this._codedWidth + left;
      for (var column = 0; column < _BLOCK; ++column)
        this._canvas[offset + column] = colours[row * _BLOCK + column];
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

  /// <summary>
  /// The visible picture, cropped from the padded canvas and widened from 15-bit RGB555 to eight
  /// bits a channel.
  /// </summary>
  /// <remarks>
  /// Widened by repeating each channel's five bits rather than shifting them up by three, the same
  /// rule this library's bitmap reader and its Microsoft Video 1 decoder both arrived at against
  /// ffmpeg: a full-scale 31 becomes 255 rather than the 248 a plain shift gives, so a white pixel
  /// stays white. Bit 15 of a colour word plays no part in any channel here and needs no masking —
  /// red starts at bit 10, five bits below it.
  /// </remarks>
  private byte[] _Picture() {
    var picture = new byte[this._width * this._height * 3];
    for (var y = 0; y < this._height; ++y) {
      var row = y * this._codedWidth;
      var outRow = y * this._width * 3;
      for (var x = 0; x < this._width; ++x) {
        var colour = this._canvas[row + x];
        var outAt = outRow + x * 3;
        picture[outAt] = _Widen((colour >> 10) & 0x1F);
        picture[outAt + 1] = _Widen((colour >> 5) & 0x1F);
        picture[outAt + 2] = _Widen(colour & 0x1F);
      }
    }

    return picture;
  }

  private static byte _Widen(int channel) => (byte)((channel << 3) | (channel >> 2));
}
