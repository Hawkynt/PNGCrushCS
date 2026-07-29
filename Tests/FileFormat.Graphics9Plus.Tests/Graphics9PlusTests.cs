using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Graphics9Plus;

namespace FileFormat.Graphics9Plus.Tests;

[TestFixture]
public sealed class Graphics9PlusTests {

  private static Graphics9PlusFile _Sample() {
    var pixels = new byte[Graphics9PlusFile.ScreenWidth * Graphics9PlusFile.ScreenRows];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % Graphics9PlusFile.ColorCount);

    return new() {
      Header = new byte[Graphics9PlusFile.HeaderSize],
      ScreenData = Atari8BitGraphics.PackGr9(pixels, Graphics9PlusFile.ScreenWidth, Graphics9PlusFile.ScreenRows),
    };
  }

  [Test]
  [Category("Unit")]
  public void FileSize_MatchesHeaderPlusScreen()
    => Assert.That(Graphics9PlusFile.FileSize, Is.EqualTo(Graphics9PlusFile.HeaderSize + Graphics9PlusFile.ScreenDataSize));

  [Test]
  [Category("Unit")]
  public void ToBytes_ProducesTheFixedSize()
    => Assert.That(Graphics9PlusWriter.ToBytes(_Sample()), Has.Length.EqualTo(Graphics9PlusFile.FileSize));

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesTheScreen() {
    var original = _Sample();
    var restored = Graphics9PlusReader.FromBytes(Graphics9PlusWriter.ToBytes(original));

    Assert.That(restored.ScreenData, Is.EqualTo(original.ScreenData));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsAnyOtherLength()
    => Assert.Throws<InvalidDataException>(() => Graphics9PlusReader.FromBytes(new byte[Graphics9PlusFile.FileSize + 1]));

  [Test]
  [Category("Unit")]
  public void ToRawImage_ProducesTheDisplayedResolution() {
    var raw = Graphics9PlusFile.ToRawImage(_Sample());

    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(Graphics9PlusFile.DisplayWidth));
      Assert.That(raw.Height, Is.EqualTo(Graphics9PlusFile.DisplayHeight));
      Assert.That(raw.PaletteCount, Is.EqualTo(Graphics9PlusFile.ColorCount));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ProducesAConformantFile() {
    var data = new byte[Graphics9PlusFile.DisplayWidth * Graphics9PlusFile.DisplayHeight * 4];
    for (var i = 0; i < data.Length; i += 4) {
      data[i] = data[i + 1] = data[i + 2] = (byte)(i % 251);
      data[i + 3] = 255;
    }

    var raw = new RawImage {
      Width = Graphics9PlusFile.DisplayWidth, Height = Graphics9PlusFile.DisplayHeight,
      Format = PixelFormat.Rgba32, PixelData = data,
    };

    Assert.That(Graphics9PlusWriter.ToBytes(Graphics9PlusFile.FromRawImage(raw)), Has.Length.EqualTo(Graphics9PlusFile.FileSize));
  }
}
