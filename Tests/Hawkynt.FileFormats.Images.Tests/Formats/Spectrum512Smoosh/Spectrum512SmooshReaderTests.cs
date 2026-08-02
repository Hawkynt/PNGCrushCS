using System;
using System.IO;
using FileFormat.Spectrum512Smoosh;

namespace FileFormat.Spectrum512Smoosh.Tests;

[TestFixture]
public sealed class Spectrum512SmooshReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Spectrum512SmooshReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Spectrum512SmooshReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sps"));
    Assert.Throws<FileNotFoundException>(() => Spectrum512SmooshReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Spectrum512SmooshReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[3];
    Assert.Throws<InvalidDataException>(() => Spectrum512SmooshReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_MinSize_Parses() {
    var data = new byte[4];
    data[0] = 0x01;

    var result = Spectrum512SmooshReader.FromBytes(data);

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
    var result = Spectrum512SmooshReader.FromStream(ms);

    Assert.That(result.RawData[0], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CopiesInputData() {
    var data = new byte[10];
    data[0] = 0x42;

    var result = Spectrum512SmooshReader.FromBytes(data);
    data[0] = 0x00;

    Assert.That(result.RawData[0], Is.EqualTo(0x42));
  }

  [Test]
  [Category("Unit")]
  /// <summary>
  /// A picture that has not been decoded is refused rather than returned black.
  /// </summary>
  /// <remarks>
  /// This used to assert the black picture, which is what the reader returned: the right size, every
  /// pixel nought, and nothing anywhere marking it as undecoded. That counted as a decode, so
  /// converting one of these would have written the black out as though it were the picture.
  /// <para/>
  /// The packing keeps a palette per scanline and codes the two apart from one another, and none of
  /// that is implemented here.
  /// </remarks>
  public void ToRawImage_RefusesWhatItCannotDecode() {
    var file = new Spectrum512SmooshFile {
      RawData = new byte[10]
    };

    Assert.Throws<NotSupportedException>(() => Spectrum512SmooshFile.ToRawImage(file));
  }
  [Test]
  [Category("Integration")]
  public void RoundTrip_RawDataPreserved() {
    var data = new byte[100];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i * 3 % 256);

    var original = new Spectrum512SmooshFile {
      RawData = data
    };

    var bytes = Spectrum512SmooshWriter.ToBytes(original);
    var restored = Spectrum512SmooshReader.FromBytes(bytes);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var data = new byte[50];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i * 7 % 256);

    var original = new Spectrum512SmooshFile {
      RawData = data
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sps");
    try {
      var bytes = Spectrum512SmooshWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = Spectrum512SmooshReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.RawData, Is.EqualTo(original.RawData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Unit")]
  public void DataType_DefaultRawData_IsEmpty() {
    var file = new Spectrum512SmooshFile();
    Assert.That(file.RawData, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void DataType_FixedDimensions() {
    var file = new Spectrum512SmooshFile();
    Assert.That(file.Width, Is.EqualTo(320));
    Assert.That(file.Height, Is.EqualTo(199));
  }

  [Test]
  [Category("Unit")]
  public void DataType_MinFileSize_Is4() {
    Assert.That(Spectrum512SmooshFile.MinFileSize, Is.EqualTo(4));
  }
}
