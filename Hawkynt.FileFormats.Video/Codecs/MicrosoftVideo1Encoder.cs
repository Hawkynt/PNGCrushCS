using System;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes Microsoft Video 1 (<c>MSVC</c>): 4x4 blocks as one colour, as two colours picked per pixel
/// by a sixteen-bit mask, or as four 2x2 quads of two colours each, with runs of unchanged blocks
/// skipped — palettised at eight bits a pixel and 5-5-5 at sixteen.
/// </summary>
/// <remarks>
/// The mode decision is FFmpeg's <c>libavcodec/msvideo1enc.c</c>, copyright (c) 2009 Konstantin
/// Shishkov, distributed there under LGPL-2.1-or-later. This adaptation is distributed with PNGCrushCS
/// under LGPL-3.0-or-later. What is not taken from there is the vector quantiser: FFmpeg reaches for
/// its ELBG, whose result depends on a pseudo-random generator, and this uses a deterministic
/// two-means seeded from the block's two most distant colours instead, so that the same picture always
/// produces the same bytes.
/// <para/>
/// <b>The decision.</b> Every block is priced the same way FFmpeg prices it — the squared error the
/// coding would leave, divided by a fixed quality of <see cref="_QUALITY"/>, plus the number of bytes
/// the coding costs — and the cheapest wins. That is what makes a flat block one solid colour and a
/// detailed one eight, and it is what makes a block that already matches the frame before cost nothing
/// at all and become part of a skip run.
/// <para/>
/// <b>Error is measured in five-bit units at both depths</b>, on the colours as the decoder will
/// reconstruct them, so one quality constant serves both. At sixteen bits that is exactly what the
/// stream holds; at eight the palette's entries are reduced to five bits for the comparison only — the
/// block still names real palette indices, so nothing is lost by it but a little sharpness in the
/// choice between codings.
/// <para/>
/// <b>Skips are measured against the reconstruction</b>, not against the picture that was handed in,
/// because the reconstruction is what the decoder is holding. A block left alone leaves whatever the
/// last frame's own quantisation put there, and comparing against the source would let that drift
/// accumulate unnoticed.
/// <para/>
/// <b>Two corners of the bitstream shape the output.</b> An all-ones top row of the mask would put the
/// flag word above 0x8000, where a sixteen-bit decoder reads a solid colour instead — so the colours of
/// the quad holding the block's top-right pixel are swapped and its mask bits inverted, which says the
/// same thing with bit 15 clear. At eight bits the constraint runs the other way for eight-colour
/// blocks, whose flag word has to reach 0x9000, and one further quad may be flipped to get there. And a
/// solid sixteen-bit block whose red is 1 would spell 0x84xx, which is the code for a skip run, so that
/// red is written as 0 — a difference of one thirty-second of the channel, and the only alternative is
/// a block that means something else entirely.
/// <para/>
/// <b>Lossy, and honest about it.</b> Two colours per block is the format; a picture that is flat, or
/// that has at most two colours in every block, comes back exactly, and anything else does not. What
/// does survive exactly at sixteen bits is any colour already on the 5-5-5 grid, since the eight-bit
/// samples are quantised by rounding and widened back by repeating the five bits.
/// </remarks>
public sealed class MicrosoftVideo1Encoder : IVideoCodecEncoder<MicrosoftVideo1Encoder> {

  /// <summary>The four-character code containers name this codec with.</summary>
  /// <remarks>
  /// Of the three codes the codec has shipped under this is the one ffmpeg writes, and
  /// <see cref="MicrosoftVideo1Decoder"/> takes all three.
  /// </remarks>
  private static readonly CodecTag _MSVC = CodecTag.FromCharacters("MSVC");

  /// <summary>The side of a coded block, in pixels.</summary>
  private const int _BLOCK = 4;

  /// <summary>How many squared-error units in five-bit colour one byte of output is worth.</summary>
  /// <remarks>FFmpeg's, where it is a constant rather than a setting, and the whole of the rate control.</remarks>
  private const int _QUALITY = 24;

  /// <summary>
  /// How many frames apart whole pictures are forced: the first, then every twenty-fifth after it.
  /// </summary>
  /// <remarks>
  /// FFmpeg's default minimum key interval, and the count is reset by any frame in which nothing was
  /// skipped, since such a frame is already one a decoder can start at.
  /// </remarks>
  private const int _KEY_FRAME_INTERVAL = 25;

  /// <summary>The two-byte code introducing a run of skipped blocks, before the count is added in.</summary>
  private const int _SKIP_PREFIX = 0x8400;

  /// <summary>The longest run of blocks one skip code can state.</summary>
  private const int _LONGEST_SKIP = 0x3FF;

  /// <summary>Set on a sixteen-bit block's flag word to make it a solid colour, and on the first colour
  /// of an eight-colour block to mark it as one.</summary>
  private const int _MARKER = 0x8000;

  /// <summary>The second flag byte of an eight-bit solid block: past the literals, short of the skips.</summary>
  private const byte _INDEXED_SOLID = 0x80;

  /// <summary>The mask bits of the quad holding the block's top-right pixel — the one bit 15 belongs to.</summary>
  private const int _TOP_RIGHT_QUAD = 0xCC00;

  /// <summary>The mask bits of the quad left of it, flipped when the eight-bit flag word must reach 0x9000.</summary>
  private const int _TOP_LEFT_QUAD = 0x3300;

