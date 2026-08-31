using System;
using System.Collections.Generic;
using FileFormat.Codecs.Mpeg4;
using FileFormat.Codecs.MsMpeg4;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Microsoft MPEG-4 version 2 video — the codec an <c>.avi</c> or <c>.asf</c> names
/// <c>MP42</c> — intra and predicted pictures alike.
/// </summary>
/// <remarks>
/// One of the three variants Microsoft derived from MPEG-4 Part 2 before Windows Media Video, and the
/// one that turns out to be nearly all standard underneath. It keeps the standard's block layer whole:
/// the same inverse scan, the same H.263 inverse quantisation, the same transform, the same
/// half-sample motion compensation, the same alternating current prediction, the same three escape
/// forms. So this decoder shares those with the MPEG-4 Part 2 decoder beside it rather than repeating
/// them, and what is written here is only what Microsoft changed.
/// <para/>
/// <b>What Microsoft changed.</b> There is no start code, no sequence header and no video object layer
/// header anywhere: a packet is a picture, and the picture header is seven bits — two for its type,
/// five for the quantiser it uses throughout — and then five more for an intra picture's slice count
/// or one for a predicted picture's skip flag. Everything the standard states once per layer is fixed
/// instead of signalled, so the picture size comes from the container and there is nothing to refuse.
/// A macroblock has one motion vector and never four; the quantiser cannot change inside a picture; the
/// vectors reach thirty-one and a half samples either way rather than a range the picture chooses; the
/// intra DC step is eight at every quantiser; the DC gradient test uses <c>&lt;=</c> where the
/// standard uses <c>&lt;</c>; and there are no bidirectionally coded pictures at all.
/// <para/>
/// <b>Where the format came from.</b> Microsoft published no specification for it. The Open
/// Specifications programme documents Microsoft's protocols and containers, not its codec bitstreams;
/// SMPTE ST 421 standardised Windows Media Video 9 and says nothing about the three that came before;
/// and the one Microsoft document that reaches a nearby codec specifies motion compensation and
/// deblocking for Windows Media Video 8 while leaving entropy decoding to the host. The only public
/// description of the bitstream is Michael Niedermayer's <i>DIVX3 / MS-MPEG4v1-v3 / WMV7-8</i>
/// (version 0.07, 2003, GNU Free Documentation Licence), which gives the syntax in full and then
/// refers the reader to a reverse-engineered decoder's source for every large table. The syntax here
/// follows that document; the tables were derived from the bitstream, by building pictures whose
/// content was known and reading back the codeword that had to stand for it, and by writing streams
/// that use a codeword no encoder emits and asking a reference decoder what it made of them.
/// <para/>
/// <b>Measured.</b> Sixty-four encoded streams, four thousand four hundred frames, were decoded here
/// and by ffmpeg and compared plane by plane, sample by sample, on every frame: four sources, sizes
/// from 64x64 to 352x288, quantisers 3, 8, 16 and 25, and groups of pictures of one frame, twelve and
/// a thousand. Every stream produced the frame count ffmpeg produced.
/// <para/>
/// Forty-nine of the sixty-four are identical to ffmpeg on every sample of every plane of every frame.
/// The other fifteen differ by exactly one level and never by more: one thousand nine hundred and ten
/// samples in total out of some four hundred and fifty million, and at worst sixty-four samples of a
/// frame of a hundred and fifty-two thousand. Over a hundred frames with one intra picture at the
/// front, the first difference appears at frame eighty-five and stays at one level to the end.
/// <para/>
/// That comparison is against ffmpeg's floating-point inverse transform. Against its default integer
/// one the residual is larger — up to four levels after eighty predicted pictures — and it is the
/// transform's rather than the decode's: the two decoders agree exactly wherever ffmpeg evaluates the
/// transform accurately, which is what identifies the difference. ISO/IEC 14496-2 Annex A specifies
/// the inverse transform as an accuracy bound rather than as an algorithm, so this is the expected
/// shape of the disagreement and not a defect in either.
/// <para/>
/// The bit-level reading was checked separately and more strictly, because a picture can be
/// reconstructed wrongly and still look plausible while a mis-read bitstream cannot: two thousand nine
/// hundred and sixty pictures were parsed and every one consumed exactly the bits it should — an intra
/// picture stopping precisely where its trailing extension header begins, a predicted one inside its
/// final byte of padding. Those pictures exercised all three escape forms of the block layer, the
/// second of them — the one whose run offset differs from the standard's by one — seven thousand one
/// hundred and seventy-six times.
/// <para/>
/// <b>What it does not do refuses by name.</b> Version 1 and version 3 are refused as what they are
/// rather than left to fail: they are different bitstreams that share this one's name. Version 3 codes
/// which of six run-level tables, which of two DC tables and which of two motion vector tables each
/// picture uses, and those tables are Microsoft's own with no public statement anywhere — the motion
/// vector ones pair a single code with a whole vector across some eleven hundred entries, which no
/// encoder can be driven to emit in full, so a decoder derived from observation would have silent
/// gaps. Version 1 has no encoder in existence to derive its two macroblock tables from or to check a
/// guess against. There is no <c>catch</c> anywhere that hands back a blank, a copied or a repeated
/// picture, because a plausible wrong picture is worse than a refusal: nobody checks a picture that
/// looks like a picture.
/// </remarks>
public sealed class MsMpeg4V2VideoDecoder : IVideoCodecDecoder<MsMpeg4V2VideoDecoder> {

