using System;
using FileFormat.Core;

namespace FileFormat.Codecs.Dv.Tests;

/// <summary>DV's frame rate is part of the recording system, not an encoder option.</summary>
[TestFixture]
public class DvVideoTimingTests {

  [Test]
  [Category("Unit")]
  public void TheRasterSuppliesTheCanonicalFrameRateWhenTheCallerDoesNot() {
    foreach (var (width, height, expected) in new[] {
      (720, 480, new Rational(30000, 1001)),
      (720, 576, new Rational(25, 1)),
    }) {
      var described = _Encoder(width, height, Rational.Unknown).DescribeStream();
      Assert.That(described.FrameRate, Is.EqualTo(expected), $"{width}x{height}");
    }
  }

  [Test]
  [Category("Unit")]
  public void AnEquivalentUnreducedFrameRateIsAcceptedAndCanonicalized() {
    foreach (var (width, height, requested, expected) in new[] {
      (720, 480, new Rational(60000, 2002), new Rational(30000, 1001)),
      (720, 576, new Rational(50, 2), new Rational(25, 1)),
    }) {
      var described = _Encoder(width, height, requested).DescribeStream();
      Assert.That(described.FrameRate, Is.EqualTo(expected), $"{width}x{height}");
    }
  }

  [Test]
  [Category("Unit")]
  public void AFrameRateThatContradictsTheRecordingSystemIsRefused() {
    foreach (var (width, height, requested, expected) in new[] {
      (720, 480, new Rational(25, 1), "30000/1001"),
      (720, 576, new Rational(30000, 1001), "25/1"),
      (720, 480, new Rational(-30000, -1001), "30000/1001"),
    }) {
      var failure = Assert.Throws<NotSupportedException>(() => _Encoder(width, height, requested));
      Assert.That(failure.Message, Does.Contain(expected).And.Contain(requested.ToString()), $"{width}x{height}");
    }
  }

  private static DvVideoEncoder _Encoder(int width, int height, Rational frameRate)
    => DvVideoEncoder.Create(new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Width = width,
      Height = height,
      FrameRate = frameRate,
    });
}
