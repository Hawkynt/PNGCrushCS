using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.H263;
using FileFormat.Codecs.RealVideo;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Decodes RealVideo 1, named <c>RV10</c> or <c>RV13</c> by RealMedia containers.</summary>
/// <remarks>
/// RealVideo 1 replaces H.263's picture/group headers and reuses its macroblock layer. The original
/// major-1/minor-0/micro-0 syntax is implemented here. Later micro versions alter intra-DC coding and
/// are refused rather than decoded through the wrong H.263 path. RealVideo 2, 3 and 4 remain distinct
/// codecs and are not accepted by this decoder.
/// </remarks>
public sealed class RealVideoDecoder : IVideoCodecDecoder<RealVideoDecoder> {

  private static readonly CodecTag[] _Tags = [
    CodecTag.FromCharacters("RV10"),
    CodecTag.FromCharacters("RV13"),
  ];

  private static readonly (string Code, string Reason)[] _Refused = [
    ("RV20", "RealVideo 2, whose picture header and coding modes are not implemented here"),
    ("RV30", "RealVideo 3, which has its own transform, intra prediction and loop filter"),
    ("RV40", "RealVideo 4, which shares the RV30/40 codec family rather than the RV10 H.263 macroblock layer"),
  ];

  private readonly RealVideoBitstreamVersion _version;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private readonly int _width;
  private readonly int _height;
  private H263Frame? _reference;

  /// <summary>
  /// The picture a predicted one would be built on, in the decoder's own planes.
  /// </summary>
  /// <remarks>
  /// This exists for the encoder beside it, which drives a decoder with its own output so that it
  /// predicts from the samples a receiving decoder will hold rather than from the frame it was handed.
  /// Handing back the planes rather than an image is the point: a round trip through RGB would
  /// quantise the reference a second time, and the residual would then be measured against something
  /// no decoder ever holds.
  /// </remarks>
  internal H263Frame? CurrentReference => this._reference;

  private RealVideoDecoder(RealVideoBitstreamVersion version, int width, int height) {
    this._version = version;
    this._width = width;
    this._height = height;
    this._macroblockWidth = (width + 15) / 16;
    this._macroblockHeight = (height + 15) / 16;
  }

  public static string CodecName => "RealVideo 1 (RV10/RV13)";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      return false;

    foreach (var tag in _Tags)
      if (stream.Codec.EqualsIgnoringCase(tag))
        return true;

