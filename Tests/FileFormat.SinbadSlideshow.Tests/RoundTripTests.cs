using System;
using System.IO;
using FileFormat.SinbadSlideshow;
using FileFormat.Core;

namespace FileFormat.SinbadSlideshow.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_PreservesAllFields() {
    var palette = new short[16];
    for (var i = 0; i < 16; ++i)
      palette[i] = (short)(i * 0x0111);

    var pixelData = new byte[32000];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new SinbadSlideshowFile {
      Palette = palette,
      PixelData = pixelData,
    };

    var bytes = SinbadSlideshowWriter.ToBytes(original);
    var restored = SinbadSlideshowReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(320));
      Assert.That(restored.Height, Is.EqualTo(200));
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new SinbadSlideshowFile {
      Palette = new short[16],
      PixelData = new byte[32000],
    };

    var bytes = SinbadSlideshowWriter.ToBytes(original);
    var restored = SinbadSlideshowReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
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

    var original = new SinbadSlideshowFile {
      Palette = palette,
      PixelData = pixelData,
    };

    var bytes = SinbadSlideshowWriter.ToBytes(original);
    var restored = SinbadSlideshowReader.FromBytes(bytes);

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
    palette[15] = 0x0007;

    var pixelData = new byte[32000];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var original = new SinbadSlideshowFile {
      Palette = palette,
      PixelData = pixelData,
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ssb");
    try {
      File.WriteAllBytes(tempPath, SinbadSlideshowWriter.ToBytes(original));
      var restored = SinbadSlideshowReader.FromFile(new FileInfo(tempPath));

      Assert.Multiple(() => {
        Assert.That(restored.Width, Is.EqualTo(320));
        Assert.That(restored.Height, Is.EqualTo(200));
        Assert.That(restored.Palette, Is.EqualTo(original.Palette));
        Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
      });
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
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

    var pixelData = new byte[32000];
    var original = new SinbadSlideshowFile {
      Palette = palette,
      PixelData = pixelData,
    };

    var raw = SinbadSlideshowFile.ToRawImage(original);
    var restored = SinbadSlideshowFile.FromRawImage(raw);
    var rawBack = SinbadSlideshowFile.ToRawImage(restored);

    Assert.Multiple(() => {
      Assert.That(rawBack.Width, Is.EqualTo(320));
      Assert.That(rawBack.Height, Is.EqualTo(200));
      Assert.That(rawBack.Format, Is.EqualTo(PixelFormat.Indexed8));
      Assert.That(rawBack.PaletteCount, Is.EqualTo(16));
      Assert.That(rawBack.PixelData.Length, Is.EqualTo(320 * 200));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_PreservesPalette() {
    // Only 0 and 7 survive the lossy 12-bit ST palette round-trip via RGB bytes
    var palette = new short[16];
    palette[0] = 0x0777; // white
    palette[5] = 0x0700; // red
    palette[10] = 0x0070; // green
    palette[15] = 0x0007; // blue

    var original = new SinbadSlideshowFile {
      Palette = palette,
      PixelData = new byte[32000],
    };

    var raw = SinbadSlideshowFile.ToRawImage(original);
    var restored = SinbadSlideshowFile.FromRawImage(raw);

    Assert.Multiple(() => {
      Assert.That(restored.Palette[0], Is.EqualTo(original.Palette[0]));
      Assert.That(restored.Palette[5], Is.EqualTo(original.Palette[5]));
      Assert.That(restored.Palette[10], Is.EqualTo(original.Palette[10]));
      Assert.That(restored.Palette[15], Is.EqualTo(original.Palette[15]));
    });
  }
}
