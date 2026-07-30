using System;
using System.IO;
using FileFormat.Core;
using FileFormat.MagicPainter;

namespace FileFormat.MagicPainter.Tests;

[TestFixture]
public sealed class MagicPainterTests {

  private static MagicPainterFile _Sample() {
    var pixels = new byte[MagicPainterFile.BitmapWidth * MagicPainterFile.BitmapHeight];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 4);

    return new() {
      BitmapData = Atari8BitGraphics.PackGr7(pixels, MagicPainterFile.BitmapHeight),
      ColorRegisters = [0x28, 0x4A, 0x6C, 0x00, 0x00],
      Rainbow = 0x07,
    };
  }

  [Test]
  [Category("Unit")]
  public void FileSize_Is3845() {
    // Five colour registers + rainbow byte + a Graphics 7 screen less its final byte.
    Assert.That(MagicPainterFile.FileSize, Is.EqualTo(3845));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LeadsWithColorRegistersThenRainbow() {
    var bytes = MagicPainterWriter.ToBytes(_Sample());

    Assert.Multiple(() => {
      Assert.That(bytes[0], Is.EqualTo(0x28));
      Assert.That(bytes[1], Is.EqualTo(0x4A));
      Assert.That(bytes[2], Is.EqualTo(0x6C));
      Assert.That(bytes[MagicPainterFile.RainbowOffset], Is.EqualTo(0x07));
    });
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesRegistersAndRainbow() {
    var original = _Sample();
    var restored = MagicPainterReader.FromBytes(MagicPainterWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.ColorRegisters, Is.EqualTo(original.ColorRegisters));
      Assert.That(restored.Rainbow, Is.EqualTo(original.Rainbow));
    });
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesEveryStoredBitmapByte() {
    var original = _Sample();
    var restored = MagicPainterReader.FromBytes(MagicPainterWriter.ToBytes(original));

    // The last byte of the screen is not stored, so it always comes back as zero.
    Assert.That(restored.BitmapData[..MagicPainterFile.StoredBitmapSize],
      Is.EqualTo(original.BitmapData[..MagicPainterFile.StoredBitmapSize]));
    Assert.That(restored.BitmapData[MagicPainterFile.StoredBitmapSize], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsAnyOtherLength()
    => Assert.Throws<InvalidDataException>(() => MagicPainterReader.FromBytes(new byte[MagicPainterFile.FileSize + 1]));

  [Test]
  [Category("Unit")]
  public void ToRawImage_ProducesTheDisplayedResolution() {
    var raw = MagicPainterFile.ToRawImage(_Sample());

    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(MagicPainterFile.DisplayWidth));
      Assert.That(raw.Height, Is.EqualTo(MagicPainterFile.DisplayHeight));
      Assert.That(raw.PaletteCount, Is.EqualTo(MagicPainterFile.ColorCount));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ProducesAConformantFile() {
    var data = new byte[MagicPainterFile.DisplayWidth * MagicPainterFile.DisplayHeight * 4];
    for (var i = 0; i < data.Length; i += 4) {
      data[i + 1] = (byte)(i % 241);
      data[i + 3] = 255;
    }

    var raw = new RawImage {
      Width = MagicPainterFile.DisplayWidth, Height = MagicPainterFile.DisplayHeight,
      Format = PixelFormat.Rgba32, PixelData = data,
    };

    Assert.That(MagicPainterWriter.ToBytes(MagicPainterFile.FromRawImage(raw)),
      Has.Length.EqualTo(MagicPainterFile.FileSize));
  }
}
