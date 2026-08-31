using System;
using FileFormat.Codecs.Theora;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Theora video, Xiph.Org's free specification.
/// </summary>
/// <remarks>
/// The codec every <c>.ogv</c> file is made of, and the descendant of On2's VP3 — which is where its
/// odder features come from: a right-handed coordinate system with the origin at the bottom left, a
/// block ordering that follows a Hilbert curve inside each super block, and a DC predictor that
/// extrapolates a gradient from three neighbours and then checks whether it has run away.
/// <para/>
/// Everything the format has is here. The three setup headers with their loop filter limits,
/// quantisation ranges and eighty Huffman codes; the run-length coded block flags; all eight macro
/// block coding modes; motion vectors in both of their codings, with per-block vectors and the
/// chroma vectors averaged from them; block-level quantisation indices; the 32-token coefficient
/// alphabet read in 64 passes over the frame; DC prediction across the four reference-frame classes;
/// the normative integer inverse transform; whole- and half-pixel prediction from either reference
/// frame; and the in-loop deblocking filter. All three pixel formats — 4:2:0, 4:2:2 and 4:4:4 —
/// decode.
/// <para/>
/// <b>What it does not do refuses by name.</b> A bitstream version other than 3.2, the reserved
/// pixel format, a reserved bit that is set, a stream that begins at an inter frame, a packet that
/// ends part way through its coded data, a Huffman table longer than the format allows, quant ranges
/// that do not cover the scale: each of those throws and says which field was wrong. There is no
/// <c>catch</c> anywhere that hands back a blank frame or repeats the last one. A zero-length packet
/// does produce the previous picture again, but that is the format saying so — section 7.11 defines
/// it as an inter frame with no coded blocks — and it arrives there by the ordinary reconstruction
/// path rather than by a special case.
/// <para/>
/// <b>Measured.</b> Every frame of every test stream was decoded here and by ffmpeg and compared
/// plane by plane, sample by sample. The planes are identical: not close, not on average, the same
/// bytes. That is the only acceptable result for a lossy codec, because the loss happened in the
/// encoder and both decoders are reading the same bitstream — and it is the measurement that
/// matters, because an error in the loop filter, in prediction or in DC prediction would show up as
/// a small difference that grows with every frame until the next intra frame.
/// <para/>
/// The RGB this hands back differs from ffmpeg's at colour edges, and only there. That is the
/// chrominance interpolation described in <see cref="TheoraColorConversion"/>, which is a display
/// convention rather than part of the decode.
/// </remarks>
public sealed class TheoraVideoDecoder : IVideoCodecDecoder<TheoraVideoDecoder> {
  /// <summary>Initializes a new instance of this type.</summary>
  public TheoraVideoDecoder() { }

  /// <summary>The names containers that name codecs with text give this one.</summary>
  /// <remarks>
  /// Ogg names it with the magic at the head of its identification header, which is <c>theora</c>;
  /// Matroska names it <c>V_THEORA</c>. Two spellings of one codec, and the decoder answers to both
  /// so that neither container has to know what the other calls it.
  /// </remarks>
  private static readonly string[] _CodecIds = ["theora", "V_THEORA"];

  /// <summary>The four-character codes containers with a code field name Theora with.</summary>
  /// <remarks>
  /// Rare, because Theora is almost always in Ogg and Ogg has no code field. A QuickTime file may
  /// carry it as <c>Theo</c> and a Video for Windows one as <c>THEO</c>.
  /// </remarks>
  private static readonly CodecTag[] _Tags = [CodecTag.FromCharacters("Theo")];

  private readonly TheoraDecoder _decoder = new();

  /// <summary>Gets the codec name.</summary>
  public static string CodecName => "Theora (Xiph.Org Theora I)";

  /// <summary>Determines whether the specified media stream is supported.</summary>
  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      return false;

    foreach (var name in _CodecIds)
      if (string.Equals(stream.CodecId, name, StringComparison.OrdinalIgnoreCase))
        return true;

    foreach (var tag in _Tags)
      if (stream.Codec.EqualsIgnoringCase(tag))
        return true;

    return false;
  }

  /// <summary>
  /// Builds a decoder for one stream, reading its three setup headers.
  /// </summary>
  /// <remarks>
  /// The headers are the stream's private data and they are not optional: a Theora stream cannot be
  /// decoded from its frames alone, because the quantisation matrices and the eighty Huffman codes
  /// that every coefficient is read through live only there. A stream whose container did not carry
  /// them is refused here rather than at the first frame, so the refusal names the stream.
  /// <para/>
  /// Nothing else is read from the stream description, not even the picture size. Theora states it
  /// in the identification header, and where a container's copy disagrees it is the container's copy
  /// that is wrong.
  /// </remarks>
  public static TheoraVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.CodecPrivateData.IsEmpty)
      throw new NotSupportedException(
        $"Stream {stream.Index} is Theora and carries no codec private data, so its identification, comment and setup headers are missing. A Theora stream cannot be decoded without them.");

    var decoder = new TheoraVideoDecoder();
    decoder._decoder.Configure(stream.CodecPrivateData);
    return decoder;
  }

  /// <summary>
  /// Decodes one packet, which for Theora is exactly one coded frame.
  /// </summary>
  /// <remarks>
  /// Always a picture, because Theora reorders nothing and holds nothing back: every data packet is
  /// one frame and that frame is due immediately. A codec with bidirectional prediction would have
  /// packets that produce no picture until a later one arrives; this one does not, which is also why
  /// <c>Flush</c> is empty.
  /// </remarks>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var picture = this._decoder.Decode(packet.Data);
    var header = this._decoder.Identification;

    frame = new() {
      Width = header.PictureWidth,
      Height = header.PictureHeight,
      Format = PixelFormat.Rgb24,
      PixelData = TheoraColorConversion.ToRgb24(picture, header),
    };

    return true;
  }
}
