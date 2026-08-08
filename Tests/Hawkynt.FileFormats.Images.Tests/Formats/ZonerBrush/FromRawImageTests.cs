using FileFormat.Core;

namespace FileFormat.ZonerBrush.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>A preview-sized picture of sixteen colours, which is all four bits a pixel can address.</summary>
  private static RawImage _SixteenColours() {
    var palette = new byte[16 * 3];
    for (var i = 0; i < 16; ++i) {
      palette[i * 3] = (byte)(i * 17);
      palette[i * 3 + 1] = (byte)(255 - i * 17);
      palette[i * 3 + 2] = (byte)(i * 9 + 3);
    }

    var pixels = new byte[ZonerBrushFile.Width * ZonerBrushFile.Height * 3];
    for (var y = 0; y < ZonerBrushFile.Height; ++y)
      for (var x = 0; x < ZonerBrushFile.Width; ++x) {
        var entry = (x / 3 + y / 5) % 16;
        var offset = (y * ZonerBrushFile.Width + x) * 3;
        pixels[offset] = palette[entry * 3];
        pixels[offset + 1] = palette[entry * 3 + 1];
        pixels[offset + 2] = palette[entry * 3 + 2];
      }

    return new() {
      Width = ZonerBrushFile.Width, Height = ZonerBrushFile.Height,
      Format = PixelFormat.Rgb24, PixelData = pixels
    };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PreviewSizedSixteenColourPicture_ReturnsEveryPixelUnchanged() {
    var source = _SixteenColours();

    var restored = ZonerBrushFile.ToRawImage(ZonerBrushReader.FromBytes(ZonerBrushWriter.ToBytes(ZonerBrushFile.FromRawImage(source))));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(ZonerBrushFile.Width));
      Assert.That(restored.Height, Is.EqualTo(ZonerBrushFile.Height));
      Assert.That(restored.EnsureFormat(PixelFormat.Rgb24).PixelData, Is.EqualTo(source.PixelData));
    });
  }

  /// <summary>The preview is a fixed size, so a picture of any other one is sampled onto it.</summary>
  [Test]
  [Category("Integration")]
  public void FromRawImage_AnyOtherSize_IsSampledOntoThePreview([Values(13, 100, 400)] int width) {
    var file = ZonerBrushFile.FromRawImage(_SixteenColours().SampleTo(width, width / 2 + 1));

    Assert.That(ZonerBrushWriter.ToBytes(file), Has.Length.EqualTo(ZonerBrushFile.MinimumFileSize));
  }

  /// <summary>
  /// The palette entry is laid out blue first, which the samples settle: all three carry the
  /// standard Windows sixteen, and its entry 1 — dark red — keeps its 0x7F in the third byte.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromRawImage_PaletteEntriesAreStoredBlueFirst() {
    var pixels = new byte[ZonerBrushFile.Width * ZonerBrushFile.Height * 3];
    for (var i = 0; i < ZonerBrushFile.Width * ZonerBrushFile.Height; ++i) {
      pixels[i * 3] = 0x7F;
      pixels[i * 3 + 1] = 0;
      pixels[i * 3 + 2] = 0;
    }

    var file = ZonerBrushFile.FromRawImage(new() {
      Width = ZonerBrushFile.Width, Height = ZonerBrushFile.Height,
      Format = PixelFormat.Rgb24, PixelData = pixels
    });

    Assert.Multiple(() => {
      Assert.That(file.Palette[0], Is.Zero);
      Assert.That(file.Palette[1], Is.Zero);
      Assert.That(file.Palette[2], Is.EqualTo(0x7F));
    });
  }

  /// <summary>The rows run bottom upwards, so the picture's first row is the file's last.</summary>
  [Test]
  [Category("Unit")]
  public void FromRawImage_TopRowOfThePictureIsTheLastRowOfTheFile() {
    var pixels = new byte[ZonerBrushFile.Width * ZonerBrushFile.Height * 3];
    for (var x = 0; x < ZonerBrushFile.Width; ++x)
      pixels[x * 3] = 255;

    var file = ZonerBrushFile.FromRawImage(new() {
      Width = ZonerBrushFile.Width, Height = ZonerBrushFile.Height,
      Format = PixelFormat.Rgb24, PixelData = pixels
    });

    var lastRow = (ZonerBrushFile.Height - 1) * ZonerBrushFile.BytesPerRow;

    Assert.Multiple(() => {
      Assert.That(file.PixelData[lastRow], Is.Not.Zero);
      Assert.That(file.PixelData[0], Is.Zero);
    });
  }
}