  /// <summary>What the top three bits of an eight-bit eight-colour flag word must not all be.</summary>
  private const int _INDEXED_EIGHT_COLOUR_BITS = 0x7000;

  /// <summary>How many pixels a block holds.</summary>
  private const int _PIXELS = _BLOCK * _BLOCK;

  private readonly MediaStreamInfo _requested;
  private readonly int _width;
  private readonly int _height;
  private readonly int _blocksAcross;
  private readonly int _blockRows;

  private int _bitsPerPixel;
  private byte[]? _palette;
  private int _paletteCount;
  private MediaStreamInfo? _stream;

  /// <summary>The palette reduced to five bits a channel, which is the space distances are measured in.</summary>
  private byte[]? _paletteFiveBit;

  /// <summary>
  /// The picture as the decoder will be holding it once this frame is written — five-bit colour
  /// triples at sixteen bits, palette indices at eight, top row first — or null before the first frame.
  /// </summary>
  private byte[]? _reconstruction;

  private int _sinceKeyFrame;
  private byte[] _buffer = new byte[4096];
  private int _length;

  /// <summary>How one block was decided to be coded.</summary>
  private enum _Coding {
    Skip,
    Solid,
    TwoColour,
    EightColour,
  }

  private MicrosoftVideo1Encoder(MediaStreamInfo stream, int bitsPerPixel) {
    this._requested = stream;
    this._width = stream.Width;
    this._height = stream.Height;
    this._blocksAcross = stream.Width / _BLOCK;
    this._blockRows = stream.Height / _BLOCK;
    this._bitsPerPixel = bitsPerPixel;

    if (bitsPerPixel == 8)
      this._AdoptPaletteFrom(stream.CodecPrivateData.Span);
  }

  public static string CodecName => "Microsoft Video 1";

  public static CodecTag Codec => _MSVC;

