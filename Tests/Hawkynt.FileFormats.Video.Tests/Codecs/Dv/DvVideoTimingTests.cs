using System;
using FileFormat.Core;

namespace FileFormat.Codecs.Dv.Tests;

/// <summary>DV's frame rate is part of the recording profile, not an encoder quality option.</summary>
[TestFixture]
public class DvVideoTimingTests {

  [Test]
  [Category("Unit")]
  public void AUniqueRasterSuppliesTheCanonicalFrameRateWhenTheCallerDoesNot() {
    foreach (var (width, height, expected) in new[] {
      (720, 480, new Rational(30000, 1001)),
      (720, 576, new Rational(25, 1)),
      (1280, 1080, new Rational(30000, 1001)),
      (1440, 1080, new Rational(25, 1)),
    }) {
      var described = _Encoder(width, height, Rational.Unknown).DescribeStream();
      Assert.That(described.FrameRate, Is.EqualTo(expected), $"{width}x{height}");
    }
  }

  [Test]
  [Category("Unit")]
  public void TheShared720RasterRequiresTheRecordingRate() {
    var failure = Assert.Throws<NotSupportedException>(() => _Encoder(960, 720, Rational.Unknown));
    Assert.That(failure.Message,
      Does.Contain("60000/1001").And.Contain("50/1").And.Contain("FrameRate"));
  }

  [Test]
  [Category("Unit")]
  public void AnEquivalentUnreducedFrameRateIsAcceptedAndCanonicalized() {
    foreach (var (width, height, requested, expected) in new[] {
      (720, 480, new Rational(60000, 2002), new Rational(30000, 1001)),
      (720, 576, new Rational(50, 2), new Rational(25, 1)),
      (1280, 1080, new Rational(60000, 2002), new Rational(30000, 1001)),
      (1440, 1080, new Rational(50, 2), new Rational(25, 1)),
      (960, 720, new Rational(120000, 2002), new Rational(60000, 1001)),
      (960, 720, new Rational(100, 2), new Rational(50, 1)),
    }) {
      var described = _Encoder(width, height, requested).DescribeStream();
      Assert.That(described.FrameRate, Is.EqualTo(expected), $"{width}x{height}");
    }
  }

  [Test]
  [Category("Unit")]
  public void AFrameRateThatNoProfileForTheRasterDefinesIsRefused() {
    foreach (var (width, height, requested, expected) in new[] {
      (720, 480, new Rational(25, 1), "30000/1001"),
      (720, 576, new Rational(30000, 1001), "25/1"),
      (1280, 1080, new Rational(25, 1), "30000/1001"),
      (1440, 1080, new Rational(30000, 1001), "25/1"),
      (960, 720, new Rational(25, 1), "60000/1001"),
    }) {
      var failure = Assert.Throws<NotSupportedException>(() => _Encoder(width, height, requested));
      Assert.That(failure.Message, Does.Contain(expected).And.Contain(requested.ToString()), $"{width}x{height}");
    }
  }

  [Test]
  [Category("Unit")]
  public void ADeclaredFrameRateMustBePositive() {
    foreach (var requested in new[] {
      new Rational(-30000, -1001),
      new Rational(25, -1),
    }) {
      var failure = Assert.Throws<NotSupportedException>(() => _Encoder(720, 480, requested));
      Assert.That(failure.Message, Does.Contain("positive").And.Contain(requested.ToString()));
    }

    // A numerator of nought is how Rational spells "not stated", so it is not a rate to refuse: the
    // raster supplies the canonical one, exactly as an unstated rate does.
    Assert.That(
      _Encoder(720, 480, new Rational(0, 1)).DescribeStream().FrameRate,
      Is.EqualTo(new Rational(30000, 1001)));
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
