using System;
using System.IO;
using FileFormat.CpcSprite;

namespace FileFormat.CpcSprite.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_PatternData_Preserved() {
    var rawData = new byte[CpcSpriteFile.ExpectedFileSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 7 % 256);

    var original = new CpcSpriteFile { RawData = rawData };

    var bytes = CpcSpriteWriter.ToBytes(original);
    var restored = CpcSpriteReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros_Preserved() {
    var original = new CpcSpriteFile { RawData = new byte[CpcSpriteFile.ExpectedFileSize] };

    var bytes = CpcSpriteWriter.ToBytes(original);
    var restored = CpcSpriteReader.FromBytes(bytes);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllOnes_Preserved() {
    var rawData = new byte[CpcSpriteFile.ExpectedFileSize];
    Array.Fill(rawData, (byte)0xFF);

    var original = new CpcSpriteFile { RawData = rawData };

    var bytes = CpcSpriteWriter.ToBytes(original);
    var restored = CpcSpriteReader.FromBytes(bytes);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile_Preserved() {
    var rawData = new byte[CpcSpriteFile.ExpectedFileSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 13 % 256);

    var original = new CpcSpriteFile { RawData = rawData };
    var tempFile = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cps"));

    try {
      File.WriteAllBytes(tempFile.FullName, CpcSpriteWriter.ToBytes(original));
      var restored = CpcSpriteReader.FromFile(tempFile);

      Assert.That(restored.RawData, Is.EqualTo(original.RawData));
    } finally {
      if (tempFile.Exists)
        tempFile.Delete();
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WriterOutput_IsAlways64Bytes() {
    var file = new CpcSpriteFile { RawData = new byte[CpcSpriteFile.ExpectedFileSize] };

    var bytes = CpcSpriteWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(CpcSpriteFile.ExpectedFileSize));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ToRawImage_ReturnsIndexed8() {
    var rawData = new byte[CpcSpriteFile.ExpectedFileSize];
    rawData[0] = 0xFF;
    var file = new CpcSpriteFile { RawData = rawData };

    var raw = CpcSpriteFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Indexed8));
    Assert.That(raw.Width, Is.EqualTo(CpcSpriteFile.PixelWidth));
    Assert.That(raw.Height, Is.EqualTo(CpcSpriteFile.PixelHeight));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_EveryByte_Preserved() {
    var rawData = new byte[CpcSpriteFile.ExpectedFileSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)i;

    var original = new CpcSpriteFile { RawData = rawData };

    var bytes = CpcSpriteWriter.ToBytes(original);
    var restored = CpcSpriteReader.FromBytes(bytes);

    for (var i = 0; i < CpcSpriteFile.ExpectedFileSize; ++i)
      Assert.That(restored.RawData[i], Is.EqualTo((byte)i), $"Byte at index {i} mismatch");
  }
}
