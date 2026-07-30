using System;
using System.IO;
using FileFormat.CpcPlus;

namespace FileFormat.CpcPlus.Tests;

[TestFixture]
public sealed class RoundTripTests {

  private const int _LINEAR_SIZE = CpcPlusFile.PixelHeight * CpcPlusFile.BytesPerRow;

  [Test]
  [Category("Integration")]
  public void RoundTrip_PatternData_PixelDataPreserved() {
    var linearData = new byte[_LINEAR_SIZE];
    for (var i = 0; i < linearData.Length; ++i)
      linearData[i] = (byte)(i * 7 % 256);

    var paletteData = new byte[CpcPlusFile.PaletteDataSize];
    paletteData[0] = 0x0F;
    paletteData[1] = 0x0A;
    paletteData[2] = 0x05;

    var original = new CpcPlusFile { PixelData = linearData, PaletteData = paletteData };

    var bytes = CpcPlusWriter.ToBytes(original);
    var restored = CpcPlusReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PaletteData_Preserved() {
    var paletteData = new byte[CpcPlusFile.PaletteDataSize];
    for (var i = 0; i < paletteData.Length; ++i)
      paletteData[i] = (byte)(i * 3);

    var original = new CpcPlusFile {
      PixelData = new byte[_LINEAR_SIZE],
      PaletteData = paletteData,
    };

    var bytes = CpcPlusWriter.ToBytes(original);
    var restored = CpcPlusReader.FromBytes(bytes);

    Assert.That(restored.PaletteData, Is.EqualTo(original.PaletteData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros_Preserved() {
    var original = new CpcPlusFile {
      PixelData = new byte[_LINEAR_SIZE],
      PaletteData = new byte[CpcPlusFile.PaletteDataSize],
    };

    var bytes = CpcPlusWriter.ToBytes(original);
    var restored = CpcPlusReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    Assert.That(restored.PaletteData, Is.EqualTo(original.PaletteData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllOnes_Preserved() {
    var linearData = new byte[_LINEAR_SIZE];
    Array.Fill(linearData, (byte)0xFF);
    var paletteData = new byte[CpcPlusFile.PaletteDataSize];
    Array.Fill(paletteData, (byte)0xFF);

    var original = new CpcPlusFile { PixelData = linearData, PaletteData = paletteData };

    var bytes = CpcPlusWriter.ToBytes(original);
    var restored = CpcPlusReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    Assert.That(restored.PaletteData, Is.EqualTo(original.PaletteData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile_Preserved() {
    var linearData = new byte[_LINEAR_SIZE];
    for (var i = 0; i < linearData.Length; ++i)
      linearData[i] = (byte)(i * 13 % 256);

    var paletteData = new byte[CpcPlusFile.PaletteDataSize];
    paletteData[0] = 0x0E;

    var original = new CpcPlusFile { PixelData = linearData, PaletteData = paletteData };
    var tempFile = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cpp"));

    try {
      File.WriteAllBytes(tempFile.FullName, CpcPlusWriter.ToBytes(original));
      var restored = CpcPlusReader.FromFile(tempFile);

      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
      Assert.That(restored.PaletteData, Is.EqualTo(original.PaletteData));
    } finally {
      if (tempFile.Exists)
        tempFile.Delete();
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WriterOutput_IsAlways16400Bytes() {
    var file = new CpcPlusFile {
      PixelData = new byte[_LINEAR_SIZE],
      PaletteData = new byte[CpcPlusFile.PaletteDataSize],
    };

    var bytes = CpcPlusWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(CpcPlusFile.ExpectedFileSize));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ToRawImage_ReturnsRgb24() {
    var paletteData = new byte[CpcPlusFile.PaletteDataSize];
    paletteData[0] = 0x0F; // R=15 -> 255
    paletteData[1] = 0x00; // G=0 -> 0
    paletteData[2] = 0x00; // B=0 -> 0

    var file = new CpcPlusFile {
      PixelData = new byte[_LINEAR_SIZE],
      PaletteData = paletteData,
    };

    var raw = CpcPlusFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Rgb24));
    Assert.That(raw.Width, Is.EqualTo(CpcPlusFile.PixelWidth));
    Assert.That(raw.Height, Is.EqualTo(CpcPlusFile.PixelHeight));
  }
}
