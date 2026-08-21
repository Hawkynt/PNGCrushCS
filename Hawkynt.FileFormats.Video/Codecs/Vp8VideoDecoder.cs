using System;
using FileFormat.Codecs.Vp8;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes VP8 video, RFC 6386.
/// </summary>
/// <remarks>
/// The codec WebM was built around, and the one every <c>V_VP8</c> track in a Matroska document is
/// coded with. Everything the format has is here: the boolean entropy decoder, segmentation, both
/// loop filters, the four token partitions' worth of coefficient coding, all fourteen intra
/// prediction modes, prediction from any of the three reference frames with the six-tap and
/// bilinear sub-pixel filters, and the probability state that carries from one frame to the next.
/// <para/>
/// <b>What it does not do refuses by name.</b> A bitstream version RFC 6386 reserves, a key frame
/// that sets the reserved colour space or clamping fields, a stream that begins at an interframe, a
/// truncated packet, a partition table that does not fit — each of those throws and says which field
/// was wrong. There is no <c>catch</c> anywhere that hands back a blank frame or repeats the last
/// one, because a picture that looks like a picture is not checked by anybody.
/// <para/>
/// <b>Measured.</b> Every frame of every test stream was decoded here and by ffmpeg and compared
/// plane by plane, sample by sample. The planes are identical: not close, not on average, the same
/// bytes. That is the only acceptable result for a lossy codec, because the loss happened in the
/// encoder and both decoders are reading the same bitstream — and it is the measurement that matters,
/// because an error in the loop filter or in prediction would show up as a small difference
/// everywhere that grows with every frame until the next key frame.
/// <para/>
/// The RGB this hands back differs from ffmpeg's at colour edges, and only there. That is the
/// chrominance interpolation described in <see cref="Vp8ColorConversion"/>, which is a display
/// convention rather than part of the decode.
/// </remarks>
public sealed class Vp8VideoDecoder : IVideoCodecDecoder<Vp8VideoDecoder> {

  /// <summary>What Matroska and WebM call this codec, which is a string because Matroska has no code field.</summary>
  private const string _MATROSKA_CODEC_ID = "V_VP8";

  /// <summary>The four-character codes containers with a code field name VP8 with.</summary>
  private static readonly CodecTag[] _Tags = [
    CodecTag.FromCharacters("VP80"),
    CodecTag.FromCharacters("vp08"),
  ];

  private readonly Vp8Decoder _decoder = new();

  public static string CodecName => "VP8 (RFC 6386)";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      return false;

    if (string.Equals(stream.CodecId, _MATROSKA_CODEC_ID, StringComparison.OrdinalIgnoreCase))
      return true;

    foreach (var tag in _Tags)
      if (stream.Codec.EqualsIgnoringCase(tag))
        return true;

    return false;
  }

  /// <summary>
  /// Builds a decoder for one stream.
  /// </summary>
  /// <remarks>
  /// Nothing is read from the stream description, not even the picture size. VP8 states it in the
  /// key frame, and where a container's copy disagrees it is the container's copy that is wrong.
  /// </remarks>
  public static Vp8VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return new();
  }

  /// <summary>
  /// Decodes one packet, which for VP8 is exactly one coded frame.
  /// </summary>
  /// <returns>
  /// <c>false</c> when the frame is one the stream asked not to be shown. VP8 can carry a frame that
  /// exists only to become a reference for later ones — an alternate reference built from several
  /// frames at once, say — and handing that back would put a picture on screen that the film does not
  /// contain.
  /// </returns>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    if (!this._decoder.Decode(packet.Data.Span, out var picture)) {
      frame = null!;
      return false;
    }

    frame = new() {
      Width = this._decoder.Width,
      Height = this._decoder.Height,
      Format = PixelFormat.Rgb24,
      PixelData = Vp8ColorConversion.ToRgb24(picture, this._decoder.Width, this._decoder.Height),
    };

    return true;
  }
}
