using System;
using System.IO;
using FileFormat.MsxFont;

namespace FileFormat.MsxFont.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_DataPreserved() {
    var rawData = new byte[2048];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 13 % 256);

    var original = new MsxFontFile { RawData = rawData };
    var bytes = MsxFontWriter.ToBytes(original);
    var restored = MsxFontReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new MsxFontFile { RawData = new byte[2048] };

    var bytes = MsxFontWriter.ToBytes(original);
    var restored = MsxFontReader.FromBytes(bytes);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var rawData = new byte[2048];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 7 % 256);

    var original = new MsxFontFile { RawData = rawData };
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".fnt");
    try {
      var bytes = MsxFontWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = MsxFontReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.RawData, Is.EqualTo(original.RawData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}
