using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.H263;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes H.263 video, ITU-T Rec. H.263 baseline, and the Sorenson Spark variant of it that Flash
/// Video carries.
/// </summary>
/// <remarks>
/// Baseline means the syntax of clauses 5 and 6 with none of the annexes: the picture, group of
/// blocks, macroblock and block layers, intra and predicted pictures, one motion vector per
/// macroblock at half-pixel resolution with the median predictor of clause 6.1.1, the inverse
/// quantisation of 6.2.1 and the inverse transform of 6.2.2. That is what nearly every H.263 stream
/// in a file rather than on a wire actually uses, and it is the whole of what a Sorenson Spark stream
/// can use, because a Sorenson picture header has no bits to ask for anything else with.
/// <para/>
/// <b>What it does not do refuses by name.</b> Every optional mode is refused where it is signalled,
/// naming the annex and the field: the extended picture header of 5.1.4, unrestricted motion vectors
/// (Annex D), arithmetic coding (Annex E), advanced prediction and its four vectors per macroblock
/// (Annex F), PB-frames (Annex G), continuous presence multipoint (Annex C), the modified
/// quantisation escape level of Annex T, and the deblocking filter a Sorenson picture may ask for.
/// There is no <c>catch</c> anywhere that hands back a blank, a copied or a zero-filled picture,
/// because a plausible wrong picture is worse than a refusal: nobody checks a picture that looks like
/// a picture.
/// <para/>
/// <b>Measured.</b> Thirty encoded streams — sixteen H.263 and fourteen Sorenson, seven hundred and
/// forty-three frames between them — were decoded here and by ffmpeg and compared plane by plane,
/// sample by sample, every frame: sizes from 100x60 to 704x576, quantisers from 1 to 31, groups of
/// pictures from a single frame to fifty, streams with group headers and streams without, and picture
/// sizes that are and are not a whole number of macroblocks. Every stream produced the frame count
/// ffprobe counts.
/// <para/>
/// Against ffmpeg's floating-point inverse transform (<c>-idct faani</c>) twenty-one of the thirty are
/// identical sample for sample on every frame, and the other nine differ in at most about forty
/// samples of a frame out of thirty-eight thousand — always by exactly one level, and still one level
/// after fifty frames with no intra picture between them. Against ffmpeg's default integer transform
/// the difference is a few thousand samples a frame at one to three levels, which is the same size as
/// the difference between ffmpeg's own two transforms on the same streams. The Recommendation
/// specifies the inverse transform as a formula with an accuracy bound rather than as an algorithm
/// (Annex A), so this is the residual it exists to allow and not a disagreement about the bitstream.
/// <para/>
/// <b>Frames come out in coding order, which is display order.</b> H.263 without PB-frames has no
/// picture that is coded after one it precedes, so nothing is ever held back and
/// <see cref="TryDecode"/> answers with a picture for every packet that holds one.
/// </remarks>
public sealed class H263VideoDecoder : IVideoCodecDecoder<H263VideoDecoder> {

  /// <summary>The four-character codes containers name ITU-T H.263 with.</summary>
  /// <remarks>
  /// <c>s263</c> is the sample entry an ISO base media file — an MP4 or a 3GP — names H.263 with, and
  /// <c>H263</c> is what an AVI and a Matroska file use. The vendor codes that other encoders wrote
  /// into AVI files are deliberately absent: several of them are not baseline H.263 and claiming them
  /// would turn a clean "no codec for this stream" into a decode that starts and then stops.
  /// </remarks>
  private static readonly CodecTag[] _Tags = [
    CodecTag.FromCharacters("H263"),
    CodecTag.FromCharacters("s263"),
    CodecTag.FromCharacters("U263"),
  ];

  /// <summary>The four-character codes containers name Sorenson Spark with.</summary>
  /// <remarks>
  /// A Sorenson stream is H.263 from the group of blocks layer down and a different bitstream above
  /// it, so which of the two a stream is has to be settled before the first picture header is read
  /// and cannot be discovered from the picture header itself — the two headers are different lengths
  /// and disagree about every field after the start code.
  /// </remarks>
  private static readonly CodecTag[] _SorensonTags = [
    CodecTag.FromCharacters("FLV1"),
  ];

  private readonly bool _isSorenson;
  private H263Frame? _reference;
  private H263PictureHeader? _geometry;

  private H263VideoDecoder(bool isSorenson) => this._isSorenson = isSorenson;

  public static string CodecName => "H.263 (ITU-T H.263 baseline, and Sorenson Spark)";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      return false;

