using System;
using FileFormat.Codecs.Indeo;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Intel Indeo Video Interactive 4.
/// </summary>
/// <remarks>
/// Everything the format has is here bar the transforms Intel specified and never shipped: the
/// picture and band headers, the wavelet subdivision into one band or four, tiling, the slant and
/// Haar transforms in two dimensions and in each one alone at both block sizes, the band that stores
/// samples untransformed, all fifteen scan patterns, all nine dequantisation matrices, run-value
/// coded coefficients with the nine built-in maps and the per-band permutations of them, the sixteen
/// built-in Huffman codebooks and the custom ones a stream may spell out, whole- and half-sample
/// motion compensation, bidirectional frames including the second frame Indeo 4 packs into the same
/// packet as the first, motion vector and quantiser inheritance between bands, and the Haar wavelet
/// recomposition of a scalable picture.
/// <para/>
/// <b>Where this came from.</b> Indeo 4 transmits none of its tables. A band header names its
/// transform, its scan and its quantisation matrix by index, so a fourteen-byte frame is a complete
/// frame for a 360x288 picture, and what those indices name exists only in a decoder. This one's are
/// FFmpeg's, whose <c>libavcodec/indeo4.c</c>, <c>indeo4data.h</c>, <c>ivi.c</c>, <c>ivi.h</c>,
/// <c>ivi_dsp.c</c> and <c>ivi_dsp.h</c> are LGPL-2.1-or-later and so may be carried into this
/// LGPL-3.0-or-later library. <see cref="IviDecoder"/> and the files beside it are that code
/// converted rather than wrapped, and the notice next to them records it.
/// <para/>
/// <b>What it refuses.</b> A band selecting one of the four discrete cosine transforms or the
/// four-by-four pass-through: the format numbers all five and Intel's own decoders implemented none
/// of them, so no clip carries one and what they would produce has never been observed. A band
/// spelling out a scan pattern or a quantisation matrix of its own, which the format reserves an
/// index for and no clip uses, so where in the header it would sit has never been observed either. A
/// clip stating a chrominance subsampling other than YVU9. Everything else is decoded, and a frame
/// that cannot be read throws and says which field was wrong rather than handing back a repeat of
/// the last picture.
/// <para/>
/// <b>Measured.</b> Every frame of the sample clips was decoded here and by ffmpeg and compared
/// sample by sample over all three planes. The planes are identical, including across the
/// bidirectional passages where a packet holds two frames and a later packet holds none.
/// </remarks>
public sealed class Indeo4VideoDecoder : IVideoCodecDecoder<Indeo4VideoDecoder> {

  /// <summary>The four-character code containers name Indeo 4 with.</summary>
  private static readonly CodecTag _Tag = CodecTag.FromCharacters("IV41");

  private readonly Indeo4Decoder _decoder = new();

  public static string CodecName => "Intel Indeo Video Interactive 4";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  /// <summary>
  /// Builds a decoder for one stream.
  /// </summary>
  /// <remarks>
  /// Nothing is taken from the stream description. Indeo 4 states its picture size, its tiling and its
  /// band subdivision in every picture header, so a container disagreeing with the frames is the
  /// container being wrong.
  /// </remarks>
  public static Indeo4VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return new();
  }

  /// <summary>
  /// Decodes one packet.
  /// </summary>
  /// <returns>
  /// <c>false</c> where the packet is an empty frame with nothing held for it. An empty frame is how
  /// Indeo 4 marks the place of a frame that travelled inside an earlier packet, and where that
  /// happened the picture held since then is what comes back.
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
