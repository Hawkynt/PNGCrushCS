using System;
using FileFormat.SyntheticArts;
using FileFormat.Core;

namespace FileFormat.SyntheticArts.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_PreservesAllFields() {
    var palette = new short[16];
    palette[0] = 0x0777;
    palette[1] = 0x0700;
    palette[2] = 0x0070;
    palette[3] = 0x0007;

    var pixelData = new byte[32000];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new SyntheticArtsFile {
      Palette = palette,
      PixelData = pixelData
    };

    var bytes = SyntheticArtsWriter.ToBytes(original);
    var restored = SyntheticArtsReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeroPixels() {
    var original = new SyntheticArtsFile {
      Palette = new short[16],
      PixelData = new byte[32000]
    };

    var bytes = SyntheticArtsWriter.ToBytes(original);
    var restored = SyntheticArtsReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_RandomData() {
    var rng = new Random(42);
    var palette = new short[16];
    for (var i = 0; i < 16; ++i)
      palette[i] = (short)(rng.Next(0, 0x0800) & 0x0777);

    var pixelData = new byte[32000];
    rng.NextBytes(pixelData);

    var original = new SyntheticArtsFile {
      Palette = palette,
      PixelData = pixelData
    };

    var bytes = SyntheticArtsWriter.ToBytes(original);
    var restored = SyntheticArtsReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var palette = new short[16];
    palette[0] = 0x0777;
    palette[3] = 0x0007;

    var pixelData = new byte[32000];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var original = new SyntheticArtsFile {
      Palette = palette,
      PixelData = pixelData
    };

    var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".srt");
    try {
      var bytes = SyntheticArtsWriter.ToBytes(original);
      System.IO.File.WriteAllBytes(tempPath, bytes);

      var restored = SyntheticArtsReader.FromFile(new System.IO.FileInfo(tempPath));

      Assert.Multiple(() => {
        Assert.That(restored.Palette, Is.EqualTo(original.Palette));
        Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
      });
    } finally {
      if (System.IO.File.Exists(tempPath))
        System.IO.File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    var palette = new short[16];
    palette[0] = 0x0000;
    palette[1] = 0x0700;
    palette[2] = 0x0070;
    palette[3] = 0x0007;

    // Build valid 2-plane pixel data for 640x200
    var chunky = new byte[640 * 200];
    for (var i = 0; i < chunky.Length; ++i)
      chunky[i] = (byte)(i % 4);

    var planar = PlanarConverter.ChunkyToAtariSt(chunky, 640, 200, 2);

    var original = new SyntheticArtsFile {
      Palette = palette,
      PixelData = planar
    };

    var raw = SyntheticArtsFile.ToRawImage(original);
    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(640));
      Assert.That(raw.Height, Is.EqualTo(200));
      Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
      Assert.That(raw.PaletteCount, Is.EqualTo(4));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_FromRawImage() {
    var rgb = new byte[4 * 3];
    rgb[0] = 0; rgb[1] = 0; rgb[2] = 0;       // black
    rgb[3] = 255; rgb[4] = 0; rgb[5] = 0;     // red
    rgb[6] = 0; rgb[7] = 255; rgb[8] = 0;     // green
    rgb[9] = 0; rgb[10] = 0; rgb[11] = 255;   // blue

    var pixels = new byte[640 * 200];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 4);

    var raw = new RawImage {
      Width = 640,
      Height = 200,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = rgb,
      PaletteCount = 4,
    };

    var file = SyntheticArtsFile.FromRawImage(raw);
    Assert.Multiple(() => {
      Assert.That(file.PixelData.Length, Is.EqualTo(32000));
      Assert.That(file.Palette.Length, Is.EqualTo(16));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SyntheticArtsFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SyntheticArtsFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 640,
      Height = 200,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[640 * 200 * 3],
    };
    Assert.Throws<ArgumentException>(() => SyntheticArtsFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongWidth_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 320,
      Height = 200,
      Format = PixelFormat.Indexed8,
      PixelData = new byte[320 * 200],
      Palette = new byte[4 * 3],
      PaletteCount = 4,
    };
    Assert.Throws<ArgumentException>(() => SyntheticArtsFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongHeight_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 640,
      Height = 400,
      Format = PixelFormat.Indexed8,
      PixelData = new byte[640 * 400],
      Palette = new byte[4 * 3],
      PaletteCount = 4,
    };
    Assert.Throws<ArgumentException>(() => SyntheticArtsFile.FromRawImage(raw));
  }
}
