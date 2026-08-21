using System;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Microsoft RLE video (<c>MRLE</c>, <c>BI_RLE8</c> and <c>BI_RLE4</c>): run-length coded
/// palettised frames with delta and skip escapes.
/// </summary>
/// <remarks>
/// The coding is the one a run-length Windows bitmap is stored with, and this does not have a second
/// copy of it. <see cref="MicrosoftRle"/> in the image package walks the opcodes onto a canvas the
/// caller supplies, which is the entire difference between the two uses: a still hands it an empty
/// canvas, and a film hands it the frame before.
/// <para/>
/// That is not a saving of code so much as a statement of what the format is. The end-of-line, delta
/// and end-of-bitmap escapes look like curiosities in a still — ways of leaving parts of a picture
/// unstated, which for a still means leaving them the background colour. In a film the very same
/// escapes are the whole of the inter-frame coding: "unstated" means "unchanged since the last
/// frame", and a decoder that started each frame from a blank canvas would decode every opcode
/// correctly and still produce a picture full of holes.
/// <para/>
/// <b>The coding is lossless</b>, so there is nothing to round and no reason for a sample to differ
/// from what any other decoder produces. Every frame of the streams this was measured on matches
/// ffmpeg's own decode of the same file exactly — no differing samples at all, on key frames and on
/// delta frames alike.
/// <para/>
/// <b>What it does not read refuses by name.</b> A depth the coding is not defined at, a depth that
/// disagrees with the compression stated beside it, a stream with no palette, rows the wrong way up,
/// and any opcode that runs off the picture or off the end of the data — each of those throws and
/// says which. There is no <c>catch</c> handing back the previous frame: a repeated frame is what a
/// correctly coded still passage looks like, so a decoder that produced one on failure would be
/// indistinguishable from one that worked.
/// </remarks>
public sealed class MicrosoftRleDecoder : IVideoCodecDecoder<MicrosoftRleDecoder> {

  /// <summary>The four-character code containers name this codec with where they name it at all.</summary>
  private static readonly CodecTag _MRLE = CodecTag.FromCharacters("MRLE");

  /// <summary>
  /// The compressions a <c>BITMAPINFOHEADER</c> states instead of a code, which is how an AVI names
  /// this codec in practice.
  /// </summary>
  /// <remarks>
  /// ffmpeg's AVI muxer writes <c>biCompression</c> = 1 into the stream format and leaves the stream
  /// handler empty, so ffprobe prints the codec tag as <c>[1][0][0][0]</c> — there is no <c>MRLE</c>
  /// anywhere in the file. A decoder that took only the four-character code would refuse every file
  /// the reference encoder writes.
  /// </remarks>
  private const uint _BI_RLE8 = 1;

  private const uint _BI_RLE4 = 2;

  private readonly int _width;
  private readonly int _height;
  private readonly int _bitsPerPixel;
  private readonly byte[] _palette;
  private readonly int _paletteCount;

  /// <summary>
  /// The picture as indices, one byte a pixel, in the order the coding names its rows.
  /// </summary>
  /// <remarks>
  /// Kept between packets and never cleared, because that is what a delta frame is predicted from.
  /// Held in coded order rather than display order so that the walk does not have to know which way
  /// up the rows run.
  /// </remarks>
  private readonly byte[] _canvas;

  private MicrosoftRleDecoder(int width, int height, int bitsPerPixel, byte[] palette, int paletteCount) {
    this._width = width;
    this._height = height;
    this._bitsPerPixel = bitsPerPixel;
    this._palette = palette;
    this._paletteCount = paletteCount;
    this._canvas = new byte[width * height];
  }

  public static string CodecName => "Microsoft RLE";

