using System;
using System.IO;
using FileFormat.Gigacad;

namespace FileFormat.Gigacad.Tests;

[TestFixture]
public sealed class GigacadReaderTests {

  private const int _EXPECTED_SIZE = 32000;

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GigacadReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GigacadReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gcd"));
    Assert.Throws<FileNotFoundException>(() => GigacadReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GigacadReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => GigacadReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooLarge_ThrowsInvalidDataException() {
    var tooLarge = new byte[_EXPECTED_SIZE + 1];
    Assert.Throws<InvalidDataException>(() => GigacadReader.FromBytes(tooLarge));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ExactSize_Parses() {
    var data = new byte[_EXPECTED_SIZE];
    data[0] = 0xFF;
    data[1] = 0x80;

    var result = GigacadReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(640));
    Assert.That(result.Height, Is.EqualTo(400));
    Assert.That(result.PixelData.Length, Is.EqualTo(_EXPECTED_SIZE));
    Assert.That(result.PixelData[0], Is.EqualTo(0xFF));
    Assert.That(result.PixelData[1], Is.EqualTo(0x80));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var data = new byte[_EXPECTED_SIZE];
    data[0] = 0xAB;

    using var ms = new MemoryStream(data);
    var result = GigacadReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(640));
    Assert.That(result.Height, Is.EqualTo(400));
    Assert.That(result.PixelData[0], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CopiesInputData() {
    var data = new byte[_EXPECTED_SIZE];
    data[0] = 0x42;

    var result = GigacadReader.FromBytes(data);
    data[0] = 0x00;

    Assert.That(result.PixelData[0], Is.EqualTo(0x42));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_AllZeros_ProducesWhiteImage() {
    var file = new GigacadFile {
      PixelData = new byte[_EXPECTED_SIZE]
    };

    var raw = GigacadFile.ToRawImage(file);

    Assert.That(raw.Width, Is.EqualTo(640));
    Assert.That(raw.Height, Is.EqualTo(400));
    Assert.That(raw.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Rgb24));
    Assert.That(raw.PixelData[0], Is.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_AllOnes_ProducesBlackImage() {
    var data = new byte[_EXPECTED_SIZE];
    for (var i = 0; i < data.Length; ++i)
      data[i] = 0xFF;

    var file = new GigacadFile {
      PixelData = data
    };

    var raw = GigacadFile.ToRawImage(file);

    Assert.That(raw.PixelData[0], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ThrowsNotSupportedException() {
    Assert.Throws<NotSupportedException>(() => GigacadFile.FromRawImage(null!));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PixelDataPreserved() {
    var data = new byte[_EXPECTED_SIZE];
    data[0] = 0xAA;
    data[79] = 0x55;
    data[_EXPECTED_SIZE - 1] = 0xDE;

    var original = new GigacadFile {
      PixelData = data
    };

    var bytes = GigacadWriter.ToBytes(original);
    var restored = GigacadReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var data = new byte[_EXPECTED_SIZE];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i * 7 % 256);

    var original = new GigacadFile {
      PixelData = data
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gcd");
    try {
      var bytes = GigacadWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = GigacadReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Unit")]
  public void WriterToBytes_Produces32000Bytes() {
    var file = new GigacadFile {
      PixelData = new byte[_EXPECTED_SIZE]
    };

    var bytes = GigacadWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(_EXPECTED_SIZE));
  }

  [Test]
  [Category("Unit")]
  public void WriterToBytes_ShortData_PadsWithZeros() {
    var file = new GigacadFile {
      PixelData = new byte[10]
    };

    var bytes = GigacadWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(_EXPECTED_SIZE));
    Assert.That(bytes[10], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void DataType_DefaultPixelData_IsEmpty() {
    var file = new GigacadFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void DataType_FixedDimensions() {
    var file = new GigacadFile();
    Assert.That(file.Width, Is.EqualTo(640));
    Assert.That(file.Height, Is.EqualTo(400));
  }

  [Test]
  [Category("Unit")]
  public void DataType_ExpectedFileSize_Is32000() {
    Assert.That(GigacadFile.ExpectedFileSize, Is.EqualTo(32000));
  }
}
