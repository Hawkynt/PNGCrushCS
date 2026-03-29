using System;
using FileFormat.Pkm;

namespace FileFormat.Pkm.Tests;

[TestFixture]
public sealed class PkmWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithPkmMagic() {
    var file = new PkmFile {
      Width = 4,
      Height = 4,
      PaddedWidth = 4,
      PaddedHeight = 4,
      Format = PkmFormat.Etc1Rgb,
      Version = "10",
      CompressedData = new byte[8]
    };

    var bytes = PkmWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo((byte)'P'));
    Assert.That(bytes[1], Is.EqualTo((byte)'K'));
    Assert.That(bytes[2], Is.EqualTo((byte)'M'));
    Assert.That(bytes[3], Is.EqualTo((byte)' '));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CorrectVersion() {
    var file = new PkmFile {
      Width = 4,
      Height = 4,
      PaddedWidth = 4,
      PaddedHeight = 4,
      Format = PkmFormat.Etc2Rgba8,
      Version = "20",
      CompressedData = new byte[16]
    };

    var bytes = PkmWriter.ToBytes(file);

    Assert.That(bytes[4], Is.EqualTo((byte)'2'));
    Assert.That(bytes[5], Is.EqualTo((byte)'0'));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CorrectDimensions() {
    var file = new PkmFile {
      Width = 100,
      Height = 50,
      PaddedWidth = 104,
      PaddedHeight = 52,
      Format = PkmFormat.Etc1Rgb,
      Version = "10",
      CompressedData = new byte[8]
    };

    var bytes = PkmWriter.ToBytes(file);

    var paddedWidth = (ushort)((bytes[8] << 8) | bytes[9]);
    var paddedHeight = (ushort)((bytes[10] << 8) | bytes[11]);
    var width = (ushort)((bytes[12] << 8) | bytes[13]);
    var height = (ushort)((bytes[14] << 8) | bytes[15]);

    Assert.That(paddedWidth, Is.EqualTo(104));
    Assert.That(paddedHeight, Is.EqualTo(52));
    Assert.That(width, Is.EqualTo(100));
    Assert.That(height, Is.EqualTo(50));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DataPreserved() {
    var compressedData = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE };

    var file = new PkmFile {
      Width = 4,
      Height = 4,
      PaddedWidth = 4,
      PaddedHeight = 4,
      Format = PkmFormat.Etc1Rgb,
      Version = "10",
      CompressedData = compressedData
    };

    var bytes = PkmWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(16 + compressedData.Length));
    for (var i = 0; i < compressedData.Length; ++i)
      Assert.That(bytes[16 + i], Is.EqualTo(compressedData[i]), $"Compressed data byte {i} mismatch");
  }
}