  /// <summary>
  /// Takes a stream whose stream format states a run-length compression, or whose code is <c>MRLE</c>.
  /// </summary>
  /// <remarks>
  /// Both, because the two containers that carry this codec name it differently and neither is
  /// wrong. An AVI's video code is its <c>BITMAPINFOHEADER</c>'s <c>biCompression</c>, so the codec
  /// arrives as the number 1 or 2; a Matroska track carrying the same header arrives the same way,
  /// since its reader lifts the compression out of the private data for exactly this reason. The
  /// four-character code is what a file written by something that had a code field to fill in says.
  /// </remarks>
  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video
           && (stream.Codec.Value is _BI_RLE8 or _BI_RLE4 || stream.Codec.EqualsIgnoringCase(_MRLE));
  }

  /// <summary>
  /// Builds a decoder from the stream's <c>BITMAPINFOHEADER</c> and the palette behind it.
  /// </summary>
  /// <remarks>
  /// Everything this codec needs beyond the packets is in that header, because the format is a
  /// Windows bitmap's body without its file. The palette in particular is not in the packets at all
  /// — a frame carries indices and nothing else — so a stream that states none has no colours to
  /// decode to and is refused here rather than decoded into a grey picture nobody asked for.
  /// </remarks>
  public static MicrosoftRleDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    var format = stream.CodecPrivateData;
    if (format.Length < BitmapInfoHeader.StructSize)
      throw new InvalidOperationException(
        $"Microsoft RLE video stream {stream.Index} carries {format.Length} bytes of stream format where a "
        + $"BITMAPINFOHEADER is {BitmapInfoHeader.StructSize}.");

    var info = BitmapInfoHeader.ReadFrom(format.Span);

    if (info.Height < 0)
      throw new NotSupportedException(
        $"Video stream {stream.Index} states a height of {info.Height}, which asks for rows top down. Microsoft "
        + "run-length coding is defined for bottom-up rows only, and no file storing it the other way up is read.");

    var width = info.Width;
    var height = info.Height;
    if (width <= 0 || height <= 0)
      throw new InvalidOperationException(
        $"Microsoft RLE video stream {stream.Index} states a picture of {width}x{height}, which has no pixels.");

    // Multiplied as a long before the canvas is asked for. A damaged header stating 65536 by 65536
    // overflows an int to zero, which would allocate a canvas of nothing and then throw an index
    // error somewhere inside the walk — a refusal that names neither the field nor the file.
    if ((long)width * height > int.MaxValue)
      throw new InvalidOperationException(
        $"Microsoft RLE video stream {stream.Index} states a picture of {width}x{height}, which is more pixels than "
        + "can be held.");

    var bitsPerPixel = _RefuseMismatchedDepth(stream, info);
    var (palette, paletteCount) = _ReadPalette(stream, info, bitsPerPixel);

    return new(width, height, bitsPerPixel, palette, paletteCount);
  }

  /// <summary>
  /// Decodes one packet, which for this codec is always exactly one whole frame.
  /// </summary>
  /// <remarks>
  /// The canvas is not cleared first. A frame that names no opcode for a pixel is saying that pixel
  /// did not change, and the skip escapes exist to say so cheaply; clearing would turn every
  /// unchanged part of the picture into palette entry zero.
  /// <para/>
  /// What comes out is a copy of the canvas rather than the canvas, since the canvas is about to be
  /// painted on again by the next frame and a caller holding several frames would otherwise find all
  /// of them holding the last one.
  /// </remarks>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    MicrosoftRle.Decode(
      packet.Data.Span, this._canvas, this._width, this._height, this._bitsPerPixel, refuseMalformed: true);

    frame = new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Indexed8,
      PixelData = this._TopDownCopy(),
      Palette = this._palette,
      PaletteCount = this._paletteCount,
    };

    return true;
  }

  /// <summary>Turns the canvas the right way up, which for this format is always a flip.</summary>
  private byte[] _TopDownCopy() {
    var picture = new byte[this._canvas.Length];
    for (var row = 0; row < this._height; ++row)
      Array.Copy(this._canvas, (this._height - 1 - row) * this._width, picture, row * this._width, this._width);

    return picture;
  }

  /// <summary>
  /// Checks the stated depth against the stated compression and returns the one they agree on.
  /// </summary>
  /// <remarks>
  /// The two say the same thing twice and a file where they disagree cannot be decoded either way
  /// round: reading four-bit opcodes at eight bits produces half a row of plausible pixels and then
  /// garbage, and the other way round produces a picture twice as wide as the header says. Naming
  /// the contradiction is the only useful answer.
  /// <para/>
  /// A stream naming itself with the four-character code rather than with a compression has only its
  /// depth to go on, so the depth is what decides.
  /// </remarks>
  private static int _RefuseMismatchedDepth(MediaStreamInfo stream, BitmapInfoHeader info) {
    var bitsPerPixel = (int)info.BitsPerPixel;
    if (bitsPerPixel is not (4 or 8))
      throw new NotSupportedException(
        $"Video stream {stream.Index} states {bitsPerPixel} bits per pixel. Microsoft run-length coding is defined at "
        + "four bits a pixel and at eight, and nothing else is read.");

    var stated = (uint)info.Compression;
    if (stated is not (_BI_RLE8 or _BI_RLE4))
      return bitsPerPixel;

    var impliedByCompression = stated == _BI_RLE8 ? 8 : 4;
    if (impliedByCompression == bitsPerPixel)
      return bitsPerPixel;

    throw new InvalidDataException(
      $"Video stream {stream.Index} states compression {stated}, which is run-length coding at {impliedByCompression} "
      + $"bits a pixel, beside a depth of {bitsPerPixel} bits. The two disagree and the frames cannot be read as "
      + "either.");
  }

  /// <summary>
  /// Lifts the palette out of the stream format, where it sits directly behind the header.
  /// </summary>
  /// <remarks>
  /// The entries are <c>RGBQUAD</c>s — blue, green, red and a byte that is not an alpha — and what a
  /// <see cref="RawImage"/> wants is red, green, blue, so the two outer bytes swap.
  /// <para/>
  /// How many there are comes from <c>biClrUsed</c> where it is stated and from the depth where it is
  /// not, which is what the field means. What is actually there is checked against that, because a
  /// stream promising 256 colours and carrying 40 would otherwise decode its last frames against
  /// whatever followed the palette in the file.
  /// </remarks>
  private static (byte[] Palette, int Count) _ReadPalette(
    MediaStreamInfo stream, BitmapInfoHeader info, int bitsPerPixel) {
    // Past the header as the header itself measures it: a BITMAPV4 or V5 header is longer than the
    // 40 bytes of the original, and the palette starts after all of it.
    var headerSize = info.HeaderSize >= BitmapInfoHeader.StructSize ? info.HeaderSize : BitmapInfoHeader.StructSize;
    var format = stream.CodecPrivateData.Span;
    if (headerSize >= format.Length)
      throw new InvalidOperationException(
        $"Microsoft RLE video stream {stream.Index} carries no palette behind its {headerSize}-byte stream format "
        + "header. The frames hold palette indices and nothing else, so there are no colours to decode them to.");

    var entries = info.ColorsUsed > 0 ? info.ColorsUsed : 1 << bitsPerPixel;
    var available = (format.Length - headerSize) / 4;
    if (available < entries)
      throw new InvalidDataException(
        $"Microsoft RLE video stream {stream.Index} states {entries} palette entries and carries {available}.");

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