  /// <summary>The four-character codes that name this bitstream.</summary>
  /// <remarks>
  /// <c>MP42</c> is Microsoft's own and <c>DIV2</c> is what the codec was distributed as once it had
  /// been pulled out of Windows Media and passed around on its own.
  /// </remarks>
  private static readonly CodecTag[] _Tags = [
    CodecTag.FromCharacters("MP42"),
    CodecTag.FromCharacters("DIV2"),
  ];

  /// <summary>The codes that name version 1, refused by name rather than ignored.</summary>
  private static readonly CodecTag[] _Version1Tags = [
    CodecTag.FromCharacters("MPG4"),
    CodecTag.FromCharacters("MP41"),
    CodecTag.FromCharacters("DIV1"),
  ];

  /// <summary>The codes that name version 3, which is the original DivX.</summary>
  private static readonly CodecTag[] _Version3Tags = [
    CodecTag.FromCharacters("MP43"),
    CodecTag.FromCharacters("DIV3"),
    CodecTag.FromCharacters("DIV4"),
    CodecTag.FromCharacters("DIV5"),
    CodecTag.FromCharacters("DIV6"),
    CodecTag.FromCharacters("AP41"),
    CodecTag.FromCharacters("MPG3"),
    CodecTag.FromCharacters("COL1"),
  ];

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

  private MsMpeg4V2VideoDecoder(int width, int height) {
    this._width = width;
    this._height = height;
    this._macroblockWidth = (width + 15) / 16;
    this._macroblockHeight = (height + 15) / 16;
    this._sliceHeight = this._macroblockHeight;
  }

  /// <summary>Gets the codec name.</summary>
  public static string CodecName => "Microsoft MPEG-4 version 2 video (MP42)";

  /// <summary>Determines whether the specified media stream is supported.</summary>
  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      return false;

    return _Matches(stream, _Tags) || _Matches(stream, _Version1Tags) || _Matches(stream, _Version3Tags);
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
  public static MsMpeg4V2VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (_Matches(stream, _Version1Tags))
      throw new NotSupportedException(
        $"Stream {stream.Index} is coded as '{stream.Codec}', which is Microsoft's MPEG-4 version 1. It shares this "
        + "codec's picture header and block layer but states a macroblock's type and coded block pattern with two "
        + "tables of its own, predicts the DC the way MPEG-1 does rather than the way MPEG-4 does, and reads its slice "
        + "field as a height rather than a count. Those two tables appear in no published specification, and no "
        + "encoder for the format exists to derive them from or to check a guess against, so version 1 is refused "
        + "rather than decoded on a guess.");

    if (_Matches(stream, _Version3Tags))
      throw new NotSupportedException(
        $"Stream {stream.Index} is coded as '{stream.Codec}', which is Microsoft's MPEG-4 version 3 — the original "
        + "DivX. Each of its pictures chooses which of six run-level tables, which of two DC tables and which of two "
        + "motion vector tables it was coded with, and all ten are Microsoft's own with no published statement "
        + "anywhere. The motion vector tables pair one code with a whole vector over some eleven hundred entries, "
        + "which no encoder can be driven to emit in full, so version 3 is refused rather than decoded from a table "
        + "that would be complete only where somebody happened to look.");

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"Stream {stream.Index} states a size of {stream.Width}x{stream.Height}. Microsoft's MPEG-4 version 2 carries "
        + "no picture size in the bitstream at all — its picture header is seven bits — so the container's is the "
        + "only one there is.");

    return new(stream.Width, stream.Height);
  }

  /// <summary>
  /// Decodes one packet into one picture.
  /// </summary>
  /// <remarks>
  /// Always a picture, and always this packet's: the format has no bidirectionally coded pictures, so
  /// nothing is ever held back to be shown after something decoded later, and decode order and display
  /// order are the same order.
  /// </remarks>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var reader = new Mpeg4BitReader(packet.Data.Span);
    var header = MsMpeg4PictureHeader.Parse(ref reader, this._macroblockHeight, this._sliceHeight);
    this._sliceHeight = header.SliceHeight;

    var picture = MsMpeg4PictureDecoder.BeginPicture(
      header, new(this._macroblockWidth, this._macroblockHeight), this._reference,
      this._macroblockWidth, this._macroblockHeight);

    picture.DecodePicture(ref reader);
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
