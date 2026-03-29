using System;
using System.IO;
using FileFormat.IffSham;

namespace FileFormat.IffSham.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_DataPreserved() {
    var rawData = new byte[100];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 13 % 256);

    var original = new IffShamFile { Width = 320, Height = 200, RawData = rawData };
    var bytes = IffShamWriter.ToBytes(original);
    var restored = IffShamReader.FromBytes(bytes);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_DimensionsPreserved_WhenNoBmhd() {
    var rawData = new byte[20];

    var original = new IffShamFile { Width = 320, Height = 200, RawData = rawData };
    var bytes = IffShamWriter.ToBytes(original);
    var restored = IffShamReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(320));
    Assert.That(restored.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var rawData = new byte[30];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 7 % 256);

    var original = new IffShamFile { RawData = rawData };
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sham");
    try {
      var bytes = IffShamWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = IffShamReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.RawData, Is.EqualTo(original.RawData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}