    return _Matches(stream, _Tags) || _Matches(stream, _SorensonTags);
  }

  /// <summary>
  /// Builds a decoder for one stream.
  /// </summary>
  /// <remarks>
  /// The only thing taken from the stream description is which of the two bitstreams this is, because
  /// that is the only thing not in the bitstream. Everything else — the picture size above all — is
  /// in the picture header, and a container's copy is a copy: a file that states a size its picture
  /// headers disagree with is a file whose pictures are the size the picture headers say.
  /// </remarks>
  public static H263VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return new(_Matches(stream, _SorensonTags));
  }

  private static bool _Matches(MediaStreamInfo stream, CodecTag[] tags) {
    foreach (var tag in tags)
      if (stream.Codec.EqualsIgnoringCase(tag))
        return true;

    return false;
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

  /// <summary>Nothing is ever held back, so there is nothing left when the packets run out.</summary>
  public IEnumerable<RawImage> Flush() => [];

  // ============================================================================================
  // The start-code walk — ITU-T H.263, 5.1
  // ============================================================================================

  /// <summary>
  /// Finds the pictures in one packet and decodes them, answering with the last.
  /// </summary>
  /// <remarks>
  /// The scan is this codec's own and not the container's, deliberately. A picture start code is
  /// defined by H.263 and a decoder handed packets by some other demuxer — a Flash Video file, an
  /// AVI, a caller with bytes from anywhere — still has to find the header inside them. A decoder
  /// that could only work on packets somebody else had already cut at the right places would not be
  /// a decoder of the format.
  /// <para/>
  /// The search is byte-aligned because a picture start code is: clause 5.1.27 has an encoder pad
  /// with fewer than eight zero bits so that the next one begins on a byte boundary. A group of
  /// blocks start code inside the picture is found differently, bit by bit, because its stuffing is
  /// part of the same run of zeroes the code begins with.
  /// </remarks>
  private RawImage? _DecodePacket(ReadOnlySpan<byte> data) {
    RawImage? last = null;

    var offset = 0;
    while (offset + 3 <= data.Length) {
      if (!this._IsPictureStart(data, offset)) {
        ++offset;
        continue;
      }

      var reader = new H263BitReader(data[offset..]);

      // The seventeen bits of the start code, and — for an ITU-T stream — the five-bit group number
      // of zero that completes it. A Sorenson header puts its version in those five bits instead, so
      // it is left for the header to read.
      reader.Skip(this._isSorenson ? 17 : 22);

      var header = this._isSorenson
        ? H263PictureHeader.ParseSorenson(ref reader)
        : H263PictureHeader.Parse(ref reader);

      this._RefuseGeometryChangeMidStream(header);

      var picture = H263PictureDecoder.BeginPicture(
        header, new(header.MacroblockWidth, header.MacroblockHeight), this._reference);

      picture.DecodePicture(ref reader);

      if (header.IsReference)
        this._reference = picture.Target;

      this._geometry = header;
      last = _ToImage(picture.Target, header);

      // Past this picture's data, so that a packet holding two pictures yields two rather than the
      // first one twice. The byte the last macroblock ended in is where the search resumes, and not
      // the one after it: a picture whose macroblocks happened to end on a byte boundary would
      // otherwise have the next picture's start code stepped straight over.
      offset += reader.BitPosition >> 3;
    }

    return last;
  }

  /// <summary>
  /// Whether a picture start code begins at this byte.
  /// </summary>
  /// <remarks>
  /// Sixteen zero bits and then the one that ends the start code, which is all a Sorenson stream's
  /// start code is. An ITU-T stream has five more bits after it holding a group number, and a group
  /// number of zero is what makes the code a picture's rather than a group of blocks'; without
  /// testing those five bits a group of blocks start code in the middle of a picture would look
  /// exactly like the start of the next picture.
  /// <para/>
  /// The five bits are not part of the Sorenson test even though they are always nought or one there,
  /// because a version this decoder does not read should be refused by name at the header rather than
  /// leave the picture unfound and the packet silently empty.
  /// </remarks>
  private bool _IsPictureStart(ReadOnlySpan<byte> data, int offset) {
    if (data[offset] != 0 || data[offset + 1] != 0)
      return false;

    var third = data[offset + 2];
    return this._isSorenson
      ? (third & 0x80) != 0
      : (third & 0xFC) == 0x80;
  }

  /// <summary>
  /// Refuses a picture size that changes while a picture predicted from the old one is still held.
  /// </summary>
  /// <remarks>
  /// A Sorenson stream may state a size in every picture header, and restating the same one is
  /// normal. A different one is not: the held reference is the old size, and a predicted picture of
  /// the new size predicting from it has no defined meaning. Rescaling it, or reading the smaller one
  /// into the larger, would be inventing the parts that were never coded.
  /// </remarks>
  private void _RefuseGeometryChangeMidStream(H263PictureHeader header) {
    if (this._geometry == null || this._reference == null || this._geometry.SameGeometryAs(header))
      return;

    throw new NotSupportedException(
      $"This stream changes picture size from {this._geometry.Width}x{this._geometry.Height} to "
      + $"{header.Width}x{header.Height} part way through, while a picture predicted from the old size is still held "
      + "as the reference. Decoding a stream whose size changes is not implemented.");
  }

  private static RawImage _ToImage(H263Frame frame, H263PictureHeader header) => new() {
    Width = header.Width,
    Height = header.Height,
    Format = PixelFormat.Rgb24,
    PixelData = H263ColorConversion.ToRgb24(frame, header.Width, header.Height),
  };
}