    return false;
  }

  public static RealVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    foreach (var (code, reason) in _Refused)
      if (stream.Codec.EqualsIgnoringCase(CodecTag.FromCharacters(code)))
        throw new NotSupportedException($"This stream is coded with {code} — {reason}. It is not implemented.");

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"This RealVideo stream states a picture size of {stream.Width}x{stream.Height}. RealVideo carries no size in "
        + "its bitstream, so the container's is the only one there is and a stream without it cannot be decoded.");

    var version = RealVideoBitstreamVersion.Read(stream.CodecPrivateData.Span, RealVideoGeneration.RealVideo10);
    if (version.Generation != RealVideoGeneration.RealVideo10)
      throw new NotSupportedException(
        $"This stream is named {stream.Codec} but its private data states bitstream version 0x{version.Version:X8}, "
        + "whose major version names a different generation of RealVideo.");

    if (version.Minor != RealVideoBitstreamVersion.IMPLEMENTED_MINOR
        || version.Micro != RealVideoBitstreamVersion.IMPLEMENTED_MICRO)
      throw new NotSupportedException(
        $"This RealVideo 1 stream states version 0x{version.Version:X8} (minor {version.Minor}, micro {version.Micro}). "
        + $"Only minor {RealVideoBitstreamVersion.IMPLEMENTED_MINOR}, micro {RealVideoBitstreamVersion.IMPLEMENTED_MICRO} "
        + "is implemented. Non-zero RV10 micro versions use RealVideo-specific predictive intra-DC coding; micro 2 "
        + "also enables overlapped motion compensation. Decoding those pictures as baseline H.263 would produce "
        + "plausible corruption rather than a reliable refusal.");

    var macroblockWidth = (stream.Width + 15) / 16;
    var macroblockHeight = (stream.Height + 15) / 16;
    if (macroblockWidth > 63 || macroblockHeight > 63)
      throw new NotSupportedException(
        $"This RealVideo stream is {stream.Width}x{stream.Height}, which is {macroblockWidth}x{macroblockHeight} "
        + "macroblocks. This decoder currently supports run positions through sixty-three macroblocks each way.");

    return new(version, stream.Width, stream.Height);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    if (packet.Data.IsEmpty) {
      frame = null!;
      return false;
    }

    var picture = this._DecodePicture(packet.Data.Span, packet.Fragments);
    frame = new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Rgb24,
      PixelData = H263ColorConversion.ToRgb24(picture, this._width, this._height),
    };
    return true;
  }

  internal H263Frame DecodePlanes(CodedPacket packet) => this._DecodePicture(packet.Data.Span, packet.Fragments);

  public IEnumerable<RawImage> Flush() => [];

  private H263Frame _DecodePicture(ReadOnlySpan<byte> data, IReadOnlyList<int> fragments) {
    var macroblockCount = this._macroblockWidth * this._macroblockHeight;

    // RV10 reuses H.263 TCOEF but extends the otherwise reserved escape level -128 with a following
    // signed twelve-bit level. Nothing else using H263BitReader gets that interpretation.
    var reader = new H263BitReader(data, realVideoExtendedEscapeLevel: true);
    var first = RealVideoSliceHeader.Read(ref reader, this._version, this._macroblockWidth, macroblockCount, false);

    var header = new H263PictureHeader {
      Width = this._width,
      Height = this._height,
      MacroblockRowsPerGroup = 1,
      IsIntra = first.IsIntra,
      IsReference = true,
      Quantiser = first.Quantiser,
      HasWideEscapeLevel = false,
      HasGroupLayer = false,
      AllowsVectorsOutsidePicture = true,
      TemporalReference = 0,
    };

    var target = new H263Frame(this._macroblockWidth, this._macroblockHeight);
    var picture = H263PictureDecoder.BeginPicture(header, target, this._reference);

    var run = first;
    var fragment = 0;
    for (; ; ) {
      if (run.IsIntra != first.IsIntra)
        throw new InvalidDataException(
          $"A run of this RealVideo picture states that it is {(run.IsIntra ? "intra" : "predicted")} coded where the "
          + $"first run states {(first.IsIntra ? "intra" : "predicted")}.");

      picture.DecodeRun(ref reader, run.FirstMacroblock, run.MacroblockCount, run.Quantiser);

      var decoded = run.FirstMacroblock + run.MacroblockCount;
      if (decoded >= macroblockCount)
        break;

      ++fragment;
      if (fragment < fragments.Count) {
        var at = fragments[fragment];
        if (at < 0 || at >= data.Length)
          throw new InvalidDataException(
            $"The container states that a piece begins at byte {at} of a RealVideo picture {data.Length} bytes long.");

        reader.SeekToBit(at << 3);
        run = RealVideoSliceHeader.Read(ref reader, this._version, this._macroblockWidth, macroblockCount, true);
      } else if (!this._TryFindNextRun(ref reader, data, macroblockCount, decoded, out run)) {
        throw new InvalidDataException(
          $"This RealVideo picture stops after {decoded} of its {macroblockCount} macroblocks: no further run header "
          + $"naming macroblock {decoded} was found.");
      }

      if (run.FirstMacroblock != decoded)
        throw new InvalidDataException(
          $"A run of this RealVideo picture begins at macroblock {run.FirstMacroblock} where {decoded} was due.");
    }

    if (header.IsReference)
      this._reference = target;

    return target;
  }

  private const int _RUN_SEARCH_BYTES = 8;

  private bool _TryFindNextRun(
    ref H263BitReader reader, ReadOnlySpan<byte> data, int macroblockCount, int due, out RealVideoSliceHeader run) {
    run = default;

    var start = (reader.BitPosition + 7) >> 3;
    for (var at = start; at <= start + _RUN_SEARCH_BYTES && at < data.Length; ++at) {
      var candidate = new H263BitReader(data);
      candidate.SeekToBit(at << 3);

      RealVideoSliceHeader header;
      try {
        header = RealVideoSliceHeader.Read(ref candidate, this._version, this._macroblockWidth, macroblockCount, true);
      } catch (InvalidDataException) {
        continue;
      }

      if (header.FirstMacroblock != due)
        continue;

      reader.SeekToBit(candidate.BitPosition);
      run = header;
      return true;
    }

    return false;
  }
}