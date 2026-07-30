using System;
using System.IO;
using FileFormat.CpcAdvanced;

namespace FileFormat.CpcAdvanced.Tests;

[TestFixture]
public sealed class RoundTripTests {

  private const int _LINEAR_SIZE = CpcAdvancedFile.PixelHeight * CpcAdvancedFile.BytesPerRow;

  [Test]
  [Category("Integration")]
  public void RoundTrip_PatternData_Preserved() {
    var linearData = new byte[_LINEAR_SIZE];
    for (var i = 0; i < linearData.Length; ++i)
      linearData[i] = (byte)(i * 7 % 256);

    var original = new CpcAdvancedFile { PixelData = linearData };

    var bytes = CpcAdvancedWriter.ToBytes(original);
    var restored = CpcAdvancedReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros_Preserved() {
    var original = new CpcAdvancedFile { PixelData = new byte[_LINEAR_SIZE] };

    var bytes = CpcAdvancedWriter.ToBytes(original);
    var restored = CpcAdvancedReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllOnes_Preserved() {
    var linearData = new byte[_LINEAR_SIZE];
    Array.Fill(linearData, (byte)0xFF);

    var original = new CpcAdvancedFile { PixelData = linearData };

    var bytes = CpcAdvancedWriter.ToBytes(original);
    var restored = CpcAdvancedReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile_Preserved() {
    var linearData = new byte[_LINEAR_SIZE];
    for (var i = 0; i < linearData.Length; ++i)
      linearData[i] = (byte)(i * 13 % 256);

    var original = new CpcAdvancedFile { PixelData = linearData };
    var tempFile = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cpa"));

    try {
      File.WriteAllBytes(tempFile.FullName, CpcAdvancedWriter.ToBytes(original));
      var restored = CpcAdvancedReader.FromFile(tempFile);

      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (tempFile.Exists)
        tempFile.Delete();
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WriterOutput_IsAlways16384Bytes() {
    var file = new CpcAdvancedFile { PixelData = new byte[_LINEAR_SIZE] };

    var bytes = CpcAdvancedWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(CpcAdvancedFile.ExpectedFileSize));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ToRawImage_ReturnsIndexed8() {
    var linearData = new byte[_LINEAR_SIZE];
    linearData[0] = 0xFF;
    var file = new CpcAdvancedFile { PixelData = linearData };

    var raw = CpcAdvancedFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Indexed8));
    Assert.That(raw.Width, Is.EqualTo(CpcAdvancedFile.PixelWidth));
    Assert.That(raw.Height, Is.EqualTo(CpcAdvancedFile.PixelHeight));
  }
}
