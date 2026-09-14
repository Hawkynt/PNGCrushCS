using System;
using FileFormat.Codecs.CineForm;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes GoPro CineForm (<c>CFHD</c>): a wavelet, intra-only codec whose every frame is a whole
/// picture.
/// </summary>
/// <remarks>
/// The elementary wavelet model follows SMPTE ST 2073-1:2017 (<i>VC-5 Video Essence — Part 1:
/// Elementary Bitstream</i>). Real CFHD files predate that cleaned-up standard and carry additional
/// CineForm tags and conventions; those were cross-checked against GoPro's dual MIT/Apache-2.0 SDK and
/// FFmpeg's LGPL <c>cfhd</c> implementation where the public standard does not define them. The measured
/// framing details are documented in <see cref="FileFormat.Codecs.CineForm.CineFormChannelDecoder"/>.
/// <para/>
/// <b>Intra only.</b> CineForm's ordinary CFHD picture path has no P/B pictures or temporal motion
/// references: each packet reconstructs independently from its own spatial wavelet coefficients.
/// <para/>
/// <b>Scope.</b> The decoder reads all three layouts FFmpeg's current <c>cfhd</c> encoder writes:
/// ten-bit YUV 4:2:2, twelve-bit RGB 4:4:4, and twelve-bit RGBA 4:4:4:4. The latter includes the
/// format's alpha companding step rather than treating channel four as linear colour data. Bayer/CFA
/// and the separate layered/interlaced VC-5 extensions are refused by name rather than guessed at.
/// </remarks>
public sealed class CineFormVideoDecoder : IVideoCodecDecoder<CineFormVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("CFHD");

  private readonly int _width;
  private readonly int _height;

  private CineFormVideoDecoder(int width, int height) {
    this._width = width;
    this._height = height;
  }

  public static string CodecName => "GoPro CineForm";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static CineFormVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can be decoded into.");

    return new(stream.Width, stream.Height);
  }

  /// <summary>Decodes one frame, which for this codec is always exactly one whole picture.</summary>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var channels = this.DecodeChannels(packet.Data);

    frame = channels.HasAlpha
      ? new() {
        Width = channels.ImageWidth,
        Height = channels.ImageHeight,
        Format = PixelFormat.Rgba32,
        PixelData = CineFormColorConversion.RgbaToRgba32(channels),
      }
      : new() {
        Width = channels.ImageWidth,
        Height = channels.ImageHeight,
        Format = PixelFormat.Rgb24,
        PixelData = channels.IsYuv
          ? CineFormColorConversion.YuvToRgb24(channels)
          : CineFormColorConversion.RgbToRgb24(channels),
      };

    return true;
  }

  /// <summary>
  /// Decodes one frame as far as its component channels, before any narrowing or colour conversion.
  /// </summary>
  /// <remarks>
  /// This is where a comparison against another decoder has to be made — see
  /// <see cref="CineFormChannelDecoder"/>'s remarks for what was measured and how. Narrowing to eight
  /// bits and choosing a colour matrix are display conventions in <see cref="CineFormColorConversion"/>
  /// that a comparison on the channels themselves never has to touch.
  /// </remarks>
  internal CineFormPictureDecoder.Result DecodeChannels(ReadOnlyMemory<byte> packet)
    => CineFormPictureDecoder.Decode(packet);
}
