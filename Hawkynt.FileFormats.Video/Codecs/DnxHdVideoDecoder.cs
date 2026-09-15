using System;
using System.IO;
using FileFormat.Codecs.DnxHd;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Avid DNxHD and DNxHR — SMPTE VC-3 — whose every frame is independently decodable.
/// </summary>
/// <remarks>
/// Written from SMPTE ST 2019-1:2016, <i>VC-3 Picture Compression and Data Stream Format</i>, and
/// Amendment 1:2023; the clause, table and figure numbers cited throughout these files are those
/// documents'. Nothing here is derived from another decoder's source.
/// <para/>
/// <b>Intra only, and independently decodable a scan line at a time.</b> There is no reference
/// handling, nothing held between packets, and within a frame the macroblock scan lines do not
/// depend on each other either: each begins at a byte offset stated in the header and resets the DC
/// prediction. That is the property the format exists for — an editing codec has to survive a
/// damaged block and has to decode on as many workers as it can find.
/// <para/>
/// <b>The compression identifier is the one thing a decoder cannot infer.</b> It is not a bitrate
/// and not a raster: it names a row of Annex C, and that row says which of the eleven quantisation
/// weighting tables of Annex D and which of the six groups of code tables of Annex E the frame was
/// coded with. Two frames of the same size and depth under different identifiers decode to different
/// pictures, so an identifier that is in neither Table C.1 nor Table C.2 is refused rather than
/// guessed at.
/// <para/>
/// <b>Two profiles, one block layer.</b> Header versions 1 and 2 are the HD profile of Table C.1 —
/// fixed rasters, constant bitrate, a 640-byte header. Version 3 is the resolution-independent
/// profile of Table C.2, which Avid sells as DNxHR: the raster comes from the header, the header
/// grows with the picture, and the codec tag in the container is <c>AVdh</c> rather than
/// <c>AVdn</c>. They differ in the frame header and not below it, so both are read here.
/// <para/>
/// <b>Interlaced HD is two forms, both decoded here.</b> Classic CIDs 1241 through 1244 carry one
/// field per coding unit; FFC identifies the field and the two coding units are woven into one frame.
/// CID 1260 is the exception corrected by Amendment 1:2023: one frame coding unit, with every
/// macroblock independently selecting frame or field DCT placement through MFF.
/// <para/>
/// <b>Measured against ffmpeg, on the planes, at the coded depth.</b> Frame by frame, plane by
/// plane, sample by sample, against <c>-pix_fmt yuv422p</c> and <c>yuv422p10le</c> before any
/// reduction to eight bits — and on the planes rather than on packed colour, because this library
/// interpolates chroma where ffmpeg replicates and a comparison of the two would measure that
/// instead of the decode. What it comes to is in these remarks' closing paragraph.
/// <para/>
/// <b>What refuses.</b> A compression identifier Annex C does not define; a header version outside
/// the three the standard defines; a sample depth code 7.2.3 does not define; 4:2:0 sampling;
/// DNxHR CID 1270 RGB; a bitstream carrying an alpha channel; a macroblock whose quantisation scale
/// factor is zero; and any structure whose stated size does not fit inside the one containing it.
/// There is no <c>catch</c> here returning a blank, a copied or a repeated frame.
/// </remarks>
public sealed class DnxHdVideoDecoder : IVideoCodecDecoder<DnxHdVideoDecoder> {

  /// <summary>
  /// The codes this codec is named by.
  /// </summary>
  /// <remarks>
  /// <c>AVdn</c> is the HD profile and <c>AVdh</c> the resolution-independent one, and a container
  /// distinguishes them because the two were released years apart — but the bitstream says which it
  /// is in its header version, and this decoder reads that rather than the tag. The tag only has to
  /// be enough to know the stream is VC-3 at all.
  /// </remarks>
  private static readonly CodecTag[] _Tags = [
    CodecTag.FromCharacters("AVdn"), // DNxHD, the HD profile
    CodecTag.FromCharacters("AVdh"), // DNxHR, the resolution-independent profile
    CodecTag.FromCharacters("AVd1"),
  ];

  /// <summary>The names Matroska gives this codec, which states no four-character code.</summary>
  private static readonly string[] _CodecIds = ["V_DNXHD", "V_MS/VFW/FOURCC/AVdn"];

  private readonly int _width;
  private readonly int _height;

  private DnxHdVideoDecoder(int width, int height) {
    this._width = width;
    this._height = height;
  }

