using System;
using System.IO;
using System.IO.Compression;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes ZeroCodec: a delta coding carried entirely inside a zlib stream. Every packet is one
/// complete, independently checksummed zlib stream that inflates to exactly one picture's worth of
/// bytes, and a decompressed byte of zero means "the byte already held at this position is unchanged"
/// while any other value is the literal new byte. There is no header of its own — a packet's whole
/// payload is that zlib stream, from its first byte to its last.
/// </summary>
/// <remarks>
/// <b>No specification exists.</b> The community write-up on MultimediaWiki states only that the codec
/// performs "difference processing" and reads and writes RGB, YUY2 and UYVY; it gives no frame layout,
/// no byte order and no rule for what the difference actually is. Everything below was established
/// here by decompressing real packets and comparing the result against ffmpeg's own decode of the same
/// file — samples.ffmpeg.org carries exactly one ZeroCodec recording, <c>sample-zeco.avi</c>, and there
/// is no encoder to build a corpus with.
/// <para/>
/// <b>The delta rule needs no notion of a key frame at all, and none is read.</b> Every packet — the
/// container's own large, full-picture ones included — decompresses to the picture's full byte count
/// and is merged into the picture already held by the same single rule: a decompressed byte of zero
/// leaves the byte already there, anything else replaces it. The very first packet a decoder ever sees
/// comes out identical to a literal copy under this rule, because the picture "already held" before
/// any packet has arrived is an all-zero buffer, and a decompressed zero at a position where nothing
/// has been written yet leaves a zero exactly where a literal reading would have put one; a packet
/// later in the stream that happens to carry a picture unrelated to the one before it — a scene cut —
/// decompresses to bytes that are almost all nonzero and so is carried by the very same rule without
/// anything having to say which packets those are. Measured directly against the one sample available:
/// applying this one rule, with no container flag consulted anywhere, reproduces all 38 of its frames
/// byte for byte, including three packets partway through the stream an order of magnitude larger than
/// the ones around them.
/// <para/>
/// One consequence of the rule is worth stating plainly rather than leaving implicit: a sample whose
/// true new value is exactly zero at a position whose previous value was not can only be written by
/// this scheme in the one case that does not matter — where the previous value already reads as
/// unchanged. Nothing in the format works around that, and nothing found in the one sample measured
/// here needed it to.
/// <para/>
/// <b>The picture is stored bottom row first</b>, the Windows DIB convention this package's other AVI
/// codecs already carry. Found by decompressing the first packet — exactly the stream's picture size —
/// and finding it a mirror image of ffmpeg's own first frame until the rows are reversed.
/// <para/>
/// <b>The one pixel layout measured</b> is sixteen bits a pixel, packed 4:2:2 with the byte order U,
/// Y, V, Y — matching ffmpeg's own report of <c>uyvy422</c> for the sample, and the only
/// <c>biBitCount</c> any file reaching this decoder has stated. The community page's other two forms,
/// full RGB and the reverse YUY2 packing, have no sample here to measure a byte layout against and are
/// refused rather than guessed at.
/// <para/>
/// <b>Measured against ffmpeg</b> on the packed samples themselves, not through an RGB conversion —
/// this is a 4:2:2 format, so the same chroma-siting ambiguity this package's other subsampled codecs
/// are compared plane by plane to avoid applies here too, and a comparison through RGB would not mean
/// what it looked like it meant. One file, 38 frames, 1280x720: every one of the 70,041,600 bytes
/// ffmpeg's own decode produces (<c>ffmpeg -threads 1 -i sample-zeco.avi -fps_mode passthrough -f
/// rawvideo -pix_fmt uyvy422</c>) is reproduced exactly, frame by frame, with no drift across the run.
/// The RGB picture <see cref="TryDecode"/> hands back converts with BT.601 coefficients, assumed rather
/// than measured — there is nothing in the one sample available to read a colour-space choice off —
/// and repeats each chroma pair across both of its luma samples rather than interpolating, a display
/// convenience the comparison above is not measured through and does not depend on.
/// <para/>
/// <b>What refuses.</b> A picture whose width is odd, since two luma samples share one chroma pair and
/// an odd width leaves the last sample with none; a depth other than sixteen bits, for want of a
/// second sample to measure any other packing against; and a packet whose zlib stream is truncated,
/// corrupt, or does not inflate to exactly the picture's own byte count.
/// </remarks>
public sealed class ZeroCodecVideoDecoder : IVideoCodecDecoder<ZeroCodecVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("ZECO");

  private readonly int _width;
  private readonly int _height;
  private readonly int _streamIndex;

  /// <summary>The picture as coded, bottom row first, kept between packets because every packet's
  /// zero bytes are read against it.</summary>
  private readonly byte[] _canvas;

  private ZeroCodecVideoDecoder(int width, int height, int streamIndex) {
    this._width = width;
    this._height = height;
    this._streamIndex = streamIndex;
    this._canvas = new byte[width * height * 2];
  }

  public static string CodecName => "ZeroCodec";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static ZeroCodecVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can "
        + "be decoded into.");

    if ((stream.Width & 1) != 0)
      throw new NotSupportedException(
        $"Video stream {stream.Index} states a width of {stream.Width}. ZeroCodec's one measured pixel layout "
        + "packs two luma samples to one chroma pair, so an odd width would leave the last sample with none.");

    if (stream.BitsPerPixel != 16)
      throw new NotSupportedException(
        $"Video stream {stream.Index} states {stream.BitsPerPixel} bits a pixel. The only ZeroCodec sample this "
        + "was measured against states 16 (packed 4:2:2), so nothing else is read.");

    return new(stream.Width, stream.Height, stream.Index);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var frameBytes = this._canvas.Length;
    var decoded = new byte[frameBytes];

    try {
      using var source = new MemoryStream(packet.Data.ToArray());
      using var zlib = new ZLibStream(source, CompressionMode.Decompress);
      zlib.ReadExactly(decoded);
    } catch (Exception ex) when (ex is EndOfStreamException or InvalidDataException) {
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries a ZeroCodec packet whose zlib stream does not inflate to the "
        + $"{frameBytes} byte(s) its picture needs.", ex);
    }

    var canvas = this._canvas;
    for (var i = 0; i < frameBytes; ++i) {
      var value = decoded[i];
      if (value != 0)
        canvas[i] = value;
    }

    frame = this._ComposeFrame();
    return true;
  }

  // ============================================================================================
  // What comes out
  // ============================================================================================

  private RawImage _ComposeFrame() {
    var stride = this._width * 2;
    var upright = _FlipVertically(this._canvas, this._height, stride);

    return new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Rgb24,
      PixelData = _UyvyToRgb24(upright, this._width, this._height),
    };
  }

  /// <summary>Turns the coded, bottom-up picture the right way up.</summary>
  private static byte[] _FlipVertically(byte[] canvas, int height, int stride) {
    var picture = new byte[canvas.Length];
    for (var row = 0; row < height; ++row)
      Array.Copy(canvas, (height - 1 - row) * stride, picture, row * stride, stride);

    return picture;
  }

  /// <summary>
  /// Converts packed 4:2:2 samples in U, Y, V, Y order to RGB24 with BT.601 coefficients, repeating
  /// each pair's chroma sample across both of its luma samples.
  /// </summary>
  private static byte[] _UyvyToRgb24(byte[] uyvy, int width, int height) {
    var rgb = new byte[width * height * 3];
    var pairs = width / 2;

    for (var y = 0; y < height; ++y) {
      var rowIn = y * width * 2;
      var rowOut = y * width * 3;

      for (var p = 0; p < pairs; ++p) {
        var at = rowIn + p * 4;
        var u = uyvy[at];
        var y0 = uyvy[at + 1];
        var v = uyvy[at + 2];
        var y1 = uyvy[at + 3];

        var outAt = rowOut + p * 6;
        _WritePixel(rgb, outAt, y0, u, v);
        _WritePixel(rgb, outAt + 3, y1, u, v);
      }
    }

    return rgb;
  }

  private static void _WritePixel(byte[] rgb, int at, byte y, byte u, byte v) {
    var c = y - 16;
    var d = u - 128;
    var e = v - 128;

    rgb[at] = _Clamp((298 * c + 409 * e + 128) >> 8);
    rgb[at + 1] = _Clamp((298 * c - 100 * d - 208 * e + 128) >> 8);
    rgb[at + 2] = _Clamp((298 * c + 516 * d + 128) >> 8);
  }

  private static byte _Clamp(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
}
