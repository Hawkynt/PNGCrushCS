using System;
using System.IO;
using FileFormat.Codecs.Cinepak;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Cinepak (<c>cvid</c>): vector quantisation with two codebooks a strip, and inter-coded
/// strips that restate only what changed.
/// </summary>
/// <remarks>
/// A frame is cut into horizontal strips, each carrying its own pair of codebooks and its own list of
/// blocks. A codebook entry is four luminance samples and one chrominance pair — a 4x4 block's worth
/// of picture at 12 bits a pixel. A block is then coded either as one entry (V1), whose four samples
/// are each stretched over a 2x2 square, or as four entries (V4), one per quadrant. One byte a block
/// or four, and everything else is in the codebooks.
/// <para/>
/// The inter-frame coding is in two places at once, which is what makes the format small. A strip's
/// vector list may say a block is unchanged and code nothing for it at all; and a strip's codebook
/// chunk may restate a handful of entries and leave the other two hundred as the frame before left
/// them. So a decoder has to keep both the picture and the codebooks between frames, and a block that
/// says nothing is not a block of nothing.
/// <para/>
/// <b>Measured.</b> The quantisation is the encoder's; a decoder reading the same bitstream has only
/// the colour conversion to get right, and that is exact integer arithmetic rather than a transform
/// with an accuracy bound. Every frame of every stream this was measured on came out identical to
/// ffmpeg's decode of the same file, sample for sample — including the frames coded as three strips
/// of which two restate nothing, which is where a wrong codebook model would show first.
/// <para/>
/// <b>What it does not read refuses by name.</b> A strip identifier that is neither intra nor inter,
/// a chunk type the format does not define, a strip reaching outside the frame, a picture size that
/// changes part way through a stream, a vector list that stops before every block is accounted for,
/// and any chunk shorter than what it says it holds. There is no <c>catch</c> handing back a blank or
/// the previous frame: a frame of nothing but unchanged blocks is a perfectly ordinary Cinepak frame,
/// so returning one on failure would be indistinguishable from working.
/// </remarks>
public sealed class CinepakVideoDecoder : IVideoCodecDecoder<CinepakVideoDecoder> {
  /// <summary>Initializes a new instance of this type.</summary>
  public CinepakVideoDecoder() { }

  /// <summary>The four-character code Cinepak is named by, in the spellings containers use.</summary>
  /// <remarks>
  /// ffmpeg's AVI muxer writes <c>cvid</c> and QuickTime files carry the same four letters in their
  /// sample entry, so one code covers both; the upper-case spelling is what several older writers
  /// used and is the same codec.
  /// </remarks>
  private static readonly CodecTag[] _Tags = [CodecTag.FromCharacters("cvid"), CodecTag.FromCharacters("CVID")];

  /// <summary>The side of a coded block, in pixels.</summary>
  private const int _BLOCK = 4;

  private const int _FRAME_HEADER_LENGTH = 10;
  private const int _STRIP_HEADER_LENGTH = 12;
  private const int _CHUNK_HEADER_LENGTH = 4;

  /// <summary>Set in the frame's flags when the strips carry on from the codebooks already loaded.</summary>
  private const int _INHERITS_CODEBOOKS = 0x01;

  private const int _STRIP_INTRA = 0x1000;
  private const int _STRIP_INTER = 0x1100;

  private readonly CinepakCodebook _v1 = new();
  private readonly CinepakCodebook _v4 = new();

  /// <summary>The picture as RGB, kept between frames because most blocks of most frames are unchanged.</summary>
  private byte[]? _canvas;

  private int _width;
  private int _height;

