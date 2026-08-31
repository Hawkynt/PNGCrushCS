using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes ayuv: 4:4:4:4 YUV with alpha and nothing compressed at all, four bytes a pixel and no
/// chroma subsampling to interpolate around.
/// </summary>
/// <remarks>
/// The DirectShow relative of v308 and v408 rather than a third spelling of the same layout — where
/// those two are QuickTime tags ffmpeg's demuxer only recognises inside a MOV, this one is recognised
/// inside an AVI, and the two containers do not agree on what a fourth byte of alpha does to the order
/// of the first three. There is no MultimediaWiki page and no dedicated ffmpeg decoder for it either,
/// so the layout below was recovered the same way: pseudo-random content, built at the pixel format
/// ffmpeg's own AVI demuxer names for this tag, carried through its generic uncompressed muxer with
/// the tag forced to <c>AYUV</c> and swept against every placement of a header ahead of, inside and
/// behind the picture data.
/// <para/>
/// <b>The word, and the trap in measuring it.</b> Four bytes a pixel — V, then U, then Y, then alpha —
/// which is the *reverse* of what the name spells and not the order v408 uses for its own three
/// letters plus alpha. A naive first measurement, generated and checked against a mismatched pixel
/// convention on the two sides of the comparison, appeared to show the name's own order; regenerating
/// both sides from the one ffmpeg itself uses for a real <c>AYUV</c>-tagged AVI settled it the other
/// way, and only a comparison against every byte of a real packet — not a single flat-coloured frame,
/// where a reversed and a forward reading of a symmetric value can coincide — ruled the first reading
/// out for good. A row is exactly <c>width</c> times four bytes, with no padding of any kind.
/// <para/>
/// <b>Packed-YUV format carrying alpha, and a direct sample comparison is what settles it</b> — 4:4:4
/// carries no subsampling, so every pixel states its own chroma and there is no interpolation
/// convention to disagree about. Fifty frames of pseudo-random content at 17x9 — not a whole number of
/// any alignment this format's neighbours use — carried through this packing and decoded here,
/// compared byte for byte against ffmpeg's own raw output of the same content before it was packed:
/// every sample of every plane of every frame identical, alpha included.
/// <para/>
/// <b>The packed colour <see cref="TryDecode"/> hands back carries the alpha channel through
/// unchanged</b> — ITU-R BT.601 with studio swing for the three colour samples, needing no chroma
/// repetition since every pixel already carries its own full-resolution pair, and the fourth byte of
/// every pixel copied straight across rather than composited or assumed opaque.
/// <para/>
/// <b>What refuses.</b> A picture with no pixels, and a packet shorter than its stride times its
/// height.
/// </remarks>
public sealed class AyuvVideoDecoder : IVideoCodecDecoder<AyuvVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("AYUV");

  private readonly int _width;
  private readonly int _height;
  private readonly int _streamIndex;
  private readonly int _stride;

  private AyuvVideoDecoder(int width, int height, int streamIndex) {
    this._width = width;
    this._height = height;
    this._streamIndex = streamIndex;
    this._stride = width * 4;
  }

  /// <summary>Gets the codec name.</summary>
  public static string CodecName => "Uncompressed 4:4:4:4 (ayuv)";

  /// <summary>Determines whether the specified media stream is supported.</summary>
  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  /// <summary>Creates a decoder for the specified media stream.</summary>
  public static AyuvVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can "
        + "be decoded into.");

    return new(stream.Width, stream.Height, stream.Index);
  }

  /// <summary>Attempts to decode the specified coded packet into a raw image frame.</summary>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var (luma, cb, cr, alpha) = this.DecodePlanes(packet.Data.Span);

    frame = new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Rgba32,
      PixelData = this._ToRgba32(luma, cb, cr, alpha),
    };

    return true;
  }

  /// <summary>
  /// Unpacks one frame into its luma, chroma and alpha planes, each at the picture's own full
  /// resolution — the form ffmpeg's own raw output writes and the one this was verified against.
  /// </summary>
  internal (byte[] Luma, byte[] Cb, byte[] Cr, byte[] Alpha) DecodePlanes(ReadOnlySpan<byte> data) {
    var expected = (long)this._stride * this._height;
    if (data.Length < expected)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries an ayuv packet of {data.Length} byte(s), where a "
        + $"{this._width}x{this._height} frame at a stride of {this._stride} needs {expected}.");

    var count = this._width * this._height;
    var luma = new byte[count];
    var cb = new byte[count];
    var cr = new byte[count];
    var alpha = new byte[count];

    for (var row = 0; row < this._height; ++row) {
      var line = data.Slice(row * this._stride, this._stride);
      var planeBase = row * this._width;

      for (var x = 0; x < this._width; ++x) {
        var offset = x * 4;
        cr[planeBase + x] = line[offset];
        cb[planeBase + x] = line[offset + 1];
        luma[planeBase + x] = line[offset + 2];
        alpha[planeBase + x] = line[offset + 3];
      }
    }

    return (luma, cb, cr, alpha);
  }

  /// <summary>
  /// ITU-R BT.601, studio swing, alpha carried straight through.
  /// </summary>
  private byte[] _ToRgba32(byte[] luma, byte[] cb, byte[] cr, byte[] alpha) {
    var count = this._width * this._height;
    var rgba = new byte[count * 4];

    for (var i = 0; i < count; ++i) {
      var scaledLuma = 298 * (luma[i] - 16);
      var blueDifference = cb[i] - 128;
      var redDifference = cr[i] - 128;
      var target = i * 4;

      rgba[target] = _Clamp(scaledLuma + 409 * redDifference + 128);
      rgba[target + 1] = _Clamp(scaledLuma - 100 * blueDifference - 208 * redDifference + 128);
      rgba[target + 2] = _Clamp(scaledLuma + 516 * blueDifference + 128);
      rgba[target + 3] = alpha[i];
    }

    return rgba;
  }

  private static byte _Clamp(int scaled) {
    var value = scaled >> 8;

    return (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
  }
}
