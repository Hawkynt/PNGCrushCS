using System;
using FileFormat.Codecs.Indeo;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Intel Indeo Video Interactive 5, the last of the Indeo line.
/// </summary>
/// <remarks>
/// Everything the format has is here: the group, picture and band headers, the wavelet subdivision
/// into one band or four, tiling, the slant transform in two dimensions and in each one alone, the
/// band that stores samples untransformed, the four-by-four chrominance transform, run-value coded
/// coefficients with the nine built-in maps and the per-band permutations of them, the sixteen
/// built-in Huffman codebooks and the custom ones a stream may spell out, half-sample motion
/// compensation, motion vector and quantiser inheritance between bands, and the five-three wavelet
/// recomposition of a scalable picture.
/// <para/>
/// <b>Where this came from.</b> Indeo 5 transmits none of its tables. A band header names its
/// quantisation matrix by position, its scan by position and its codebook by a three-bit index, so a
/// two-byte frame is a complete frame and the matrices, scans, run-value maps and codebook
/// descriptors it depends on exist only in a decoder. This one's are FFmpeg's, whose
/// <c>libavcodec/indeo5.c</c>, <c>indeo5data.h</c>, <c>ivi.c</c>, <c>ivi.h</c>, <c>ivi_dsp.c</c> and
/// <c>ivi_dsp.h</c> are LGPL-2.1-or-later and so may be carried into this LGPL-3.0-or-later library.
/// <see cref="IviDecoder"/> and the files beside it are that code converted rather than wrapped, and
/// the notice next to them records it.
/// <para/>
/// <b>What it refuses.</b> A password-protected clip, whose frame data is scrambled with a key the
/// file does not carry. A clip stating the YV12 picture format or four-by-four luminance blocks —
/// both are in the format and neither was ever written by an encoder, so what they would produce has
/// never been observed. A band header carrying extended transform information, for the same reason.
/// Everything else is decoded, and a frame that cannot be read throws and says which field was wrong
/// rather than handing back a repeat of the last picture.
/// <para/>
/// <b>Measured.</b> Every frame of the sample clips was decoded here and by ffmpeg and compared
/// sample by sample over all three planes. The planes are identical. That is the only acceptable
/// result: both decoders read the same bitstream, and one sample of difference in a reference frame
/// would be added to every frame predicted from it until the next key frame.
/// </remarks>
public sealed class Indeo5VideoDecoder : IVideoCodecDecoder<Indeo5VideoDecoder> {

  /// <summary>The four-character code containers name Indeo 5 with.</summary>
  private static readonly CodecTag _Tag = CodecTag.FromCharacters("IV50");

  private readonly Indeo5Decoder _decoder;

  public static string CodecName => "Intel Indeo Video Interactive 5";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  /// <summary>
  /// Builds a decoder for one stream.
  /// </summary>
  /// <remarks>
  /// The container's picture size is what the decoder starts with, and the first key frame's group
  /// header replaces it with the one the stream itself states. A container stating no size at all is
  /// refused here rather than at the first frame, because the buffers a frame decodes into are laid
  /// out before any frame is read.
  /// </remarks>
  public static Indeo5VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"This Indeo 5 stream is described as {stream.Width}x{stream.Height}. The picture size a stream "
        + "states in its first key frame replaces the container's, but the container has to state one for "
        + "the decoder to be built at all, and this one states a picture with no area.");

    return new(stream.Width, stream.Height);
  }

  private Indeo5VideoDecoder(int width, int height) => this._decoder = new(width, height);

  /// <summary>
  /// Decodes one packet.
  /// </summary>
  /// <returns>
  /// <c>false</c> where the packet is an empty frame, which states that the picture did not change
  /// and carries nothing to show.
  /// </returns>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var picture = this._decoder.Decode(packet.Data);

    if (picture == null) {
      frame = null!;
      return false;
    }

    frame = new() {
      Width = picture.Width,
      Height = picture.Height,
      Format = PixelFormat.Rgb24,
      PixelData = IviColorConversion.ToRgb24(picture),
    };

    return true;
  }
}
