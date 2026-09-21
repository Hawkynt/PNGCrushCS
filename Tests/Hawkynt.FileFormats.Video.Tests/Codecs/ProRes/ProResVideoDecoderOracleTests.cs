using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.Core;
using FileFormat.Mp4;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.ProRes.Tests;

/// <summary>
/// The read direction against FFmpeg's own ProRes files, one file per shape of the format.
/// </summary>
/// <remarks>
/// The encoder oracle beside this one asks FFmpeg to read what this package writes. That catches a
/// writer which disagrees with the world, but it cannot catch a reader and a writer which agree with
/// each other and not with it — the two halves of a codec written together share their mistakes, and
/// a round trip through both of them is precisely the measurement that cannot see one.
/// <para/>
/// So this is the other direction. FFmpeg writes the frame, this package reads it, and FFmpeg's own
/// decode of the same file is what the planes are compared against. Nothing this package wrote is
/// anywhere in the loop, which is what makes the answer evidence. The comparison is on component
/// planes at their coded depth for the same reason the encoder oracle is: narrowing and colour
/// conversion are display conventions two correct decoders may differ over.
/// <para/>
/// It is also the only check that puts real ProRes in front of the frame-stuffing refusal. A writer
/// which never pads its frames cannot demonstrate that the rule permits the padding a real encoder
/// emits, and a rule that refuses real files is worse than no rule.
/// </remarks>
[TestFixture]
public sealed class ProResVideoDecoderOracleTests {

  /// <summary>The 4:2:2 profiles as FFmpeg's <c>prores_ks</c> numbers them, and their coded depth.</summary>
  [TestCase(0, "apco", "yuv422p10le")]
  [TestCase(1, "apcs", "yuv422p10le")]
  [TestCase(2, "apcn", "yuv422p10le")]
  [TestCase(3, "apch", "yuv422p10le")]
  [Category("Oracle")]
  public void EveryFourTwoTwoProfileFFmpegWritesIsReadBackSampleForSample(
    int profile, string codec, string pixelFormat) {
    FFmpegOracle.RequireAvailable();

    var movie = ProResFFmpegFixtures.Write(profile, width: 64, height: 48);
    _AssertReadsBackWhatFFmpegSees(movie, codec, pixelFormat, chromaShift: 1, alphaChannelType: 0);
  }

  /// <summary>4:4:4 at twelve bits, with and without the alpha channel that only these profiles have.</summary>
  [TestCase(4, "ap4h", 16, 2)]
  [TestCase(4, "ap4h", 8, 1)]
  [TestCase(4, "ap4h", 0, 0)]
  [TestCase(5, "ap4x", 16, 2)]
  [TestCase(5, "ap4x", 8, 1)]
  [TestCase(5, "ap4x", 0, 0)]
  [Category("Oracle")]
  public void EveryFourFourFourShapeFFmpegWritesIsReadBackSampleForSample(
    int profile, string codec, int alphaBits, int alphaChannelType) {
    FFmpegOracle.RequireAvailable();

    var movie = ProResFFmpegFixtures.Write(profile, width: 64, height: 48, alphaBits: alphaBits);
    _AssertReadsBackWhatFFmpegSees(
      movie,
      codec,
      alphaChannelType == 0 ? "yuv444p12le" : "yuva444p12le",
      chromaShift: 0,
      alphaChannelType: alphaChannelType);
  }

  /// <summary>
  /// Both field orders, at a height whose fields are an odd number of rows.
  /// </summary>
  /// <remarks>
  /// Fifty rows make each field twenty-five, so the last macroblock row of each field is partial and
  /// the two fields are not the same height. That is where a reader which took the frame's height for
  /// a picture's height, or which put the second picture's rows on the first picture's parity, stops
  /// agreeing with FFmpeg.
  /// </remarks>
  [TestCase("tff", 1)]
  [TestCase("bff", 2)]
  [Category("Oracle")]
  public void BothFieldOrdersFFmpegWritesAreReadBackSampleForSample(string fieldMode, int interlaceMode) {
    FFmpegOracle.RequireAvailable();

    var movie = ProResFFmpegFixtures.Write(profile: 2, width: 64, height: 50, fieldMode: fieldMode);
    var header = _AssertReadsBackWhatFFmpegSees(movie, "apcn", "yuv422p10le", chromaShift: 1, alphaChannelType: 0);

    Assert.That(header.InterlaceMode, Is.EqualTo(interlaceMode),
      "the field order FFmpeg coded is the one that must be read back");
  }

  /// <summary>
  /// Reads one FFmpeg-written frame with this package and with FFmpeg, and requires the two to agree.
  /// </summary>
  private static ProResFrameHeader _AssertReadsBackWhatFFmpegSees(
    string movie,
    string codec,
    string pixelFormat,
    int chromaShift,
    int alphaChannelType) {
    try {
      var container = Mp4Reader.FromBytes(File.ReadAllBytes(movie));
      var stream = VideoIO.FirstVideoStream(container);
      Assert.That(stream, Is.Not.Null, "FFmpeg's own file declares no video stream");
      Assert.That(stream!.Codec.ToString(), Is.EqualTo(codec), "FFmpeg wrote a different profile than asked for");

      var packets = Mp4Container.ReadPackets(container, stream.Index).ToArray();
      Assert.That(packets, Is.Not.Empty, "FFmpeg's own file carries no ProRes packet");

      var planes = ProResVideoDecoder.Create(stream).DecodePlanes(packets[0].Data, out var header);

      Assert.Multiple(() => {
        Assert.That(header.AlphaChannelType, Is.EqualTo(alphaChannelType));
        Assert.That(planes.ChromaWidth, chromaShift == 0 ? Is.EqualTo(planes.Width) : Is.LessThan(planes.Width));
        Assert.That(planes.BitDepth, Is.EqualTo(chromaShift == 0 ? 12 : 10));
      });

      var decoded = ProResFFmpegFixtures.DecodeToRaw(movie, pixelFormat);
      ProResPlaneComparison.AssertColour(
        decoded, planes, stream.Width, stream.Height, chromaShift, maximumDelta: 1);

      if (alphaChannelType != 0)
        ProResPlaneComparison.AssertAlphaAgreesWithFFmpeg(
          decoded, planes, stream.Width, stream.Height, alphaChannelType == 1 ? 8 : 16);

      return header;
    } finally {
      ProResFFmpegFixtures.Discard(movie);
    }
  }
}
