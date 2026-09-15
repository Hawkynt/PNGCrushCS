using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FileFormat.Avi;
using FileFormat.Core;
using FileFormat.FlicVideo;
using FileFormat.Flv;
using FileFormat.Matroska;
using FileFormat.Mjpeg;
using FileFormat.Ogg;
using FileFormat.RealMedia;
using FileFormat.Mp4;
using FileFormat.RoqVideo;
using FileFormat.Yuv4Mpeg;

namespace Hawkynt.FileFormats.Video.Tests;

/// <summary>
/// Holds the Oracle column of the codec table to the thing it claims.
/// </summary>
/// <remarks>
/// An encoder and a decoder written here from the same reading of a format agree with each other
/// whether or not that reading is right, and most of these formats were recovered by measurement
/// rather than read out of a published description — which is precisely the case where the two
/// halves share a mistake. The column names the tool that has looked from outside, and this is what
/// stops the naming being decoration: the encoder writes a picture, the file goes to the tool the
/// registry says has read one, and the frame the tool hands back has to be the picture that went in.
/// <para/>
/// The two checks that need no tool run everywhere — a codec cannot claim an oracle nothing here can
/// run, and a codec with no encoder cannot claim one at all. The one that runs FFmpeg reports
/// inconclusive on a machine without it.
/// </remarks>
[TestFixture]
[Category("Conformance")]
public sealed class EncoderOracleTests {

  /// <summary>Where <c>ORACLE_SURVEY</c> points when every encoder is to be re-measured.</summary>
  private const string _SURVEY_VARIABLE = "ORACLE_SURVEY";

  /// <summary>The oracles this fixture knows how to run.</summary>
  private static readonly ConformanceOracle[] _Runnable = [ConformanceOracle.FFmpeg];

  // ============================================================================================
  // Checks that need no tool
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void NoEncoderClaimsAnOracleThisFixtureCannotRun() {
    var unrunnable = VideoFormatRegistry.AllEncoders
      .SelectMany(encoder => encoder.VerifiedBy.Select(oracle => (encoder.CodecName, Oracle: oracle)))
      .Where(claim => !_Runnable.Contains(claim.Oracle))
      .Select(claim => $"{claim.CodecName} claims {claim.Oracle}")
      .OrderBy(static text => text, StringComparer.Ordinal)
      .ToList();

    Assert.That(unrunnable, Is.Empty,
      "A claim nothing can run is a claim nothing can check:\n" + string.Join("\n", unrunnable));
  }

  [Test]
  [Category("Unit")]
  public void NoContainerClaimsAnOracleThisFixtureCannotRun() {
    var unrunnable = VideoFormatRegistry.AllFormats
      .SelectMany(format => format.VerifiedBy.Select(oracle => (format.Name, Oracle: oracle)))
      .Where(claim => !_Runnable.Contains(claim.Oracle))
      .Select(claim => $"{claim.Name} claims {claim.Oracle}")
      .OrderBy(static text => text, StringComparer.Ordinal)
      .ToList();

    Assert.That(unrunnable, Is.Empty,
      "A claim nothing can run is a claim nothing can check:\n" + string.Join("\n", unrunnable));
  }

  // ============================================================================================
  // The check that runs FFmpeg
  // ============================================================================================

  private static IEnumerable<TestCaseData> Claims() {
    var claims = VideoFormatRegistry.AllEncoders
      .Where(static encoder => encoder.VerifiedBy.Length != 0)
      .OrderBy(static encoder => encoder.CodecName, StringComparer.Ordinal)
      .SelectMany(static encoder => encoder.VerifiedBy.Select(oracle => (encoder.CodecName, Oracle: oracle)))
      .ToList();

    if (claims.Count == 0) {
      yield return new TestCaseData(string.Empty, ConformanceOracle.None).SetName("{m}(nothing is claimed)");
      yield break;
    }

    foreach (var (codec, oracle) in claims)
      yield return new TestCaseData(codec, oracle).SetName($"{{m}}({codec}, {oracle})");
  }

