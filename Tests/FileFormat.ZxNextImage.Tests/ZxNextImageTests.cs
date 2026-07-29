using System;
using System.IO;
using FileFormat.Core;
using FileFormat.ZxNextImage;

namespace FileFormat.ZxNextImage.Tests;

[TestFixture]
public sealed class ZxNextImageTests {

  private static ZxNextImageFile _Sample() {
    var palette = new byte[ZxNextImageFile.PaletteDataSize];
    for (var i = 0; i < ZxNextImageFile.ColorCount; ++i) {
      palette[i * 2] = (byte)i;
      palette[i * 2 + 1] = (byte)(i & 1);
    }

    var pixels = new byte[ZxNextImageFile.PixelDataSize];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 256);

    return new() { PaletteData = palette, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void FileSize_Is49664() {
    // 512-byte palette + 256x192 one-byte indices.
    Assert.That(ZxNextImageFile.FileSize, Is.EqualTo(49664));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesPaletteAndPixels() {
    var original = _Sample();
    var restored = ZxNextImageReader.FromBytes(ZxNextImageWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.PaletteData, Is.EqualTo(original.PaletteData));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsAnyOtherLength()
    => Assert.Throws<InvalidDataException>(() => ZxNextImageReader.FromBytes(new byte[ZxNextImageFile.FileSize - 1]));

  [Test]
  [Category("Unit")]
  public void Palette_ExpandsThreeBitChannelsAcrossTheFullRange() {
    // Entry 0xFF sets every channel bit; it must come out as pure white.
    var file = _Sample() with { PaletteData = new byte[ZxNextImageFile.PaletteDataSize] };
    file.PaletteData[0] = 0xFF;
    file.PaletteData[1] = 0x01;

    var raw = ZxNextImageFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(raw.Palette![0], Is.EqualTo(255));
      Assert.That(raw.Palette[1], Is.EqualTo(255));
      Assert.That(raw.Palette[2], Is.EqualTo(255));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ProducesTheImageResolution() {
    var raw = ZxNextImageFile.ToRawImage(_Sample());

    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(ZxNextImageFile.ImageWidth));
      Assert.That(raw.Height, Is.EqualTo(ZxNextImageFile.ImageHeight));
      Assert.That(raw.PaletteCount, Is.EqualTo(ZxNextImageFile.ColorCount));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ProducesAConformantFile() {
    var data = new byte[ZxNextImageFile.ImageWidth * ZxNextImageFile.ImageHeight * 4];
    for (var i = 0; i < data.Length; i += 4) {
      data[i] = (byte)(i % 251);
      data[i + 2] = (byte)(i % 199);
      data[i + 3] = 255;
    }

    var raw = new RawImage {
      Width = ZxNextImageFile.ImageWidth, Height = ZxNextImageFile.ImageHeight,
      Format = PixelFormat.Rgba32, PixelData = data,
    };

    Assert.That(ZxNextImageWriter.ToBytes(ZxNextImageFile.FromRawImage(raw)),
      Has.Length.EqualTo(ZxNextImageFile.FileSize));
  }
}
