using System;
using System.IO;
using FileFormat.Codecs.Dv;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes DV — the tape format of IEC 61834 and SMPTE 314M — at 25 and 50 Mbit.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's LGPL-2.1-or-later DV decoder (<c>libavcodec/dvdec.c</c>, <c>dv.c</c>,
/// <c>dvdata.c</c>, <c>dv_profile.c</c> and <c>simple_idct.c</c>); provenance and licence are in
/// <c>Codecs/Dv/THIRD-PARTY-NOTICE.FFmpeg.txt</c>.
/// <para/>
/// <b>Nothing about a DV frame is negotiable.</b> It is a recording format: a frame is a whole number
/// of 80-byte DIF blocks because that is what fits between two tape edits, and only six arrangements
/// of them exist at standard definition. A 525/60 frame at 25 Mbit is 120000 bytes and 720x480 and
/// 4:1:1, always; a 625/50 one is 144000 bytes and 720x576 and either 4:2:0 or 4:1:1 depending on
/// which of the two standards wrote it; DVCPRO50 is the same rasters at 4:2:2 in twice the space. So
/// the geometry is not something this decoder works out — it reads which of the six the frame says it
/// is, and a frame that says something else is refused rather than guessed at.
/// <para/>
/// <b>The container is not consulted for it either.</b> A DV frame states its own system and signal
/// type in its header and VAUX packs, which is what lets a single frame be cut out of a tape dump and
/// still decode; the stream's four-character code only says that the stream is DV at all, and it is
/// as likely to be <c>dvsd</c> on a DVCPRO50 file as <c>dv50</c> is.
/// <para/>
/// <b>Blocks are not stored where they are shown.</b> The five macroblocks of a video segment are
/// taken from five widely separated parts of the picture, and the segments walk it in a serpentine —
/// a recording format's answer to a tape dropout, since a lost DIF block then costs five scattered
/// macroblocks rather than a stripe. Within a segment the blocks share a bit pool: a block that
/// overruns its own budget spills into what its neighbours left, which takes three passes to unpick.
/// <para/>
/// <b>Measured against FFmpeg, on the planes.</b> Frame by frame, plane by plane, sample by sample,
/// against <c>-pix_fmt yuv411p</c>, <c>yuv420p</c> and <c>yuv422p</c> before any colour conversion —
/// on the planes rather than on RGB, because comparing packed colour measures two colour conversions
/// rather than the decode. Every profile below is identical to FFmpeg's output on every sample.
/// <para/>
/// <b>What refuses.</b> DVCPRO HD — the 100 Mbit 1080i and 720p profiles of SMPTE 370M — is refused
/// by name: its macroblocks carry eight blocks rather than six and its quantiser is a different
/// table, and reading one as the standard-definition profile with the same flags would produce a
/// picture rather than an error. A signal type no standard defines, a packet shorter than the profile
/// it claims, and a frame whose raster is not the one the container described are refused too.
/// </remarks>
public sealed class DvVideoDecoder : IVideoCodecDecoder<DvVideoDecoder> {

  /// <summary>
  /// The codes this codec is named by.
  /// </summary>
  /// <remarks>
  /// A long list for one bitstream, because DV reached files through a dozen capture cards and each
  /// registered its own code. None of them changes what is decoded — <c>dv50</c> and <c>dvsd</c> name
  /// the same syntax and the frame itself says which profile it is in — so they are simply the
  /// spellings a file may carry.
  /// </remarks>
  private static readonly CodecTag[] _Tags = [
    CodecTag.FromCharacters("dvsd"), // the generic code, whatever the profile
    CodecTag.FromCharacters("dv25"), // 25 Mbit, stated
    CodecTag.FromCharacters("dv50"), // DVCPRO50
    CodecTag.FromCharacters("dvsl"), // DV in long-play mode
    CodecTag.FromCharacters("dvc "), // Apple's DV
    CodecTag.FromCharacters("dvcs"), // Apple's DVCPRO
    CodecTag.FromCharacters("cdvc"), // Canopus DV
    CodecTag.FromCharacters("CDV5"), // Canopus DVCPRO50
    CodecTag.FromCharacters("dvis"), // Pinnacle
    CodecTag.FromCharacters("pdvc"), // Pinnacle DVCPRO
    CodecTag.FromCharacters("SL25"), // SoftLab-NSK 625/50
    CodecTag.FromCharacters("SLDV"), // SoftLab-NSK
    // The high-definition codes are claimed so that a DVCPRO HD file is refused by name rather than
    // reaching the registry's "nothing decodes this", which says nothing about what is missing.
    CodecTag.FromCharacters("dvhd"),
    CodecTag.FromCharacters("dvh1"),
    CodecTag.FromCharacters("CDVH"),
  ];