  [TestCaseSource(nameof(Claims))]
  public void TheOracleAnEncoderClaims_ReadsBackWhatItWrites(string codecName, ConformanceOracle oracle) {
    if (oracle == ConformanceOracle.None)
      Assert.Pass("No encoder claims an oracle, so there is nothing to hold to one.");

    FFmpegOracle.RequireAvailable();

    var encoder = VideoFormatRegistry.AllEncoders.FirstOrDefault(e => e.CodecName == codecName);
    Assert.That(encoder, Is.Not.Null, $"no encoder is registered under '{codecName}'");

    var (accepted, detail) = _AskFFmpegAbout(encoder!);

    Assert.That(accepted, Is.True,
      $"{codecName} says ffmpeg has read what it writes, and it has not: {detail}");
  }

  private static IEnumerable<TestCaseData> ContainerClaims() {
    var claims = VideoFormatRegistry.AllFormats
      .Where(static format => format.VerifiedBy.Length != 0)
      .OrderBy(static format => format.Name, StringComparer.Ordinal)
      .SelectMany(static format => format.VerifiedBy.Select(oracle => (format.Name, Oracle: oracle)))
      .ToList();

    if (claims.Count == 0) {
      yield return new TestCaseData(string.Empty, ConformanceOracle.None).SetName("{m}(nothing is claimed)");
      yield break;
    }

    foreach (var (container, oracle) in claims)
      yield return new TestCaseData(container, oracle).SetName($"{{m}}({container}, {oracle})");
  }

  [TestCaseSource(nameof(ContainerClaims))]
  public void TheOracleAContainerClaims_ReadsBackWhatItsMuxerWrites(string container, ConformanceOracle oracle) {
    if (oracle == ConformanceOracle.None)
      Assert.Pass("No container claims an oracle, so there is nothing to hold to one.");

    FFmpegOracle.RequireAvailable();

    var (accepted, detail) = _AskFFmpegAboutContainer(container);

    Assert.That(accepted, Is.True,
      $"{container} says ffmpeg has read what its muxer writes, and it has not: {detail}");
  }

  /// <summary>Writes a clip into one container with whatever codec that container will carry.</summary>
  /// <remarks>
  /// Which codec does not matter here and is deliberately not fixed: the claim is about the
  /// container layout, and any codec FFmpeg can find inside the file has proved the layout was
  /// followed well enough to find it.
  /// </remarks>
  private static (bool Accepted, string Detail) _AskFFmpegAboutContainer(string container) {
    var detail = "no encoder here produced packets this container would carry";

    // One small picture, not the whole ladder. A container that will hold a codec at all holds it at
    // 64 by 48, and walking every geometry of every codec through a container that holds none of
    // them costs an hour to learn what the first size already said.
    foreach (var encoder in VideoFormatRegistry.AllEncoders.OrderBy(static e => e.CodecName, StringComparer.Ordinal)) {
      var (accepted, why) = _AskFFmpegAbout(encoder, container, [(64, 48)]);
      if (accepted)
        return (true, $"{encoder.CodecName}: {why}");

      detail = why;
    }

    return (false, detail);
  }

  // ============================================================================================
  // Re-measuring every encoder
  // ============================================================================================

  /// <summary>Writes a clip into every container and records which ones FFmpeg reads back.</summary>
  [Test]
  [Category("Conformance")]
  [Explicit("Starts an FFmpeg per container per codec; run it when re-measuring the Oracle column.")]
  public void SurveyEveryContainerAgainstFFmpeg() {
    var destination = Environment.GetEnvironmentVariable(_SURVEY_VARIABLE);
    if (string.IsNullOrWhiteSpace(destination))
      Assert.Inconclusive($"Set {_SURVEY_VARIABLE} to the file the survey should be written to.");

    FFmpegOracle.RequireAvailable();

    var report = new StringBuilder();
    foreach (var format in VideoFormatRegistry.AllFormats.OrderBy(static f => f.Name, StringComparer.Ordinal)) {
      var (accepted, detail) = _AskFFmpegAboutContainer(format.Name);
      report.Append(format.Name).Append('\t').Append(accepted ? "FFmpeg" : "none").Append('\t').AppendLine(detail);
    }

    File.WriteAllText(destination!, report.ToString());
    TestContext.Out.WriteLine($"Survey written to {destination}");
  }

