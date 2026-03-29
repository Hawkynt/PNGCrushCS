using System;
using System.IO;
using FileFormat.CpcOverscan;

namespace FileFormat.CpcOverscan.Tests;

[TestFixture]
public sealed class RoundTripTests {

  private const int _LINEAR_SIZE = CpcOverscanFile.PixelHeight * CpcOverscanFile.BytesPerRow;

  [Test]
  [Category("Integration")]
  public void RoundTrip_PatternData_Preserved() {
    var linearData = new byte[_LINEAR_SIZE];
    for (var i = 0; i < linearData.Length; ++i)
      linearData[i] = (byte)(i * 7 % 256);

    var original = new CpcOverscanFile { PixelData = linearData };

    var bytes = CpcOverscanWriter.ToBytes(original);
    var restored = CpcOverscanReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros_Preserved() {
    var original = new CpcOverscanFile { PixelData = new byte[_LINEAR_SIZE] };

    var bytes = CpcOverscanWriter.ToBytes(original);
    var restored = CpcOverscanReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllOnes_Preserved() {
    var linearData = new byte[_LINEAR_SIZE];
    Array.Fill(linearData, (byte)0xFF);

    var original = new CpcOverscanFile { PixelData = linearData };

    var bytes = CpcOverscanWriter.ToBytes(original);
    var restored = CpcOverscanReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile_Preserved() {
    var linearData = new byte[_LINEAR_SIZE];
    for (var i = 0; i < linearData.Length; ++i)
      linearData[i] = (byte)(i * 13 % 256);

    var original = new CpcOverscanFile { PixelData = linearData };
    var tempFile = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cpo"));

    try {
      File.WriteAllBytes(tempFile.FullName, CpcOverscanWriter.ToBytes(original));
      var restored = CpcOverscanReader.FromFile(tempFile);

      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (tempFile.Exists)
        tempFile.Delete();
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WriterOutput_IsAlways32768Bytes() {
    var file = new CpcOverscanFile { PixelData = new byte[_LINEAR_SIZE] };

    var bytes = CpcOverscanWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(CpcOverscanFile.ExpectedFileSize));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Bank1Data_Preserved() {
    var linearData = new byte[_LINEAR_SIZE];
    // Put data in bank 1 region (lines 136-271)
    for (var i = 136 * CpcOverscanFile.BytesPerRow; i < linearData.Length; ++i)
      linearData[i] = (byte)(i % 256);

    var original = new CpcOverscanFile { PixelData = linearData };

    var bytes = CpcOverscanWriter.ToBytes(original);
    var restored = CpcOverscanReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ToRawImage_ReturnsIndexed8() {
    var file = new CpcOverscanFile { PixelData = new byte[_LINEAR_SIZE] };

    var raw = CpcOverscanFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Indexed8));
    Assert.That(raw.Width, Is.EqualTo(CpcOverscanFile.PixelWidth));
    Assert.That(raw.Height, Is.EqualTo(CpcOverscanFile.PixelHeight));
  }
}
