using System;
using System.IO;
using FileFormat.Core;
using FileFormat.KssPaint;

namespace FileFormat.KssPaint.Tests;

[TestFixture]
public sealed class KssPaintTests {

  private static KssPaintFile _Sample() {
    var pixels = new byte[Atari8BitGraphics.Gr7Width * KssPaintFile.BitmapHeight];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 4);

    return new() {
      BitmapData = Atari8BitGraphics.PackGr7(pixels, KssPaintFile.BitmapHeight),
      Colors = [0x00, 0x28, 0x4A, 0x6C],
    };
  }

  [Test]
  [Category("Unit")]
  public void FileSize_Is6404() {
    // 6400-byte Graphics 15 screen + 4 colour bytes.
    Assert.That(KssPaintFile.FileSize, Is.EqualTo(6404));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PlacesColorsAfterTheBitmap() {
    var bytes = KssPaintWriter.ToBytes(_Sample());

    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.EqualTo(KssPaintFile.FileSize));
      Assert.That(bytes[KssPaintFile.ColorOffset], Is.EqualTo(0x00));
      Assert.That(bytes[KssPaintFile.ColorOffset + 1], Is.EqualTo(0x28));
      Assert.That(bytes[KssPaintFile.ColorOffset + 3], Is.EqualTo(0x6C));
    });
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesBitmapAndColors() {
    var original = _Sample();
    var restored = KssPaintReader.FromBytes(KssPaintWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
      Assert.That(restored.Colors, Is.EqualTo(original.Colors));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsAnyOtherLength()
    => Assert.Throws<InvalidDataException>(
      () => KssPaintReader.FromBytes(new byte[KssPaintFile.FileSize - 1]));

  [Test]
  [Category("Unit")]
  public void ToRawImage_ProducesTheDisplayedResolution() {
    var raw = KssPaintFile.ToRawImage(_Sample());

    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(KssPaintFile.DisplayWidth));
      Assert.That(raw.Height, Is.EqualTo(KssPaintFile.DisplayHeight));
      Assert.That(raw.PaletteCount, Is.EqualTo(KssPaintFile.ColorCount));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ProducesAConformantFile() {
    var data = new byte[KssPaintFile.DisplayWidth * KssPaintFile.DisplayHeight * 4];
    for (var i = 0; i < data.Length; i += 4) {
      data[i + 2] = (byte)(i % 233);
      data[i + 3] = 255;
    }

    var raw = new RawImage {
      Width = KssPaintFile.DisplayWidth, Height = KssPaintFile.DisplayHeight,
      Format = PixelFormat.Rgba32, PixelData = data,
    };

    Assert.That(KssPaintWriter.ToBytes(KssPaintFile.FromRawImage(raw)),
      Has.Length.EqualTo(KssPaintFile.FileSize));
  }
}
