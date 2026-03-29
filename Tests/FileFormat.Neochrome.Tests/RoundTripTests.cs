using System;
using FileFormat.Neochrome;

namespace FileFormat.Neochrome.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_PreservesAllFields() {
    var palette = new short[16];
    for (var i = 0; i < 16; ++i)
      palette[i] = (short)(i * 0x0111); // gradient: 0x0000, 0x0111, 0x0222, ...

    var pixelData = new byte[32000];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new NeochromeFile {
      Flag = 0,
      Palette = palette,
      AnimSpeed = 5,
      AnimDirection = 1,
      AnimSteps = 10,
      AnimXOffset = 16,
      AnimYOffset = 32,
      AnimWidth = 64,
      AnimHeight = 48,
      PixelData = pixelData
    };

    var bytes = NeochromeWriter.ToBytes(original);
    var restored = NeochromeReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(320));
      Assert.That(restored.Height, Is.EqualTo(200));
      Assert.That(restored.Flag, Is.EqualTo(original.Flag));
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
      Assert.That(restored.AnimSpeed, Is.EqualTo(original.AnimSpeed));
      Assert.That(restored.AnimDirection, Is.EqualTo(original.AnimDirection));
      Assert.That(restored.AnimSteps, Is.EqualTo(original.AnimSteps));
      Assert.That(restored.AnimXOffset, Is.EqualTo(original.AnimXOffset));
      Assert.That(restored.AnimYOffset, Is.EqualTo(original.AnimYOffset));
      Assert.That(restored.AnimWidth, Is.EqualTo(original.AnimWidth));
      Assert.That(restored.AnimHeight, Is.EqualTo(original.AnimHeight));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeroPixels() {
    var original = new NeochromeFile {
      Palette = new short[16],
      PixelData = new byte[32000]
    };

    var bytes = NeochromeWriter.ToBytes(original);
    var restored = NeochromeReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(320));
      Assert.That(restored.Height, Is.EqualTo(200));
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

    var original = new NeochromeFile {
      Flag = (short)rng.Next(short.MinValue, short.MaxValue),
      Palette = palette,
      AnimSpeed = (byte)rng.Next(0, 256),
      AnimDirection = (byte)rng.Next(0, 256),
      AnimSteps = (short)rng.Next(short.MinValue, short.MaxValue),
      AnimXOffset = (short)rng.Next(short.MinValue, short.MaxValue),
      AnimYOffset = (short)rng.Next(short.MinValue, short.MaxValue),
      AnimWidth = (short)rng.Next(short.MinValue, short.MaxValue),
      AnimHeight = (short)rng.Next(short.MinValue, short.MaxValue),
      PixelData = pixelData
    };

    var bytes = NeochromeWriter.ToBytes(original);
    var restored = NeochromeReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Flag, Is.EqualTo(original.Flag));
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
      Assert.That(restored.AnimSpeed, Is.EqualTo(original.AnimSpeed));
      Assert.That(restored.AnimDirection, Is.EqualTo(original.AnimDirection));
      Assert.That(restored.AnimSteps, Is.EqualTo(original.AnimSteps));
      Assert.That(restored.AnimXOffset, Is.EqualTo(original.AnimXOffset));
      Assert.That(restored.AnimYOffset, Is.EqualTo(original.AnimYOffset));
      Assert.That(restored.AnimWidth, Is.EqualTo(original.AnimWidth));
      Assert.That(restored.AnimHeight, Is.EqualTo(original.AnimHeight));
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

    var original = new NeochromeFile {
      Palette = palette,
      PixelData = pixelData
    };

    var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".neo");
    try {
      var bytes = NeochromeWriter.ToBytes(original);
      System.IO.File.WriteAllBytes(tempPath, bytes);

      var restored = NeochromeReader.FromFile(new System.IO.FileInfo(tempPath));

      Assert.Multiple(() => {
        Assert.That(restored.Width, Is.EqualTo(320));
        Assert.That(restored.Height, Is.EqualTo(200));
        Assert.That(restored.Palette, Is.EqualTo(original.Palette));
        Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
      });
    } finally {
      if (System.IO.File.Exists(tempPath))
        System.IO.File.Delete(tempPath);
    }
  }
}
