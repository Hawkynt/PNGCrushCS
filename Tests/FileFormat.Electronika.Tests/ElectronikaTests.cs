using System;
using System.IO;
using FileFormat.Electronika;
using FileFormat.Core;

namespace FileFormat.Electronika.Tests;

[TestFixture]
public class ElectronikaReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ElectronikaReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => ElectronikaReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ElectronikaReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => ElectronikaReader.FromBytes(new byte[1]));

  [Test]
  public void FromBytes_ExactSize_Succeeds() {
    var data = new byte[16384];
    var result = ElectronikaReader.FromBytes(data);
    Assert.That(result.Width, Is.EqualTo(512));
    Assert.That(result.Height, Is.EqualTo(256));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ElectronikaReader.FromStream(null!));
}

[TestFixture]
public class ElectronikaWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ElectronikaWriter.ToBytes(null!));

  [Test]
  public void ToBytes_OutputSize_Is16384() {
    var file = new ElectronikaFile { PixelData = new byte[16384] };
    var bytes = ElectronikaWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(16384));
  }

  [Test]
  public void ToBytes_DataPreserved() {
    var data = new byte[16384];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i & 0xFF);
    var file = new ElectronikaFile { PixelData = data };
    var bytes = ElectronikaWriter.ToBytes(file);
    Assert.That(bytes, Is.EqualTo(data));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_AllBytes_Preserved() {
    var original = new byte[16384];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i * 7 & 0xFF);
    var file = ElectronikaReader.FromBytes(original);
    var written = ElectronikaWriter.ToBytes(file);
    Assert.That(written, Is.EqualTo(original));
  }

  [Test]
  public void RoundTrip_ViaFile() {
    var original = new byte[16384];
    var tmp = Path.GetTempFileName();
    try {
      File.WriteAllBytes(tmp, original);
      var file = ElectronikaReader.FromFile(new FileInfo(tmp));
      var written = ElectronikaWriter.ToBytes(file);
      Assert.That(written, Is.EqualTo(original));
    } finally {
      File.Delete(tmp);
    }
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void FixedWidth_Is512()
    => Assert.That(ElectronikaFile.FixedWidth, Is.EqualTo(512));

  [Test]
  public void FixedHeight_Is256()
    => Assert.That(ElectronikaFile.FixedHeight, Is.EqualTo(256));

  [Test]
  public void FileSize_Is16384()
    => Assert.That(ElectronikaFile.FileSize, Is.EqualTo(16384));

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ElectronikaFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ElectronikaFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 512, Height = 256, Format = PixelFormat.Rgb24, PixelData = new byte[512 * 256 * 3] };
    Assert.Throws<ArgumentException>(() => ElectronikaFile.FromRawImage(raw));
  }
}
