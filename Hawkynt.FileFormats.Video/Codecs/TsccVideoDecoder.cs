using System;
using System.IO;
using System.IO.Compression;
using FileFormat.Bmp;
using FileFormat.Codecs.Tscc;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes TechSmith Screen Capture (TSCC, also known as Ensharpen): zlib over a run-length coding
/// that is Microsoft's own in every particular but the width of a pixel.
/// </summary>
/// <remarks>
/// Two layers, and each is simple on its own. A packet is zlib-compressed data; decompressing it
/// yields a stream of run-length opcodes — a count and a colour repeated, or one of three escapes —
/// walked onto the picture from the bottom row up. See <see cref="TsccRle"/> for the walk itself,
/// which is close enough to <c>MicrosoftRle</c> in the image package to be describable as the same
/// coding at a different pixel width, and is not shared with it only because the two live in
/// different assemblies.
/// <para/>
/// <b>Not every packet carries a picture.</b> A screen-capture video spends most of its frames on
/// content that has not changed at all, and this codec's answer to a completely unchanged frame is
/// not to compress an empty delta — compressing nothing costs more bytes than it saves — but to write
/// a few bytes that are not zlib data at all and carry no picture: on real files, exactly the packets
/// that fail zlib's own header check are exactly the packets ffmpeg produces no frame for, measured on
/// a stream where 348 of 849 packets pass the check and ffprobe counts 348 decoded frames. What
/// decides whether a packet is one of them is the packet itself — a valid zlib stream starts with a
/// header whose checksum property (<c>(CMF × 256 + FLG) mod 31 = 0</c>) essentially never holds by
/// accident, so it is read off the bytes rather than a flag the format states nowhere.
/// <see cref="_LooksLikeZlib"/> is that test, and a packet that fails it makes <see cref="TryDecode"/>
/// return <see langword="false"/> rather than a repeated picture — the same "not yet" this interface
/// already has a word for, and not a frame manufactured to have something to return.
/// <para/>
/// <b>The canvas persists and the zlib stream does not.</b> Every key frame's decompression starts
/// fresh — measured directly: the first frame of every sample file consumes its packet to the last
/// byte and reaches zlib's own end marker, which a continuation of an earlier stream could not do —
/// while the picture a delta frame's escapes are read against is the frame before it, exactly as
/// <c>MicrosoftRle</c> already models for the codec this one measures byte-for-byte against.
/// <para/>
/// <b>Measured against ffmpeg</b> at every depth a real file was found at — 16-bit (555), 24-bit and
/// 32-bit — across four samples and thousands of frames, plane for plane and frame for frame, with no
/// differing samples anywhere. The palettised 8-bit path, which none of the samples this was measured
/// on happens to use, was checked the other way round: a hand-built stream, decoded here and by
/// ffmpeg, agrees on every sample including its palette.
/// <para/>
/// <b>What refuses.</b> A depth this format does not define; a palettised stream with no palette to
/// decode its indices to, since the palette is the container's business and not carried in any frame;
/// and any opcode that runs off the picture or off the end of the decompressed data.
/// </remarks>
public sealed class TsccVideoDecoder : IVideoCodecDecoder<TsccVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("tscc");

  private readonly int _width;
  private readonly int _height;
  private readonly int _bytesPerPixel;
  private readonly bool _paletteMode;
  private readonly byte[] _palette;
  private readonly int _paletteCount;
  private readonly int _streamIndex;

  /// <summary>The picture as coded, bottom row first, kept between packets because that is what a
  /// delta frame's escapes are read against.</summary>
  private readonly byte[] _canvas;

  private TsccVideoDecoder(
    int width, int height, int bytesPerPixel, bool paletteMode, byte[] palette, int paletteCount, int streamIndex) {
    this._width = width;
    this._height = height;
    this._bytesPerPixel = bytesPerPixel;
    this._paletteMode = paletteMode;
    this._palette = palette;
    this._paletteCount = paletteCount;
    this._streamIndex = streamIndex;
    this._canvas = new byte[width * height * bytesPerPixel];
  }

  /// <summary>Gets the codec name.</summary>
  public static string CodecName => "TechSmith Screen Capture";

  /// <summary>Determines whether the specified media stream is supported.</summary>
  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  /// <summary>Creates a decoder for the specified media stream.</summary>
  public static TsccVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can "
        + "be decoded into.");

    var bitsPerPixel = stream.BitsPerPixel;
    var (bytesPerPixel, paletteMode) = bitsPerPixel switch {
      8 => (1, true),
      16 => (2, false),
      24 => (3, false),
      32 => (4, false),
      _ => throw new NotSupportedException(
        $"Video stream {stream.Index} states {bitsPerPixel} bits a pixel. TSCC is defined at 8 (palettised), 16, 24 "
        + "and 32 bits a pixel, and nothing else is read."),
    };

    byte[] palette = [];
    var paletteCount = 0;
    if (paletteMode)
      (palette, paletteCount) = _ReadPalette(stream);

    return new(stream.Width, stream.Height, bytesPerPixel, paletteMode, palette, paletteCount, stream.Index);
  }

  /// <summary>
  /// Lifts the palette out of the stream's <c>BITMAPINFOHEADER</c>, where the format's own document
  /// says it lives: "the RGB palette information should be transported by the container format."
  /// </summary>
  private static (byte[] Palette, int Count) _ReadPalette(MediaStreamInfo stream) {
    var format = stream.CodecPrivateData.Span;
    if (format.Length <= BitmapInfoHeader.StructSize)
      throw new InvalidDataException(
        $"Video stream {stream.Index} is palettised TSCC and carries no palette behind its stream format. The "
        + "frames hold palette indices and nothing else, so there are no colours to decode them to.");

    var info = BitmapInfoHeader.ReadFrom(format);
    var headerSize = info.HeaderSize >= BitmapInfoHeader.StructSize ? info.HeaderSize : BitmapInfoHeader.StructSize;
    if (headerSize >= format.Length)
      throw new InvalidDataException(
        $"Video stream {stream.Index} is palettised TSCC and carries no palette behind its {headerSize}-byte "
        + "stream format header.");

    var entries = info.ColorsUsed > 0 ? info.ColorsUsed : 256;
    var available = (format.Length - headerSize) / 4;
    if (available < entries)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states {entries} palette entries and carries {available}.");

    var palette = new byte[entries * 3];
    for (var entry = 0; entry < entries; ++entry) {
      var at = headerSize + entry * 4;
      palette[entry * 3] = format[at + 2];
      palette[entry * 3 + 1] = format[at + 1];
      palette[entry * 3 + 2] = format[at];
    }

    return (palette, entries);
  }

  /// <summary>Attempts to decode the specified coded packet into a raw image frame.</summary>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var data = packet.Data.Span;

    // A packet that does not open with a zlib stream carries nothing to decode: the frame is
    // exactly the one before it, and this codec's way of saying so is not to compress an unchanged
    // picture — which would cost more bytes than it saves — but to skip zlib, and skip the frame.
    if (!_LooksLikeZlib(data)) {
      frame = null!;
      return false;
    }

    var decompressed = _Inflate(data);
    TsccRle.Decode(decompressed, this._canvas, this._width, this._height, this._bytesPerPixel);

    frame = this._ComposeFrame();
    return true;
  }

  /// <summary>
  /// Whether <paramref name="data"/> opens with a valid zlib header, judged the way the format itself
  /// can be — RFC 1950's own check, <c>(CMF × 256 + FLG) mod 31 = 0</c>, with the compression method
  /// nibble additionally required to be eight (DEFLATE), which is the only one zlib ever emits.
  /// </summary>
  private static bool _LooksLikeZlib(ReadOnlySpan<byte> data) {
    if (data.Length < 2)
      return false;

    var cmf = data[0];
    var flg = data[1];
    return (cmf & 0x0F) == 8 && (cmf * 256 + flg) % 31 == 0;
  }

  private static byte[] _Inflate(ReadOnlySpan<byte> compressed) {
    using var source = new MemoryStream(compressed.ToArray());
    using var zlib = new ZLibStream(source, CompressionMode.Decompress);
    using var output = new MemoryStream();
    zlib.CopyTo(output);
    return output.ToArray();
  }

  private RawImage _ComposeFrame() {
    var picture = _FlipVertically(this._canvas, this._height, this._width * this._bytesPerPixel);

    if (this._paletteMode)
      return new() {
        Width = this._width,
        Height = this._height,
        Format = PixelFormat.Indexed8,
        PixelData = picture,
        Palette = this._palette,
        PaletteCount = this._paletteCount,
      };

    return this._bytesPerPixel switch {
      2 => new() { Width = this._width, Height = this._height, Format = PixelFormat.Rgb24, PixelData = _Widen555(picture, this._width * this._height) },
      3 => new() { Width = this._width, Height = this._height, Format = PixelFormat.Bgr24, PixelData = picture },
      4 => new() { Width = this._width, Height = this._height, Format = PixelFormat.Bgra32, PixelData = picture },
      _ => throw new NotSupportedException($"Video stream {this._streamIndex} has no colour conversion for {this._bytesPerPixel} bytes a pixel."),
    };
  }

  /// <summary>Turns the coded, bottom-up canvas the right way up.</summary>
  private static byte[] _FlipVertically(byte[] canvas, int height, int stride) {
    var picture = new byte[canvas.Length];
    for (var row = 0; row < height; ++row)
      Array.Copy(canvas, (height - 1 - row) * stride, picture, row * stride, stride);

    return picture;
  }

  /// <summary>
  /// Widens a packed 5-5-5 picture to eight bits a channel by repeating each channel's five bits
  /// rather than shifting them, the rule this package's other 5-5-5 codecs are measured against.
  /// </summary>
  private static byte[] _Widen555(byte[] packed, int pixelCount) {
    var picture = new byte[pixelCount * 3];
    for (var i = 0; i < pixelCount; ++i) {
      var colour = packed[i * 2] | (packed[i * 2 + 1] << 8);
      picture[i * 3] = _Widen((colour >> 10) & 0x1F);
      picture[i * 3 + 1] = _Widen((colour >> 5) & 0x1F);
      picture[i * 3 + 2] = _Widen(colour & 0x1F);
    }

    return picture;
  }

  private static byte _Widen(int channel) => (byte)((channel << 3) | (channel >> 2));
}
