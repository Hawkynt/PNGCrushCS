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
public sealed class PrintMasterWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PrintMasterWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ProducesCorrectHeader() {
    var file = new PrintMasterFile { Width = 88, Height = 52, PixelData = new byte[11 * 52] };
    var bytes = PrintMasterWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(11)); // widthBytes
    Assert.That(bytes[1], Is.EqualTo(0));
    Assert.That(bytes[2], Is.EqualTo(52)); // height
    Assert.That(bytes[3], Is.EqualTo(0));
    Assert.That(bytes.Length, Is.EqualTo(4 + 11 * 52));
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

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void PrintMasterFile_Defaults() {
    var file = new PrintMasterFile();
    Assert.That(file.Width, Is.EqualTo(0));
    Assert.That(file.Height, Is.EqualTo(0));
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void PrintMasterFile_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PrintMasterFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void PrintMasterFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PrintMasterFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void PrintMasterFile_FromRawImage_WrongFormat_Throws() {
    var raw = new RawImage {
      Width = 88, Height = 52,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[88 * 52 * 3],
    };
    Assert.Throws<ArgumentException>(() => PrintMasterFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void PrintMasterFile_ToRawImage_ReturnsIndexed1() {
    var file = new PrintMasterFile { Width = 16, Height = 4, PixelData = new byte[8] };
    var raw = PrintMasterFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed1));
    Assert.That(raw.PaletteCount, Is.EqualTo(2));
    Assert.That(raw.Width, Is.EqualTo(16));
    Assert.That(raw.Height, Is.EqualTo(4));
  }
}
