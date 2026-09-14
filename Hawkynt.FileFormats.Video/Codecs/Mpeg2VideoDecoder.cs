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
/// <b>Implemented MPEG-2 syntax.</b> The sequence and picture coding extensions; 4:2:0 with MPEG-2's
/// chrominance siting, 4:2:2, and the 4:4:4 syntax defined by 13818-2; <c>intra_dc_precision</c>;
/// linear and non-linear quantiser scales; both coefficient scans and both intra VLC tables;
/// separate luminance/chrominance quantiser matrices; concealment motion vectors; and MPEG-2
/// mismatch control. Frame pictures support frame and field DCT, frame and field motion prediction,
/// forward/backward B prediction and dual-prime prediction. Field pictures are paired as coded
/// frames with top/bottom parity preserved, including I/I, I/P, P/P and B/B pairs; a second P field
/// can use the first reconstructed field immediately, and field macroblocks support field, 16x8 and
/// dual-prime motion compensation with the reference-field rules of 7.6.3.
/// <para/>
/// <b>4:4:4 boundary.</b> H.262 defines 4:4:4 macroblock ordering, coded-block-pattern extension,
/// quantisation and full-resolution chroma motion-vector rules, and those decoding rules are
/// implemented here. H.262 Annex D also says that 4:4:4 is not supported by <em>any</em> defined
/// profile. In particular there is no such conforming subset as "High Profile 4:4:4". The decoder
/// therefore accepts chroma_format 3 as interoperability syntax, but does not claim that such a
/// bitstream conforms to High Profile or to another H.262 profile.
/// <para/>
/// <b>Still refused.</b> The sequence, spatial-picture and temporal-picture scalability extensions
/// are not implemented. A sequence that changes reference-picture geometry while old references are
/// live is also refused rather than guessed. Malformed field pairs, forbidden field-reference
/// selections and motion vectors that leave their reference plane are rejected instead of clamped or
/// replaced with a plausible-looking picture.
/// <para/>
/// <b>Measured.</b> The established corpus contains thirty-seven streams and eleven hundred frames,
/// decoded here and by ffmpeg and compared sample by sample on every frame: progressive and
/// interlaced frame pictures, 4:2:0 and 4:2:2, 64x48 up to 704x480, sizes that are and are not whole
/// macroblocks, the alternate scan, non-linear quantisation, the second intra table and every intra
/// DC precision, through elementary, program and transport streams. Twenty-seven of those thirty-seven
/// streams are identical sample for sample; the other ten differ in at most thirteen samples of one
/// frame, by at most three levels, without reference-chain drift. The advanced field-picture,
/// dual-prime and 4:4:4 paths additionally have bit-exact unit streams and executable FFmpeg oracle
/// tests; when FFmpeg is absent those oracle tests are reported inconclusive rather than silently
/// counted as evidence.
/// </remarks>
public sealed class Mpeg2VideoDecoder : IVideoCodecDecoder<Mpeg2VideoDecoder> {

  private static readonly CodecTag[] _Tags = [
    CodecTag.FromCharacters("MPG2"),
    CodecTag.FromCharacters("MPEG"),
    CodecTag.FromCharacters("mp2v"),
    CodecTag.FromCharacters("m2v1"),
    CodecTag.FromCharacters("hdv1"),
    CodecTag.FromCharacters("hdv2"),
    CodecTag.FromCharacters("hdv3"),
    CodecTag.FromCharacters("EM2V"),
    CodecTag.FromCharacters("MMES"),
  ];

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

  /// <summary>Builds a decoder for one stream.</summary>
  /// <remarks>
  /// Nothing is read from the stream description, not even the dimensions. Every coded picture
  /// property needed for reconstruction is carried by the sequence and picture headers/extensions.
  /// </remarks>
  public static Mpeg2VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return new();
  }

  /// <summary>Decodes one packet and hands back whichever complete frame is due for display.</summary>
  /// <returns>
  /// <c>false</c> when the packet decoded but no complete frame is due yet. That includes the first
  /// anchor of a stream and the first field picture of a field-coded frame.
  /// </returns>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    this._decoder.DecodePacket(packet.Data.Span);
    return this._decoder.TryTakeReady(out frame);
  }

  /// <summary>The complete pictures still held when the packets run out.</summary>
  public IEnumerable<RawImage> Flush() => this._decoder.Flush();
}
