using System;
using FileFormat.MacPaint;

namespace FileFormat.MacPaint.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeroPixels() {
    var original = new MacPaintFile {
      Version = 0,
      BrushPatterns = new byte[304],
      PixelData = new byte[51840]
    };

    var bytes = MacPaintWriter.ToBytes(original);
    var restored = MacPaintReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(576));
      Assert.That(restored.Height, Is.EqualTo(720));
      Assert.That(restored.Version, Is.EqualTo(0));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_CheckerboardPattern() {
    var pixelData = new byte[51840];
    for (var row = 0; row < 720; ++row)
      for (var col = 0; col < 72; ++col)
        pixelData[row * 72 + col] = (byte)(row % 2 == 0 ? 0xAA : 0x55);

    var original = new MacPaintFile {
      Version = 2,
      BrushPatterns = new byte[304],
      PixelData = pixelData
    };

    var bytes = MacPaintWriter.ToBytes(original);
    var restored = MacPaintReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(576));
      Assert.That(restored.Height, Is.EqualTo(720));
      Assert.That(restored.Version, Is.EqualTo(2));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PreservesBrushPatterns() {
    var patterns = new byte[304];
    for (var i = 0; i < patterns.Length; ++i)
      patterns[i] = (byte)(i * 7 % 256);

    var original = new MacPaintFile {
      Version = 2,
      BrushPatterns = patterns,
      PixelData = new byte[51840]
    };

    var bytes = MacPaintWriter.ToBytes(original);
    var restored = MacPaintReader.FromBytes(bytes);

    Assert.That(restored.BrushPatterns, Is.EqualTo(original.BrushPatterns));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_RandomPixelData() {
    var pixelData = new byte[51840];
    var rng = new Random(12345);
    rng.NextBytes(pixelData);

    var original = new MacPaintFile {
      Version = 0,
      BrushPatterns = new byte[304],
      PixelData = pixelData
    };

    var bytes = MacPaintWriter.ToBytes(original);
    var restored = MacPaintReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaStream() {
    var original = new MacPaintFile {
      Version = 2,
      BrushPatterns = new byte[304],
      PixelData = new byte[51840]
    };

    var bytes = MacPaintWriter.ToBytes(original);
    using var ms = new System.IO.MemoryStream(bytes);
    var restored = MacPaintReader.FromStream(ms);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(576));
      Assert.That(restored.Height, Is.EqualTo(720));
      Assert.That(restored.Version, Is.EqualTo(2));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }
}
