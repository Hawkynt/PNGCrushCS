using System;
using System.IO;
using FileFormat.MsxSprite;

namespace FileFormat.MsxSprite.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_DataPreserved() {
    var rawData = new byte[2048];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 13 % 256);

    var original = new MsxSpriteFile { RawData = rawData };
    var bytes = MsxSpriteWriter.ToBytes(original);
    var restored = MsxSpriteReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new MsxSpriteFile { RawData = new byte[2048] };

    var bytes = MsxSpriteWriter.ToBytes(original);
    var restored = MsxSpriteReader.FromBytes(bytes);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var rawData = new byte[2048];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 7 % 256);

    var original = new MsxSpriteFile { RawData = rawData };
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".spt");
    try {
      var bytes = MsxSpriteWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = MsxSpriteReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.RawData, Is.EqualTo(original.RawData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}
