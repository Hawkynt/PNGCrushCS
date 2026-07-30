using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Zoom4;

namespace FileFormat.Zoom4.Tests;

[TestFixture]
public sealed class Zoom4Tests {

  private static Zoom4File _Sample() {
    var pixels = new byte[Zoom4File.ScreenWidth * Zoom4File.ScreenRows];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % Zoom4File.ColorCount);

    return new() {
      Header = new byte[Zoom4File.HeaderSize],
      ScreenData = Atari8BitGraphics.PackGr9(pixels, Zoom4File.ScreenWidth, Zoom4File.ScreenRows),
    };
  }

  [Test]
  [Category("Unit")]
  public void FileSize_MatchesHeaderPlusScreen()
    => Assert.That(Zoom4File.FileSize, Is.EqualTo(Zoom4File.HeaderSize + Zoom4File.ScreenDataSize));

  [Test]
  [Category("Unit")]
  public void ToBytes_ProducesTheFixedSize()
    => Assert.That(Zoom4Writer.ToBytes(_Sample()), Has.Length.EqualTo(Zoom4File.FileSize));

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesTheScreen() {
    var original = _Sample();
    var restored = Zoom4Reader.FromBytes(Zoom4Writer.ToBytes(original));

    Assert.That(restored.ScreenData, Is.EqualTo(original.ScreenData));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsAnyOtherLength()
    => Assert.Throws<InvalidDataException>(() => Zoom4Reader.FromBytes(new byte[Zoom4File.FileSize + 1]));

  [Test]
  [Category("Unit")]
  public void ToRawImage_ProducesTheDisplayedResolution() {
    var raw = Zoom4File.ToRawImage(_Sample());

    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(Zoom4File.DisplayWidth));
      Assert.That(raw.Height, Is.EqualTo(Zoom4File.DisplayHeight));
      Assert.That(raw.PaletteCount, Is.EqualTo(Zoom4File.ColorCount));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ProducesAConformantFile() {
    var data = new byte[Zoom4File.DisplayWidth * Zoom4File.DisplayHeight * 4];
    for (var i = 0; i < data.Length; i += 4) {
      data[i] = data[i + 1] = data[i + 2] = (byte)(i % 251);
      data[i + 3] = 255;
    }

    var raw = new RawImage {
      Width = Zoom4File.DisplayWidth, Height = Zoom4File.DisplayHeight,
      Format = PixelFormat.Rgba32, PixelData = data,
    };

    Assert.That(Zoom4Writer.ToBytes(Zoom4File.FromRawImage(raw)), Has.Length.EqualTo(Zoom4File.FileSize));
  }
}
