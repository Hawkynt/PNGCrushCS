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
/// <b>Intra only.</b> CineForm has no MPEG-style P/B pictures or motion references here. Progressive
/// samples use three spatial wavelet levels. Legacy interlaced YUV remains one independently decodable
/// picture and uses a horizontal/field-pair transform at the finest level followed by the same two
/// coarser spatial levels.
/// <para/>
/// <b>Scope.</b> The decoder reads ten-bit YUV 4:2:2 progressive and legacy interlaced samples, plus
/// twelve-bit RGB 4:4:4, RGBA 4:4:4:4 and Bayer RAW progressive samples. Bayer remains raw: its four
/// decorrelated half-resolution channels are rebuilt into <see cref="PixelFormat.Cfa16"/> RGGB sensor
/// samples and are not demosaiced into display RGB. The separate 14/17-subband field/field-plus
/// transforms, and the layered, stereoscopic and legacy 3-D wavelet organizations, remain distinct
/// extensions outside this single-picture decoder and are refused explicitly rather than silently
/// interpreted as the ten-subband picture layout.
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

    if (channels.IsBayer) {
      frame = CineFormBayerConversion.ToRggb12(channels);
      return true;
    }

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
  /// Decodes one frame as far as its component channels, before any narrowing, CFA reconstruction or
  /// display colour conversion.
  /// </summary>
  /// <remarks>
  /// This is where a comparison against another decoder has to be made — see
  /// <see cref="CineFormChannelDecoder"/>'s remarks for what was measured and how. Narrowing to eight
  /// bits, choosing a colour matrix, or rebuilding a Bayer mosaic are representation steps that a
  /// comparison on the coded channels themselves never has to touch.
  /// </remarks>
  internal CineFormPictureDecoder.Result DecodeChannels(ReadOnlyMemory<byte> packet)
    => CineFormPictureDecoder.Decode(packet);
}
