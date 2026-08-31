using System;
using FileFormat.Codecs.CineForm;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes GoPro CineForm (<c>CFHD</c>): a wavelet, intra-only codec whose every frame is a whole
/// picture.
/// </summary>
/// <remarks>
/// Written from the free part of its specification, SMPTE ST 2073-1:2017 (<i>VC-5 Video Essence —
/// Part 1: Elementary Bitstream</i>) — GoPro's own SDK documentation describes VC-5 as a "superset" of
/// the original CineForm compression engine, standardised and better defined; what that means in
/// practice, and what real files carry beyond what the free standard states, is in
/// <see cref="FileFormat.Codecs.CineForm.CineFormChannelDecoder"/>'s remarks. Nothing here is derived
/// from GoPro's SDK source or from ffmpeg's <c>cfhd</c> decoder — both are used, if at all, only as a
/// black-box oracle on their output.
/// <para/>
/// <b>Intra only</b>, like every other codec of this shape in this library: no reference handling, no
/// state carried between packets beyond the stream's declared dimensions.
/// <para/>
/// <b>Scope.</b> ffmpeg's own <c>cfhd</c> encoder writes exactly three pixel formats —
/// <c>yuv422p10le</c>, <c>gbrp12le</c> and <c>gbrap12le</c> — and this decoder reads the two of them
/// without alpha: ten-bit 4:2:2 YUV and twelve-bit RGB, three channels each, both confirmed against
/// real encoded files. A frame stating any other channel count, including the alpha-bearing
/// <c>gbrap12le</c> layout, is refused by name: alpha's channel position was never measured against a
/// real file, and guessing at it would risk exactly the wrong-picture-that-looks-right failure this
/// library refuses to ship.
/// </remarks>
public sealed class CineFormVideoDecoder : IVideoCodecDecoder<CineFormVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("CFHD");

  private readonly int _width;
  private readonly int _height;

  private CineFormVideoDecoder(int width, int height) {
    this._width = width;
    this._height = height;
  }

  /// <summary>Gets the codec name.</summary>
  public static string CodecName => "GoPro CineForm";

  /// <summary>Determines whether the specified media stream is supported.</summary>
  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  /// <summary>Creates a decoder for the specified media stream.</summary>
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

    frame = new() {
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