  /// <summary>The names Matroska and QuickTime give this codec where they name codecs with text.</summary>
  private static readonly string[] _CodecIds = [
    "V_MS/VFW/FOURCC/dvsd",
    "V_DV",
    "dvsd",
    "dvc ",
    "dvcp",
  ];

  private readonly int _width;
  private readonly int _height;
  private readonly int[] _factors = DvSegmentDecoder.BuildFactors();
  private readonly DvSegmentDecoder.Scratch _scratch = new();

  private DvProfile? _profile;
  private DvGeometry.Segment[] _segments = [];

  private DvVideoDecoder(int width, int height) {
    this._width = width;
    this._height = height;
  }

  public static string CodecName => "DV (IEC 61834 / SMPTE 314M)";

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
  /// Builds a decoder from the stream description, which for this codec says almost nothing.
  /// </summary>
  /// <remarks>
  /// The raster is kept only to check the frames against it. Everything a DV frame needs is in the
  /// frame, and a container that disagrees with it is a file that was cut or repackaged wrongly — but
  /// a container that states nothing at all is common enough in raw DIF dumps that it is allowed, and
  /// then the frame is simply believed.
  /// </remarks>
  public static DvVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width < 0 || stream.Height < 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can be decoded into.");

    return new(stream.Width, stream.Height);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var planes = this.DecodePlanes(packet.Data.Span, out var profile);

    frame = new() {
      Width = profile.Width,
      Height = profile.Height,
      Format = PixelFormat.Rgb24,
      PixelData = DvColorConversion.ToRgb24(planes),
    };

    return true;
  }

  /// <summary>
  /// Decodes one frame as far as its component planes, before any colour conversion.
  /// </summary>
  /// <remarks>
  /// This is where a comparison against another decoder has to be made. The planes are the output of
  /// the decoding process; packing them into RGB is a display convention that two correct decoders
  /// are free to disagree about.
  /// </remarks>
  internal DvPlanes DecodePlanes(ReadOnlySpan<byte> frame, out DvProfile profile) {
    profile = DvProfile.Identify(frame);

    if (frame.Length < profile.FrameSize)
      throw new InvalidDataException(
        $"This packet states the {profile.Name} profile, whose frame is {profile.FrameSize} bytes; the packet is {frame.Length}.");

    if ((this._width != 0 && this._width != profile.Width) || (this._height != 0 && this._height != profile.Height))
      throw new InvalidDataException(
        $"A DV frame states the {profile.Name} profile, a raster of {profile.Width}x{profile.Height}, "
        + $"in a stream the container describes as {this._width}x{this._height}.");

    // The segment layout depends only on the profile, and a stream almost never changes profile mid
    // way — but one that does is legal and costs only this rebuild.
    if (this._profile != profile) {
      this._segments = DvGeometry.Segments(profile);
      this._profile = profile;
    }

    var planes = DvPlanes.Allocate(profile);
    var coded = frame[..profile.FrameSize];
    foreach (var segment in this._segments)
      DvSegmentDecoder.Decode(coded, profile, segment, this._factors, planes, this._scratch);

    return planes;
  }
}
