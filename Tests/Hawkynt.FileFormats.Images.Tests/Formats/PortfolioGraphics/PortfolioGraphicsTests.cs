using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.PortfolioGraphics.Tests;

/// <summary>The Portfolio's screen: 1920 bytes of bits and nothing else.</summary>
[TestFixture]
public sealed class PortfolioGraphicsTests {

  private static RawImage _Picture() {
    var pixels = new byte[240 * 64 * 3];
    for (var y = 0; y < 64; ++y)
    for (var x = 0; x < 240; ++x) {
      var ink = (x / 10 + y / 8) % 2 == 0;
      var at = (y * 240 + x) * 3;
      pixels[at] = pixels[at + 1] = pixels[at + 2] = (byte)(ink ? 0 : 255);
    }

    return new() { Width = 240, Height = 64, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void Written_IsTheScreenWithNoHeaderInFrontOfIt() {
    var bytes = PortfolioGraphicsWriter.ToBytes(PortfolioGraphicsFile.FromRawImage(_Picture()));
    Assert.That(bytes, Has.Length.EqualTo(1920), "thirty bytes a row for sixty-four rows");
  }

  [Test]
  [Category("Unit")]
  public void Decoded_TreatsAClearBitAsPaperAndASetOneAsInk() {
    var raw = new byte[1920];
    raw[0] = 0x80;

    var image = PortfolioGraphicsFile.ToRawImage(PortfolioGraphicsReader.FromBytes(raw));
    var unpacked = BilevelRows.Unpack(image.PixelData, 240, 64);

    Assert.Multiple(() => {
      Assert.That(image.Palette![0], Is.EqualTo(255), "index zero is the paper");
      Assert.That(image.Palette[3], Is.EqualTo(0), "index one is the ink");
      Assert.That(unpacked[0], Is.EqualTo(1));
      Assert.That(unpacked[1], Is.EqualTo(0));
    });
  }

  [Test]
  [Category("Unit")]
  public void Read_TakesAFullScreenAsTheScreenAndAnythingElseAsTheRunLengthForm() {
    // A run-length stream: a repeat count and a byte, per row.
    var packed = new byte[64 * 2];
    for (var y = 0; y < 64; ++y) {
      packed[y * 2] = 30;
      packed[y * 2 + 1] = 0;
    }

    Assert.That(PortfolioGraphicsReader.FromBytes(packed).PixelData, Has.Length.EqualTo(1920));
    Assert.That(PortfolioGraphicsReader.FromBytes(new byte[1920]).PixelData, Has.Length.EqualTo(1920));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_KeepsTheBitmap() {
    var original = PortfolioGraphicsFile.FromRawImage(_Picture());
    var restored = PortfolioGraphicsReader.FromBytes(PortfolioGraphicsWriter.ToBytes(original));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
