using System;
using System.IO;
using FileFormat.Pkm;

namespace FileFormat.Pkm.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_V10() {
    var compressedData = new byte[32];
    for (var i = 0; i < compressedData.Length; ++i)
      compressedData[i] = (byte)(i * 13);

    var original = new PkmFile {
      Width = 8,
      Height = 8,
      PaddedWidth = 8,
      PaddedHeight = 8,
      Format = PkmFormat.Etc1Rgb,
      Version = "10",
      CompressedData = compressedData
    };

    var bytes = PkmWriter.ToBytes(original);
    var restored = PkmReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PaddedWidth, Is.EqualTo(original.PaddedWidth));
    Assert.That(restored.PaddedHeight, Is.EqualTo(original.PaddedHeight));
    Assert.That(restored.Format, Is.EqualTo(original.Format));
    Assert.That(restored.Version, Is.EqualTo(original.Version));
    Assert.That(restored.CompressedData, Is.EqualTo(original.CompressedData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_V20() {
    var compressedData = new byte[64];
    for (var i = 0; i < compressedData.Length; ++i)
      compressedData[i] = (byte)(i ^ 0xAA);

    var original = new PkmFile {
      Width = 13,
      Height = 7,
      PaddedWidth = 16,
      PaddedHeight = 8,
      Format = PkmFormat.Etc2Rgba8,
      Version = "20",
      CompressedData = compressedData
    };

    var bytes = PkmWriter.ToBytes(original);
    var restored = PkmReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PaddedWidth, Is.EqualTo(original.PaddedWidth));
    Assert.That(restored.PaddedHeight, Is.EqualTo(original.PaddedHeight));
    Assert.That(restored.Format, Is.EqualTo(PkmFormat.Etc2Rgba8));
    Assert.That(restored.Version, Is.EqualTo("20"));
    Assert.That(restored.CompressedData, Is.EqualTo(original.CompressedData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var compressedData = new byte[16];
    for (var i = 0; i < compressedData.Length; ++i)
      compressedData[i] = (byte)i;

    var original = new PkmFile {
      Width = 4,
      Height = 4,
      PaddedWidth = 4,
      PaddedHeight = 4,
      Format = PkmFormat.Etc1Rgb,
      Version = "10",
      CompressedData = compressedData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pkm");
    try {
      var bytes = PkmWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = PkmReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.CompressedData, Is.EqualTo(original.CompressedData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}
