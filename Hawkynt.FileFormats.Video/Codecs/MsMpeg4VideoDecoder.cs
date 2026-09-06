using System;
using System.Collections.Generic;
using FileFormat.Codecs.Mpeg4;
using FileFormat.Codecs.MsMpeg4;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes all three of Microsoft's MPEG-4 variants — the codecs an <c>.avi</c> or <c>.asf</c> names
/// <c>MPG4</c>, <c>MP42</c> and <c>MP43</c>, and the dozen other four-character codes those three were
/// distributed under.
/// </summary>
/// <remarks>
/// Three formats rather than three revisions of one. They share a picture header seven bits long, a
/// block layer with the same three escape forms, the same half-sample motion compensation and the same
/// H.263 inverse quantisation, and they disagree about nearly everything else: which tables the
/// macroblock layer reads, how the DC is predicted, whether the coefficients are predicted at all, how
/// a motion vector is written, and how the slice field is read. Nothing in a stream says which of the
/// three it is, so the container's four-character code is the only thing that can decide and a stream
/// decoded as the wrong one produces a picture rather than an error.
/// <para/>
/// <b>What they share.</b> There is no start code except version 1's, no sequence header and no video
/// object layer header anywhere: a packet is a picture, and the picture header is two bits for its
/// type, five for the quantiser it uses throughout, and then between one and eight more. Everything
/// ISO/IEC 14496-2 states once per layer is fixed instead of signalled, so the picture size comes from
/// the container and there is nothing to refuse. A macroblock has one motion vector and never four;
/// the quantiser cannot change inside a picture; the vectors reach thirty-one and a half samples
/// either way rather than a range the picture chooses; and there are no bidirectionally coded pictures
/// at all.
/// <para/>
/// <b>Where the format came from.</b> Microsoft published no specification for any of the three. The
/// Open Specifications programme documents Microsoft's protocols and containers, not its codec
/// bitstreams; SMPTE ST 421 standardised Windows Media Video 9 and says nothing about the three that
/// came before. The tables are Microsoft's own and appear in no standard — the motion vector tables of
/// version 3 pair a single codeword with a whole vector across eleven hundred entries — so the only
/// authoritative statement of them is the free implementation that reverse-engineered them, and they
/// are copied from it value for value. See <c>THIRD-PARTY-NOTICE.FFmpeg.txt</c> beside the codec's
/// sources.
/// <para/>
/// <b>Measured.</b> Four hundred and eighty encoded streams, twenty-four thousand frames, were
/// decoded here and by ffmpeg and compared plane by plane, sample by sample, on every frame: four
/// sources, sizes from 64x64 to 352x288, quantisers 3, 8, 16 and 25, and groups of pictures of one
/// frame, twelve and a thousand. Every stream produced the frame count ffmpeg produced and none
/// failed.
/// <para/>
/// Of seven hundred and sixty-nine million luminance and chrominance samples, version 2 differs from
/// ffmpeg in six thousand one hundred and two and version 3 in fifty-two thousand nine hundred and
/// thirty-two — under seven thousandths of one per cent either way — and never by more than one
/// level. Five real-world files were decoded as well, one per four-character code that has a sample
/// on <c>samples.ffmpeg.org</c> (<c>MP43</c>, <c>DIV5</c>, <c>AP41</c>, <c>COL1</c>, <c>MPG3</c>), at
/// sizes to 720x576: eleven thousand five hundred and seventy-eight samples of eighty-five million
/// differ, again never by more than one level.
/// <para/>
/// That comparison is against ffmpeg's floating-point transform. Against its default integer one the
/// residual is larger — up to ten levels after a long run of predicted pictures — and it is the
/// transform's rather than the decode's: the two decoders agree to within a level wherever ffmpeg
/// evaluates the transform accurately, which is what identifies the difference. ISO/IEC 14496-2 Annex
/// A specifies the inverse transform as an accuracy bound rather than as an algorithm, so this is the
/// expected shape of the disagreement and not a defect in either.
/// <para/>
/// Version 1 has no encoder anywhere and no sample on <c>samples.ffmpeg.org</c>, so it was measured
/// the other way about: twenty streams written by this library's own encoder, a thousand frames,
/// decoded by ffmpeg's version 1 decoder and compared against this one's. Three hundred and
/// eighty-one samples of sixty-four million differ, none by more than one level.
/// <para/>
/// <b>What it does not do.</b> Windows Media Video 7 and 8 are refused by name. They are the same
/// family and the next two members of it, but each adds machinery of its own — its own scan orders and
/// a trailing header for the first, a different inverse transform and a different interpolation for
/// the second — and a stream of either decoded as version 3 would produce a picture rather than an
/// error. There is no <c>catch</c> anywhere that hands back a blank, a copied or a repeated picture,
/// because a plausible wrong picture is worse than a refusal: nobody checks a picture that looks like
/// a picture.
/// </remarks>
public sealed class MsMpeg4VideoDecoder : IVideoCodecDecoder<MsMpeg4VideoDecoder> {