  /// <summary>
  /// Writes a clip with every registered encoder and records which ones FFmpeg reads back.
  /// </summary>
  /// <remarks>
  /// This is where the claims come from. Opt-in because it starts an FFmpeg for every encoder and
  /// every container it tries, and because its answer is only as good as the machine it runs on: an
  /// encoder that fails here on a build without FFmpeg has not been judged, which is not the same as
  /// having been judged and found wanting. It prints what it found and edits nothing.
  /// </remarks>
  [Test]
  [Category("Conformance")]
  [Explicit("Starts an FFmpeg per encoder; run it when re-measuring the Oracle column.")]
  public void SurveyEveryEncoderAgainstFFmpeg() {
    var destination = Environment.GetEnvironmentVariable(_SURVEY_VARIABLE);
    if (string.IsNullOrWhiteSpace(destination))
      Assert.Inconclusive($"Set {_SURVEY_VARIABLE} to the file the survey should be written to.");

    FFmpegOracle.RequireAvailable();

    var report = new StringBuilder();
    foreach (var encoder in VideoFormatRegistry.AllEncoders.OrderBy(static e => e.CodecName, StringComparer.Ordinal)) {
      var (accepted, detail) = _AskFFmpegAbout(encoder);
      report.Append(encoder.CodecName).Append('\t').Append(accepted ? "FFmpeg" : "none").Append('\t').AppendLine(detail);
    }

    File.WriteAllText(destination!, report.ToString());
    TestContext.Out.WriteLine($"Survey written to {destination}");
  }

  // ============================================================================================
  // Writing a clip the encoder will take and a container will hold
  // ============================================================================================

  /// <summary>The sizes tried, in the order they are tried.</summary>
  /// <remarks>
  /// 64 by 48 divides by every block size in use here and is small enough that a slow coder is still
  /// quick. The rest are for the formats that take one geometry and no other: DV's frame is a fixed
  /// length and its standard-definition profiles state their own picture size, and H.261 has one bit
  /// of PTYPE to name its format with, so QCIF and CIF are the only two pictures it can describe.
  /// </remarks>
  private static readonly (int Width, int Height)[] _Sizes = [
    (64, 48), (176, 144), (352, 288), (720, 480), (720, 576),
  ];

  /// <summary>How many pictures go in. More than one, so an inter-coded packet is reached.</summary>
  private const int _FRAMES = 3;

  private delegate byte[] Muxer(IReadOnlyList<MediaStreamInfo> streams, IEnumerable<CodedPacket> packets);

  /// <summary>The containers tried, with the extension FFmpeg dispatches on.</summary>
  /// <remarks>
  /// A codec has to be named by something a demuxer can carry before any decoder is reached, and no
  /// one container names them all: the VFW-style codes belong in an AVI, the QuickTime ones in an
  /// MP4, Matroska carries whatever the other two will not, and Xiph's codecs are named by the
  /// header packet an Ogg stream opens with rather than by any code a container holds.
  /// <para/>
  /// YUV4MPEG2 is deliberately not among them. It states the planes and no codec, so anything
  /// written into one comes back out of FFmpeg as a picture of the right size whether the encoder
  /// that produced those bytes was understood or not — and it was: MagicYUV passed through it on a
  /// build of FFmpeg with no MagicYUV decoder in it at all. A container that cannot fail is not a
  /// test.
  /// </remarks>
  private static readonly (string Format, string Extension, Muxer Mux)[] _Containers = [
    ("Avi", ".avi", static (streams, packets) => VideoIO.Mux<AviWriter>(streams, packets)),
    ("Mp4", ".mp4", static (streams, packets) => VideoIO.Mux<Mp4Writer>(streams, packets)),
    ("Matroska", ".mkv", static (streams, packets) => VideoIO.Mux<MatroskaWriter>(streams, packets)),
    // RealVideo carries its bitstream version in the container, and only this one carries it in
    // the shape a RealVideo decoder expects; without it the codec has no container to be asked
    // about in.
    ("RealMedia", ".rm", static (streams, packets) => VideoIO.Mux<RealMediaWriter>(streams, packets)),
    ("Flv", ".flv", static (streams, packets) => VideoIO.Mux<FlvWriter>(streams, packets)),
    ("Fli", ".flc", static (streams, packets) => VideoIO.Mux<FliWriter>(streams, packets)),
    ("Roq", ".roq", static (streams, packets) => VideoIO.Mux<RoqWriter>(streams, packets)),
    ("Mjpeg", ".mjpg", static (streams, packets) => VideoIO.Mux<MjpegWriter>(streams, packets)),
    ("Ogg", ".ogv", static (streams, packets) => VideoIO.Mux<OggWriter>(streams, packets)),
  ];

