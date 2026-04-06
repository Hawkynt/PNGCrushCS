using System;
using System.IO;
using FileFormat.Core;
using FileFormat.PrintMaster;

namespace FileFormat.PrintMaster.Tests;

[TestFixture]
public sealed class PrintMasterReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PrintMasterReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pm"));
    Assert.Throws<FileNotFoundException>(() => PrintMasterReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PrintMasterReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => PrintMasterReader.FromBytes(new byte[2]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_Parses() {
    // widthBytes=11 (88 pixels), height=52
    var data = new byte[4 + 11 * 52];
    data[0] = 11; data[1] = 0; // widthBytes = 11
    data[2] = 52; data[3] = 0; // height = 52
    data[4] = 0xFF; // first pixel byte

    var result = PrintMasterReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(88));
    Assert.That(result.Height, Is.EqualTo(52));
    Assert.That(result.PixelData[0], Is.EqualTo(0xFF));
  }
}

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_PreservesData() {
    var pixelData = new byte[11 * 52];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new PrintMasterFile { Width = 88, Height = 52, PixelData = pixelData };

    var bytes = PrintMasterWriter.ToBytes(original);
    var restored = PrintMasterReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(88));
    Assert.That(restored.Height, Is.EqualTo(52));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    var pixelData = new byte[11 * 52];
    pixelData[0] = 0xAA;
    var original = new PrintMasterFile { Width = 88, Height = 52, PixelData = pixelData };

    var raw = PrintMasterFile.ToRawImage(original);
    var restored = PrintMasterFile.FromRawImage(raw);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[11 * 52];
    pixelData[0] = 0xDE;
    var original = new PrintMasterFile { Width = 88, Height = 52, PixelData = pixelData };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pm");
    try {
      File.WriteAllBytes(tempPath, PrintMasterWriter.ToBytes(original));
      var restored = PrintMasterReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_VariableDimensions() {
    var pixelData = new byte[4 * 10]; // 32x10
    pixelData[0] = 0xFF;
    var original = new PrintMasterFile { Width = 32, Height = 10, PixelData = pixelData };

    var bytes = PrintMasterWriter.ToBytes(original);
    var restored = PrintMasterReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(32));
    Assert.That(restored.Height, Is.EqualTo(10));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}

