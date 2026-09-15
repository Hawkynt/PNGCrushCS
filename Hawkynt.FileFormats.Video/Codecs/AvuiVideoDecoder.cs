using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Avid Meridien Uncompressed (AVUI): its run-blanked UYVY 4:2:2 fields and optional inverted
/// alpha companion.
/// </summary>
/// <remarks>
/// AVUI is fixed to the two D1 geometries Avid documents, 720x486 and 720x576. The APRG private-data
/// atom says whether rows are progressive or split into two fields; older descriptions with no APRG
/// are treated as interlaced, matching the long-standing reference-decoder behaviour. NTSC interlace
/// stores the odd field first while PAL stores the even field first.
/// <para/>
/// A depth-32 sample carries a second field-laid-out plane after the UYVY data. Every other byte is an
/// inverted alpha value, which is why this decoder returns RGBA32 for that variant and RGB24 for the
/// ordinary depth-16 one. Colour is interpreted in the 601/709 legal range Avid prescribes; at these
/// SD geometries the shared conversion path uses BT.601 limited range.
/// <para/>
/// There is no MultimediaWiki page for this one and no published layout, so the byte offsets were
/// recovered rather than read: pseudo-random <c>uyvy422</c> content carried through ffmpeg's own avui
/// encoder, swept against every placement of the blanking runs ahead of, between and behind the
/// picture data, keeping the one that reproduces every sample. That sweep is what the field offsets
/// here and in <see cref="AvuiVideoEncoder"/> encode, and the two agree with ffmpeg's own encoder byte
/// for byte at both geometries in both field modes.
/// </remarks>
public sealed class AvuiVideoDecoder : IVideoCodecDecoder<AvuiVideoDecoder> {

  private static readonly CodecTag _AVUI = CodecTag.FromCharacters("AVUI");

  private readonly AvuiVideoFormat _layout;
  private readonly int _depth;

  private AvuiVideoDecoder(AvuiVideoFormat layout, int depth) {
    this._layout = layout;
    this._depth = depth;
  }

  public static string CodecName => "Avid Meridien Uncompressed";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_AVUI);
  }

  public static AvuiVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (!Accepts(stream))
      throw new NotSupportedException($"Stream codec '{stream.Codec}' is not Avid Meridien Uncompressed ('AVUI').");

    var layout = AvuiVideoFormat.For(stream, defaultInterlaced: true);
    var describedDepth = AvuiVideoFormat.SampleDepth(stream.CodecPrivateData.Span);
    var depth = stream.BitsPerPixel != 0 ? stream.BitsPerPixel : describedDepth;
    if (depth == 0)
      depth = AvuiVideoFormat.OpaqueDepth;
    if (depth is not (AvuiVideoFormat.OpaqueDepth or AvuiVideoFormat.AlphaDepth))
      throw new NotSupportedException(
        $"Avid Meridien Uncompressed samples are 16-bit UYVY or 32-bit when alpha is present; the stream declares {depth} bits per pixel.");

    return new(layout, depth);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var (y, cb, cr, alpha) = this.DecodePlanesWithAlpha(packet.Data.Span);
    var chroma = checked((AvuiVideoFormat.Width / 2) * this._layout.Height);
    var pixels = new byte[checked(y.Length + 2 * chroma)];
    y.CopyTo(pixels, 0);
    cb.CopyTo(pixels, y.Length);
    cr.CopyTo(pixels, y.Length + chroma);

    var yuv = new RawImage {
      Width = AvuiVideoFormat.Width,
      Height = this._layout.Height,
      Format = PixelFormat.Yuv422P8,
      PixelData = pixels,
      ColorInfo = RawImageColorInfo.Bt601Limited,
    };

    if (alpha == null) {
      frame = FastRawImageConverter.Convert(yuv, PixelFormat.Rgb24);
      return true;
    }

    var rgba = FastRawImageConverter.Convert(yuv, PixelFormat.Rgba32);
    for (var i = 0; i < alpha.Length; ++i)
      rgba.PixelData[i * 4 + 3] = alpha[i];

    frame = rgba;
    return true;
  }

  /// <summary>Decodes just the Y, Cb and Cr planes, preserving the codec-level helper used by tests.</summary>
  internal (byte[] Y, byte[] Cb, byte[] Cr) DecodePlanes(ReadOnlySpan<byte> data) {
    var (y, cb, cr, _) = this.DecodePlanesWithAlpha(data);
    return (y, cb, cr);
  }

  /// <summary>Unpacks the field order, UYVY pairs and, at depth 32, the inverted alpha companion.</summary>
  internal (byte[] Y, byte[] Cb, byte[] Cr, byte[]? Alpha) DecodePlanesWithAlpha(ReadOnlySpan<byte> data) {
    var opaqueLength = this._layout.OpaqueLength;
    if (data.Length < opaqueLength)
      throw new InvalidDataException(
        $"A {AvuiVideoFormat.Width}x{this._layout.Height} AVUI packet in this field mode needs at least {opaqueLength} byte(s); "
        + $"the packet contains {data.Length} byte(s).");

    var alphaLength = this._depth == AvuiVideoFormat.AlphaDepth ? this._layout.AlphaPacketLength : 0;
    if (alphaLength != 0 && data.Length < alphaLength)
      throw new InvalidDataException(
        $"A depth-32 {AvuiVideoFormat.Width}x{this._layout.Height} AVUI packet needs {alphaLength} byte(s) for colour and alpha; "
        + $"the packet contains {data.Length} byte(s).");

    var luma = new byte[checked(AvuiVideoFormat.Width * this._layout.Height)];
    var chroma = new byte[checked((AvuiVideoFormat.Width / 2) * this._layout.Height)];
    var cb = new byte[chroma.Length];
    var cr = new byte[chroma.Length];
    var alpha = alphaLength == 0 ? null : new byte[luma.Length];

    for (var field = 0; field < this._layout.Fields; ++field) {
      var source = this._layout.FieldOffset(field);
      var alphaSource = alpha == null ? 0 : this._layout.AlphaFieldOffset(field);

      for (var row = this._layout.FirstRow(field); row < this._layout.Height; row += this._layout.RowStep) {
        var yAt = checked(row * AvuiVideoFormat.Width);
        var cAt = checked(row * (AvuiVideoFormat.Width / 2));

        for (var pair = 0; pair < AvuiVideoFormat.Width / 2; ++pair) {
          cb[cAt + pair] = data[source++];
          luma[yAt + pair * 2] = data[source++];
          cr[cAt + pair] = data[source++];
          luma[yAt + pair * 2 + 1] = data[source++];

          if (alpha != null) {
            alpha[yAt + pair * 2] = (byte)(byte.MaxValue - data[alphaSource]);
            alphaSource += 2;
            alpha[yAt + pair * 2 + 1] = (byte)(byte.MaxValue - data[alphaSource]);
            alphaSource += 2;
          }
        }
      }
    }

    return (luma, cb, cr, alpha);
  }
}
