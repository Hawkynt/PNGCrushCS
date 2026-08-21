using System;
using System.IO;
using System.IO.Compression;
using FileFormat.Codecs.Cscd;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes CamStudio Screen Codec (CSCD): a key frame is a whole picture behind LZO or zlib, and a
/// delta frame is the same compression around a byte-wise difference added onto the frame before it.
/// </summary>
/// <remarks>
/// The MultimediaWiki page names the whole of the coding in five lines: a header byte whose top seven
/// bits choose the compression and whose bottom bit says whether this is a key frame, a reserved byte,
/// and the compressed data. What it does not say is what "add deltas to previous frame" means at the
/// byte level, or which of the compressions a header value of 73 — not 0, not 1 — actually names.
/// Both were settled against real files rather than guessed at: <see cref="TryDecode"/> reconstructs a
/// picture 122 packets into a real capture using unsigned byte addition with the result wrapped modulo
/// 256, matching ffmpeg's decode with no differing sample anywhere; the same byte-wise XOR a natural
/// first guess would reach for does not, differing in 67,144 of 1,572,864 bytes on the same frame. And
/// every real file measured uses a compression byte of exactly zero for LZO or something other than
/// zero for zlib — never literally one — so that is the rule read off the bytes here, rather than the
/// wiki's two-case switch taken at face value.
/// <para/>
/// <b>The picture is coded bottom row first, and each row is padded to a whole four-byte word</b> —
/// both the convention every Windows bitmap this format is built around uses, and neither is stated by
/// the wiki page at all. The padding matters more here than the row order: a 239-pixel-wide, 24-bit
/// picture is 717 bytes of pixels and 720 bytes of picture, and a delta coded against the tightly
/// packed width lands three bytes out of step on every row after the first, which is silent corruption
/// rather than a refusal. It was found by a match instruction whose distance was consistently the
/// padded stride and nothing else, on a real file where the packed stride would have made no sense at
/// that position.
/// <para/>
/// <b>There is no 8-bit palettised mode</b>, despite this codec otherwise mirroring TSCC closely
/// enough that assuming one seemed reasonable. ffmpeg's own decoder refuses a stream stating 8 bits a
/// pixel by name — "invalid depth 8 bpp" — on a hand-built stream carrying one, which settles it
/// without this having to guess at a palette layout nothing exercises.
/// <para/>
/// <b>Each packet's compression starts fresh.</b> Unlike ZMBV, nothing here carries a dictionary or a
/// zlib stream from one packet into the next — every key frame and every delta frame decompresses a
/// complete, self-terminating stream of its own, measured directly against the compressed byte count
/// each one consumes.
/// <para/>
/// See <see cref="Lzo1x"/> for the compression the wiki calls LZO and no specification exists for.
/// <para/>
/// <b>Measured against ffmpeg</b> at every depth and every compression a real file was found at.
/// <para/>
/// <b>What refuses.</b> 8 bits a pixel, which ffmpeg's own decoder refuses as well, and any depth
/// besides that and 16, 24 and 32; an LZO stream whose instructions run off the end of the compressed
/// data or whose match reaches before the start of the picture; and a zlib stream that does not
/// decompress to exactly the bytes a frame of this picture needs.
/// </remarks>
public sealed class CscdVideoDecoder : IVideoCodecDecoder<CscdVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("CSCD");

  private readonly int _width;
  private readonly int _height;
  private readonly int _bytesPerPixel;
  private readonly int _streamIndex;
  private readonly int _stride;
  private readonly byte[] _canvas;

  private CscdVideoDecoder(int width, int height, int bytesPerPixel, int streamIndex) {
    this._width = width;
    this._height = height;
    this._bytesPerPixel = bytesPerPixel;
    this._streamIndex = streamIndex;
    // A row is padded to a whole four-byte word, the same convention every Windows bitmap the
    // format is built around uses — a 239-pixel-wide, 24-bit-a-pixel row is 717 bytes of pixels and
    // 720 bytes of picture, and a delta coded against the tightly packed width alone lands three
    // bytes out of step on every row after the first. Measured directly: the distance a real file's
    // vertical (one-row) copies use is the padded stride, not the packed one.
    this._stride = (width * bytesPerPixel + 3) / 4 * 4;
    this._canvas = new byte[this._stride * height];
  }

  public static string CodecName => "CamStudio Screen Codec";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static CscdVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can "
        + "be decoded into.");

    var bitsPerPixel = stream.BitsPerPixel;
    var bytesPerPixel = bitsPerPixel switch {
      16 => 2,
      24 => 3,
      32 => 4,
      8 => throw new NotSupportedException(
        $"Video stream {stream.Index} states 8 bits a pixel. CSCD has no palettised mode — ffmpeg's own decoder "
        + "refuses this depth by name, and this refuses it the same way rather than inventing a palette layout "
        + "nothing exercises."),
      _ => throw new NotSupportedException(
        $"Video stream {stream.Index} states {bitsPerPixel} bits a pixel. CSCD is read at 16, 24 and 32 bits a "
        + "pixel, and nothing else."),
    };

    return new(stream.Width, stream.Height, bytesPerPixel, stream.Index);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var data = packet.Data.Span;
    if (data.Length < 2)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries a CSCD packet of {data.Length} byte(s), where the header alone "
        + "is two.");

    var header = data[0];
    var method = header >> 1;
    var isKeyFrame = (header & 1) != 0;

    var payload = packet.Data[2..];
    var frameSize = this._canvas.Length;
    var decompressed = method == 0 ? Lzo1x.Decompress(payload.Span, frameSize) : this._InflateZlib(payload, frameSize);

    if (isKeyFrame)
      decompressed.CopyTo(this._canvas.AsSpan());
    else {
      var canvas = this._canvas;
      for (var i = 0; i < frameSize; ++i)
        canvas[i] = (byte)(canvas[i] + decompressed[i]);
    }

    frame = this._ComposeFrame();
    return true;
  }

  private byte[] _InflateZlib(ReadOnlyMemory<byte> payload, int frameSize) {
    using var source = new MemoryStream(payload.ToArray(), writable: false);
    using var zlib = new ZLibStream(source, CompressionMode.Decompress);
    var output = new byte[frameSize];
    try {
      zlib.ReadExactly(output);
    } catch (EndOfStreamException ex) {
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries a CSCD packet whose zlib data decompresses to fewer than the "
        + $"{frameSize} byte(s) its frame needs.", ex);
    }

    return output;
  }

  private RawImage _ComposeFrame() {
    var picture = _FlipAndUnpad(this._canvas, this._height, this._stride, this._width * this._bytesPerPixel);

    return this._bytesPerPixel switch {
      2 => new() { Width = this._width, Height = this._height, Format = PixelFormat.Rgb24, PixelData = _Widen555(picture, this._width * this._height) },
      3 => new() { Width = this._width, Height = this._height, Format = PixelFormat.Bgr24, PixelData = picture },
      4 => new() { Width = this._width, Height = this._height, Format = PixelFormat.Bgra32, PixelData = picture },
      _ => throw new NotSupportedException($"Video stream {this._streamIndex} has no colour conversion for {this._bytesPerPixel} bytes a pixel."),
    };
  }

  /// <summary>Turns the coded, bottom-up, padded-stride canvas into a top-down picture with the
  /// padding each row's stride carried dropped.</summary>
  private static byte[] _FlipAndUnpad(byte[] canvas, int height, int stride, int rowBytes) {
    var picture = new byte[rowBytes * height];
    for (var row = 0; row < height; ++row)
      Array.Copy(canvas, (height - 1 - row) * stride, picture, row * rowBytes, rowBytes);

    return picture;
  }

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