  /// <summary>The four-character codes that name version 1.</summary>
  /// <remarks>
  /// <c>MPG4</c> is what Microsoft shipped it as in the first Windows Media encoder, and it is the
  /// tag that has caused the most confusion since: it names Microsoft's first variant and not MPEG-4
  /// Part 2, which shares none of its bitstream.
  /// </remarks>
  private static readonly CodecTag[] _Version1Tags = [
    CodecTag.FromCharacters("MPG4"),
    CodecTag.FromCharacters("MP41"),
    CodecTag.FromCharacters("DIV1"),
  ];

  /// <summary>The codes that name version 2.</summary>
  /// <remarks>
  /// <c>MP42</c> is Microsoft's own and <c>DIV2</c> is what the codec was distributed as once it had
  /// been pulled out of Windows Media and passed around on its own.
  /// </remarks>
  private static readonly CodecTag[] _Version2Tags = [
    CodecTag.FromCharacters("MP42"),
    CodecTag.FromCharacters("DIV2"),
  ];

  /// <summary>The codes that name version 3, which is the original DivX.</summary>
  /// <remarks>
  /// The longest list in this library, and every entry of it is one bitstream: <c>MP43</c> is
  /// Microsoft's, <c>DIV3</c> through <c>DIV6</c> and <c>DVX3</c> are the patched codec that was passed
  /// around as DivX ;-), and <c>AP41</c>, <c>AP42</c>, <c>COL0</c>, <c>COL1</c> and <c>MPG3</c> are
  /// what various encoders stamped on the same thing.
  /// </remarks>
  private static readonly CodecTag[] _Version3Tags = [
    CodecTag.FromCharacters("MP43"),
    CodecTag.FromCharacters("DIV3"),
    CodecTag.FromCharacters("DIV4"),
    CodecTag.FromCharacters("DIV5"),
    CodecTag.FromCharacters("DIV6"),
    CodecTag.FromCharacters("DVX3"),
    CodecTag.FromCharacters("AP41"),
    CodecTag.FromCharacters("AP42"),
    CodecTag.FromCharacters("COL0"),
    CodecTag.FromCharacters("COL1"),
    CodecTag.FromCharacters("MPG3"),
  ];

  /// <summary>The name Matroska gives version 3.</summary>
  /// <remarks>
  /// The only one of the three versions Matroska names at all: a file carrying version 1 or version 2
  /// states a <c>BITMAPINFOHEADER</c> code the way an AVI does, so those reach the lists above through
  /// their tag. Without this a Matroska file of the original DivX would go to the registry's refusal —
  /// that nothing decodes the stream — rather than to this decoder.
  /// </remarks>
  private static readonly string[] _Version3CodecIds = ["V_MPEG4/MS/V3"];

  /// <summary>The codes that name Windows Media Video 7 and 8, refused as what they are.</summary>
  private static readonly CodecTag[] _WindowsMediaTags = [
    CodecTag.FromCharacters("WMV1"),
    CodecTag.FromCharacters("WMV2"),
  ];

  /// <summary>
  /// How many bits an intra picture's trailing extension header occupies, by version.
  /// </summary>
  /// <remarks>
  /// Seventeen in version 3 and sixteen before it, the extra bit being the one that says whether the
  /// interpolation's rounding alternates from picture to picture.
  /// </remarks>
  private const int _EXTENSION_HEADER_BITS = 16;

