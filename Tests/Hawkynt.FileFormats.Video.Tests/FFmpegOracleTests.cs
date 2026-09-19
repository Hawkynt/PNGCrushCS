namespace Hawkynt.FileFormats.Video.Tests;

/// <summary>
/// What the FFmpeg oracle counts as FFmpeg complaining about the file it was handed.
/// </summary>
/// <remarks>
/// The oracle's whole worth is that <c>-loglevel error</c> is silent about a stream FFmpeg read
/// cleanly, so every line it prints fails the claim. Exactly one line is exempt, and an exemption is
/// the one change to an oracle that can make it stop reporting a real defect — so what it does and,
/// more to the point, what it does not reach is pinned here rather than left to a reading of the
/// filter. These run everywhere: they are about the text, and start no FFmpeg.
/// </remarks>
[TestFixture]
[Category("Unit")]
public sealed class FFmpegOracleTests {

  /// <summary>Exactly what FFmpeg 4.2.2 prints for any H.261 clip, its own encoder's included.</summary>
  private const string _BENIGN = "[h261 @ 0x5618b4c22b80] warning: first frame is no keyframe";

  [Test]
  public void SilenceStaysSilence() => Assert.That(FFmpegOracle.SignificantDiagnostics(string.Empty), Is.Empty);

  [Test]
  public void WhitespaceAloneIsNotAComplaint()
    => Assert.That(FFmpegOracle.SignificantDiagnostics("\r\n  \n"), Is.Empty);

  [Test]
  public void TheH261KeyframeWarningDoesNotCount()
    => Assert.That(FFmpegOracle.SignificantDiagnostics(_BENIGN), Is.Empty);

  /// <summary>FFmpeg prints it once per decoder it opened, which is twice for a bare elementary stream.</summary>
  [Test]
  public void BothCopiesOfItDoNotCount()
    => Assert.That(
      FFmpegOracle.SignificantDiagnostics(
        "[h261 @ 0x564e18045b80] warning: first frame is no keyframe\n"
        + "[h261 @ 0x564e180c4300] warning: first frame is no keyframe\n"),
      Is.Empty);

  /// <summary>
  /// The exemption is H.261's alone. In a format that does have an I picture the same words mean the
  /// encoder failed to write one, which is exactly the defect this oracle exists to catch.
  /// </summary>
  [TestCase("[h263 @ 0x1] warning: first frame is no keyframe")]
  [TestCase("[mpeg4 @ 0x1] warning: first frame is no keyframe")]
  [TestCase("[mpegvideo @ 0x1] warning: first frame is no keyframe")]
  public void TheSameWordsFromAnyOtherDecoderDoCount(string line)
    => Assert.That(FFmpegOracle.SignificantDiagnostics(line), Is.EqualTo(line));

  /// <summary>Every other thing H.261's own decoder can say still fails the claim.</summary>
  [TestCase("[h261 @ 0x1] Error at MB: 42")]
  [TestCase("[h261 @ 0x1] illegal mb_type")]
  [TestCase("[h261 @ 0x1] warning: first frame is no keyframe, and the second is not either")]
  public void EveryOtherComplaintFromH261StillCounts(string line)
    => Assert.That(FFmpegOracle.SignificantDiagnostics(line), Is.EqualTo(line));

  [Test]
  public void ARealComplaintBesideTheBenignOneSurvives() {
    const string real = "[h261 @ 0x1] Error at MB: 42";

    Assert.That(FFmpegOracle.SignificantDiagnostics($"{_BENIGN}\n{real}\n{_BENIGN}"), Is.EqualTo(real));
  }

  [Test]
  public void EveryRealComplaintIsReportedAndInTheOrderFFmpegPrintedThem() {
    const string first = "[h261 @ 0x1] Error at MB: 42";
    const string second = "[image2 @ 0x1] Could not open file";

    Assert.That(
      FFmpegOracle.SignificantDiagnostics($"{first}\r\n{_BENIGN}\r\n{second}\r\n"),
      Is.EqualTo($"{first}\n{second}"));
  }
}
