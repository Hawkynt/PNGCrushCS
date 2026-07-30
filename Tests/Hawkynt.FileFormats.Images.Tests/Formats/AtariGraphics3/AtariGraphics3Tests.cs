using System;
using System.IO;
using FileFormat.Core;
using FileFormat.AtariGraphics3;

namespace FileFormat.AtariGraphics3.Tests;

[TestFixture]
public sealed class AtariGraphics3Tests {

  private static AtariGraphics3File _Sample(bool storedColors) {
    var pixels = new byte[Atari8BitGraphics.Gr3Width * Atari8BitGraphics.Gr3Height];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % AtariGraphics3File.ColorCount);

    return new() {
      ScreenData = Atari8BitGraphics.PackGr3(pixels),
      Colors = [0x00, 0x28, 0x4A, 0x6C],
      HasStoredColors = storedColors,
    };
  }

  [Test]
  [Category("Unit")]
  public void ScreenSize_Is240Bytes() {
    // 40x24 pixels at two bits each.
    Assert.That(AtariGraphics3File.PlainFileSize, Is.EqualTo(240));
    Assert.That(AtariGraphics3File.ColoredFileSize, Is.EqualTo(244));
  }

  [Test]
  [Category("Unit")]
  [TestCase(true)]
  [TestCase(false)]
  public void RoundTrip_PreservesTheScreen(bool storedColors) {
    var original = _Sample(storedColors);
    var restored = AtariGraphics3Reader.FromBytes(AtariGraphics3Writer.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.ScreenData, Is.EqualTo(original.ScreenData));
      Assert.That(restored.HasStoredColors, Is.EqualTo(storedColors));
    });
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesStoredColors() {
    var original = _Sample(storedColors: true);
    var restored = AtariGraphics3Reader.FromBytes(AtariGraphics3Writer.ToBytes(original));

    Assert.That(restored.Colors, Is.EqualTo(original.Colors));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WithoutColors_UsesTheOperatingSystemDefaults() {
    var restored = AtariGraphics3Reader.FromBytes(new byte[AtariGraphics3File.PlainFileSize]);

    Assert.That(restored.Colors, Is.EqualTo(AtariGraphics3File.DefaultColors.ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsAnyOtherLength()
    => Assert.Throws<InvalidDataException>(() => AtariGraphics3Reader.FromBytes(new byte[242]));

  [Test]
  [Category("Unit")]
  public void ToRawImage_DrawsEachPixelAsAn8x8Block() {
    var raw = AtariGraphics3File.ToRawImage(_Sample(storedColors: true));

    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(AtariGraphics3File.DisplayWidth));
      Assert.That(raw.Height, Is.EqualTo(AtariGraphics3File.DisplayHeight));
      for (var y = 0; y < 8; ++y)
      for (var x = 0; x < 8; ++x)
        Assert.That(raw.PixelData[y * AtariGraphics3File.DisplayWidth + x], Is.EqualTo(raw.PixelData[0]));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ProducesAConformantFile() {
    var w = AtariGraphics3File.DisplayWidth;
    var h = AtariGraphics3File.DisplayHeight;
    var data = new byte[w * h * 4];
    for (var i = 0; i < data.Length; i += 4) {
      data[i] = (byte)(i % 251);
      data[i + 3] = 255;
    }

    var raw = new RawImage { Width = w, Height = h, Format = PixelFormat.Rgba32, PixelData = data };

    Assert.That(AtariGraphics3Writer.ToBytes(AtariGraphics3File.FromRawImage(raw)),
      Has.Length.EqualTo(AtariGraphics3File.ColoredFileSize));
  }
}