  /// <summary>
  /// Containers that prove a muxer and can prove nothing about a codec.
  /// </summary>
  /// <remarks>
  /// YUV4MPEG2 states the planes and names no codec, so whatever is written into one comes back out
  /// of FFmpeg as a picture whether or not the encoder that produced those bytes was understood —
  /// MagicYUV passed through it on a build with no MagicYUV decoder in it. That makes it
  /// useless for a codec claim and perfectly good for a container claim: FFmpeg still had to follow
  /// the header this muxer wrote to find the planes at all.
  /// </remarks>
  private static readonly (string Format, string Extension, Muxer Mux)[] _ContainersJudgingOnlyTheMuxer = [
    ("Yuv4Mpeg", ".y4m", static (streams, packets) => VideoIO.Mux<Yuv4MpegWriter>(streams, packets)),
  ];

  /// <summary>
  /// Codecs whose elementary stream is a file in its own right, and the name to write it under.
  /// </summary>
  /// <remarks>
  /// Some codecs here have no container in this package that will carry them, and are ordinarily
  /// handed about as bare packets. Refusing to try that would record "nothing has read this" when
  /// the reason is that nothing offered it in a form anything reads.
  /// <para/>
  /// Keyed by the code the encoder writes and not open to any encoder that produces bytes, because
  /// FFmpeg decides what a file is from its name here rather than from a container's stream header.
  /// Handed <c>.h261</c>, it read a MagicYUV frame as an H.261 picture and answered with a 176x144
  /// image — a pass that says nothing whatever about the encoder that wrote those bytes.
  /// </remarks>
  private static readonly (string Codec, string Extension)[] _ElementaryStreams = [
    ("H261", ".h261"),
    ("H263", ".h263"),
    ("h263", ".h263"),
  ];

  private static byte[] _Concatenated(IEnumerable<CodedPacket> packets) {
    using var stream = new MemoryStream();
    foreach (var packet in packets)
      stream.Write(packet.Data.Span);

    return stream.ToArray();
  }

  /// <summary>The bare-packet file this encoder's own code is written as, where there is one.</summary>
  private static IEnumerable<(string Format, string Extension, Muxer Mux)> _ElementaryStreamsFor(VideoCodecEncoderEntry entry) {
    foreach (var (codec, extension) in _ElementaryStreams)
      if (entry.Codec == CodecTag.FromCharacters(codec))
        yield return (string.Empty, extension, static (_, packets) => _Concatenated(packets));
  }

  private static (bool Accepted, string Detail) _AskFFmpegAbout(
    VideoCodecEncoderEntry entry, string? onlyContainer = null, (int Width, int Height)[]? sizes = null) {
    var directory = Directory.CreateTempSubdirectory("encoderoracle");
    var detail = "no size and no container this encoder takes produced a file";

    try {
      foreach (var (width, height) in sizes ?? _Sizes)
      foreach (var picture in _Pictures(width, height)) {
        if (_Encode(entry, width, height, picture) is not var (described, packets) || packets.Count == 0)
          continue;

        var candidates = onlyContainer == null
          ? _Containers.Concat(_ElementaryStreamsFor(entry))
          : _Containers.Concat(_ContainersJudgingOnlyTheMuxer).Where(candidate => candidate.Format == onlyContainer);

        foreach (var (format, extension, mux) in candidates) {

          byte[] file;
          try {
            file = mux([described!], packets);
          } catch (Exception) {
            // A container that will not carry this codec. Another one may.
            continue;
          }

          var path = Path.Combine(directory.FullName, "clip" + extension);
          File.WriteAllBytes(path, file);

          var (decoded, output) = FFmpegOracle.TryDecodeFirstFrame(path, width, height);
          if (decoded)
            return (true, $"read the {width}x{height} clip written as {extension}");

          detail = $"at {width}x{height} in {extension}, {output}";
        }
      }

      return (false, detail);
    } finally {
      try { directory.Delete(recursive: true); } catch { /* best effort */ }
    }
  }

