using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Motion JPEG: each packet is a whole JPEG, <c>FF D8</c> through <c>FF D9</c>.
/// </summary>
/// <remarks>
/// The entire codec is "hand the packet to the JPEG reader". That is not an oversight — Motion JPEG
/// genuinely is a sequence of independent JPEGs, with no prediction between them and no state
/// carried across, which is why cameras can write one a frame at a time.
/// <para/>
/// It is worth having as a codec rather than as a special case inside a container precisely because
/// it appears in more than one: an <c>MJPG</c> AVI's packets are Motion JPEG and so is the whole of a
/// raw <c>.mjpg</c> stream, and both now reach this one decoder without either container knowing
/// about JPEG or about each other.
/// </remarks>
public sealed class MotionJpegDecoder : IVideoCodecDecoder<MotionJpegDecoder> {

  /// <summary><c>MJPG</c> as it sits in a little-endian four-character code field.</summary>
  private static readonly CodecTag _MJPG = CodecTag.FromCharacters("MJPG");

  public static string CodecName => "Motion JPEG";

  /// <summary>
  /// Takes a stream tagged <c>MJPG</c> in either case.
  /// </summary>
  /// <remarks>
  /// ffprobe reads a container patched from <c>MJPG</c> to <c>mjpg</c> as the same codec with the
  /// same frame count, so a decoder that took only one spelling would refuse a file every other tool
  /// plays.
  /// </remarks>
  public static bool Accepts(MediaStreamInfo stream) {
    System.ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_MJPG);
  }

  public static MotionJpegDecoder Create(MediaStreamInfo stream) {
    System.ArgumentNullException.ThrowIfNull(stream);

    return new();
  }

  /// <summary>
  /// Decodes one packet, which for this codec always produces exactly one picture.
  /// </summary>
  /// <remarks>
  /// The dimensions the container declared are deliberately not checked against the ones the JPEG
  /// states. Where the two disagree it is the JPEG that is right — it is the thing that was actually
  /// coded — and a reader that trusted the container's copy would hand back a picture cut to a size
  /// nothing in the file has.
  /// </remarks>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    frame = JpegFile.ToRawImage(JpegReader.FromSpan(packet.Data.Span));
    return true;
  }
}