  /// <summary>
  /// Builds an encoder for the stream described, refusing a depth or a size the coding has no form for.
  /// </summary>
  /// <remarks>
  /// A stated depth of eight or sixteen fixes the coding; nothing stated leaves it to the first
  /// picture, which makes a palettised one an eight-bit stream and everything else a sixteen-bit one.
  /// The size has to be a whole number of blocks in both directions, because the format codes nothing
  /// but whole blocks and states nowhere what a partial one would cover — the reference encoder refuses
  /// such a size too rather than padding it.
  /// </remarks>
  public static MicrosoftVideo1Encoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Microsoft Video 1 can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"A Microsoft Video 1 encoder needs the picture size up front; {stream.Width}x{stream.Height} was supplied.");
    if (stream.Width % _BLOCK != 0 || stream.Height % _BLOCK != 0)
      throw new NotSupportedException(
        $"A picture of {stream.Width}x{stream.Height} is not a whole number of 4x4 blocks. Microsoft Video 1 codes "
        + "nothing but whole blocks and states nowhere what a partial one covers.");
    if (stream.BitsPerPixel is not (0 or 8 or 16))
      throw new NotSupportedException(
        $"Video stream {stream.Index} asks for {stream.BitsPerPixel} bits per pixel. Microsoft Video 1 is defined at "
        + "eight bits a pixel, where the values are palette indices, and at sixteen, where they are 5-5-5 colours. "
        + "Nothing else is written.");

    return new(stream, stream.BitsPerPixel);
  }

  /// <summary>
  /// Codes one picture against the one before it, or whole when there is none.
  /// </summary>
  /// <remarks>
  /// Always produces a packet, and flags it as a key frame when nothing in it was skipped — which is
  /// every frame the reconstruction was thrown away for, and also any later frame in which every block
  /// happened to change. A frame identical to the one before it is a single skip run over the whole
  /// picture, which is how this format spells "nothing happened".
  /// </remarks>
  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"Microsoft Video 1 geometry is fixed at {this._width}x{this._height}; received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException(
        "The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    this._TakeDepthFrom(frame);
    var picture = this._bitsPerPixel == 8 ? this._Indices(frame) : _FiveBitColours(frame);

    var keyFrame = this._reconstruction == null || this._sinceKeyFrame >= _KEY_FRAME_INTERVAL;
    if (keyFrame)
      this._reconstruction = new byte[picture.Length];

    this._length = 0;
    var wholePicture = this._EncodeFrame(picture, keyFrame);
    this._sinceKeyFrame = wholePicture ? 1 : this._sinceKeyFrame + 1;

    packet = new(
      this._requested.Index,
      this._buffer.AsSpan(0, this._length).ToArray(),
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: wholePicture);
    return true;
  }

  /// <summary>
  /// The stream as a muxer needs it: a <c>BITMAPINFOHEADER</c> naming this codec, with the palette
  /// behind it where the coding is palettised.
  /// </summary>
  /// <remarks>
  /// That is what an AVI's <c>strf</c> is and what a Matroska <c>V_MS/VFW/FOURCC</c> track's private
  /// data is, and it is where <see cref="MicrosoftVideo1Decoder"/> reads the depth — the one thing the
  /// packets themselves do not say, and the thing that decides whether their literals are one byte wide
  /// or two.
  /// </remarks>
  public MediaStreamInfo DescribeStream() {
    if (this._stream != null)
      return this._stream;

    if (this._bitsPerPixel == 0)
      throw new InvalidOperationException(
        "A Microsoft Video 1 stream whose depth was not stated cannot be described before the first picture has "
        + "decided it. Encode the first picture first, or hand Create a stream stating 8 or 16 bits per pixel.");
    if (this._bitsPerPixel == 8 && this._palette == null)
      throw new InvalidOperationException(
        "An eight-bit Microsoft Video 1 stream cannot be described before its palette is known. Encode the first "
        + "picture first, or hand Create a stream whose CodecPrivateData is a BITMAPINFOHEADER with a palette behind it.");

    var entries = this._bitsPerPixel == 8 ? this._paletteCount : 0;
    var header = new BitmapInfoHeader(
      HeaderSize: BitmapInfoHeader.StructSize,
      Width: this._width,
      Height: this._height,
      Planes: 1,
      BitsPerPixel: (short)this._bitsPerPixel,
      Compression: unchecked((int)_MSVC.Value),
      ImageSize: 0,
      XPixelsPerMeter: 0,
      YPixelsPerMeter: 0,
      ColorsUsed: entries,
      ImportantColors: 0);

    var format = new byte[BitmapInfoHeader.StructSize + entries * 4];
    header.WriteTo(format);
    for (var entry = 0; entry < entries; ++entry) {
      var at = BitmapInfoHeader.StructSize + entry * 4;
      format[at] = this._palette![entry * 3 + 2];
      format[at + 1] = this._palette[entry * 3 + 1];
      format[at + 2] = this._palette[entry * 3];
    }

    return this._stream = new() {
      Index = this._requested.Index,
      Kind = MediaStreamKind.Video,
      Codec = _MSVC,
      Handler = _MSVC,
      CodecId = "V_MS/VFW/FOURCC",
      TimeBase = this._requested.TimeBase,
      FrameRate = this._requested.FrameRate,
      DeclaredFrameCount = this._requested.DeclaredFrameCount,
      Width = this._width,
      Height = this._height,
      BitsPerPixel = this._bitsPerPixel,
      CodecPrivateData = format,
      Language = this._requested.Language,
      Name = this._requested.Name,
    };
  }

  // ============================================================================================
  // What goes in
  // ============================================================================================

  /// <summary>Fixes the depth from the first picture where the stream did not state one.</summary>
  /// <remarks>
  /// A palettised picture makes an eight-bit stream and anything else a sixteen-bit one, because those
  /// are the two things the format has: indices, or colours. Once fixed the depth is in the stream
  /// header and cannot change, so a later picture of the other kind is refused.
  /// </remarks>
  private void _TakeDepthFrom(RawImage frame) {
    if (this._bitsPerPixel == 0)
      this._bitsPerPixel = frame.Format == PixelFormat.Indexed8 ? 8 : 16;

    if (this._bitsPerPixel == 8 && frame.Format != PixelFormat.Indexed8)
      throw new NotSupportedException(
        $"This Microsoft Video 1 stream is palettised and a {frame.Format} picture has no indices to code; it is "
        + "refused rather than quantised, since which colours to reduce it to is not this codec's decision.");
  }

  /// <summary>
  /// The picture as one palette index per pixel, top row first, with the palette taken from it.
  /// </summary>
  /// <remarks>
  /// An index past the end of the palette is refused: the stream header states how many colours there
  /// are and the decoder reads exactly that many, so such an index would come out as whatever happened
  /// to follow the palette in the file.
  /// </remarks>
  private byte[] _Indices(RawImage frame) {
    this._TakePaletteFrom(frame);

    var pixels = this._width * this._height;
    var indices = frame.PixelData.AsSpan(0, pixels).ToArray();
    for (var i = 0; i < pixels; ++i)
      if (indices[i] >= this._paletteCount)
        throw new InvalidDataException(
          $"Pixel {i % this._width},{i / this._width} is palette index {indices[i]} and the palette has "
          + $"{this._paletteCount} entries; the stream header names that many colours and no more.");

    return indices;
  }

  /// <summary>Fixes the palette from the first picture, or checks a later one against it.</summary>
  /// <remarks>
  /// The palette sits in the stream header once, so every frame is drawn through the same one and a
  /// picture bringing another is refused — its indices would come out as the wrong colours and nothing
  /// in the file would say so.
  /// </remarks>
  private void _TakePaletteFrom(RawImage frame) {
    if (frame.Palette == null || frame.PaletteCount <= 0)
      throw new InvalidDataException(
        "A palettised picture without a palette cannot be coded: the frames hold indices and the header holds the "
        + "colours, and there are none to put there.");

    var entries = Math.Min(frame.PaletteCount, 256);
    if (frame.Palette.Length < entries * 3)
      throw new InvalidDataException(
        $"The picture states a palette of {frame.PaletteCount} entries but carries {frame.Palette.Length / 3}.");

    if (this._palette == null) {
      this._AdoptPalette(frame.Palette.AsSpan(0, entries * 3).ToArray(), entries);
      return;
    }

    if (entries == this._paletteCount && frame.Palette.AsSpan(0, entries * 3).SequenceEqual(this._palette))
      return;

    throw new InvalidDataException(
      "The picture carries a different palette from the one the stream was described with. The palette is stated "
      + "once in the stream header, so it cannot change between frames.");
  }

  /// <summary>Takes a palette out of a stream format that already carries one at eight bits.</summary>
  private void _AdoptPaletteFrom(ReadOnlySpan<byte> format) {
    if (format.Length < BitmapInfoHeader.StructSize)
      return;

    var info = BitmapInfoHeader.ReadFrom(format);
    if (info.BitsPerPixel != 8 || info.HeaderSize < BitmapInfoHeader.StructSize)
      return;

    var entries = info.ColorsUsed > 0 ? info.ColorsUsed : 256;
    if (entries > 256 || format.Length < info.HeaderSize + entries * 4)
      return;

    var palette = new byte[entries * 3];
    for (var entry = 0; entry < entries; ++entry) {
      var at = info.HeaderSize + entry * 4;
      palette[entry * 3] = format[at + 2];
      palette[entry * 3 + 1] = format[at + 1];
      palette[entry * 3 + 2] = format[at];
    }

    this._AdoptPalette(palette, entries);
  }

  private void _AdoptPalette(byte[] palette, int entries) {
    this._palette = palette;
    this._paletteCount = entries;

    var reduced = new byte[entries * 3];
    for (var i = 0; i < reduced.Length; ++i)
      reduced[i] = _ToFiveBit(palette[i]);

    this._paletteFiveBit = reduced;
  }

  /// <summary>
  /// The picture as five-bit red, green and blue triples, top row first.
  /// </summary>
  /// <remarks>
  /// Rounded rather than truncated, because the decoder widens a five-bit channel by repeating its
  /// bits: rounding is the exact inverse of that, so every colour already on the 5-5-5 grid — every
  /// colour that came out of a decode of this codec, for one — survives a re-encode untouched, where
  /// dropping the low three bits would leave the brightest end of the range short.
  /// </remarks>
  private static byte[] _FiveBitColours(RawImage frame) {
    var rgb = frame.ToRgb24();
    var samples = frame.Width * frame.Height * 3;
    var reduced = new byte[samples];
    for (var i = 0; i < samples; ++i)
      reduced[i] = _ToFiveBit(rgb[i]);

    return reduced;
  }

  private static byte _ToFiveBit(byte channel) => (byte)((channel * 31 + 127) / 255);

  // ============================================================================================
  // The frame
  // ============================================================================================

  /// <summary>
  /// Writes one frame block by block from the bottom left, and says whether every block was coded.
  /// </summary>
  /// <remarks>
  /// Skip runs are gathered as they go and written out in front of the first block that is not skipped,
  /// or at the end of the frame — split whenever one run reaches the longest count a skip code can
  /// state. A frame in which nothing was skipped is a picture a decoder can start at, whether it was
  /// the first frame or merely one in which everything changed.
  /// <para/>
  /// The two zero bytes at the end are what the reference encoder writes. Every decoder stops when the
  /// last block is accounted for and never reads them, so they are two bytes of nothing — but they are
  /// two bytes of nothing that every existing Video 1 frame ends with.
  /// </remarks>
  private bool _EncodeFrame(byte[] picture, bool keyFrame) {
    var reconstruction = this._reconstruction!;
    var total = this._blocksAcross * this._blockRows;
    var skips = 0;
    var wholePicture = true;

    var indexed = this._bitsPerPixel == 8;
    Span<byte> block = stackalloc byte[_PIXELS * 3];
    Span<byte> indices = stackalloc byte[_PIXELS];
    Span<int> assignment = stackalloc int[_PIXELS];
    Span<int> colours = stackalloc int[24];
    Span<byte> indexedColours = stackalloc byte[8];

    for (var blockIndex = 0; blockIndex < total; ++blockIndex) {
      int mask;
      var coding = indexed
        ? this._ChooseIndexed(picture, reconstruction, blockIndex, keyFrame, indices, assignment, indexedColours, out mask)
        : this._ChooseColour(picture, reconstruction, blockIndex, keyFrame, block, assignment, colours, out mask);

      if (coding == _Coding.Skip) {
        ++skips;
        wholePicture = false;
      }

      if ((coding != _Coding.Skip && skips > 0) || skips == _LONGEST_SKIP) {
        this._PutWord(_SKIP_PREFIX | skips);
        skips = 0;
      }

      if (coding == _Coding.Skip)
        continue;

      if (indexed)
        this._WriteIndexedBlock(reconstruction, blockIndex, coding, mask, indexedColours);
      else
        this._WriteColourBlock(reconstruction, blockIndex, coding, mask, colours);
    }

    if (skips > 0)
      this._PutWord(_SKIP_PREFIX | skips);

    this._PutWord(0);
    return wholePicture;
  }

  // ============================================================================================
  // Deciding one block, at sixteen bits
  // ============================================================================================

  /// <summary>
  /// Prices the four codings of one sixteen-bit block and returns the cheapest, with its mask and its
  /// colours.
  /// </summary>
  /// <remarks>
  /// The price is FFmpeg's: the squared error the coding leaves, in five-bit channel units, divided by
  /// the quality constant, plus the bytes the coding costs — two for a solid block, six for two colours
  /// and eighteen for eight. A skip costs no bytes at all, which is why an unchanged block always wins
  /// it, and it is not offered on a frame that has to stand on its own.
  /// </remarks>
  private _Coding _ChooseColour(
    byte[] picture,
    byte[] reconstruction,
    int blockIndex,
    bool keyFrame,
    Span<byte> block,
    Span<int> assignment,
    Span<int> colours,
    out int mask) {
    var (left, top) = this._Corner(blockIndex);
    for (var pixel = 0; pixel < _PIXELS; ++pixel) {
      var at = this._Sample(left, top, pixel);
      block[pixel * 3] = picture[at];
      block[pixel * 3 + 1] = picture[at + 1];
      block[pixel * 3 + 2] = picture[at + 2];
    }

    var coding = _Coding.Solid;
    mask = 0;
    var best = int.MaxValue;

    if (!keyFrame) {
      var error = 0;
      for (var pixel = 0; pixel < _PIXELS; ++pixel) {
        var at = this._Sample(left, top, pixel);
        for (var channel = 0; channel < 3; ++channel) {
          var difference = reconstruction[at + channel] - block[pixel * 3 + channel];
          error += difference * difference;
        }
      }

      best = error / _QUALITY;
      coding = _Coding.Skip;
    }

    Span<int> solid = stackalloc int[3];
    _Centroid(block, _PIXELS, solid);

    // Red of 1 would spell 0x84xx once the solid marker is set, which is the code for a skip run.
    if (solid[0] == 1)
      solid[0] = 0;

    var solidScore = _SolidError(block, _PIXELS, solid) / _QUALITY + 2;
    if (solidScore < best) {
      best = solidScore;
      coding = _Coding.Solid;
      colours[0] = solid[0];
      colours[1] = solid[1];
      colours[2] = solid[2];
    }

    Span<int> pair = stackalloc int[6];
    Span<int> pairAssignment = stackalloc int[_PIXELS];
    _TwoMeans(block, _PIXELS, pair, pairAssignment);
    var pairScore = _SquaredError(block, _PIXELS, pair, pairAssignment) / _QUALITY + 6;
    if (pairScore < best) {
      best = pairScore;
      coding = _Coding.TwoColour;
      pair.CopyTo(colours);
      pairAssignment.CopyTo(assignment);
    }

    Span<byte> quad = stackalloc byte[4 * 3];
    Span<int> quadPair = stackalloc int[6];
    Span<int> cornerAssignment = stackalloc int[4];
    Span<int> quadColours = stackalloc int[24];
    Span<int> quadAssignment = stackalloc int[_PIXELS];
    var quadError = 0;
    for (var index = 0; index < 4; ++index) {
      for (var corner = 0; corner < 4; ++corner) {
        var pixel = _QuadPixel(index, corner);
        quad[corner * 3] = block[pixel * 3];
        quad[corner * 3 + 1] = block[pixel * 3 + 1];
        quad[corner * 3 + 2] = block[pixel * 3 + 2];
      }

      _TwoMeans(quad, 4, quadPair, cornerAssignment);
      quadError += _SquaredError(quad, 4, quadPair, cornerAssignment);

      quadPair.CopyTo(quadColours[(index * 6)..]);
      for (var corner = 0; corner < 4; ++corner)
        quadAssignment[_QuadPixel(index, corner)] = cornerAssignment[corner];
    }

    var quadScore = quadError / _QUALITY + 18;
    if (quadScore < best) {
      coding = _Coding.EightColour;
      quadColours.CopyTo(colours);
      quadAssignment.CopyTo(assignment);
    }

    if (coding is _Coding.TwoColour or _Coding.EightColour)
      mask = _MaskFrom(assignment);

    switch (coding) {
      case _Coding.TwoColour when (mask & _MARKER) != 0:
        // Bit 15 set would put the flag word where a solid colour lives. Swapping the two colours and
        // inverting every bit says the same thing with the bit clear.
        mask ^= 0xFFFF;
        _SwapColours(colours, 0, 1);
        break;
      case _Coding.EightColour when (mask & _MARKER) != 0:
        mask ^= _TOP_RIGHT_QUAD;
        _SwapColours(colours, 6, 7);
        break;
    }

    return coding;
  }

  /// <summary>Writes one sixteen-bit block and paints it into the reconstruction.</summary>
  private void _WriteColourBlock(byte[] reconstruction, int blockIndex, _Coding coding, int mask, ReadOnlySpan<int> colours) {
    var (left, top) = this._Corner(blockIndex);

    switch (coding) {
      case _Coding.Solid:
        this._PutWord(_MARKER | _Packed(colours, 0));
        for (var pixel = 0; pixel < _PIXELS; ++pixel)
          _Paint(reconstruction, this._Sample(left, top, pixel), colours, 0);

        return;
      case _Coding.TwoColour:
        this._PutWord(mask);
        this._PutWord(_Packed(colours, 0));
        this._PutWord(_Packed(colours, 1));
        for (var pixel = 0; pixel < _PIXELS; ++pixel)
          _Paint(reconstruction, this._Sample(left, top, pixel), colours, _IsFirst(mask, pixel) ? 0 : 1);

        return;
      default:
        this._PutWord(mask);
        this._PutWord(_MARKER | _Packed(colours, 0));
        for (var colour = 1; colour < 8; ++colour)
          this._PutWord(_Packed(colours, colour));

        for (var pixel = 0; pixel < _PIXELS; ++pixel)
          _Paint(reconstruction, this._Sample(left, top, pixel), colours, _Quad(pixel) * 2 + (_IsFirst(mask, pixel) ? 0 : 1));

        return;
    }
  }

  private static void _Paint(byte[] reconstruction, int at, ReadOnlySpan<int> colours, int colour) {
    reconstruction[at] = (byte)colours[colour * 3];
    reconstruction[at + 1] = (byte)colours[colour * 3 + 1];
    reconstruction[at + 2] = (byte)colours[colour * 3 + 2];
  }

  /// <summary>One colour as the 5-5-5 word the format holds, red in the high bits.</summary>
  private static int _Packed(ReadOnlySpan<int> colours, int colour)
    => (colours[colour * 3] << 10) | (colours[colour * 3 + 1] << 5) | colours[colour * 3 + 2];

  private static void _SwapColours(Span<int> colours, int first, int second) {
    for (var channel = 0; channel < 3; ++channel)
      (colours[first * 3 + channel], colours[second * 3 + channel])
        = (colours[second * 3 + channel], colours[first * 3 + channel]);
  }

  // ============================================================================================
  // Deciding one block, at eight bits
  // ============================================================================================

  /// <summary>
  /// Prices the same four codings of one eight-bit block, where the colours have to be palette entries.
  /// </summary>
  /// <remarks>
  /// The candidates are the indices the block already uses and no others. Which means a block of one or
  /// two colours is coded exactly, and a block of more is approximated out of colours that are at least
  /// known to be in the picture — where a nearest-entry search over the whole palette could reach for a
  /// colour the picture never contained. With at most sixteen candidates in a block and four in a quad,
  /// every pair is tried and the best one wins outright rather than being converged on.
  /// </remarks>
  private _Coding _ChooseIndexed(
    byte[] picture,
    byte[] reconstruction,
    int blockIndex,
    bool keyFrame,
    Span<byte> indices,
    Span<int> assignment,
    Span<byte> colours,
    out int mask) {
    var (left, top) = this._Corner(blockIndex);
    for (var pixel = 0; pixel < _PIXELS; ++pixel)
      indices[pixel] = picture[this._Pixel(left, top, pixel)];

    var coding = _Coding.Solid;
    mask = 0;
    var best = int.MaxValue;

    if (!keyFrame) {
      var error = 0;
      for (var pixel = 0; pixel < _PIXELS; ++pixel)
        error += this._IndexDistance(indices[pixel], reconstruction[this._Pixel(left, top, pixel)]);

      best = error / _QUALITY;
      coding = _Coding.Skip;
    }

    var solid = this._BestSingleIndex(indices, _PIXELS, out var solidError);
    var solidScore = solidError / _QUALITY + 2;
    if (solidScore < best) {
      best = solidScore;
      coding = _Coding.Solid;
      colours[0] = solid;
    }

    Span<int> pairAssignment = stackalloc int[_PIXELS];
    var pairError = this._BestIndexPair(indices, _PIXELS, out var pairFirst, out var pairSecond, pairAssignment);
    var pairScore = pairError / _QUALITY + 6;
    if (pairScore < best) {
      best = pairScore;
      coding = _Coding.TwoColour;
      colours[0] = pairFirst;
      colours[1] = pairSecond;
      pairAssignment.CopyTo(assignment);
    }

    Span<byte> quad = stackalloc byte[4];
    Span<int> cornerAssignment = stackalloc int[4];
    Span<byte> quadColours = stackalloc byte[8];
    Span<int> quadAssignment = stackalloc int[_PIXELS];
    var quadError = 0;
    for (var index = 0; index < 4; ++index) {
      for (var corner = 0; corner < 4; ++corner)
        quad[corner] = indices[_QuadPixel(index, corner)];

      quadError += this._BestIndexPair(quad, 4, out var first, out var second, cornerAssignment);
      quadColours[index * 2] = first;
      quadColours[index * 2 + 1] = second;
      for (var corner = 0; corner < 4; ++corner)
        quadAssignment[_QuadPixel(index, corner)] = cornerAssignment[corner];
    }

    var quadScore = quadError / _QUALITY + 18;
    if (quadScore < best) {
      coding = _Coding.EightColour;
      quadColours.CopyTo(colours);
      quadAssignment.CopyTo(assignment);
    }

    if (coding is _Coding.TwoColour or _Coding.EightColour)
      mask = _MaskFrom(assignment);

    switch (coding) {
      case _Coding.TwoColour when (mask & _MARKER) != 0:
        mask ^= 0xFFFF;
        (colours[0], colours[1]) = (colours[1], colours[0]);
        break;
      case _Coding.EightColour:
        // Where sixteen bits needs the flag word below 0x8000, eight bits needs an eight-colour block's
        // above 0x9000 — the top bit is what tells the two codings apart, and the three below it are
        // what keeps the word clear of the solid and skip codes. Flipping a quad inverts its own four
        // mask bits and nothing else, so the top-right quad sets bit 15 and the top-left one, if the
        // word is still short, sets the bits beneath it.
        if ((mask & _MARKER) == 0) {
          mask ^= _TOP_RIGHT_QUAD;
          (colours[6], colours[7]) = (colours[7], colours[6]);
        }

        if ((mask & _INDEXED_EIGHT_COLOUR_BITS) == 0) {
          mask ^= _TOP_LEFT_QUAD;
          (colours[4], colours[5]) = (colours[5], colours[4]);
        }

        break;
    }

    return coding;
  }

  /// <summary>Writes one eight-bit block and paints it into the reconstruction.</summary>
  private void _WriteIndexedBlock(byte[] reconstruction, int blockIndex, _Coding coding, int mask, ReadOnlySpan<byte> colours) {
    var (left, top) = this._Corner(blockIndex);

    switch (coding) {
      case _Coding.Solid:
        this._Put(colours[0]);
        this._Put(_INDEXED_SOLID);
        for (var pixel = 0; pixel < _PIXELS; ++pixel)
          reconstruction[this._Pixel(left, top, pixel)] = colours[0];

        return;
      case _Coding.TwoColour:
        this._PutWord(mask);
        this._Put(colours[0]);
        this._Put(colours[1]);
        for (var pixel = 0; pixel < _PIXELS; ++pixel)
          reconstruction[this._Pixel(left, top, pixel)] = colours[_IsFirst(mask, pixel) ? 0 : 1];

        return;
      default:
        this._PutWord(mask);
        for (var colour = 0; colour < 8; ++colour)
          this._Put(colours[colour]);

        for (var pixel = 0; pixel < _PIXELS; ++pixel)
          reconstruction[this._Pixel(left, top, pixel)] = colours[_Quad(pixel) * 2 + (_IsFirst(mask, pixel) ? 0 : 1)];

        return;
    }
  }

  /// <summary>The index, out of those the block already uses, that costs least as the whole block.</summary>
  private byte _BestSingleIndex(ReadOnlySpan<byte> indices, int count, out int error) {
    var best = indices[0];
    error = int.MaxValue;

    for (var candidate = 0; candidate < count; ++candidate) {
      var total = 0;
      for (var pixel = 0; pixel < count; ++pixel)
        total += this._IndexDistance(indices[pixel], indices[candidate]);

      if (total >= error)
        continue;

      error = total;
      best = indices[candidate];
    }

    return best;
  }

  /// <summary>
  /// The pair of indices, out of those the block already uses, whose nearest-of-two costs least.
  /// </summary>
  private int _BestIndexPair(ReadOnlySpan<byte> indices, int count, out byte first, out byte second, Span<int> assignment) {
    first = indices[0];
    second = indices[0];
    var error = int.MaxValue;

    for (var a = 0; a < count; ++a)
      for (var b = a; b < count; ++b) {
        var total = 0;
        for (var pixel = 0; pixel < count; ++pixel)
          total += Math.Min(this._IndexDistance(indices[pixel], indices[a]), this._IndexDistance(indices[pixel], indices[b]));

        if (total >= error)
          continue;

        error = total;
        first = indices[a];
        second = indices[b];
      }

    for (var pixel = 0; pixel < count; ++pixel)
      assignment[pixel] = this._IndexDistance(indices[pixel], first) <= this._IndexDistance(indices[pixel], second) ? 0 : 1;

    return error;
  }

  /// <summary>How far apart two palette entries are, squared, in the five-bit space of the sixteen-bit coding.</summary>
  private int _IndexDistance(byte left, byte right) {
    if (left == right)
      return 0;

    var palette = this._paletteFiveBit!;
    var total = 0;
    for (var channel = 0; channel < 3; ++channel) {
      var difference = palette[left * 3 + channel] - palette[right * 3 + channel];
      total += difference * difference;
    }

    return total;
  }

  // ============================================================================================
  // The quantiser
  // ============================================================================================

  /// <summary>The rounded mean of a set of colours.</summary>
  private static void _Centroid(ReadOnlySpan<byte> points, int count, Span<int> centre) {
    Span<int> sums = stackalloc int[3];
    for (var point = 0; point < count; ++point)
      for (var channel = 0; channel < 3; ++channel)
        sums[channel] += points[point * 3 + channel];

    for (var channel = 0; channel < 3; ++channel)
      centre[channel] = (sums[channel] + count / 2) / count;
  }

  /// <summary>
  /// Splits a set of colours into two, seeded from the two furthest apart and refined by Lloyd's rule
  /// until nothing moves.
  /// </summary>
  /// <remarks>
  /// Where FFmpeg reaches for ELBG, which starts from a pseudo-random pick and would make the same
  /// picture encode to different bytes on different runs. Seeding from the extreme pair is what makes
  /// this deterministic, and it is also what makes a block that genuinely has two colours in it come out
  /// exact: the two seeds are those colours, nothing moves, and the error is nought.
  /// <para/>
  /// A set whose colours are all the same has no split to make; both halves take that colour, every
  /// pixel is assigned to the first, and the caller's swap-for-bit-15 puts them back either way round
  /// without changing the picture.
  /// </remarks>
  private static void _TwoMeans(ReadOnlySpan<byte> points, int count, Span<int> codebook, Span<int> assignment) {
    assignment[..count].Clear();

    var furthest = -1;
    var seedFirst = 0;
    var seedSecond = 0;
    for (var a = 0; a < count; ++a)
      for (var b = a + 1; b < count; ++b) {
        var distance = 0;
        for (var channel = 0; channel < 3; ++channel) {
          var difference = points[a * 3 + channel] - points[b * 3 + channel];
          distance += difference * difference;
        }

        if (distance <= furthest)
          continue;

        furthest = distance;
        seedFirst = a;
        seedSecond = b;
      }

    for (var channel = 0; channel < 3; ++channel) {
      codebook[channel] = points[seedFirst * 3 + channel];
      codebook[3 + channel] = points[seedSecond * 3 + channel];
    }

    if (furthest <= 0)
      return;

    Span<int> counts = stackalloc int[2];
    Span<int> sums = stackalloc int[6];
    for (var step = 0; step < 8; ++step) {
      var moved = step == 0;
      for (var point = 0; point < count; ++point) {
        var choice = _Nearest(points, point, codebook);
        if (assignment[point] == choice)
          continue;

        assignment[point] = choice;
        moved = true;
      }

      if (!moved)
        return;

      counts.Clear();
      sums.Clear();
      for (var point = 0; point < count; ++point) {
        var half = assignment[point];
        ++counts[half];
        for (var channel = 0; channel < 3; ++channel)
          sums[half * 3 + channel] += points[point * 3 + channel];
      }

      // A half nothing was assigned to keeps its seed, which is still a colour of the block and still
      // the one furthest from the other half.
      for (var half = 0; half < 2; ++half)
        if (counts[half] > 0)
          for (var channel = 0; channel < 3; ++channel)
            codebook[half * 3 + channel] = (sums[half * 3 + channel] + counts[half] / 2) / counts[half];
    }
  }

  private static int _Nearest(ReadOnlySpan<byte> points, int point, ReadOnlySpan<int> codebook) {
    var first = 0;
    var second = 0;
    for (var channel = 0; channel < 3; ++channel) {
      var toFirst = points[point * 3 + channel] - codebook[channel];
      var toSecond = points[point * 3 + channel] - codebook[3 + channel];
      first += toFirst * toFirst;
      second += toSecond * toSecond;
    }

    return first <= second ? 0 : 1;
  }

  /// <summary>What a set of colours costs when each takes the codebook entry it was assigned.</summary>
  private static int _SquaredError(
    ReadOnlySpan<byte> points, int count, ReadOnlySpan<int> codebook, ReadOnlySpan<int> assignment) {
    var total = 0;
    for (var point = 0; point < count; ++point) {
      var at = assignment[point] * 3;
      for (var channel = 0; channel < 3; ++channel) {
        var difference = points[point * 3 + channel] - codebook[at + channel];
        total += difference * difference;
      }
    }

    return total;
  }

  /// <summary>What a set of colours costs when every one of them takes the same colour.</summary>
  private static int _SolidError(ReadOnlySpan<byte> points, int count, ReadOnlySpan<int> colour) {
    var total = 0;
    for (var point = 0; point < count; ++point)
      for (var channel = 0; channel < 3; ++channel) {
        var difference = points[point * 3 + channel] - colour[channel];
        total += difference * difference;
      }

    return total;
  }

  // ============================================================================================
  // Where a pixel is
  // ============================================================================================

  /// <summary>
  /// Where a block's top-left pixel is, given that blocks are coded from the bottom of the picture.
  /// </summary>
  private (int Left, int Top) _Corner(int blockIndex) {
    var blockRow = blockIndex / this._blocksAcross;
    var blockColumn = blockIndex % this._blocksAcross;

    return (blockColumn * _BLOCK, this._height - (blockRow + 1) * _BLOCK);
  }

  /// <summary>Where in a top-down picture the block pixel numbered <paramref name="pixel"/> sits.</summary>
  /// <remarks>
  /// The numbering is the mask's: <c>column + row * 4</c> with rows counted upwards from the bottom of
  /// the block, so pixel 0 is the bottom left one and pixel 15 the top right — which is the pixel bit 15
  /// belongs to, and the reason the flag word's top bit can be steered by swapping a quad's colours.
  /// </remarks>
  private int _Pixel(int left, int top, int pixel)
    => (top + _BLOCK - 1 - pixel / _BLOCK) * this._width + left + pixel % _BLOCK;

  private int _Sample(int left, int top, int pixel) => this._Pixel(left, top, pixel) * 3;

  /// <summary>Which 2x2 quad a block pixel is in: 0 bottom left, 1 bottom right, 2 top left, 3 top right.</summary>
  private static int _Quad(int pixel) => (pixel / _BLOCK >= 2 ? 2 : 0) + (pixel % _BLOCK >= 2 ? 1 : 0);

  /// <summary>Which block pixel is corner <paramref name="corner"/> of quad <paramref name="quad"/>.</summary>
  private static int _QuadPixel(int quad, int corner)
    => (quad / 2 * 2 + corner / 2) * _BLOCK + quad % 2 * 2 + corner % 2;

  /// <summary>The mask that says which pixels take their colour pair's first colour.</summary>
  private static int _MaskFrom(ReadOnlySpan<int> assignment) {
    var mask = 0;
    for (var pixel = 0; pixel < _PIXELS; ++pixel)
      if (assignment[pixel] == 0)
        mask |= 1 << pixel;

    return mask;
  }

  private static bool _IsFirst(int mask, int pixel) => ((mask >> pixel) & 1) != 0;

  // ============================================================================================
  // The output buffer
  // ============================================================================================

  private void _Put(byte value) {
    if (this._length == this._buffer.Length)
      Array.Resize(ref this._buffer, this._buffer.Length * 2);

    this._buffer[this._length++] = value;
  }

  private void _PutWord(int value) {
    this._Put((byte)value);
    this._Put((byte)(value >> 8));
  }
}
