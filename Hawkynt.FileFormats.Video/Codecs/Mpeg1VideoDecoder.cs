using System;
using System.Collections.Generic;
using FileFormat.Codecs.Mpeg;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes MPEG-1 video, ISO/IEC 11172-2: I, P and B pictures.
/// </summary>
/// <remarks>
/// The first codec in this library that is a codec — everything before it either handed the packet to
/// an image reader or read the packet as samples. So the parts a video codec is made of are all here
/// for the first time: variable-length codes (<see cref="MpegVlcTables"/>), dequantisation with the
/// default and any loaded weighting matrices (<see cref="MpegQuantisation"/>), the inverse transform
/// (<see cref="MpegInverseDct"/>), motion compensation at half-pixel resolution
/// (<see cref="MpegMotionCompensation"/>) and prediction from the pictures either side
/// (<see cref="MpegPictureDecoder"/>).
/// <para/>
/// <b>What it does not do refuses by name.</b> A D picture, a forbidden picture type, a quantiser
/// scale of zero, a motion vector that points off the reference, a picture whose slices leave
/// macroblocks uncoded — each of those throws, naming the field and the clause. It does not have a
/// <c>catch</c> that hands back a blank or a repeated frame anywhere, because a plausible wrong
/// picture is worse than a refusal: nobody checks a picture that looks like a picture.
/// <para/>
/// <b>Measured.</b> Thirty-one encoded streams were decoded here and by ffmpeg and compared plane by
/// plane, sample by sample, every frame — static and moving content, sizes that are and are not whole
/// macroblocks, 16x16 to 704x480, quantiser scales from 1 to 31, loaded quantiser matrices, one slice
/// per row and six, groups of pictures open and closed, and sequences with no B pictures and with
/// four between anchors. Every stream produced the frame count ffprobe counts.
/// <para/>
/// Against ffmpeg's floating-point inverse transform (<c>-idct faani</c>) sixteen of the thirty-one
/// are identical byte for byte on every frame, and the other fifteen differ in at most thirty-two
/// samples of one frame — always by exactly one level, never growing from frame to frame, and
/// vanishing at the next intra picture. Against ffmpeg's default integer transform the difference is
/// hundreds to a couple of thousand samples a frame at one to three levels, which is the same size as
/// the difference between ffmpeg's own two transforms on the same streams. The standard specifies the
/// inverse transform as a formula with an accuracy bound rather than as an algorithm (11172-2 Annex
/// A), so this is the residual it exists to allow, and not a disagreement about the bitstream.
/// <para/>
/// <b>Frames come out in display order.</b> B pictures are coded after the picture they are predicted
/// backwards from, so an anchor is held until the next anchor arrives and handed out then. That is
/// why <see cref="TryDecode"/> answers "not yet" to the first packet of a stream and why
/// <see cref="Flush"/> is not empty: the last anchor of a stream has no successor to displace it.
/// <para/>
/// <b>An MPEG-2 stream reaching this decoder decodes.</b> The engine behind both this and
/// <see cref="Mpeg2VideoDecoder"/> is one decoder that reads whichever standard the bitstream turns
/// out to be, and 13818-2 is defined so that a decoder of it decodes 11172-2 as well. The two types
/// exist to claim different four-character codes and to answer <see cref="CodecName"/> differently,
/// not because they decode differently — so a container that names a stream MPEG-1 and hands over
/// MPEG-2 pictures gets those pictures rather than a refusal it did not need.
/// </remarks>
public sealed class Mpeg1VideoDecoder : IVideoCodecDecoder<Mpeg1VideoDecoder> {

  /// <summary>The four-character codes containers name MPEG-1 video with.</summary>
  /// <remarks>
  /// <c>MPEG</c> is deliberately absent, and <see cref="Mpeg2VideoDecoder"/> claims it instead. AVI
  /// files carrying MPEG-2 use it too, both types here read both standards, and a code that means
  /// "one of the two" belongs with whichever of them names the later one.
  /// </remarks>
  private static readonly CodecTag[] _Tags = [
    CodecTag.FromCharacters("MPG1"),
    CodecTag.FromCharacters("PIM1"),
    CodecTag.FromCharacters("mp1v"),
  ];

  private readonly MpegVideoDecoder _decoder = new();

  public static string CodecName => "MPEG-1 video (ISO/IEC 11172-2)";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      return false;

    foreach (var tag in _Tags)
      if (stream.Codec.EqualsIgnoringCase(tag))
        return true;

    return false;
  }

  /// <summary>
  /// Builds a decoder for one stream.
  /// </summary>
  /// <remarks>
  /// Nothing is read from the stream description, not even the dimensions. Every one of them is in
  /// the sequence header of the stream itself, and a container's copy is a copy — an AVI that states
  /// a size its sequence header disagrees with is a file whose pictures are the size the sequence
  /// header says.
  /// </remarks>
  public static Mpeg1VideoDecoder Create(MediaStreamInfo stream) {
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
