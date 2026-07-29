using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Mamut;

namespace FileFormat.Mamut.Tests;

[TestFixture]
public sealed class MamutTests {

  private static MamutFile _Sample() {
    var pixels = new byte[MamutFile.BitmapWidth * MamutFile.BitmapHeight];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % MamutFile.ColorCount);

    return new() { BitmapData = Atari8BitGraphics.PackGr7(pixels, MamutFile.BitmapHeight) };
  }

  [Test]
  [Category("Unit")]
  public void FileSize_IsTheBitmapAlone() {
    // Mamut stores no palette; the operating system's default registers colour the screen.
    Assert.That(MamutFile.FileSize, Is.EqualTo(3840));
    Assert.That(MamutFile.FileSize, Is.EqualTo(MamutFile.BitmapDataSize));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesTheBitmap() {
    var original = _Sample();
    var restored = MamutReader.FromBytes(MamutWriter.ToBytes(original));

    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsAnyOtherLength()
    => Assert.Throws<InvalidDataException>(() => MamutReader.FromBytes(new byte[MamutFile.FileSize - 1]));

  [Test]
  [Category("Unit")]
  public void ToRawImage_UsesTheOperatingSystemDefaultPalette() {
    var raw = MamutFile.ToRawImage(_Sample());

    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(MamutFile.DisplayWidth));
      Assert.That(raw.Height, Is.EqualTo(MamutFile.DisplayHeight));
      Assert.That(raw.PaletteCount, Is.EqualTo(MamutFile.ColorCount));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ProducesAConformantFile() {
    var data = new byte[MamutFile.DisplayWidth * MamutFile.DisplayHeight * 4];
    for (var i = 0; i < data.Length; i += 4) {
      data[i] = (byte)(i % 251);
      data[i + 3] = 255;
    }

    var raw = new RawImage {
      Width = MamutFile.DisplayWidth, Height = MamutFile.DisplayHeight,
      Format = PixelFormat.Rgba32, PixelData = data,
    };

    Assert.That(MamutWriter.ToBytes(MamutFile.FromRawImage(raw)), Has.Length.EqualTo(MamutFile.FileSize));
  }
}
