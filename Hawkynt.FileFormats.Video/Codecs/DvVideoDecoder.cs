using System;
using System.IO;
using FileFormat.Codecs.Dv;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes the DV family profiles implemented here: IEC 61834-2/SMPTE 314M at standard definition
/// and SMPTE 370M DVCPRO HD at 100 Mbit/s.
/// </summary>
/// <remarks>
/// The standard-definition block layer and the DV100 block layer deliberately remain separate. They
/// share DIF framing and run/level VLCs, but DV100 has eight blocks per macroblock, different budgets,
/// different weighting/quantisation and high-definition shuffles. Both are cross-checked against
/// FFmpeg's LGPL-2.1-or-later implementation; see <c>Codecs/Dv/THIRD-PARTY-NOTICE.FFmpeg.txt</c>.
/// <para/>
/// IEC 61834-3 consumer HD-DVCR is not SMPTE 370M and is still refused by name in
/// <see cref="DvProfile.Identify(ReadOnlySpan{byte})"/> rather than being guessed at.
/// </remarks>
public sealed class DvVideoDecoder : IVideoCodecDecoder<DvVideoDecoder> {

  private static readonly CodecTag[] _Tags = [
    CodecTag.FromCharacters("dvsd"),
    CodecTag.FromCharacters("dv25"),
    CodecTag.FromCharacters("dv50"),
    CodecTag.FromCharacters("dvsl"),
    CodecTag.FromCharacters("cdvc"),
    CodecTag.FromCharacters("CDV5"),
    CodecTag.FromCharacters("dvis"),
    CodecTag.FromCharacters("pdvc"),
    CodecTag.FromCharacters("SL25"),
    CodecTag.FromCharacters("SLDV"),
    CodecTag.FromCharacters("dvc "),
    CodecTag.FromCharacters("dvcp"),
    CodecTag.FromCharacters("dvcs"),
    CodecTag.FromCharacters("dvl "),
    CodecTag.FromCharacters("dvlp"),
    CodecTag.FromCharacters("dvpp"),
    CodecTag.FromCharacters("dv5n"),
    CodecTag.FromCharacters("dv5p"),
    CodecTag.FromCharacters("AVdv"),
    CodecTag.FromCharacters("dvhd"),
    CodecTag.FromCharacters("dvh1"),
    CodecTag.FromCharacters("dvh2"),
    CodecTag.FromCharacters("dvh3"),
    CodecTag.FromCharacters("dvh4"),
    CodecTag.FromCharacters("dvh5"),
    CodecTag.FromCharacters("dvh6"),
    CodecTag.FromCharacters("dvhq"),
    CodecTag.FromCharacters("dvhp"),
    CodecTag.FromCharacters("CDVH"),
  ];

  private static readonly string[] _CodecIds = [
    "V_MS/VFW/FOURCC/dvsd",
    "V_DV",
  ];

  private readonly int _width;
  private readonly int _height;
  private readonly int[] _factors = DvSegmentDecoder.BuildFactors();
  private readonly DvSegmentDecoder.Scratch _scratch = new();
  private readonly Dv100SegmentDecoder.Scratch _dv100Scratch = new();

  private DvProfile? _profile;
  private DvGeometry.Segment[] _segments = [];

  private DvVideoDecoder(int width, int height) {
    this._width = width;
    this._height = height;
  }

  public static string CodecName => "DV (IEC 61834 / SMPTE 314M / SMPTE 370M)";

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

  /// <summary>Decodes one frame to its native component planes before display colour conversion.</summary>
  internal DvPlanes DecodePlanes(ReadOnlySpan<byte> frame, out DvProfile profile) {
    profile = DvProfile.Identify(frame);

    if (frame.Length < profile.FrameSize)
      throw new InvalidDataException(
        $"This packet states the {profile.Name} profile, whose frame is {profile.FrameSize} bytes; the packet is {frame.Length}.");

    if ((this._width != 0 && this._width != profile.Width) || (this._height != 0 && this._height != profile.Height))
      throw new InvalidDataException(
        $"A DV frame states the {profile.Name} profile, a raster of {profile.Width}x{profile.Height}, "
        + $"in a stream the container describes as {this._width}x{this._height}.");

    if (this._profile != profile) {
      this._segments = DvGeometry.Segments(profile);
      this._profile = profile;
    }

    var planes = DvPlanes.Allocate(profile);
    var coded = frame[..profile.FrameSize];
    if (profile.IsDv100) {
      foreach (var segment in this._segments)
        Dv100SegmentDecoder.Decode(coded, profile, segment, planes, this._dv100Scratch);
    } else {
      foreach (var segment in this._segments)
        DvSegmentDecoder.Decode(coded, profile, segment, this._factors, planes, this._scratch);
    }

    return planes;
  }
}
