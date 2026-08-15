using System;
using FileFormat.Core;

namespace Hawkynt.FileFormats.Video.Tests;

/// <summary>The pieces the video abstractions are built out of.</summary>
[TestFixture]
public sealed class VideoAbstractionTests {

  [Test]
  [Category("Unit")]
  public void ATimeBaseKeepsRatesThatAreNotWholeNumbers() {
    // NTSC runs at 30000/1001 frames a second. Stored as 29.97 it drifts by about a frame in a
    // thousand, which over a feature-length film is seconds of picture against sound.
    var timeBase = new Rational(1001, 30000);

    Assert.That(timeBase.Scale(30000), Is.EqualTo(TimeSpan.FromSeconds(1001)));
    Assert.That(timeBase.ToDouble(), Is.EqualTo(1001d / 30000).Within(1e-15));
  }

  [Test]
  [Category("Unit")]
  public void AnUnstatedTimeBaseSaysSo() {
    Assert.That(Rational.Unknown.IsKnown, Is.False);
    Assert.That(Rational.Unknown.Scale(1000), Is.EqualTo(TimeSpan.Zero));
    Assert.That(Rational.Unknown.ToString(), Is.EqualTo("unknown"));
  }

  [Test]
  [Category("Unit")]
  public void ALargeTimestampSurvivesAMicrosecondTimeBase() {
    // Through Int128 rather than double: 2^53 microseconds is past what a double holds exactly, and
    // a rounded timestamp is a frame shown at the wrong moment.
    var microseconds = new Rational(1, 1_000_000);

    Assert.That(microseconds.Scale(9_007_199_254_740_995L), Is.EqualTo(TimeSpan.FromTicks(90_071_992_547_409_950L)));
  }

  [Test]
  [Category("Unit")]
  public void AFourCharacterCodeReadsBackAsItsLetters() {
    Assert.That(CodecTag.FromCharacters("MJPG").ToString(), Is.EqualTo("MJPG"));
    Assert.That(CodecTag.FromCharacters("MJPG").Value, Is.EqualTo(0x47504A4Du));
  }

  [Test]
  [Category("Unit")]
  public void ACodeThatIsNotFourLettersKeepsItsNumber() {
    // BI_RGB is the number zero and has no name to give. A refusal that printed four control
    // characters would name nothing at all.
    Assert.That(CodecTag.None.ToString(), Is.EqualTo("0x00000000"));
  }

  [Test]
  [Category("Unit")]
  public void SpellingIsNotPartOfACodecsIdentity() {
    // ffprobe reads a container patched from MJPG to mjpg as the same codec with the same frame
    // count, so a decoder that took only one spelling would refuse a file every other tool plays.
    Assert.That(CodecTag.FromCharacters("MJPG").EqualsIgnoringCase(CodecTag.FromCharacters("mjpg")), Is.True);
    Assert.That(CodecTag.FromCharacters("MJPG").EqualsIgnoringCase(CodecTag.FromCharacters("MJPA")), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void MetadataThatSaysNothingSaysSo() {
    Assert.That(VideoMetadata.Empty.IsEmpty, Is.True);
    Assert.That(new VideoMetadata { Title = "A film" }.IsEmpty, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void CoverArtIsAnImageAndCarriesAnImagesMetadata() {
    // Reusing ImageMetadata rather than duplicating it: a cover is a picture, and a picture's
    // metadata already has a model that the image optimiser knows how to work on.
    var art = new CoverArt([0x89, 0x50, 0x4E, 0x47], "image/png", "Front cover", "cover",
      new ImageMetadata { TextEntries = [new("Title", "A film")] });

    var metadata = new VideoMetadata { CoverArt = [art] };

    Assert.That(metadata.IsEmpty, Is.False);
    Assert.That(metadata.CoverArt[0].Metadata!.TextEntries[0].Text, Is.EqualTo("A film"));
  }
}