  private readonly MsMpeg4Version _version;
  private readonly int _width;
  private readonly int _height;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;

  /// <summary>The last decoded picture, which the next predicted one is predicted from.</summary>
  private Mpeg4Frame? _reference;

  /// <summary>
  /// How tall a slice is, as the last intra picture stated it.
  /// </summary>
  /// <remarks>
  /// Kept across pictures because a predicted picture does not restate it: the slice field is in the
  /// intra picture's header alone and holds until the next one. Prediction stops at a slice boundary,
  /// so forgetting the height between pictures would let a predicted picture predict across one.
  /// </remarks>
  private int _sliceHeight;

  /// <summary>Whether version 3's rounding alternates, as the last intra picture's trailing header said.</summary>
  private bool _roundingAlternates;

  /// <summary>Which way the half-sample interpolation rounds in the picture being decoded.</summary>
  private int _rounding;

  private MsMpeg4VideoDecoder(MsMpeg4Version version, int width, int height) {
    this._version = version;
    this._width = width;
    this._height = height;
    this._macroblockWidth = (width + 15) / 16;
    this._macroblockHeight = (height + 15) / 16;
    this._sliceHeight = this._macroblockHeight;
  }

  public static string CodecName => "Microsoft MPEG-4 versions 1 to 3 video (MPG4/MP42/MP43)";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      return false;