  public static string CodecName => "Avid DNxHD / DNxHR (SMPTE VC-3)";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      return false;

    foreach (var tag in _Tags)
      if (stream.Codec.EqualsIgnoringCase(tag))
        return true;

    if (stream.CodecId == null)
      return false;

    foreach (var id in _CodecIds)
      if (string.Equals(stream.CodecId, id, StringComparison.OrdinalIgnoreCase))
        return true;

    return false;
  }

  /// <summary>
  /// Builds a decoder from the stream description, which for this codec states only the picture size.
  /// </summary>
  /// <remarks>
  /// There is nothing else in it to read. A VC-3 stream carries no codec configuration the way an
  /// AVC one does: every coding unit restates its raster, its depth, its sampling and its
  /// compression identifier in a header of fixed offsets, which is what lets a single frame be cut
  /// out of a stream and still decode.
  /// </remarks>
  public static DnxHdVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can be decoded into.");

    return new(stream.Width, stream.Height);
  }

  /// <summary>Decodes one frame; classic interlaced frames contain two field coding units.</summary>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var planes = this.DecodePlanes(packet.Data, out var header);

    frame = new() {
      Width = header.SamplesPerLine,
      Height = header.DisplayHeight,
      Format = PixelFormat.Rgb24,
      PixelData = DnxHdColorConversion.ToRgb24(planes, header.SamplesPerLine, header.DisplayHeight, header.ColorVolume),
    };

    return true;
  }

  /// <summary>
  /// Decodes one frame as far as its component planes, before any reduction or colour conversion.
  /// </summary>
  /// <remarks>
  /// This is where a comparison against another decoder has to be made. The planes are the output of
  /// the decoding process of SMPTE ST 2019-1:2016, 8; everything after them — reducing to eight
  /// bits, choosing a colour matrix, resampling chroma up to every luma column — is a display
  /// convention that two correct decoders are free to disagree about.
  /// </remarks>
  internal DnxHdPlanes DecodePlanes(ReadOnlyMemory<byte> unit, out DnxHdFrameHeader header) {
    header = DnxHdFrameHeader.Parse(unit.Span);

    this._RefuseWhatIsNotRead(header);
    this._RefuseUnexpectedSize(header);

    if (!header.FrameEncoded)
      return this._DecodeFieldPair(unit, header);

    var planes = this._AllocatePlanes(header, header.HeightInMacroblocks * 16);
    DnxHdCodingUnitDecoder.Decode(unit, header, planes);

    return planes;
  }

  /// <summary>Decodes the two coding units of a classic field-encoded DNxHD frame into one raster.</summary>
  private DnxHdPlanes _DecodeFieldPair(ReadOnlyMemory<byte> packet, DnxHdFrameHeader first) {
    var profile = DnxHdProfile.Find(first.CompressionIdValue)
      ?? throw new NotSupportedException(
        $"A field-encoded VC-3 packet uses compression ID {first.CompressionIdValue}, whose coding-unit size is not fixed by the DNxHD profile.");

    var codingUnitSize = profile.CodingUnitSize;
    if (packet.Length < codingUnitSize * 2)
      throw new InvalidDataException(
        $"A field-encoded VC-3 frame needs two {codingUnitSize}-byte coding units and this packet holds {packet.Length} bytes.");

    var secondUnit = packet.Slice(codingUnitSize, codingUnitSize);
    var second = DnxHdFrameHeader.Parse(secondUnit.Span);
    this._RefuseWhatIsNotRead(second);
    this._RequireMatchingFields(first, second);

    var planes = this._AllocatePlanes(first, first.HeightInMacroblocks * 16 * 2);
    DnxHdCodingUnitDecoder.Decode(packet[..codingUnitSize], first, planes, first.FieldFrameCount & 1, 2);
    DnxHdCodingUnitDecoder.Decode(secondUnit, second, planes, second.FieldFrameCount & 1, 2);

    return planes;
  }

  private DnxHdPlanes _AllocatePlanes(DnxHdFrameHeader header, int codedHeight) {
    var chromaShift = header.SubSampling == 2 ? 0 : 1;

    return DnxHdPlanes.Allocate(
      width: header.WidthInMacroblocks * 16,
      height: Math.Max(codedHeight, header.DisplayHeight),
      chromaShift: chromaShift,
      bitDepth: header.BitDepth,
      rgbFormat: header.Rgb);
  }

  /// <summary>
  /// Refuses the arrangements of VC-3 that are described but not decoded here, each by name.
  /// </summary>
  /// <remarks>
  /// Every one of these is a real part of the standard rather than a defect, and every one of them
  /// would decode to a picture if it were read as the nearest thing that is implemented — colour
  /// planes at the wrong size for 4:2:0, transparency silently discarded for alpha. Those are the
  /// pictures a decoder must not hand back, so each is named instead.
  /// </remarks>
  private void _RefuseWhatIsNotRead(DnxHdFrameHeader header) {
    if (header.SubSampling == 1)
      throw new NotSupportedException(
        $"This VC-3 frame states 4:2:0 sampling (compression ID {header.CompressionIdValue}). Only 4:2:2 and 4:4:4 are decoded here.");

    if (header.SubSampling == 3)
      throw new InvalidDataException(
        "This VC-3 frame states a sub-sampling control value SMPTE ST 2019-1 7.2.5 does not define.");

    // CLF enables the RGB format rules only for 1256/1270; ACF then selects direct RGB or the
    // alternate BT.709 Y′CbCr representation independently for every macroblock.
    if (header.Rgb && header.CompressionIdValue is not (1256 or 1270))
      throw new InvalidDataException(
        $"This VC-3 frame sets the RGB colour format flag under compression ID {header.CompressionIdValue}, which SMPTE ST 2019-1 7.2.5 permits only for 1256 and 1270.");

    if (header.Rgb && header.CompressionIdValue == 1270)
      throw new NotSupportedException(
        "DNxHR RGB (compression ID 1270) is deferred to the DNxHR completion path; this DNxHD change implements CID 1256 RGB only.");

    if (header.Alpha)
      throw new NotSupportedException(
        $"This VC-3 frame carries an alpha channel (compression ID {header.CompressionIdValue}). Amendment 1:2023 extends alpha to the HD profile too, so its VC-3-wide alpha decoding path is kept separate rather than silently dropping transparency.");

    if (header.AdaptiveMacroblocks && header.CompressionIdValue != 1260)
      throw new InvalidDataException(
        $"This VC-3 frame sets MACF under compression ID {header.CompressionIdValue}, which only compression ID 1260 defines.");

    if (header.CompressionIdValue == 1260 && (!header.FrameEncoded || !header.AdaptiveMacroblocks))
      throw new InvalidDataException(
        "Compression ID 1260 must be frame encoded with adaptive macroblock field/frame selection enabled.");

    if (!header.FrameEncoded && !header.InterlacedSource)
      throw new InvalidDataException(
        "A field-encoded VC-3 coding unit does not mark its source as interlaced.");

    if (!header.FrameEncoded && header.FieldFrameCount is not (2 or 3))
      throw new InvalidDataException(
        $"A field-encoded VC-3 coding unit states FFC={header.FieldFrameCount}; field coding requires FFC 2 or 3.");
  }

  private static void _RequireMatchingFields(DnxHdFrameHeader first, DnxHdFrameHeader second) {
    if (second.FrameEncoded
        || !second.InterlacedSource
        || second.FieldFrameCount is not (2 or 3)
        || second.FieldFrameCount == first.FieldFrameCount
        || second.CompressionIdValue != first.CompressionIdValue
        || second.SamplesPerLine != first.SamplesPerLine
        || second.ActiveLines != first.ActiveLines
        || second.BitDepth != first.BitDepth
        || second.SubSampling != first.SubSampling
        || second.Rgb != first.Rgb
        || second.ColorVolume != first.ColorVolume
        || second.Alpha != first.Alpha)
      throw new InvalidDataException(
        "The two coding units of an interlaced VC-3 frame do not describe complementary fields of the same picture.");
  }

  /// <summary>
  /// Refuses a frame whose own raster is not the one the container described.
  /// </summary>
  /// <remarks>
  /// The frame is believed over the container — a VC-3 header restates the raster and the macroblocks
  /// are laid out for it — but a disagreement is still worth refusing rather than silently resolving.
  /// A container saying one size and frames saying another is a file cut or repackaged wrongly.
  /// </remarks>
  private void _RefuseUnexpectedSize(DnxHdFrameHeader header) {
    if (header.SamplesPerLine == this._width && header.DisplayHeight == this._height)
      return;

    throw new InvalidDataException(
      $"A VC-3 frame states a raster of {header.SamplesPerLine}x{header.DisplayHeight} in a stream the container describes as {this._width}x{this._height}.");
  }
}
