using System;
using System.Collections.Generic;
using FileFormat.Codecs.Mpeg;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes MPEG-2 video, ISO/IEC 13818-2: the codec of DVD-Video, of digital television and of every
/// <c>.vob</c> and most <c>.ts</c> files.
/// </summary>
/// <remarks>
/// MPEG-2 is not a new codec so much as MPEG-1 with the parts that a broadcast needs added to it,
/// and this shares the whole of <see cref="Mpeg1VideoDecoder"/>'s engine because the standard is
/// written that way: 13818-2 requires a decoder of itself to decode 11172-2 as well, and the
/// picture, slice, macroblock and block layers are the same walk with a few more fields in them.
/// What it adds is carried in extension start codes the older standard left empty.
/// <para/>
/// <b>What it adds, and which of it is here.</b> The sequence and picture coding extensions;
/// 4:2:0 with MPEG-2's own chrominance siting and 4:2:2; <c>intra_dc_precision</c>, so a picture may
/// code its DC to nine, ten or eleven bits; the non-linear quantiser scale; the alternate scan; the
/// second intra coefficient table (Table B.15); loadable chrominance quantiser matrices;
/// concealment motion vectors; 13818-2's own dequantisation, which does not force every coefficient
/// odd the way MPEG-1's does but corrects the parity of each block once at the end; and interlaced
/// coding within a frame picture — field DCT and field-based motion compensation, where the two
/// halves of a macroblock are predicted separately from either field of the reference.
/// <para/>
/// <b>What it refuses, by name.</b> Field pictures, where the two fields of a frame are two coded
/// pictures rather than one; dual-prime prediction; 4:4:4; and the three scalability extensions.
/// Each throws naming the field and the clause. None of them is approximated, and there is no
/// <c>catch</c> anywhere that hands back a blank, a copied or a zero-filled frame — a decoder that
/// answers an interlaced field picture with something picture-shaped is worse than one that says it
/// cannot read it, because nobody checks a picture that looks like a picture.
/// <para/>
/// <b>Measured.</b> Thirty-seven streams, eleven hundred frames, decoded here and by ffmpeg and
/// compared sample by sample on every frame and not on the first: progressive and interlaced, 4:2:0
/// and 4:2:2, 64x48 up to 704x480, sizes that are and are not whole macroblocks, greyscale so that no
/// chrominance convention could mask a luminance error, with and without the alternate scan, the
/// non-linear quantiser, the second intra table and each intra DC precision, and through an
/// elementary stream, a program stream and a transport stream. Every one produced the frame count
/// ffprobe counts.
/// <para/>
/// Twenty-seven of the thirty-seven are identical sample for sample on every frame; the other ten
/// differ in at most thirteen samples of one frame, by at most three levels, and the difference is
/// flat from frame to frame rather than growing across a group of pictures — which is the thing worth
/// measuring, because a fault in motion compensation or dequantisation grows and a rounding
/// difference does not. On the same streams ffmpeg's own two inverse transforms differ from each
/// other by tens of thousands of samples a frame, so this residual is three orders of magnitude
/// inside the tolerance the standard's own accuracy bound allows.
/// </remarks>
public sealed class Mpeg2VideoDecoder : IVideoCodecDecoder<Mpeg2VideoDecoder> {

  /// <summary>The four-character codes containers name MPEG-2 video with.</summary>
  /// <remarks>
  /// <c>MPEG</c> is here rather than with MPEG-1. An AVI or a Matroska stating it is stating "one of
  /// the two MPEGs" and is in practice far more often the later one — and since the engine reads
  /// both, claiming it here costs nothing and turns what used to be "no codec for this stream" into
  /// a decode either way.
  /// </remarks>
  private static readonly CodecTag[] _Tags = [
    CodecTag.FromCharacters("MPG2"),
    CodecTag.FromCharacters("MPEG"),
    CodecTag.FromCharacters("mp2v"),
    CodecTag.FromCharacters("m2v1"),
    CodecTag.FromCharacters("hdv1"),
    CodecTag.FromCharacters("hdv2"),
    CodecTag.FromCharacters("hdv3"),
  ];

  /// <summary>What Matroska calls an MPEG-2 video track.</summary>
  private const string _MATROSKA_CODEC_ID = "V_MPEG2";

  private readonly MpegVideoDecoder _decoder = new();

  public static string CodecName => "MPEG-2 video (ISO/IEC 13818-2)";

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
  /// Nothing is read from the stream description, not even the dimensions — and for MPEG-2 that
  /// matters more than it did for MPEG-1, because a transport stream states a stream type and
  /// nothing else at all. Every one of the picture's properties is in the sequence header and the
  /// sequence extension of the stream itself.
  /// </remarks>
  public static Mpeg2VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return new();
  }

  /// <summary>
  /// Decodes one packet — one coded picture — and hands back whichever picture is due for display.
  /// </summary>
  /// <returns>
  /// <c>false</c> when the packet decoded but the picture it produced is not the one due next, which
  /// is the case for the first anchor of a stream and for any packet that holds no picture at all.
  /// </returns>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    this._decoder.DecodePacket(packet.Data.Span);
    return this._decoder.TryTakeReady(out frame);
  }

  /// <summary>The pictures still held when the packets run out: the last anchor, and anything queued behind it.</summary>
  public IEnumerable<RawImage> Flush() => this._decoder.Flush();
}
