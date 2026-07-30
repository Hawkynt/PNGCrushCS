using System;
using FileFormat.Ccitt;

namespace FileFormat.Ccitt.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Group3_1D_SmallImage() {
    var width = 16;
    var height = 4;
    var bytesPerRow = width / 8;
    var pixelData = new byte[bytesPerRow * height];
    pixelData[0] = 0xFF;                          // row 0: 8 black + 8 white
    pixelData[2] = 0x00; pixelData[3] = 0xFF;    // row 1: 8 white + 8 black
    pixelData[4] = 0b10101010; pixelData[5] = 0b01010101; // row 2: alternating
    pixelData[6] = 0b11001100; pixelData[7] = 0b00110011; // row 3: striped

    var original = new CcittFile {
      Width = width,
      Height = height,
      Format = CcittFormat.Group3_1D,
      PixelData = pixelData
    };

    var compressed = CcittWriter.ToBytes(original);
    var restored = CcittReader.FromBytes(compressed, width, height, CcittFormat.Group3_1D);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Format, Is.EqualTo(CcittFormat.Group3_1D));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Group3_1D_LargerImage() {
    var width = 64;
    var height = 8;
    var bytesPerRow = width / 8;
    var pixelData = new byte[bytesPerRow * height];

    // Create a pattern: each row has progressively more black pixels
    for (var row = 0; row < height; ++row)
      for (var byteIdx = 0; byteIdx < bytesPerRow; ++byteIdx)
        pixelData[row * bytesPerRow + byteIdx] = (byte)(byteIdx < row ? 0xFF : 0x00);

    var original = new CcittFile {
      Width = width,
      Height = height,
      Format = CcittFormat.Group3_1D,
      PixelData = pixelData
    };

    var compressed = CcittWriter.ToBytes(original);
    var restored = CcittReader.FromBytes(compressed, width, height, CcittFormat.Group3_1D);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Group3_1D_AllWhiteImage() {
    var width = 256;
    var height = 4;
    var bytesPerRow = width / 8;
    var pixelData = new byte[bytesPerRow * height]; // all white

    var original = new CcittFile {
      Width = width,
      Height = height,
      Format = CcittFormat.Group3_1D,
      PixelData = pixelData
    };

    var compressed = CcittWriter.ToBytes(original);
    var restored = CcittReader.FromBytes(compressed, width, height, CcittFormat.Group3_1D);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Group3_1D_AllBlackImage() {
    var width = 256;
    var height = 4;
    var bytesPerRow = width / 8;
    var pixelData = new byte[bytesPerRow * height];
    Array.Fill(pixelData, (byte)0xFF);

    var original = new CcittFile {
      Width = width,
      Height = height,
      Format = CcittFormat.Group3_1D,
      PixelData = pixelData
    };

    var compressed = CcittWriter.ToBytes(original);
    var restored = CcittReader.FromBytes(compressed, width, height, CcittFormat.Group3_1D);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Group4_SmallImage() {
    var width = 16;
    var height = 4;
    var bytesPerRow = width / 8;
    var pixelData = new byte[bytesPerRow * height];
    pixelData[0] = 0xFF;
    pixelData[2] = 0x00; pixelData[3] = 0xFF;
    pixelData[4] = 0b10101010; pixelData[5] = 0b01010101;
    pixelData[6] = 0b11001100; pixelData[7] = 0b00110011;

    var original = new CcittFile {
      Width = width,
      Height = height,
      Format = CcittFormat.Group4,
      PixelData = pixelData
    };

    var compressed = CcittWriter.ToBytes(original);
    var restored = CcittReader.FromBytes(compressed, width, height, CcittFormat.Group4);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Format, Is.EqualTo(CcittFormat.Group4));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Group4_LargerImage() {
    var width = 64;
    var height = 8;
    var bytesPerRow = width / 8;
    var pixelData = new byte[bytesPerRow * height];

    for (var row = 0; row < height; ++row)
      for (var byteIdx = 0; byteIdx < bytesPerRow; ++byteIdx)
        pixelData[row * bytesPerRow + byteIdx] = (byte)(byteIdx < row ? 0xFF : 0x00);

    var original = new CcittFile {
      Width = width,
      Height = height,
      Format = CcittFormat.Group4,
      PixelData = pixelData
    };

    var compressed = CcittWriter.ToBytes(original);
    var restored = CcittReader.FromBytes(compressed, width, height, CcittFormat.Group4);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Group3_1D_ViaStream() {
    var width = 8;
    var pixelData = new byte[] { 0b11001100 };

    var original = new CcittFile {
      Width = width,
      Height = 1,
      Format = CcittFormat.Group3_1D,
      PixelData = pixelData
    };

    var compressed = CcittWriter.ToBytes(original);
    using var ms = new System.IO.MemoryStream(compressed);
    var restored = CcittReader.FromStream(ms, width, 1, CcittFormat.Group3_1D);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
