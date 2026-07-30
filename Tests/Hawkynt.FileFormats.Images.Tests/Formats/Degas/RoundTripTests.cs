using System;
using FileFormat.Degas;

namespace FileFormat.Degas.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_LowRes_Uncompressed() {
    var palette = new short[16];
    for (var i = 0; i < 16; ++i)
      palette[i] = (short)(i * 0x111 & 0x777);

    var pixelData = new byte[32000];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new DegasFile {
      Width = 320,
      Height = 200,
      Resolution = DegasResolution.Low,
      IsCompressed = false,
      Palette = palette,
      PixelData = pixelData
    };

    var bytes = DegasWriter.ToBytes(original);
    var restored = DegasReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.Resolution, Is.EqualTo(original.Resolution));
      Assert.That(restored.IsCompressed, Is.EqualTo(original.IsCompressed));
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_HighRes_Uncompressed() {
    var palette = new short[16];
    palette[0] = 0x000;
    palette[1] = 0x777;

    var pixelData = new byte[32000];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 2 == 0 ? 0xAA : 0x55);

    var original = new DegasFile {
      Width = 640,
      Height = 400,
      Resolution = DegasResolution.High,
      IsCompressed = false,
      Palette = palette,
      PixelData = pixelData
    };

    var bytes = DegasWriter.ToBytes(original);
    var restored = DegasReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(640));
      Assert.That(restored.Height, Is.EqualTo(400));
      Assert.That(restored.Resolution, Is.EqualTo(DegasResolution.High));
      Assert.That(restored.IsCompressed, Is.False);
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LowRes_Compressed() {
    var palette = new short[16];
    for (var i = 0; i < 16; ++i)
      palette[i] = (short)(i * 0x111 & 0x777);

    var pixelData = new byte[32000];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 3 % 256);

    var original = new DegasFile {
      Width = 320,
      Height = 200,
      Resolution = DegasResolution.Low,
      IsCompressed = true,
      Palette = palette,
      PixelData = pixelData
    };

    var bytes = DegasWriter.ToBytes(original);
    var restored = DegasReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.Resolution, Is.EqualTo(original.Resolution));
      Assert.That(restored.IsCompressed, Is.True);
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MediumRes_Uncompressed() {
    var palette = new short[16];
    palette[0] = 0x000;
    palette[1] = 0x700;
    palette[2] = 0x070;
    palette[3] = 0x007;

    var pixelData = new byte[32000];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 4);

    var original = new DegasFile {
      Width = 640,
      Height = 200,
      Resolution = DegasResolution.Medium,
      IsCompressed = false,
      Palette = palette,
      PixelData = pixelData
    };

    var bytes = DegasWriter.ToBytes(original);
    var restored = DegasReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(640));
      Assert.That(restored.Height, Is.EqualTo(200));
      Assert.That(restored.Resolution, Is.EqualTo(DegasResolution.Medium));
      Assert.That(restored.IsCompressed, Is.False);
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }
}
