using System;
using System.IO;
using FileFormat.Gd2;

namespace FileFormat.Gd2.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_SmallImage_PixelDataPreserved() {
    var width = 3;
    var height = 2;
    var pixelData = new byte[width * height * 4];
    // Pixel 0: opaque red
    pixelData[0] = 0x00; pixelData[1] = 0xFF; pixelData[2] = 0x00; pixelData[3] = 0x00;
    // Pixel 1: opaque green
    pixelData[4] = 0x00; pixelData[5] = 0x00; pixelData[6] = 0xFF; pixelData[7] = 0x00;
    // Pixel 2: opaque blue
    pixelData[8] = 0x00; pixelData[9] = 0x00; pixelData[10] = 0x00; pixelData[11] = 0xFF;
    // Pixel 3: semi-transparent white
    pixelData[12] = 0x3F; pixelData[13] = 0xFF; pixelData[14] = 0xFF; pixelData[15] = 0xFF;
    // Pixel 4: fully transparent black
    pixelData[16] = 0x7F; pixelData[17] = 0x00; pixelData[18] = 0x00; pixelData[19] = 0x00;
    // Pixel 5: opaque white
    pixelData[20] = 0x00; pixelData[21] = 0xFF; pixelData[22] = 0xFF; pixelData[23] = 0xFF;

    var original = new Gd2File {
      Width = width,
      Height = height,
      ChunkSize = 3,
      PixelData = pixelData,
    };

    var bytes = Gd2Writer.ToBytes(original);
    var restored = Gd2Reader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Version, Is.EqualTo(original.Version));
    Assert.That(restored.Format, Is.EqualTo(original.Format));
    Assert.That(restored.ChunkSize, Is.EqualTo(original.ChunkSize));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new Gd2File {
      Width = 2,
      Height = 2,
      ChunkSize = 2,
      PixelData = new byte[2 * 2 * 4],
    };

    var bytes = Gd2Writer.ToBytes(original);
    var restored = Gd2Reader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(2));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var width = 4;
    var height = 3;
    var pixelData = new byte[width * height * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 128);

    var original = new Gd2File {
      Width = width,
      Height = height,
      ChunkSize = 4,
      PixelData = pixelData,
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gd2");
    try {
      var bytes = Gd2Writer.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = Gd2Reader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.Version, Is.EqualTo(original.Version));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    var width = 2;
    var height = 2;
    var rgba = new byte[width * height * 4];
    // Opaque red
    rgba[0] = 255; rgba[1] = 0; rgba[2] = 0; rgba[3] = 255;
    // Opaque green
    rgba[4] = 0; rgba[5] = 255; rgba[6] = 0; rgba[7] = 255;
    // Transparent blue
    rgba[8] = 0; rgba[9] = 0; rgba[10] = 255; rgba[11] = 0;
    // Opaque white
    rgba[12] = 255; rgba[13] = 255; rgba[14] = 255; rgba[15] = 255;

    var raw = new FileFormat.Core.RawImage {
      Width = width,
      Height = height,
      Format = FileFormat.Core.PixelFormat.Rgba32,
      PixelData = rgba,
    };

    var gd2File = Gd2File.FromRawImage(raw);
    var bytes = Gd2Writer.ToBytes(gd2File);
    var restored = Gd2Reader.FromBytes(bytes);
    var rawRestored = Gd2File.ToRawImage(restored);

    Assert.That(rawRestored.Width, Is.EqualTo(width));
    Assert.That(rawRestored.Height, Is.EqualTo(height));
    Assert.That(rawRestored.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Rgba32));

    // Opaque red pixel preserved exactly
    Assert.That(rawRestored.PixelData[0], Is.EqualTo(255)); // R
    Assert.That(rawRestored.PixelData[1], Is.EqualTo(0));   // G
    Assert.That(rawRestored.PixelData[2], Is.EqualTo(0));   // B
    Assert.That(rawRestored.PixelData[3], Is.EqualTo(255)); // A opaque

    Assert.That(rawRestored.PixelData[4], Is.EqualTo(0));
    Assert.That(rawRestored.PixelData[5], Is.EqualTo(255));
    Assert.That(rawRestored.PixelData[6], Is.EqualTo(0));
    Assert.That(rawRestored.PixelData[7], Is.EqualTo(255));

    // Transparent pixel: alpha=0 -> GD2 alpha=127 -> back alpha=0
    Assert.That(rawRestored.PixelData[11], Is.EqualTo(0));

    // Opaque white
    Assert.That(rawRestored.PixelData[12], Is.EqualTo(255));
    Assert.That(rawRestored.PixelData[13], Is.EqualTo(255));
    Assert.That(rawRestored.PixelData[14], Is.EqualTo(255));
    Assert.That(rawRestored.PixelData[15], Is.EqualTo(255));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_OpaquePixel_AlphaPreservedExactly() {
    var raw = new FileFormat.Core.RawImage {
      Width = 1,
      Height = 1,
      Format = FileFormat.Core.PixelFormat.Rgba32,
      PixelData = [0xAB, 0xCD, 0xEF, 255],
    };

    var gd2File = Gd2File.FromRawImage(raw);

    // GD2 alpha for fully opaque (255) should be 0
    Assert.That(gd2File.PixelData[0], Is.EqualTo(0));

    var rawBack = Gd2File.ToRawImage(gd2File);

    // Fully opaque should round-trip exactly
    Assert.That(rawBack.PixelData[0], Is.EqualTo(0xAB));
    Assert.That(rawBack.PixelData[1], Is.EqualTo(0xCD));
    Assert.That(rawBack.PixelData[2], Is.EqualTo(0xEF));
    Assert.That(rawBack.PixelData[3], Is.EqualTo(255));
  }
}
