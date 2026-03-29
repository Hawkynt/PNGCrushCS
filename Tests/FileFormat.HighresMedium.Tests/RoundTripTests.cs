using System;
using FileFormat.HighresMedium;
using FileFormat.Core;

namespace FileFormat.HighresMedium.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_PreservesAllFields() {
    var palette1 = new short[16];
    palette1[0] = 0x0777;
    palette1[1] = 0x0700;
    palette1[2] = 0x0070;
    palette1[3] = 0x0007;

    var palette2 = new short[16];
    palette2[0] = 0x0000;
    palette2[1] = 0x0770;
    palette2[2] = 0x0077;
    palette2[3] = 0x0707;

    var pixelData1 = new byte[32000];
    var pixelData2 = new byte[32000];
    for (var i = 0; i < 32000; ++i) {
      pixelData1[i] = (byte)(i * 13 % 256);
      pixelData2[i] = (byte)(i * 17 % 256);
    }

    var original = new HighresMediumFile {
      Palette1 = palette1,
      PixelData1 = pixelData1,
      Palette2 = palette2,
      PixelData2 = pixelData2
    };

    var bytes = HighresMediumWriter.ToBytes(original);
    var restored = HighresMediumReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Palette1, Is.EqualTo(original.Palette1));
      Assert.That(restored.PixelData1, Is.EqualTo(original.PixelData1));
      Assert.That(restored.Palette2, Is.EqualTo(original.Palette2));
      Assert.That(restored.PixelData2, Is.EqualTo(original.PixelData2));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeroPixels() {
    var original = new HighresMediumFile {
      Palette1 = new short[16],
      PixelData1 = new byte[32000],
      Palette2 = new short[16],
      PixelData2 = new byte[32000]
    };

    var bytes = HighresMediumWriter.ToBytes(original);
    var restored = HighresMediumReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.PixelData1, Is.EqualTo(original.PixelData1));
      Assert.That(restored.PixelData2, Is.EqualTo(original.PixelData2));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_RandomData() {
    var rng = new Random(42);
    var palette1 = new short[16];
    var palette2 = new short[16];
    for (var i = 0; i < 16; ++i) {
      palette1[i] = (short)(rng.Next(0, 0x0800) & 0x0777);
      palette2[i] = (short)(rng.Next(0, 0x0800) & 0x0777);
    }

    var pixelData1 = new byte[32000];
    var pixelData2 = new byte[32000];
    rng.NextBytes(pixelData1);
    rng.NextBytes(pixelData2);

    var original = new HighresMediumFile {
      Palette1 = palette1,
      PixelData1 = pixelData1,
      Palette2 = palette2,
      PixelData2 = pixelData2
    };

    var bytes = HighresMediumWriter.ToBytes(original);
    var restored = HighresMediumReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Palette1, Is.EqualTo(original.Palette1));
      Assert.That(restored.PixelData1, Is.EqualTo(original.PixelData1));
      Assert.That(restored.Palette2, Is.EqualTo(original.Palette2));
      Assert.That(restored.PixelData2, Is.EqualTo(original.PixelData2));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var palette1 = new short[16];
    palette1[0] = 0x0777;
    var palette2 = new short[16];
    palette2[0] = 0x0700;

    var pixelData1 = new byte[32000];
    var pixelData2 = new byte[32000];
    for (var i = 0; i < 32000; ++i) {
      pixelData1[i] = (byte)(i * 11 % 256);
      pixelData2[i] = (byte)(i * 23 % 256);
    }

    var original = new HighresMediumFile {
      Palette1 = palette1,
      PixelData1 = pixelData1,
      Palette2 = palette2,
      PixelData2 = pixelData2
    };

    var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".hrm");
    try {
      var bytes = HighresMediumWriter.ToBytes(original);
      System.IO.File.WriteAllBytes(tempPath, bytes);

      var restored = HighresMediumReader.FromFile(new System.IO.FileInfo(tempPath));

      Assert.Multiple(() => {
        Assert.That(restored.Palette1, Is.EqualTo(original.Palette1));
        Assert.That(restored.PixelData1, Is.EqualTo(original.PixelData1));
        Assert.That(restored.Palette2, Is.EqualTo(original.Palette2));
        Assert.That(restored.PixelData2, Is.EqualTo(original.PixelData2));
      });
    } finally {
      if (System.IO.File.Exists(tempPath))
        System.IO.File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void ToRawImage_ProducesRgb24() {
    var palette1 = new short[16];
    palette1[0] = 0x0000; // black
    palette1[1] = 0x0700; // red

    var palette2 = new short[16];
    palette2[0] = 0x0000; // black
    palette2[1] = 0x0070; // green

    var original = new HighresMediumFile {
      Palette1 = palette1,
      PixelData1 = new byte[32000],
      Palette2 = palette2,
      PixelData2 = new byte[32000]
    };

    var raw = HighresMediumFile.ToRawImage(original);

    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(640));
      Assert.That(raw.Height, Is.EqualTo(200));
      Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(raw.PixelData.Length, Is.EqualTo(640 * 200 * 3));
    });
  }

  [Test]
  [Category("Integration")]
  public void ToRawImage_BlendsTwoFrames() {
    // Frame 1: all pixels = color index 1 (red = 0x0700 -> R=255)
    // Frame 2: all pixels = color index 1 (green = 0x0070 -> G=255)
    // Blended: R=127, G=127, B=0
    var palette1 = new short[16];
    palette1[0] = 0x0000;
    palette1[1] = 0x0700;

    var palette2 = new short[16];
    palette2[0] = 0x0000;
    palette2[1] = 0x0070;

    // Build planar data where all pixels = index 1 (for 2-plane, bit 0 is set)
    var chunky = new byte[640 * 200];
    for (var i = 0; i < chunky.Length; ++i)
      chunky[i] = 1;

    var planar = PlanarConverter.ChunkyToAtariSt(chunky, 640, 200, 2);

    var file = new HighresMediumFile {
      Palette1 = palette1,
      PixelData1 = (byte[])planar.Clone(),
      Palette2 = palette2,
      PixelData2 = planar
    };

    var raw = HighresMediumFile.ToRawImage(file);

    // First pixel: R=(255+0)/2=127, G=(0+255)/2=127, B=(0+0)/2=0
    Assert.Multiple(() => {
      Assert.That(raw.PixelData[0], Is.EqualTo(127)); // R
      Assert.That(raw.PixelData[1], Is.EqualTo(127)); // G
      Assert.That(raw.PixelData[2], Is.EqualTo(0));   // B
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HighresMediumFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HighresMediumFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 640,
      Height = 200,
      Format = PixelFormat.Indexed8,
      PixelData = new byte[640 * 200],
      Palette = new byte[4 * 3],
      PaletteCount = 4,
    };
    Assert.Throws<ArgumentException>(() => HighresMediumFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongWidth_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 320,
      Height = 200,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[320 * 200 * 3],
    };
    Assert.Throws<ArgumentException>(() => HighresMediumFile.FromRawImage(raw));
  }
}
