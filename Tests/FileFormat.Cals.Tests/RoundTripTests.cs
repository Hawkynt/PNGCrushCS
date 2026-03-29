using System;
using System.IO;
using FileFormat.Cals;

namespace FileFormat.Cals.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_200dpi() {
    var original = new CalsFile {
      Width = 32,
      Height = 8,
      Dpi = 200,
      Orientation = "portrait",
      SrcDocId = "TEST001",
      DstDocId = "DST001",
      PixelData = _CreateTestPixelData(32, 8)
    };

    var bytes = CalsWriter.ToBytes(original);
    var restored = CalsReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Dpi, Is.EqualTo(200));
    Assert.That(restored.Orientation, Is.EqualTo("portrait"));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_300dpi() {
    var original = new CalsFile {
      Width = 64,
      Height = 16,
      Dpi = 300,
      Orientation = "landscape",
      SrcDocId = "SCAN300",
      DstDocId = "NONE",
      PixelData = _CreateTestPixelData(64, 16)
    };

    var bytes = CalsWriter.ToBytes(original);
    var restored = CalsReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Dpi, Is.EqualTo(300));
    Assert.That(restored.Orientation, Is.EqualTo("landscape"));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var original = new CalsFile {
      Width = 16,
      Height = 4,
      Dpi = 200,
      PixelData = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE }
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cal");
    try {
      var bytes = CalsWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = CalsReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_NonByteAlignedWidth() {
    var original = new CalsFile {
      Width = 13,
      Height = 3,
      Dpi = 400,
      PixelData = new byte[] { 0b11010110, 0b10000000, 0b10101010, 0b10000000, 0b01110110, 0b10000000 }
    };

    var bytes = CalsWriter.ToBytes(original);
    var restored = CalsReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(13));
    Assert.That(restored.Height, Is.EqualTo(3));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  private static byte[] _CreateTestPixelData(int width, int height) {
    var bytesPerRow = (width + 7) / 8;
    var data = new byte[bytesPerRow * height];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i * 17 % 256);
    return data;
  }
}
