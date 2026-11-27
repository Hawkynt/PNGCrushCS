using System;
using FileFormat.Core;
using FileFormat.Pwc;

namespace FileFormat.Pwc.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [TestCase(1, 1, 2)]
  [TestCase(1, 1, 16)]
  [TestCase(1, 1, 256)]
  [TestCase(2, 1, 2)]
  [TestCase(1, 7, 9)]
  [TestCase(13, 7, 2)]
  [TestCase(13, 7, 5)]
  [TestCase(31, 17, 16)]
  [TestCase(33, 19, 100)]
  [TestCase(64, 40, 256)]
  [TestCase(257, 3, 200)]
  [Category("Unit")]
  public void RawImage_SurvivesWriteAndRead(int width, int height, int colors) {
    var raw = PwcWriterTests._Indexed(width, height, colors);
    var file = PwcFile.FromRawImage(raw);
    var back = PwcFile.ToRawImage(PwcReader.FromBytes(PwcWriter.ToBytes(file)));

    Assert.That(back.Width, Is.EqualTo(width));
    Assert.That(back.Height, Is.EqualTo(height));
    Assert.That(back.PixelData, Is.EqualTo(raw.PixelData));
    Assert.That(back.Palette.AsSpan(0, colors * 3).ToArray(), Is.EqualTo(raw.Palette));
  }

  [TestCase(PwcCodingFlags.None)]
  [TestCase(PwcCodingFlags.NoCornerGuesses)]
  [TestCase(PwcCodingFlags.NoListDecision)]
  [TestCase(PwcCodingFlags.NoCornerGuesses | PwcCodingFlags.NoListDecision)]
  [TestCase(PwcCodingFlags.Gray)]
  [Category("Unit")]
  public void EightBitOptions_SurviveWriteAndRead(PwcCodingFlags flags) {
    var file = PwcFile.FromRawImage(PwcWriterTests._Indexed(45, 23, 256, 7)) with { CodingFlags = flags };
    var back = PwcReader.FromBytes(PwcWriter.ToBytes(file));

    Assert.That(back.CodingFlags, Is.EqualTo(flags));
    Assert.That(back.PixelData, Is.EqualTo(file.PixelData));
  }

  [TestCase(1, 2)]
  [TestCase(4, 11)]
  [TestCase(8, 60)]
  [Category("Unit")]
  public void BottomUpAndAdaptationLimit_SurviveWriteAndRead(int depth, int colors) {
    foreach (var maxLps in new[] { 0, 1, 3, 7, 30, 255 }) {
      var file = PwcFile.FromRawImage(PwcWriterTests._Indexed(21, 14, colors, maxLps)) with { BottomUp = true, MaxLps = maxLps };
      Assert.That(file.BitsPerPixel, Is.EqualTo(depth));
      var back = PwcReader.FromBytes(PwcWriter.ToBytes(file));

      Assert.That(back.BottomUp, Is.True);
      Assert.That(back.MaxLps, Is.EqualTo(maxLps));
      Assert.That(back.PixelData, Is.EqualTo(file.PixelData), $"maxlps {maxLps}");
    }
  }

  [TestCase(2)]
  [TestCase(16)]
  [TestCase(256)]
  [Category("Unit")]
  public void WidestAndTallestPictures_SurviveWriteAndRead(int colors) {
    foreach (var (width, height) in new[] { (65535, 2), (2, 65535) }) {
      var raw = PwcWriterTests._Indexed(width, height, colors);
      var back = PwcReader.FromBytes(PwcWriter.ToBytes(PwcFile.FromRawImage(raw)));
      Assert.That(back.PixelData, Is.EqualTo(raw.PixelData), $"{width}x{height}");
    }
  }

  [Test]
  [Category("Unit")]
  public void FileWithoutPalette_DecodesAgainstTheLinearRamp() {
    var file = PwcFile.FromRawImage(PwcWriterTests._Indexed(9, 6, 256)) with { StoresPalette = false, CodingFlags = PwcCodingFlags.Gray };
    var back = PwcReader.FromBytes(PwcWriter.ToBytes(file));

    Assert.That(back.StoresPalette, Is.False);
    Assert.That(back.Palette, Is.EqualTo(PwcReader.LinearPalette(8)));
    Assert.That(back.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void LinearPalettes_MatchTheReferenceLibrary() {
    Assert.That(PwcReader.LinearPalette(1), Is.EqualTo(new byte[] { 255, 255, 255, 0, 0, 0 }));
    Assert.That(PwcReader.LinearPalette(4)[..6], Is.EqualTo(new byte[] { 8, 8, 8, 24, 24, 24 }));
    Assert.That(PwcReader.LinearPalette(8)[^3..], Is.EqualTo(new byte[] { 255, 255, 255 }));
  }
}
