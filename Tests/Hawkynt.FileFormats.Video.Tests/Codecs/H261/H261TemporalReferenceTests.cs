using System;
using System.IO;
using FileFormat.Codecs.H263;
using FileFormat.Core;

namespace FileFormat.Codecs.H261.Tests;

/// <summary>Temporal-reference timing from ITU-T H.261 clauses 3.1 and 4.2.1.2.</summary>
[TestFixture]
public sealed class H261TemporalReferenceTests {

  [Test]
  [Category("Unit")]
  public void TimestampGapsCountNonTransmittedSourcePictures() {
    var encoder = H261VideoEncoder.Create(_Stream(
      timeBase: new(1001, 30000),
      frameRate: new(30000, 1001)));
    var picture = _FlatPicture();

    Assert.That(encoder.TryEncode(picture, 100, out var first), Is.True);
    Assert.That(encoder.TryEncode(picture, 103, out var second), Is.True);
    Assert.That(encoder.TryEncode(picture, 104, out var third), Is.True);

    Assert.That(
      new[] { _TemporalReference(first), _TemporalReference(second), _TemporalReference(third) },
      Is.EqualTo(new[] { 0, 3, 4 }));
  }

  [Test]
  [Category("Unit")]
  public void FrameRateCountsSourcePicturesWhenPacketsHaveNoTimestamps() {
    // Exactly half the H.261 source rate: one source picture is omitted between every coded pair.
    var encoder = H261VideoEncoder.Create(_Stream(frameRate: new(15000, 1001)));
    var picture = _FlatPicture();

    Assert.That(encoder.TryEncode(picture, null, out var first), Is.True);
    Assert.That(encoder.TryEncode(picture, null, out var second), Is.True);
    Assert.That(encoder.TryEncode(picture, null, out var third), Is.True);

    Assert.That(
      new[] { _TemporalReference(first), _TemporalReference(second), _TemporalReference(third) },
      Is.EqualTo(new[] { 0, 2, 4 }));
  }

  [Test]
  [Category("Unit")]
  public void CoarseContainerTimestampsAreMappedToTheNearestSourcePicture() {
    var encoder = H261VideoEncoder.Create(_Stream(timeBase: new(1, 1000)));
    var picture = _FlatPicture();

    Assert.That(encoder.TryEncode(picture, 0, out var first), Is.True);
    Assert.That(encoder.TryEncode(picture, 33, out var second), Is.True);
    Assert.That(encoder.TryEncode(picture, 67, out var third), Is.True);

    Assert.That(
      new[] { _TemporalReference(first), _TemporalReference(second), _TemporalReference(third) },
      Is.EqualTo(new[] { 0, 1, 2 }));
  }

  [Test]
  [Category("Unit")]
  public void ADeclaredRateFasterThanTheH261SourceClockIsRefused() {
    var refusal = Assert.Throws<NotSupportedException>(
      () => H261VideoEncoder.Create(_Stream(frameRate: new(60000, 1001))));

    Assert.Multiple(() => {
      Assert.That(refusal!.Message, Does.Contain("30000/1001"));
      Assert.That(refusal.Message, Does.Contain("3.1"));
    });
  }

  [Test]
  [Category("Unit")]
  public void TwoPicturesInTheSameSourceIntervalAreRefused() {
    var encoder = H261VideoEncoder.Create(_Stream(timeBase: new(1, 1000)));
    var picture = _FlatPicture();

    Assert.That(encoder.TryEncode(picture, 0, out _), Is.True);
    var refusal = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(picture, 1, out _));

    Assert.That(refusal!.Message, Does.Contain("source picture 0"));
  }

  private static int _TemporalReference(CodedPacket packet) {
    var reader = new H263BitReader(packet.Data.Span);
    Assert.That(reader.ReadBits(H261PictureHeader.StartCodeLength), Is.EqualTo(H261PictureHeader.StartCode));
    return reader.ReadBits(5);
  }

  private static MediaStreamInfo _Stream(Rational timeBase = default, Rational frameRate = default) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("H261"),
    TimeBase = timeBase.IsKnown ? timeBase : Rational.Unknown,
    FrameRate = frameRate.IsKnown ? frameRate : Rational.Unknown,
    Width = 176,
    Height = 144,
  };

  private static RawImage _FlatPicture() {
    const int width = 176;
    const int height = 144;
    var planes = new byte[width * height * 3 / 2];
    planes.AsSpan(0, width * height).Fill(99);
    planes.AsSpan(width * height).Fill(128);

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Yuv420P8,
      PixelData = planes,
    };
  }
}
