using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.H261;
using FileFormat.Codecs.H263;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes H.261 video, ITU-T Recommendation H.261, <i>Video codec for audiovisual services at
/// p x 64 kbit/s</i> — the direct ancestor of the H.263 decoder beside this one, and the codec both
/// H.263 and every hybrid predictive standard after it grew out of.
/// </summary>
/// <remarks>
/// The whole of the Recommendation's normative coding is here: the picture, group of blocks, macroblock
/// and block layers of clause 4.2; whole-pixel motion compensation and the in-loop spatial filter of
/// clause 3.2; and the two fixed picture formats, QCIF and CIF, clause 3.1 defines. What is not
/// implemented refuses by name: the still image transmission of Annex D, which needs four ordinary
/// pictures reassembled into one at four times the resolution and is signalled by a bit this decoder
/// reads and rejects rather than silently ignores.
/// <para/>
/// <b>Where this and H.263 coincide, and where they do not.</b> H.263 (see <c>H263VideoDecoder</c>)
/// kept three things from this Recommendation unchanged, and this decoder reuses the classes that
/// implement them rather than writing a second copy: the inverse transform and its accuracy-bound
/// specification (<c>H263InverseDct</c>), the coefficient dequantisation and zig-zag scan
/// (<c>H263Quantisation</c>), and the reconstructed-picture buffer and colour conversion
/// (<c>H263Frame</c>, <c>H263ColorConversion</c>) — H.261's chrominance siting and studio-range samples
/// are the same convention H.263 states in its own clause 4.1, inherited from this Recommendation's
/// Figure 2. Everything else is written fresh, because everything else differs:
/// <list type="bullet">
/// <item>Only two picture sizes exist, chosen by one bit of PTYPE — no source-format field with five
/// choices and no extended header for anything else (clause 3.1, 4.2.1.3).</item>
/// <item>There is no picture-level intra/inter flag at all. Every macroblock states its own prediction
/// mode in MTYPE (Table 2), and one picture may freely mix intra- and inter-coded macroblocks; only the
/// very first picture of a stream is constrained, by having nothing yet to predict from.</item>
/// <item>Motion vectors are whole-pixel, not half-pixel — clause 3.2.2 gives them integer components not
/// exceeding &#177;15 — so there is no bilinear interpolation and the chrominance vector is derived by
/// truncating rather than by H.263's Table 18 rounding.</item>
/// <item>The optional loop filter (clause 3.2.3) is part of prediction, not a post-decode step: when a
/// macroblock's MTYPE asks for it, a 2D spatial filter runs on the motion-compensated prediction
/// <b>before</b> the residual is added to it. H.263 baseline has no loop filter of any kind.</item>
/// <item>A macroblock's address is coded as the difference from the last transmitted one (clause
/// 4.2.3.1), and a gap greater than one means the macroblocks in between carry no bits at all — they
/// are never coded, not coded-with-nothing, which this decoder implements by seeding every predicted
/// picture's canvas with a copy of the reference before decoding a single macroblock of it.</item>
/// <item>The coefficient table (Table 5) carries an explicit end-of-block symbol rather than folding it
/// into every code the way H.263's Table 16 does, and that symbol cannot be a block's first thing — so
/// the first coefficient of a coded block and every one after it are read from two different tables.
/// </item>
/// </list>
/// There is no <c>catch</c> anywhere that hands back a blank, a copied or a zero-filled picture.
/// </remarks>
public sealed class H261VideoDecoder : IVideoCodecDecoder<H261VideoDecoder> {
  /// <summary>Initializes a new instance of this type.</summary>
  public H261VideoDecoder() { }

  /// <summary>The four-character code containers name ITU-T H.261 with.</summary>
  private static readonly CodecTag[] _Tags = [
    CodecTag.FromCharacters("H261"),
  ];

  private H263Frame? _reference;
  private H261PictureHeader? _geometry;

  /// <summary>Gets the codec name.</summary>
  public static string CodecName => "H.261 (ITU-T H.261)";

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

  /// <summary>Creates a decoder for the specified media stream.</summary>
  public static H261VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return new();
  }

  /// <summary>Decodes one packet and hands back the picture it holds.</summary>
  /// <returns><c>false</c> when the packet held no picture at all.</returns>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var picture = this._DecodePacket(packet.Data.Span);
    if (picture == null) {
      frame = null!;
      return false;
    }

    frame = picture;
    return true;
  }

  /// <summary>Nothing is ever held back: H.261 has no bidirectional prediction to reorder around.</summary>
  public IEnumerable<RawImage> Flush() => [];

  // ============================================================================================
  // The start-code walk — ITU-T H.261, 4.2.1
  // ============================================================================================

  /// <summary>
  /// Finds the pictures in one packet and decodes them, answering with the last.
  /// </summary>
  /// <remarks>
  /// Bit by bit and not byte-aligned, because clause 4.2 states no stuffing before a picture start code
  /// the way H.263's clause 5.1.27 does before its own — H.261's group and picture start codes are
  /// fixed-length patterns with nothing variable in front of them.
  /// </remarks>
  private RawImage? _DecodePacket(ReadOnlySpan<byte> data) {
    RawImage? last = null;

    var reader = new H263BitReader(data);
    while (reader.BitsRemaining >= H261PictureHeader.StartCodeLength) {
      if (reader.NextBits(H261PictureHeader.StartCodeLength) != H261PictureHeader.StartCode) {
        reader.Skip(1);
        continue;
      }

      reader.Skip(H261PictureHeader.StartCodeLength);

      var header = H261PictureHeader.Parse(ref reader);
      this._RefuseGeometryChangeMidStream(header);

      var picture = H261PictureDecoder.BeginPicture(header, this._reference);
      picture.DecodePicture(ref reader);

      // Every decoded H.261 picture is a valid reference for the next one: there are no B-parts, no
      // disposable pictures, nothing like Sorenson Spark's third picture type.
      this._reference = picture.Target;
      this._geometry = header;
      last = _ToImage(picture.Target, header);
    }

    return last;
  }

  /// <summary>
  /// Refuses a picture size that changes while a picture predicted from the old size is still held.
  /// </summary>
  private void _RefuseGeometryChangeMidStream(H261PictureHeader header) {
    if (this._geometry == null || this._geometry.SameGeometryAs(header))
      return;

    throw new NotSupportedException(
      $"This stream changes picture size from {this._geometry.Width}x{this._geometry.Height} to "
      + $"{header.Width}x{header.Height} part way through, while a picture predicted from the old size is still "
      + "held as the reference. Decoding a stream whose size changes is not implemented.");
  }

  private static RawImage _ToImage(H263Frame frame, H261PictureHeader header) => new() {
    Width = header.Width,
    Height = header.Height,
    Format = PixelFormat.Rgb24,
    PixelData = H263ColorConversion.ToRgb24(frame, header.Width, header.Height),
  };
}
