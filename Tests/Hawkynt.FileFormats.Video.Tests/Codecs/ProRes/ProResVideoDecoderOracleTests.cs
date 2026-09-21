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
  /// A 4444 frame stamped bitstream version 0 reads identically to the same frame stamped version 1.
  /// </summary>
  /// <remarks>
  /// RDD 36:2022, 6.4 fixes <c>chroma_format</c> at 2 and <c>alpha_channel_type</c> at 0 for version
  /// 0, and this package used to refuse a version 0 frame that said otherwise. Every ProRes 4444
  /// frame ffmpeg wrote before it began stamping version 1 says otherwise — ffmpeg 6.1, which a
  /// current Ubuntu ships and which the CI oracle leg installs, among them — so the refusal made this
  /// package unable to read the reference encoder's own output. The read-direction oracle above finds
  /// it wherever the ffmpeg to hand is old enough.
  /// <para/>
  /// Which is the problem this test exists for: on a machine with a current ffmpeg there is nothing
  /// to find, and a regression would go unnoticed until CI. So the older encoder's output is
  /// reconstructed exactly — one byte of a real ffmpeg frame, the <c>bitstream_version</c> at offset
  /// 3 of the frame header, set back to 0 — and the decode has to be unchanged by it. Version 1 added
  /// no field to the header and moved none, so "unchanged" is the whole claim.
  /// </remarks>
  [TestCase(4, 16)]
  [TestCase(5, 8)]
  [Category("Oracle")]
  public void AFourFourFourFrameStampedTheOlderBitstreamVersionReadsTheSame(int profile, int alphaBits) {
    FFmpegOracle.RequireAvailable();

    var movie = ProResFFmpegFixtures.Write(profile, width: 64, height: 48, alphaBits: alphaBits);
    try {
      var container = Mp4Reader.FromBytes(File.ReadAllBytes(movie));
      var stream = VideoIO.FirstVideoStream(container)!;
      var frame = Mp4Container.ReadPackets(container, stream.Index).First().Data.ToArray();

      // frame_size and frame_identifier are eight bytes; bitstream_version is byte 3 of the header.
      const int VERSION_AT = 8 + 3;
      Assert.That(frame[VERSION_AT], Is.EqualTo(1),
        "this ffmpeg stamps version 1 for 4444; the older stamping is what is being reconstructed");

      var decoder = ProResVideoDecoder.Create(stream);
      var asWritten = decoder.DecodePlanes(frame, out var written);

      var older = (byte[])frame.Clone();
      older[VERSION_AT] = 0;
      var asOlder = decoder.DecodePlanes(older, out var deviating);

      Assert.Multiple(() => {
        Assert.That(written.DeviatesFromItsStatedVersion, Is.False);
        Assert.That(deviating.DeviatesFromItsStatedVersion, Is.True,
          "a version 0 frame stating 4:4:4 is read, and recorded as not written the way 6.4 says");
        Assert.That(deviating.ChromaFormat, Is.EqualTo(3));
        Assert.That(deviating.AlphaChannelType, Is.EqualTo(written.AlphaChannelType));
        Assert.That(asOlder.BitDepth, Is.EqualTo(asWritten.BitDepth));
        Assert.That(asOlder.Luma, Is.EqualTo(asWritten.Luma).AsCollection);
        Assert.That(asOlder.Cb, Is.EqualTo(asWritten.Cb).AsCollection);
        Assert.That(asOlder.Cr, Is.EqualTo(asWritten.Cr).AsCollection);
        Assert.That(asOlder.Alpha, Is.EqualTo(asWritten.Alpha).AsCollection);
      });
    } finally {
      ProResFFmpegFixtures.Discard(movie);
    }
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
