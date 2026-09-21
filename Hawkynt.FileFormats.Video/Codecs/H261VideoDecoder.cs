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
/// clause 3.2; the two fixed coded picture formats, QCIF and CIF, clause 3.1 defines; and Annex D's
/// high-resolution still-image transmission, whose four ordinary QCIF/CIF sub-images are interleaved
/// back into a picture twice as wide and twice as high.
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
/// <item>Only two coded picture sizes exist, chosen by one bit of PTYPE — no source-format field with
/// five choices and no extended header for anything else (clause 3.1, 4.2.1.3). Annex D obtains its
/// larger still picture by transmitting four of those ordinary coded sizes, not by adding a third.</item>
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

  /// <summary>The four-character code containers name ITU-T H.261 with.</summary>
  private static readonly CodecTag[] _Tags = [
    CodecTag.FromCharacters("H261"),
  ];

  /// <summary>Figure D.1: sample parity for sub-images 0, 1, 2 and 3 respectively.</summary>
  private static readonly (int X, int Y)[] _StillImageOffsets = [
    (0, 0),
    (0, 1),
    (1, 1),
    (1, 0),
  ];

  private H263Frame? _reference;
  private H261PictureHeader? _geometry;
  private readonly H263Frame?[] _stillImageSubImages = new H263Frame?[4];
  private int _lastStillImageSubImage = -1;

  public static string CodecName => "H.261 (ITU-T H.261)";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      return false;

    foreach (var tag in _Tags)
      if (stream.Codec.EqualsIgnoringCase(tag))
        return true;

    return false;
  }

  public static H261VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return new();
  }

  /// <summary>Decodes one packet and hands back the last complete ordinary or Annex D picture it holds.</summary>
  /// <returns><c>false</c> when the packet held no complete output picture.</returns>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var picture = this._DecodePacket(packet.Data.Span);
    if (picture == null) {
      frame = null!;
      return false;
    }

    frame = picture;
    return true;
  }

  /// <summary>Nothing is ever held back for reordering: H.261 has no bidirectional prediction.</summary>
  /// <remarks>
  /// An incomplete Annex D still image is not a delayed frame: without all four sub-images there is no
  /// high-resolution picture to emit, so flushing deliberately does not fabricate one.
  /// </remarks>
  public IEnumerable<RawImage> Flush() => [];

  // ============================================================================================
  // The start-code walk — ITU-T H.261, 4.2.1
  // ============================================================================================

  /// <summary>
  /// Finds the pictures in one packet and decodes them, answering with the last complete output image.
  /// </summary>
  /// <remarks>
  /// Bit by bit and not byte-aligned, because clause 4.2 states no stuffing before a picture start code
  /// the way H.263's clause 5.1.27 does before its own — H.261's group and picture start codes are
  /// fixed-length patterns with nothing variable in front of them. Annex D relies on that property to
  /// place four picture syntaxes back to back without padding between them.
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

      // Annex D.3 explicitly says the reference memory for the current frame is always the previous
      // frame regardless of whether either one is motion video or a still-image sub-picture. Keep the
      // coded-size frame here; the assembled high-resolution picture is display output, never a motion
      // reference.
      this._reference = picture.Target;
      this._geometry = header;

      if (header.IsStillImage) {
        var assembled = this._AcceptStillImageSubPicture(header, picture.Target);
        if (assembled != null)
          last = assembled;
      } else {
        this._ResetStillImageAssembly();
        last = _ToImage(picture.Target, header.Width, header.Height);
      }
    }

    return last;
  }

  /// <summary>
  /// Accepts one Annex D sub-image and returns the assembled four-times-area still picture when complete.
  /// </summary>
  private RawImage? _AcceptStillImageSubPicture(H261PictureHeader header, H263Frame subImage) {
    var index = header.StillImageSubImageIndex;

    // Annex D.3 says 0,1,2,3 in sequence, allows repetition of the current number, and says an encoder
    // should not go backwards. A lost packet or a non-conforming backward jump must not make us combine
    // fresh samples with stale ones from a previous still picture, so any discontinuity starts a new
    // incomplete assembly rather than guessing.
    if (this._lastStillImageSubImage >= 0
        && index != this._lastStillImageSubImage
        && index != this._lastStillImageSubImage + 1)
      this._ResetStillImageAssembly();

    if (this._lastStillImageSubImage < 0 && index != 0)
      this._ResetStillImageAssembly();

    this._stillImageSubImages[index] = subImage;
    this._lastStillImageSubImage = index;

    for (var i = 0; i < this._stillImageSubImages.Length; ++i)
      if (this._stillImageSubImages[i] == null)
        return null;

    var assembled = _AssembleStillImage(header, this._stillImageSubImages);
    this._ResetStillImageAssembly();
    return assembled;
  }

  /// <summary>Interleaves Annex D Figure D.1's four parity classes into the high-resolution planes.</summary>
  private static RawImage _AssembleStillImage(H261PictureHeader header, H263Frame?[] subImages) {
    var target = new H263Frame(header.MacroblockWidth * 2, header.MacroblockHeight * 2);

    for (var index = 0; index < 4; ++index) {
      var source = subImages[index]!;
      var (offsetX, offsetY) = _StillImageOffsets[index];
      _InterleavePlane(source.Luma, source.LumaWidth, source.LumaHeight, target.Luma, target.LumaWidth, offsetX, offsetY);
      _InterleavePlane(source.Cb, source.ChromaWidth, source.ChromaHeight, target.Cb, target.ChromaWidth, offsetX, offsetY);
      _InterleavePlane(source.Cr, source.ChromaWidth, source.ChromaHeight, target.Cr, target.ChromaWidth, offsetX, offsetY);
    }

    return _ToImage(target, header.Width * 2, header.Height * 2);
  }

  private static void _InterleavePlane(
    ReadOnlySpan<byte> source, int sourceWidth, int sourceHeight, Span<byte> target, int targetWidth,
    int offsetX, int offsetY) {
    for (var y = 0; y < sourceHeight; ++y) {
      var sourceRow = y * sourceWidth;
      var targetRow = (2 * y + offsetY) * targetWidth + offsetX;
      for (var x = 0; x < sourceWidth; ++x)
        target[targetRow + 2 * x] = source[sourceRow + x];
    }
  }

  private void _ResetStillImageAssembly() {
    Array.Clear(this._stillImageSubImages);
    this._lastStillImageSubImage = -1;
  }

  /// <summary>
  /// Refuses a coded picture size that changes while a picture predicted from the old size is still held.
  /// </summary>
  private void _RefuseGeometryChangeMidStream(H261PictureHeader header) {
    if (this._geometry == null || this._geometry.SameGeometryAs(header))
      return;

    throw new NotSupportedException(
      $"This stream changes coded picture size from {this._geometry.Width}x{this._geometry.Height} to "
      + $"{header.Width}x{header.Height} part way through, while a picture predicted from the old size is still "
      + "held as the reference. Decoding a stream whose coded size changes is not implemented.");
  }

  private static RawImage _ToImage(H263Frame frame, int width, int height) => new() {
    Width = width,
    Height = height,
    Format = PixelFormat.Rgb24,
    PixelData = H263ColorConversion.ToRgb24(frame, width, height),
  };
}
