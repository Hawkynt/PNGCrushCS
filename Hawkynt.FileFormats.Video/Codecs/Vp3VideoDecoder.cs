using System;
using FileFormat.Codecs.Vp3;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes On2 VP3 video, the codec Theora was built from.
/// </summary>
/// <remarks>
/// VP3.1 is the version this reads, which is what <c>VP31</c> and <c>VP32</c> streams are and what
/// almost every VP3 file in existence holds. Everything the format has is here: the run-length coded
/// block flags, all eight macro block coding modes with their eight ways of coding the modes
/// themselves, motion vectors from either of two reference frames at half-pixel accuracy, the
/// eighty built-in DCT token codebooks, DC prediction from four weighted neighbours, the exact
/// integer inverse DCT with its DC-only shortcut, and the deblocking loop filter.
/// <para/>
/// <b>Where the specification for this came from.</b> On2 donated VP3 to Xiph.Org, who built Theora
/// on it, and the Theora specification is complete and free. The two formats share the frame layout,
/// the transform, the quantisation, the coding modes, the motion vector coding and the loop filter,
/// and the specification writes down VP3's hard-coded tables — the loop filter limits, the
/// quantisation scales and base matrices, and all eighty codebooks — in its Appendix B, because
/// Theora carries in a setup header what VP3 has built in. Every table and every procedure here comes
/// from that document. The one thing it does not state is VP3's own frame header, which it says only
/// is "substantially different"; that was derived from VP3 streams, and
/// <see cref="Vp3FrameHeader"/> says exactly how, field by field, rather than implying a source that
/// does not exist.
/// <para/>
/// <b>What it does not do refuses by name.</b> A <c>VP30</c> stream is the earlier VP3.0 bitstream and
/// is refused when the decoder is built, naming the code the container states. A stream that starts
/// at an inter frame, a packet that ends in the middle of a frame, a run of block flags longer than
/// the frame has blocks, a coefficient token that would write past the end of a block, a frame whose
/// tokens do not account for every coefficient of every coded block — each throws and says which
/// field was wrong. There is no <c>catch</c> anywhere that hands back a blank frame or repeats the
/// last one. That matters more here than in most codecs: a repeated frame is exactly what a
/// legitimate still passage looks like in VP3, so a decoder that produced one on failure would be
/// indistinguishable from one that worked.
/// <para/>
/// <b>Measured.</b> Every frame of every VP3.1 test stream was decoded here and by ffmpeg and
/// compared plane by plane, sample by sample. The planes are identical: not close, not on average,
/// the same bytes, on the last frame of a hundred-frame run as on the first. That is the only
/// acceptable result, because both decoders are reading the same bitstream and the loss happened in
/// the encoder — and because an error of one anywhere in the inverse DCT or the loop filter would be
/// added to the next frame's error and the one after that, growing until the next intra frame.
/// <para/>
/// The RGB this hands back differs from ffmpeg's at colour edges, and only there. That is the
/// chrominance interpolation described in <see cref="Vp3ColorConversion"/>, which is a display
/// convention rather than part of the decode.
/// </remarks>
public sealed class Vp3VideoDecoder : IVideoCodecDecoder<Vp3VideoDecoder> {

  /// <summary>The four-character codes containers name VP3 with.</summary>
  /// <remarks>
  /// <c>VP31</c> and <c>VP32</c> are the same bitstream — the second is a later encoder, not a later
  /// format. <c>VP30</c> is named here so that a file holding it is refused for being VP3.0 rather
  /// than for being nothing anybody recognises.
  /// </remarks>
  private static readonly CodecTag[] _Tags = [
    CodecTag.FromCharacters("VP30"),
    CodecTag.FromCharacters("VP31"),
    CodecTag.FromCharacters("VP32"),
  ];

  /// <summary>The code of the earlier bitstream this does not read.</summary>
  private static readonly CodecTag _Version30 = CodecTag.FromCharacters("VP30");

  private readonly Vp3Decoder _decoder;

  /// <summary>Gets the codec name.</summary>
  public static string CodecName => "On2 VP3";

  /// <summary>Determines whether the specified media stream is supported.</summary>
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
  /// The picture size is taken from the stream description because VP3 has none of its own to take it
  /// from — unlike VP8, whose key frame states the size and where a container disagreeing with it is
  /// the container being wrong. VP3 relied on its container for the size, and AVI and QuickTime are
  /// the containers it was carried in.
  /// </remarks>
  public static Vp3VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Codec.EqualsIgnoringCase(_Version30))
      throw new NotSupportedException(
        "This stream is coded VP30, the VP3.0 bitstream. VP3.0 differs from VP3.1 in more than its frame "
        + "header — a VP3.0 key frame cannot be read with VP3.1's rules at any bit offset — and this decoder "
        + "implements VP3.1, which is what VP31 and VP32 streams hold.");

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"This VP3 stream is described as {stream.Width}×{stream.Height}. VP3 carries no picture size of its "
        + "own, so the container has to state one, and this one states a picture with no area.");

    return new(stream.Width, stream.Height);
  }

  private Vp3VideoDecoder(int width, int height) => this._decoder = new(width, height);

  /// <summary>
  /// Decodes one packet, which for VP3 is exactly one coded frame.
  /// </summary>
  /// <returns>
  /// Always <c>true</c>. VP3 has no frame that exists only to become a reference for later ones and
  /// no way to say a frame should not be shown, so every packet that decodes is a picture.
  /// </returns>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var picture = this._decoder.Decode(packet.Data);

    frame = new() {
      Width = this._decoder.Width,
      Height = this._decoder.Height,
      Format = PixelFormat.Rgb24,
      PixelData = Vp3ColorConversion.ToRgb24(picture, this._decoder.Width, this._decoder.Height),
    };

    return true;
  }
}
