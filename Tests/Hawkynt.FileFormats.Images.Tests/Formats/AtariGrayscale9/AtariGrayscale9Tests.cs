using System;
using System.IO;
using FileFormat.Core;
using FileFormat.AtariGrayscale9;

namespace FileFormat.AtariGrayscale9.Tests;

[TestFixture]
public sealed class AtariGrayscale9Tests {

  private static AtariGrayscale9File _Sample() {
    var pixels = new byte[AtariGrayscale9File.ScreenWidth * AtariGrayscale9File.ScreenRows];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % AtariGrayscale9File.ColorCount);

    return new() {
      Header = new byte[AtariGrayscale9File.HeaderSize],
      ScreenData = Atari8BitGraphics.PackGr9(pixels, AtariGrayscale9File.ScreenWidth, AtariGrayscale9File.ScreenRows),
    };
  }

  [Test]
  [Category("Unit")]
  public void FileSize_MatchesHeaderPlusScreen()
    => Assert.That(AtariGrayscale9File.FileSize, Is.EqualTo(AtariGrayscale9File.HeaderSize + AtariGrayscale9File.ScreenDataSize));

  [Test]
  [Category("Unit")]
  public void ToBytes_ProducesTheFixedSize()
    => Assert.That(AtariGrayscale9Writer.ToBytes(_Sample()), Has.Length.EqualTo(AtariGrayscale9File.FileSize));

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesTheScreen() {
    var original = _Sample();
    var restored = AtariGrayscale9Reader.FromBytes(AtariGrayscale9Writer.ToBytes(original));

    Assert.That(restored.ScreenData, Is.EqualTo(original.ScreenData));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsAnyOtherLength()
    => Assert.Throws<InvalidDataException>(() => AtariGrayscale9Reader.FromBytes(new byte[AtariGrayscale9File.FileSize + 1]));

  [Test]
  [Category("Unit")]
  public void ToRawImage_ProducesTheDisplayedResolution() {
    var raw = AtariGrayscale9File.ToRawImage(_Sample());

    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(AtariGrayscale9File.DisplayWidth));
      Assert.That(raw.Height, Is.EqualTo(AtariGrayscale9File.DisplayHeight));
      Assert.That(raw.PaletteCount, Is.EqualTo(AtariGrayscale9File.ColorCount));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ProducesAConformantFile() {
    var data = new byte[AtariGrayscale9File.DisplayWidth * AtariGrayscale9File.DisplayHeight * 4];
    for (var i = 0; i < data.Length; i += 4) {
      data[i] = data[i + 1] = data[i + 2] = (byte)(i % 251);
      data[i + 3] = 255;
    }

    var raw = new RawImage {
      Width = AtariGrayscale9File.DisplayWidth, Height = AtariGrayscale9File.DisplayHeight,
      Format = PixelFormat.Rgba32, PixelData = data,
    };

    Assert.That(AtariGrayscale9Writer.ToBytes(AtariGrayscale9File.FromRawImage(raw)), Has.Length.EqualTo(AtariGrayscale9File.FileSize));
  }
}