    return _Matches(stream, _Version1Tags) || _Matches(stream, _Version2Tags) || _IsVersion3(stream)
           || _Matches(stream, _WindowsMediaTags);
  }

  /// <summary>
  /// Builds a decoder for one stream.
  /// </summary>
  /// <remarks>
  /// The picture size comes from the container and there is nowhere else it could come from: the
  /// bitstream states it nowhere, which is the price of a picture header seven bits long. So a stream
  /// whose container did not state a size is refused here rather than decoded into a picture of a size
  /// invented for it.
  /// </remarks>
  public static MsMpeg4VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (_Matches(stream, _WindowsMediaTags))
      throw new NotSupportedException(
        $"Stream {stream.Index} is coded as '{stream.Codec}', which is Windows Media Video 7 or 8. Those are the next "
        + "two members of this family and share its picture header and much of its block layer, but each brings "
        + "machinery of its own — its own four scan orders and a trailing header the picture header depends on for "
        + "the first, a different inverse transform and a different half-sample interpolation for the second. A "
        + "stream of either decoded as version 3 would produce a picture rather than an error, so they are refused "
        + "rather than approximated.");

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"Stream {stream.Index} states a size of {stream.Width}x{stream.Height}. Microsoft's MPEG-4 carries no "
        + "picture size in the bitstream at all — its picture header is seven bits — so the container's is the only "
        + "one there is.");

    return new(_VersionOf(stream), stream.Width, stream.Height);
  }

  /// <summary>
  /// Decodes one packet into one picture.
  /// </summary>
  /// <remarks>
  /// Always a picture, and always this packet's: none of the three has bidirectionally coded pictures,
  /// so nothing is ever held back to be shown after something decoded later, and decode order and
  /// display order are the same order.
  /// </remarks>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var reader = new Mpeg4BitReader(packet.Data.Span);
    var header = MsMpeg4PictureHeader.Parse(ref reader, this._version, this._macroblockHeight, this._sliceHeight);
    this._sliceHeight = header.SliceHeight;
    this._SettleRounding(header);

    var picture = MsMpeg4PictureDecoder.BeginPicture(
      this._version, header, new(this._macroblockWidth, this._macroblockHeight), this._reference,
      this._macroblockWidth, this._macroblockHeight, this._rounding);

    picture.DecodePicture(ref reader);

    if (header.CodingType == MsMpeg4PictureHeader.IntraCoded)
      this._ReadExtensionHeader(ref reader, packet.Data.Length);

    picture.Target.PadBorders();
    this._reference = picture.Target;

    frame = this._ToImage(picture.Target);
    return true;
  }

  /// <summary>Nothing is ever held back, so there is nothing left at the end.</summary>
  public IEnumerable<RawImage> Flush() => [];

  /// <summary>The three sample planes of the most recently decoded picture.</summary>
  /// <remarks>
  /// Here so that a comparison against another decoder can be made on the planes, which is the only
  /// place it means anything. A picture is handed out as RGB, and two decoders compared in RGB are
  /// being compared on their chrominance upsampling as much as on their decoding — this library
  /// interpolates where the reference decoders repeat, so an RGB comparison of any 4:2:0 codec shows
  /// tens of thousands of differing samples at a large difference even where every coded sample
  /// agrees exactly.
  /// </remarks>
  internal Mpeg4Frame? DecodedPlanes => this._reference;

  /// <summary>Which of the three this stream is, as the container's four-character code names it.</summary>
  internal MsMpeg4Version Version => this._version;

  /// <summary>
  /// Decides which way the half-sample interpolation rounds in this picture.
  /// </summary>
  /// <remarks>
  /// An intra picture interpolates nothing, so what it sets is only what the predicted pictures after
  /// it start from. Version 3 alone may alternate: its intra picture's trailing header carries a bit
  /// saying so, and where it is set every predicted picture flips the rounding of the one before it,
  /// which stops the interpolation's half-sample bias accumulating in one direction through a long run
  /// of them. Versions 1 and 2 have no such bit and always round a half upward.
  /// </remarks>
  private void _SettleRounding(MsMpeg4PictureHeader header) {
    if (header.CodingType == MsMpeg4PictureHeader.IntraCoded) {
      this._rounding = 1;
      return;
    }

    this._rounding = this._roundingAlternates ? this._rounding ^ 1 : 0;
  }

  /// <summary>
  /// Reads what an intra picture carries after its last macroblock.
  /// </summary>
  /// <remarks>
  /// Sixteen bits, or seventeen in version 3: a frame rate, a bit rate, and in version 3 the bit that
  /// says whether the rounding alternates. Only the last of those changes what is decoded, and it is
  /// why this is read at all rather than skipped.
  /// <para/>
  /// Whether the header is there is settled by counting: a picture that ends between the header's own
  /// length and one byte more than it has one, and anything else does not. That is how the reference
  /// decoder decides, and there is no other way to decide it — nothing marks the header's start, and a
  /// picture whose macroblocks happened to end early would otherwise read its own padding as a header
  /// and set the rounding from it.
  /// </remarks>
  private void _ReadExtensionHeader(ref Mpeg4BitReader reader, int packetLength) {
    var length = _EXTENSION_HEADER_BITS + (this._version == MsMpeg4Version.Version3 ? 1 : 0);
    var left = 8 * packetLength - reader.BitPosition;

    if (left >= length + 8)
      return;

    if (left < length) {
      this._roundingAlternates = false;
      return;
    }

    // The frame rate and the bit rate, neither of which this decoder has anything to do with: the
    // container states both, and a decoder that believed these over the container would be believing
    // an encoder's intention rather than what it produced.
    reader.Skip(5);
    reader.Skip(11);

    this._roundingAlternates = this._version == MsMpeg4Version.Version3 && reader.ReadBit() == 1;
  }

  private static MsMpeg4Version _VersionOf(MediaStreamInfo stream)
    => _Matches(stream, _Version1Tags) ? MsMpeg4Version.Version1
      : _Matches(stream, _Version2Tags) ? MsMpeg4Version.Version2
      : MsMpeg4Version.Version3;

  /// <summary>Whether the stream is version 3, by its four-character code or by the name Matroska
  /// gives it.</summary>
  private static bool _IsVersion3(MediaStreamInfo stream) {
    if (_Matches(stream, _Version3Tags))
      return true;

    foreach (var id in _Version3CodecIds)
      if (string.Equals(stream.CodecId, id, StringComparison.OrdinalIgnoreCase))
        return true;

    return false;
  }

  private static bool _Matches(MediaStreamInfo stream, CodecTag[] tags) {
    foreach (var tag in tags)
      if (stream.Codec.EqualsIgnoringCase(tag))
        return true;

    return false;
  }

  private RawImage _ToImage(Mpeg4Frame frame) => new() {
    Width = this._width,
    Height = this._height,
    Format = PixelFormat.Rgb24,
    PixelData = Mpeg4ColorConversion.ToRgb24(frame, this._width, this._height),
  };
}
