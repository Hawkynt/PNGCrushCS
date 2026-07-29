using System;
using System.IO;
using FileFormat.Core;
using FileFormat.TextureEditorMikey;

namespace FileFormat.TextureEditorMikey.Tests;

[TestFixture]
public sealed class TextureEditorMikeyTests {

  private static TextureEditorMikeyFile _Sample() {
    var pixels = new byte[TextureEditorMikeyFile.ScreenWidth * TextureEditorMikeyFile.ScreenRows];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % TextureEditorMikeyFile.ColorCount);

    return new() {
      Header = new byte[TextureEditorMikeyFile.HeaderSize],
      ScreenData = Atari8BitGraphics.PackGr9(pixels, TextureEditorMikeyFile.ScreenWidth, TextureEditorMikeyFile.ScreenRows),
    };
  }

  [Test]
  [Category("Unit")]
  public void FileSize_MatchesHeaderPlusScreen()
    => Assert.That(TextureEditorMikeyFile.FileSize, Is.EqualTo(TextureEditorMikeyFile.HeaderSize + TextureEditorMikeyFile.ScreenDataSize));

  [Test]
  [Category("Unit")]
  public void ToBytes_ProducesTheFixedSize()
    => Assert.That(TextureEditorMikeyWriter.ToBytes(_Sample()), Has.Length.EqualTo(TextureEditorMikeyFile.FileSize));

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesTheScreen() {
    var original = _Sample();
    var restored = TextureEditorMikeyReader.FromBytes(TextureEditorMikeyWriter.ToBytes(original));

    Assert.That(restored.ScreenData, Is.EqualTo(original.ScreenData));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsAnyOtherLength()
    => Assert.Throws<InvalidDataException>(() => TextureEditorMikeyReader.FromBytes(new byte[TextureEditorMikeyFile.FileSize + 1]));

  [Test]
  [Category("Unit")]
  public void ToRawImage_ProducesTheDisplayedResolution() {
    var raw = TextureEditorMikeyFile.ToRawImage(_Sample());

    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(TextureEditorMikeyFile.DisplayWidth));
      Assert.That(raw.Height, Is.EqualTo(TextureEditorMikeyFile.DisplayHeight));
      Assert.That(raw.PaletteCount, Is.EqualTo(TextureEditorMikeyFile.ColorCount));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ProducesAConformantFile() {
    var data = new byte[TextureEditorMikeyFile.DisplayWidth * TextureEditorMikeyFile.DisplayHeight * 4];
    for (var i = 0; i < data.Length; i += 4) {
      data[i] = data[i + 1] = data[i + 2] = (byte)(i % 251);
      data[i + 3] = 255;
    }

    var raw = new RawImage {
      Width = TextureEditorMikeyFile.DisplayWidth, Height = TextureEditorMikeyFile.DisplayHeight,
      Format = PixelFormat.Rgba32, PixelData = data,
    };

    Assert.That(TextureEditorMikeyWriter.ToBytes(TextureEditorMikeyFile.FromRawImage(raw)), Has.Length.EqualTo(TextureEditorMikeyFile.FileSize));
  }
}