  /// <summary>Runs the encoder, or answers null where it will not take this picture.</summary>
  private static (MediaStreamInfo? Described, List<CodedPacket> Packets)? _Encode(
    VideoCodecEncoderEntry entry, int width, int height, RawImage picture) {
    try {
      var requested = new MediaStreamInfo {
        Index = 0,
        Kind = MediaStreamKind.Video,
        Codec = entry.Codec,
        Handler = entry.Codec,
        Width = width,
        Height = height,
        TimeBase = new Rational(1, 25),
        FrameRate = new Rational(25, 1),
      };

      var encoder = entry.CreateEncoder(requested);
      var packets = new List<CodedPacket>();

      for (var frame = 0; frame < _FRAMES; ++frame)
        if (encoder.TryEncode(picture, frame, out var packet))
          packets.Add(packet);

      packets.AddRange(encoder.Flush());

      return (encoder.DescribeStream(), packets);
    } catch (Exception) {
      // Refused the picture, the size or the request. Not something to blame the oracle for.
      return null;
    }
  }

  /// <summary>The pictures offered, in the order they are offered.</summary>
  /// <remarks>
  /// Four representations rather than one, because the encoders here disagree about what a picture
  /// is: some take packed RGB, some indexed pixels, and some native planar YUV. Offering all four
  /// measures whether the bytes are readable instead of whether the fixture guessed right.
  /// </remarks>
  private static IEnumerable<RawImage> _Pictures(int width, int height) {
    yield return _Rgb24(width, height);
    yield return _Rgba32(width, height);
    yield return _Indexed(width, height);
    yield return _Yuv420P8(width, height);
  }

  private static RawImage _Rgb24(int width, int height) {
    var data = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      data[at] = (byte)(x * 255 / Math.Max(1, width - 1));
      data[at + 1] = (byte)(y * 255 / Math.Max(1, height - 1));
      data[at + 2] = (byte)((x / 8 + y / 8) % 2 == 0 ? 255 : 0);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  private static RawImage _Rgba32(int width, int height) {
    var data = new byte[width * height * 4];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 4;
      data[at] = (byte)(x * 255 / Math.Max(1, width - 1));
      data[at + 1] = (byte)(y * 255 / Math.Max(1, height - 1));
      data[at + 2] = (byte)((x / 8 + y / 8) % 2 == 0 ? 255 : 0);
      data[at + 3] = 255;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = data };
  }

  private static RawImage _Indexed(int width, int height) {
    var palette = new byte[256 * 3];
    for (var i = 0; i < 256; ++i) {
      palette[i * 3] = (byte)i;
      palette[i * 3 + 1] = (byte)(255 - i);
      palette[i * 3 + 2] = (byte)(i % 2 == 0 ? 255 : 0);
    }

    var data = new byte[width * height];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
      data[y * width + x] = (byte)((x + y) & 0xFF);

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = data,
      Palette = palette,
      PaletteCount = 256,
    };
  }

  private static RawImage _Yuv420P8(int width, int height) {
    var chromaWidth = (width + 1) >> 1;
    var chromaHeight = (height + 1) >> 1;
    var lumaLength = checked(width * height);
    var chromaLength = checked(chromaWidth * chromaHeight);
    var data = new byte[checked(lumaLength + 2 * chromaLength)];
    var uAt = lumaLength;
    var vAt = lumaLength + chromaLength;

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
      data[y * width + x] = (byte)(16 + (x * 131 + y * 47) % 220);

    for (var y = 0; y < chromaHeight; ++y)
    for (var x = 0; x < chromaWidth; ++x) {
      data[uAt + y * chromaWidth + x] = (byte)(16 + (x * 67 + y * 29) % 225);
      data[vAt + y * chromaWidth + x] = (byte)(16 + (x * 31 + y * 89) % 225);
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Yuv420P8,
      PixelData = data,
      ColorInfo = RawImageColorInfo.Bt601Limited,
    };
  }
}
