using System;
using System.IO;
using FileFormat.Codecs.DnxHd;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Avid DNxHD and DNxHR — SMPTE VC-3 — whose every frame is a whole picture.
/// </summary>
/// <remarks>
/// Written from SMPTE ST 2019-1:2016, <i>VC-3 Picture Compression and Data Stream Format</i>; the
/// clause, table and figure numbers cited throughout these files are that document's. Nothing here
/// is derived from another decoder's source.
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
/// <b>Measured against ffmpeg, on the planes, at the coded depth.</b> Frame by frame, plane by
/// plane, sample by sample, against <c>-pix_fmt yuv422p</c> and <c>yuv422p10le</c> before any
/// reduction to eight bits — and on the planes rather than on packed colour, because this library
/// interpolates chroma where ffmpeg replicates and a comparison of the two would measure that
/// instead of the decode. What it comes to is in these remarks' closing paragraph.
/// <para/>
/// <b>What refuses.</b> A compression identifier Annex C does not define; a header version outside
/// the three the standard defines; a sample depth code 7.2.3 does not define; an interlaced frame,
/// whether field-encoded or the one identifier that codes an interlaced frame with adaptive
/// macroblocks; 4:2:0 sampling; RGB-coded bitstreams; a bitstream carrying an alpha channel; a
/// macroblock whose quantisation scale factor is zero; and any structure whose stated size does not
/// fit inside the one containing it. There is no <c>catch</c> here returning a blank, a copied or a
/// repeated frame.
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

  /// <summary>Decodes one frame, which for a progressive stream is one coding unit.</summary>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var planes = this.DecodePlanes(packet.Data, out var header);

    frame = new() {
      Width = header.SamplesPerLine,
      Height = header.ActiveLines,
      Format = PixelFormat.Rgb24,
      PixelData = DnxHdColorConversion.ToRgb24(planes, header.SamplesPerLine, header.ActiveLines, header.ColorVolume),
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

    var chromaShift = header.SubSampling == 2 ? 0 : 1;
    var planes = DnxHdPlanes.Allocate(
      width: header.WidthInMacroblocks * 16,
      height: header.HeightInMacroblocks * 16,
      chromaShift: chromaShift,
      bitDepth: header.BitDepth);

    DnxHdCodingUnitDecoder.Decode(unit, header, planes);

    return planes;
  }

  /// <summary>
  /// Refuses the arrangements of VC-3 that are described but not decoded here, each by name.
  /// </summary>
  /// <remarks>
  /// Every one of these is a real part of the standard rather than a defect, and every one of them
  /// would decode to a picture if it were read as the nearest thing that is implemented — a
  /// half-height frame for a field-encoded interlaced coding unit, colour planes at the wrong size
  /// for 4:2:0, a colour cast for an RGB bitstream. Those are the pictures a decoder must not hand
  /// back, so each is named instead.
  /// </remarks>
  private void _RefuseWhatIsNotRead(DnxHdFrameHeader header) {
    // 7.2.5, FFE: a progressive frame is always frame-encoded. An interlaced source is field-encoded
    // — two coding units to a frame, each half the height — except for compression ID 1260, which is
    // frame-encoded with macroblocks that choose field or frame coding one at a time (7.2.2, MACF).
    if (header.InterlacedSource || !header.FrameEncoded || header.AdaptiveMacroblocks)
      throw new NotSupportedException(
        $"This VC-3 frame is interlaced (compression ID {header.CompressionIdValue}). Field-encoded coding units and the adaptive macroblock mode of compression ID 1260 are not decoded here, and an interlaced frame is refused rather than returned as a half-height progressive one.");

    if (header.SubSampling == 1)
      throw new NotSupportedException(
        $"This VC-3 frame states 4:2:0 sampling (compression ID {header.CompressionIdValue}). Only 4:2:2 and 4:4:4 are decoded here.");

    if (header.SubSampling == 3)
      throw new InvalidDataException(
        "This VC-3 frame states a sub-sampling control value SMPTE ST 2019-1 7.2.5 does not define.");

    // 7.2.5's colour format flag says a bitstream is coded "using the RGB format rules and tables",
    // but for the two identifiers that may set it the choice is actually made a macroblock at a time
    // — 6.3 and Table 6 put the mode in the macroblock header, so a flagged bitstream whose
    // macroblocks all say luma and colour difference is Y′CbCr throughout. That is what ffmpeg's own
    // DNxHR 4:4:4 encoder writes, and what a container reports as a Y′CbCr pixel format. So the flag
    // alone is not grounds for refusing; the per-macroblock mode is checked where it is read, in
    // <see cref="DnxHdCodingUnitDecoder"/>.
    if (header.Rgb && header.CompressionIdValue is not (1256 or 1270))
      throw new InvalidDataException(
        $"This VC-3 frame sets the RGB colour format flag under compression ID {header.CompressionIdValue}, which SMPTE ST 2019-1 7.2.5 permits only for 1256 and 1270.");

    if (header.Alpha)
      throw new NotSupportedException(
        $"This VC-3 frame carries an alpha channel (compression ID {header.CompressionIdValue}). Its alpha macroblocks are not decoded, and a frame that has them is refused rather than returned with its transparency dropped.");
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
    if (header.SamplesPerLine == this._width && header.ActiveLines == this._height)
      return;

    throw new InvalidDataException(
      $"A VC-3 frame states a raster of {header.SamplesPerLine}x{header.ActiveLines} in a stream the container describes as {this._width}x{this._height}.");
  }
}
