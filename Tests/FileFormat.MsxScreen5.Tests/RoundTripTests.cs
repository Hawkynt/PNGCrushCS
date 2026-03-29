using System;
using System.IO;
using FileFormat.MsxScreen5;

namespace FileFormat.MsxScreen5.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_WithPalette_PreservesData() {
    var pixelData = new byte[MsxScreen5File.PixelDataSize];
    var palette = new byte[MsxScreen5File.PaletteSize];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 3 % 256);
    for (var i = 0; i < palette.Length; ++i)
      palette[i] = (byte)(i * 7 % 256);

    var original = new MsxScreen5File {
      PixelData = pixelData,
      Palette = palette,
      HasBsaveHeader = false
    };

    var bytes = MsxScreen5Writer.ToBytes(original);
    var restored = MsxScreen5Reader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(256));
    Assert.That(restored.Height, Is.EqualTo(212));
    Assert.That(restored.HasBsaveHeader, Is.False);
    Assert.That(restored.PixelData, Is.EqualTo(pixelData));
    Assert.That(restored.Palette, Is.EqualTo(palette));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WithoutPalette_PreservesPixelData() {
    var pixelData = new byte[MsxScreen5File.PixelDataSize];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var original = new MsxScreen5File {
      PixelData = pixelData,
      Palette = null,
      HasBsaveHeader = false
    };

    var bytes = MsxScreen5Writer.ToBytes(original);
    var restored = MsxScreen5Reader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(pixelData));
    Assert.That(restored.Palette, Is.Null);
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WithBsaveHeader_PreservesHeader() {
    var pixelData = new byte[MsxScreen5File.PixelDataSize];
    var palette = new byte[MsxScreen5File.PaletteSize];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new MsxScreen5File {
      PixelData = pixelData,
      Palette = palette,
      HasBsaveHeader = true
    };

    var bytes = MsxScreen5Writer.ToBytes(original);
    var restored = MsxScreen5Reader.FromBytes(bytes);

    Assert.That(restored.HasBsaveHeader, Is.True);
    Assert.That(restored.PixelData, Is.EqualTo(pixelData));
    Assert.That(restored.Palette, Is.EqualTo(palette));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros_PreservesData() {
    var original = new MsxScreen5File {
      PixelData = new byte[MsxScreen5File.PixelDataSize],
      Palette = new byte[MsxScreen5File.PaletteSize],
      HasBsaveHeader = false
    };

    var bytes = MsxScreen5Writer.ToBytes(original);
    var restored = MsxScreen5Reader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.All.EqualTo(0));
    Assert.That(restored.Palette, Is.All.EqualTo(0));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile_PreservesData() {
    var pixelData = new byte[MsxScreen5File.PixelDataSize];
    var palette = new byte[MsxScreen5File.PaletteSize];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);
    for (var i = 0; i < palette.Length; ++i)
      palette[i] = (byte)(i * 5 % 256);

    var original = new MsxScreen5File {
      PixelData = pixelData,
      Palette = palette,
      HasBsaveHeader = false
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sc5");
    try {
      var bytes = MsxScreen5Writer.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);
      var restored = MsxScreen5Reader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(256));
      Assert.That(restored.Height, Is.EqualTo(212));
      Assert.That(restored.PixelData, Is.EqualTo(pixelData));
      Assert.That(restored.Palette, Is.EqualTo(palette));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ToRawImage_ProducesCorrectDimensions() {
    var pixelData = new byte[MsxScreen5File.PixelDataSize];
    var palette = new byte[MsxScreen5File.PaletteSize];
    palette[0] = 0x77; palette[1] = 0x07; // entry 0: white

    var file = new MsxScreen5File {
      PixelData = pixelData,
      Palette = palette,
      HasBsaveHeader = false
    };

    var rawImage = MsxScreen5File.ToRawImage(file);

    Assert.That(rawImage.Width, Is.EqualTo(256));
    Assert.That(rawImage.Height, Is.EqualTo(212));
    Assert.That(rawImage.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Indexed8));
    Assert.That(rawImage.PixelData.Length, Is.EqualTo(256 * 212));
    Assert.That(rawImage.PaletteCount, Is.EqualTo(16));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ToRawImage_PixelValuesInRange() {
    var pixelData = new byte[MsxScreen5File.PixelDataSize];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var file = new MsxScreen5File {
      PixelData = pixelData,
      Palette = MsxScreen5File.DefaultMsx2Palette,
      HasBsaveHeader = false
    };

    var rawImage = MsxScreen5File.ToRawImage(file);

    for (var i = 0; i < rawImage.PixelData.Length; ++i)
      Assert.That(rawImage.PixelData[i], Is.LessThan(16));
  }
}
