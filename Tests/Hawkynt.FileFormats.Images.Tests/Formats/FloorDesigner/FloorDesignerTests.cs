using System;
using System.IO;
using FileFormat.Core;
using FileFormat.FloorDesigner;

namespace FileFormat.FloorDesigner.Tests;

[TestFixture]
public sealed class FloorDesignerTests {

  private static FloorDesignerFile _Sample() {
    var pixels = new byte[FloorDesignerFile.ScreenWidth * FloorDesignerFile.ScreenRows];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % FloorDesignerFile.ColorCount);

    return new() {
      Header = new byte[FloorDesignerFile.HeaderSize],
      ScreenData = Atari8BitGraphics.PackGr9(pixels, FloorDesignerFile.ScreenWidth, FloorDesignerFile.ScreenRows),
    };
  }

  [Test]
  [Category("Unit")]
  public void FileSize_MatchesHeaderPlusScreen()
    => Assert.That(FloorDesignerFile.FileSize, Is.EqualTo(FloorDesignerFile.HeaderSize + FloorDesignerFile.ScreenDataSize));

  [Test]
  [Category("Unit")]
  public void ToBytes_ProducesTheFixedSize()
    => Assert.That(FloorDesignerWriter.ToBytes(_Sample()), Has.Length.EqualTo(FloorDesignerFile.FileSize));

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesTheScreen() {
    var original = _Sample();
    var restored = FloorDesignerReader.FromBytes(FloorDesignerWriter.ToBytes(original));

    Assert.That(restored.ScreenData, Is.EqualTo(original.ScreenData));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsAnyOtherLength()
    => Assert.Throws<InvalidDataException>(() => FloorDesignerReader.FromBytes(new byte[FloorDesignerFile.FileSize + 1]));

  [Test]
  [Category("Unit")]
  public void ToRawImage_ProducesTheDisplayedResolution() {
    var raw = FloorDesignerFile.ToRawImage(_Sample());

    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(FloorDesignerFile.DisplayWidth));
      Assert.That(raw.Height, Is.EqualTo(FloorDesignerFile.DisplayHeight));
      Assert.That(raw.PaletteCount, Is.EqualTo(FloorDesignerFile.ColorCount));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ProducesAConformantFile() {
    var data = new byte[FloorDesignerFile.DisplayWidth * FloorDesignerFile.DisplayHeight * 4];
    for (var i = 0; i < data.Length; i += 4) {
      data[i] = data[i + 1] = data[i + 2] = (byte)(i % 251);
      data[i + 3] = 255;
    }

    var raw = new RawImage {
      Width = FloorDesignerFile.DisplayWidth, Height = FloorDesignerFile.DisplayHeight,
      Format = PixelFormat.Rgba32, PixelData = data,
    };

    Assert.That(FloorDesignerWriter.ToBytes(FloorDesignerFile.FromRawImage(raw)), Has.Length.EqualTo(FloorDesignerFile.FileSize));
  }
}
