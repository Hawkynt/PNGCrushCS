using System;
using System.IO;
using FileFormat.Spectrum512Comp;

namespace FileFormat.Spectrum512Comp.Tests;

[TestFixture]
public sealed class Spectrum512CompReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Spectrum512CompReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Spectrum512CompReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".spc"));
    Assert.Throws<FileNotFoundException>(() => Spectrum512CompReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Spectrum512CompReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[3];
    Assert.Throws<InvalidDataException>(() => Spectrum512CompReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_MinSize_Parses() {
    var data = new byte[4];
    data[0] = 0x01;

    var result = Spectrum512CompReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(199));
    Assert.That(result.RawData.Length, Is.EqualTo(4));
    Assert.That(result.RawData[0], Is.EqualTo(0x01));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var data = new byte[10];
    data[0] = 0xAB;

    using var ms = new MemoryStream(data);
    var result = Spectrum512CompReader.FromStream(ms);

    Assert.That(result.RawData[0], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CopiesInputData() {
    var data = new byte[10];
    data[0] = 0x42;

    var result = Spectrum512CompReader.FromBytes(data);
    data[0] = 0x00;

    Assert.That(result.RawData[0], Is.EqualTo(0x42));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ThrowsNotSupportedException() {
    Assert.Throws<NotSupportedException>(() => Spectrum512CompFile.FromRawImage(null!));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_RawDataPreserved() {
    var data = new byte[100];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i * 3 % 256);

    var original = new Spectrum512CompFile {
      RawData = data
    };

    var bytes = Spectrum512CompWriter.ToBytes(original);
    var restored = Spectrum512CompReader.FromBytes(bytes);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var data = new byte[50];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i * 7 % 256);

    var original = new Spectrum512CompFile {
      RawData = data
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".spc");
    try {
      var bytes = Spectrum512CompWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = Spectrum512CompReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.RawData, Is.EqualTo(original.RawData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Unit")]
  public void WriterToBytes_PreservesLength() {
    var file = new Spectrum512CompFile {
      RawData = new byte[200]
    };

    var bytes = Spectrum512CompWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void DataType_DefaultRawData_IsEmpty() {
    var file = new Spectrum512CompFile();
    Assert.That(file.RawData, Is.Not.Null);
    Assert.That(file.RawData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void DataType_FixedDimensions() {
    var file = new Spectrum512CompFile();
    Assert.That(file.Width, Is.EqualTo(320));
    Assert.That(file.Height, Is.EqualTo(199));
  }

  [Test]
  [Category("Unit")]
  public void DataType_DecompressedSize_Is51104() {
    Assert.That(Spectrum512CompFile.DecompressedSize, Is.EqualTo(51104));
  }

  [Test]
  [Category("Unit")]
  public void DataType_MinFileSize_Is4() {
    Assert.That(Spectrum512CompFile.MinFileSize, Is.EqualTo(4));
  }
}
