using System;
using System.IO;
using FileFormat.Core;
using FileFormat.DrawIt;

namespace FileFormat.DrawIt.Tests;

[TestFixture]
public sealed class DrawItTests {

  private static DrawItFile _Sample() {
    var pixels = new byte[DrawItFile.BitmapWidth * DrawItFile.BitmapHeight];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 4);

    return new() {
      BitmapData = Atari8BitGraphics.PackGr7(pixels, DrawItFile.BitmapHeight),
      ColorRegisters = [0x28, 0x4A, 0x6C, 0x00, 0x00],
    };
  }

  [Test]
  [Category("Unit")]
  public void FileSize_Is3845() {
    // 3840-byte Graphics 7 screen + five GTIA colour registers.
    Assert.That(DrawItFile.FileSize, Is.EqualTo(3845));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ProducesTheFixedSize()
    => Assert.That(DrawItWriter.ToBytes(_Sample()), Has.Length.EqualTo(DrawItFile.FileSize));

  [Test]
  [Category("Unit")]
  public void ToBytes_PlacesColorRegistersAfterTheBitmap() {
    var bytes = DrawItWriter.ToBytes(_Sample());

    Assert.Multiple(() => {
      Assert.That(bytes[DrawItFile.ColorRegisterOffset], Is.EqualTo(0x28));
      Assert.That(bytes[DrawItFile.ColorRegisterOffset + 1], Is.EqualTo(0x4A));
      Assert.That(bytes[DrawItFile.ColorRegisterOffset + 2], Is.EqualTo(0x6C));
    });
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesBitmapAndRegisters() {
    var original = _Sample();
    var restored = DrawItReader.FromBytes(DrawItWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
      Assert.That(restored.ColorRegisters, Is.EqualTo(original.ColorRegisters));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsAnyOtherLength()
    => Assert.Throws<InvalidDataException>(() => DrawItReader.FromBytes(new byte[DrawItFile.FileSize - 1]));

  [Test]
  [Category("Unit")]
  public void ToRawImage_ProducesTheDisplayedResolution() {
    var raw = DrawItFile.ToRawImage(_Sample());

    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(DrawItFile.DisplayWidth));
      Assert.That(raw.Height, Is.EqualTo(DrawItFile.DisplayHeight));
      Assert.That(raw.PaletteCount, Is.EqualTo(DrawItFile.ColorCount));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_DoublesEachLogicalPixel() {
    var raw = DrawItFile.ToRawImage(_Sample());

    // Every 2x2 block of the displayed image comes from one stored pixel.
    Assert.Multiple(() => {
      Assert.That(raw.PixelData[1], Is.EqualTo(raw.PixelData[0]));
      Assert.That(raw.PixelData[DrawItFile.DisplayWidth], Is.EqualTo(raw.PixelData[0]));
      Assert.That(raw.PixelData[DrawItFile.DisplayWidth + 1], Is.EqualTo(raw.PixelData[0]));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_RejectsForeignDimensions() {
    var raw = new RawImage {
      Width = 100, Height = 100, Format = PixelFormat.Rgba32, PixelData = new byte[100 * 100 * 4],
    };

    Assert.Throws<ArgumentException>(() => DrawItFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ProducesAConformantFile() {
    var data = new byte[DrawItFile.DisplayWidth * DrawItFile.DisplayHeight * 4];
    for (var i = 0; i < data.Length; i += 4) {
      data[i] = (byte)(i % 251);
      data[i + 3] = 255;
    }

    var raw = new RawImage {
      Width = DrawItFile.DisplayWidth, Height = DrawItFile.DisplayHeight,
      Format = PixelFormat.Rgba32, PixelData = data,
    };

    Assert.That(DrawItWriter.ToBytes(DrawItFile.FromRawImage(raw)), Has.Length.EqualTo(DrawItFile.FileSize));
  }
}