  /// <summary>Gets the codec name.</summary>
  public static string CodecName => "Cinepak";

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
  /// Builds a decoder for one stream.
  /// </summary>
  /// <remarks>
  /// Nothing is read from the stream description, not even the dimensions. Every Cinepak frame states
  /// its own size in its own header, and a container's copy is a copy — so the frame is believed and
  /// the container is not consulted, which is also what lets the same decoder serve an AVI and a
  /// QuickTime sample entry without knowing which it is.
  /// </remarks>
  public static CinepakVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return new();
  }

  /// <summary>Decodes one packet, which for this codec is always exactly one whole frame.</summary>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    this._DecodeFrame(packet.Data.Span);

    var picture = new byte[this._canvas!.Length];
    Array.Copy(this._canvas, picture, picture.Length);

    frame = new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Rgb24,
      PixelData = picture,
    };

    return true;
  }

  // ============================================================================================
  // The frame
  // ============================================================================================

  private void _DecodeFrame(ReadOnlySpan<byte> data) {
    if (data.Length < _FRAME_HEADER_LENGTH)
      throw new InvalidDataException(
        $"A Cinepak frame is {data.Length} bytes, where its header alone is {_FRAME_HEADER_LENGTH}.");

    var flags = data[0];
    var stated = (data[1] << 16) | (data[2] << 8) | data[3];
    var width = _Be16(data, 4);
    var height = _Be16(data, 6);
    var strips = _Be16(data, 8);

    if (stated > data.Length)
      throw new InvalidDataException(
        $"A Cinepak frame header states {stated} bytes and the packet holds {data.Length}.");

    this._PrepareCanvas(width, height);

    // A frame that does not inherit is a frame whose strips each define what they use, so anything
    // left in the codebooks is not theirs. Keeping it would let a block reach a vector no chunk of
    // this frame ever wrote.
    if ((flags & _INHERITS_CODEBOOKS) == 0) {
      this._v1.Clear();
      this._v4.Clear();
    }

    var at = _FRAME_HEADER_LENGTH;
    var previousBottom = 0;

    for (var strip = 0; strip < strips; ++strip) {
      if (at + _STRIP_HEADER_LENGTH > data.Length)
        throw new InvalidDataException(
          $"A Cinepak frame states {strips} strips and ends {_STRIP_HEADER_LENGTH - (data.Length - at)} bytes into "
          + $"the header of strip {strip}.");

      var identifier = _Be16(data, at);
      var length = _Be16(data, at + 2);
      var (top, bottom) = _StripRows(data, at, strip, previousBottom);
      var left = _Be16(data, at + 6);
      var right = _Be16(data, at + 10);

      if (identifier is not (_STRIP_INTRA or _STRIP_INTER))
        throw new InvalidDataException(
          $"Cinepak strip {strip} is identified as 0x{identifier:X4}, which is neither an intra-coded strip "
          + $"(0x{_STRIP_INTRA:X4}) nor an inter-coded one (0x{_STRIP_INTER:X4}).");

      if (length < _STRIP_HEADER_LENGTH || at + length > data.Length)
        throw new InvalidDataException(
          $"Cinepak strip {strip} states {length} bytes and {data.Length - at} remain in the frame.");

      this._RefuseUnusableStrip(strip, left, top, right, bottom);
      this._DecodeStrip(data.Slice(at + _STRIP_HEADER_LENGTH, length - _STRIP_HEADER_LENGTH), strip, left, top, right, bottom);

      previousBottom = bottom;
      at += length;
    }
  }

  /// <summary>
  /// Where a strip's rows are, allowing for the way every strip after the first states its height.
  /// </summary>
  /// <remarks>
  /// The header's four coordinates are absolute in principle. In practice every encoder writes zero
  /// as a later strip's top and its height as its bottom, so the strip is placed under the one before
  /// it and the second number is a length rather than a position. ffmpeg's own files are like this
  /// throughout: a three-strip 64x48 frame states 0..16 three times over, and read literally that
  /// would draw all three strips on top of each other across the top third of the picture and leave
  /// the rest of it as the frame before.
  /// <para/>
  /// The condition is a top of zero on a strip that is not the first, because a genuine absolute top
  /// of zero can only belong to the first strip.
  /// </remarks>
  private static (int Top, int Bottom) _StripRows(ReadOnlySpan<byte> data, int at, int strip, int previousBottom) {
    var top = _Be16(data, at + 4);
    var bottom = _Be16(data, at + 8);

    return strip > 0 && top == 0 ? (previousBottom, previousBottom + bottom) : (top, bottom);
  }

  private void _RefuseUnusableStrip(int strip, int left, int top, int right, int bottom) {
    if (right <= left || bottom <= top || right > this._width || bottom > this._height)
      throw new InvalidDataException(
        $"Cinepak strip {strip} covers {left}..{right} by {top}..{bottom}, which is not inside the "
        + $"{this._width}x{this._height} picture it belongs to.");

    if ((right - left) % _BLOCK != 0 || (bottom - top) % _BLOCK != 0)
      throw new NotSupportedException(
        $"Cinepak strip {strip} is {right - left}x{bottom - top}, which is not a whole number of 4x4 blocks. "
        + "Cinepak codes nothing but whole blocks and states nowhere what a partial one covers.");
  }

  private void _PrepareCanvas(int width, int height) {
    if (width <= 0 || height <= 0)
      throw new InvalidDataException(
        $"A Cinepak frame states a picture of {width}x{height}, which has no pixels.");

    if (this._canvas == null) {
      this._width = width;
      this._height = height;
      this._canvas = new byte[width * height * 3];
      return;
    }

    if (width == this._width && height == this._height)
      return;

    // The held picture is the old size and the blocks this frame does not restate are meant to come
    // from it. Rescaling it, or reading the smaller into the larger, would invent the parts that were
    // never coded.
    throw new NotSupportedException(
      $"This Cinepak stream changes picture size from {this._width}x{this._height} to {width}x{height} part way "
      + "through, while the frame it predicts from is the old size. Decoding a stream whose size changes is not "
      + "implemented.");
  }

  // ============================================================================================
  // The strip
  // ============================================================================================

  private void _DecodeStrip(ReadOnlySpan<byte> data, int strip, int left, int top, int right, int bottom) {
    var at = 0;

    while (at + _CHUNK_HEADER_LENGTH <= data.Length) {
      var identifier = _Be16(data, at);
      var length = _Be16(data, at + 2);

      if (length < _CHUNK_HEADER_LENGTH || at + length > data.Length)
        throw new InvalidDataException(
          $"A Cinepak chunk of type 0x{identifier:X4} in strip {strip} states {length} bytes and "
          + $"{data.Length - at} remain in the strip.");

      var body = data.Slice(at + _CHUNK_HEADER_LENGTH, length - _CHUNK_HEADER_LENGTH);
      this._DecodeChunk(identifier, body, strip, left, top, right, bottom);
      at += length;
    }
  }

  private void _DecodeChunk(
    int identifier, ReadOnlySpan<byte> body, int strip, int left, int top, int right, int bottom) {
    switch (identifier) {
      // Codebooks stated in full, from entry zero. The low bit of the high nibble picks the codebook
      // and the next one up picks the depth, which is why the four numbers look arbitrary and are not.
      case 0x2000: this._v4.ReplaceFromStart(body, grey: false); return;
      case 0x2200: this._v1.ReplaceFromStart(body, grey: false); return;
      case 0x2400: this._v4.ReplaceFromStart(body, grey: true); return;
      case 0x2600: this._v1.ReplaceFromStart(body, grey: true); return;

      // The same four codebooks, updated selectively against a bitmap of which entries changed.
      case 0x2100: this._v4.Update(body, grey: false); return;
      case 0x2300: this._v1.Update(body, grey: false); return;
      case 0x2500: this._v4.Update(body, grey: true); return;
      case 0x2700: this._v1.Update(body, grey: true); return;

      case 0x3000: this._DecodeIntraVectors(body, strip, left, top, right, bottom); return;
      case 0x3100: this._DecodeInterVectors(body, strip, left, top, right, bottom); return;
      case 0x3200: this._DecodeV1OnlyVectors(body, strip, left, top, right, bottom); return;

      default:
        throw new NotSupportedException(
          $"A Cinepak chunk in strip {strip} is of type 0x{identifier:X4}, which is not one of the codebook or "
          + "vector chunks the format defines.");
    }
  }

  // ============================================================================================
  // The vector lists
  // ============================================================================================

  /// <summary>
  /// Every block coded, one flag bit each: set for V4, clear for V1 (chunk 0x3000).
  /// </summary>
  /// <remarks>
  /// The flags are interleaved with the vectors rather than tabled ahead of them. Four bytes cover
  /// the next thirty-two blocks, those blocks' vector references follow immediately, and the next
  /// four bytes of flags come after those. There is nowhere else they could go: a table would have to
  /// be found, and its length depends on the block count, which depends on the strip.
  /// </remarks>
  private void _DecodeIntraVectors(
    ReadOnlySpan<byte> data, int strip, int left, int top, int right, int bottom) {
    var reader = new CinepakVectorReader(data, strip);

    for (var y = top; y < bottom; y += _BLOCK)
    for (var x = left; x < right; x += _BLOCK)
      if (reader.NextFlag())
        this._PaintV4(x, y, reader.NextVector(4));
      else
        this._PaintV1(x, y, reader.NextVector(1)[0]);
  }

  /// <summary>
  /// A block is skipped, V1 or V4, coded as one flag bit or two (chunk 0x3100).
  /// </summary>
  /// <remarks>
  /// Zero skips the block; one is followed by a second bit that picks V1 or V4. A skipped block is
  /// the whole reason the previous frame has to still be on the canvas — nothing is coded for it, so
  /// what is already there is the answer.
  /// </remarks>
  private void _DecodeInterVectors(
    ReadOnlySpan<byte> data, int strip, int left, int top, int right, int bottom) {
    var reader = new CinepakVectorReader(data, strip);

    for (var y = top; y < bottom; y += _BLOCK)
    for (var x = left; x < right; x += _BLOCK) {
      if (!reader.NextFlag())
        continue;

      if (reader.NextFlag())
        this._PaintV4(x, y, reader.NextVector(4));
      else
        this._PaintV1(x, y, reader.NextVector(1)[0]);
    }
  }

  /// <summary>Every block coded from the V1 codebook, one byte each and no flags at all (chunk 0x3200).</summary>
  private void _DecodeV1OnlyVectors(
    ReadOnlySpan<byte> data, int strip, int left, int top, int right, int bottom) {
    var at = 0;

    for (var y = top; y < bottom; y += _BLOCK)
    for (var x = left; x < right; x += _BLOCK) {
      if (at >= data.Length)
        throw new InvalidDataException(
          $"A Cinepak V1 vector list in strip {strip} ends before the block at column {x}, row {y}.");

      this._PaintV1(x, y, data[at++]);
    }
  }

  // ============================================================================================
  // Painting a block
  // ============================================================================================

  /// <summary>
  /// Paints a block from one V1 vector, each of its four colours stretched over a 2x2 square.
  /// </summary>
  private void _PaintV1(int x, int y, byte vector) {
    var colours = this._v1[vector];
    var canvas = this._canvas!;

    for (var row = 0; row < _BLOCK; ++row) {
      var offset = ((y + row) * this._width + x) * 3;
      for (var column = 0; column < _BLOCK; ++column) {
        var sample = (row / 2) * 2 + column / 2;
        colours.Slice(sample * 3, 3).CopyTo(canvas.AsSpan(offset + column * 3, 3));
      }
    }
  }

  /// <summary>
  /// Paints a block from four V4 vectors, one per 2x2 quadrant.
  /// </summary>
  /// <remarks>
  /// Each quadrant takes its whole colour from its own vector — one of that vector's four samples per
  /// pixel — which is what gives a V4 block four times the luminance detail of a V1 one and the same
  /// chrominance detail. The vectors are listed in reading order: top left, top right, bottom left,
  /// bottom right.
  /// </remarks>
  private void _PaintV4(int x, int y, ReadOnlySpan<byte> vectors) {
    var canvas = this._canvas!;

    for (var row = 0; row < _BLOCK; ++row) {
      var offset = ((y + row) * this._width + x) * 3;
      for (var column = 0; column < _BLOCK; ++column) {
        var quadrant = (row / 2) * 2 + column / 2;
        var sample = (row % 2) * 2 + column % 2;
        this._v4[vectors[quadrant]].Slice(sample * 3, 3).CopyTo(canvas.AsSpan(offset + column * 3, 3));
      }
    }
  }

  private static int _Be16(ReadOnlySpan<byte> data, int at) => (data[at] << 8) | data[at + 1];

  /// <summary>
  /// Walks a vector chunk, whose flag bits and vector bytes come from the same stream in turn.
  /// </summary>
  /// <remarks>
  /// A struct rather than a class because one is made per vector chunk and it holds nothing but a
  /// position and a thirty-two-bit register.
  /// </remarks>
  private ref struct CinepakVectorReader {

    private readonly ReadOnlySpan<byte> _data;
    private readonly int _strip;
    private int _at;
    private uint _flags;
    private int _bitsLeft;

    internal CinepakVectorReader(ReadOnlySpan<byte> data, int strip) {
      this._data = data;
      this._strip = strip;
      this._at = 0;
      this._flags = 0;
      this._bitsLeft = 0;
    }

    /// <summary>The next flag bit, refilling the register from the stream when it runs dry.</summary>
    /// <remarks>
    /// The refill happens where the register empties and not at any block boundary, so a block whose
    /// two bits straddle a word takes the first from the old register and the second from four bytes
    /// read at that moment. Any other arrangement would need the reader to know how many bits the
    /// block after next will want.
    /// </remarks>
    internal bool NextFlag() {
      if (this._bitsLeft == 0) {
        if (this._at + 4 > this._data.Length)
          throw new InvalidDataException(
            $"A Cinepak vector list in strip {this._strip} ends with {this._data.Length - this._at} byte(s) where "
            + "the next four are a word of flags. Every block of a strip is accounted for by this chunk.");

        this._flags = (uint)((this._data[this._at] << 24) | (this._data[this._at + 1] << 16)
                             | (this._data[this._at + 2] << 8) | this._data[this._at + 3]);
        this._at += 4;
        this._bitsLeft = 32;
      }

      --this._bitsLeft;
      return ((this._flags >> this._bitsLeft) & 1) != 0;
    }

    /// <summary>The next one or four codebook references.</summary>
    internal ReadOnlySpan<byte> NextVector(int count) {
      if (this._at + count > this._data.Length)
        throw new InvalidDataException(
          $"A Cinepak vector list in strip {this._strip} ends {count - (this._data.Length - this._at)} byte(s) short "
          + $"of a block's {count} codebook reference(s).");

      var start = this._at;
      this._at += count;
      return this._data.Slice(start, count);
    }
  }
}
