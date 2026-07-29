using System;
using System.IO;
using FileFormat.Core;
using FileFormat.VidigPaint;

namespace FileFormat.VidigPaint.Tests;

[TestFixture]
public sealed class VidigPaintTests {

  private static VidigPaintFile _Sample() {
    var pixels = new byte[VidigPaintFile.ScreenWidth * VidigPaintFile.ScreenRows];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % VidigPaintFile.ColorCount);

    return new() {
      Header = new byte[VidigPaintFile.HeaderSize],
      ScreenData = Atari8BitGraphics.PackGr9(pixels, VidigPaintFile.ScreenWidth, VidigPaintFile.ScreenRows),
    };
  }

  [Test]
  [Category("Unit")]
  public void FileSize_IsTheScreenPlusABackgroundByte()
    => Assert.That(VidigPaintFile.FileSize, Is.EqualTo(VidigPaintFile.ScreenDataSize + 1));

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesTheBackgroundColor() {
    var original = _Sample() with { BackgroundColor = 0x94 };
    var restored = VidigPaintReader.FromBytes(VidigPaintWriter.ToBytes(original));

    Assert.That(restored.BackgroundColor, Is.EqualTo(0x94));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ProducesTheFixedSize()
    => Assert.That(VidigPaintWriter.ToBytes(_Sample()), Has.Length.EqualTo(VidigPaintFile.FileSize));

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesTheScreen() {
    var original = _Sample();
    var restored = VidigPaintReader.FromBytes(VidigPaintWriter.ToBytes(original));

    Assert.That(restored.ScreenData, Is.EqualTo(original.ScreenData));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsAnyOtherLength()
    => Assert.Throws<InvalidDataException>(() => VidigPaintReader.FromBytes(new byte[VidigPaintFile.FileSize + 1]));

  [Test]
  [Category("Unit")]
  public void ToRawImage_ProducesTheDisplayedResolution() {
    var raw = VidigPaintFile.ToRawImage(_Sample());

    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(VidigPaintFile.DisplayWidth));
      Assert.That(raw.Height, Is.EqualTo(VidigPaintFile.DisplayHeight));
      Assert.That(raw.PaletteCount, Is.EqualTo(VidigPaintFile.ColorCount));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ProducesAConformantFile() {
    var data = new byte[VidigPaintFile.DisplayWidth * VidigPaintFile.DisplayHeight * 4];
    for (var i = 0; i < data.Length; i += 4) {
      data[i] = data[i + 1] = data[i + 2] = (byte)(i % 251);
      data[i + 3] = 255;
    }

    var raw = new RawImage {
      Width = VidigPaintFile.DisplayWidth, Height = VidigPaintFile.DisplayHeight,
      Format = PixelFormat.Rgba32, PixelData = data,
    };

    Assert.That(VidigPaintWriter.ToBytes(VidigPaintFile.FromRawImage(raw)), Has.Length.EqualTo(VidigPaintFile.FileSize));
  }
}
