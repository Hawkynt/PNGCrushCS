using System;
using System.IO;
using FileFormat.NokiaNlm;
using FileFormat.Core;

namespace FileFormat.NokiaNlm.Tests;

[TestFixture]
public class NokiaNlmReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NokiaNlmReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => NokiaNlmReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NokiaNlmReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => NokiaNlmReader.FromBytes(new byte[1]));

  [Test]
  public void FromBytes_ExactSize_Succeeds() {
    var data = new byte[508];
    var result = NokiaNlmReader.FromBytes(data);
    Assert.That(result.Width, Is.EqualTo(84));
    Assert.That(result.Height, Is.EqualTo(48));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NokiaNlmReader.FromStream(null!));
}

[TestFixture]
public class NokiaNlmWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NokiaNlmWriter.ToBytes(null!));

  [Test]
  public void ToBytes_OutputSize_Is508() {
    var file = new NokiaNlmFile { PixelData = new byte[508] };
    var bytes = NokiaNlmWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(508));
  }

  [Test]
  public void ToBytes_DataPreserved() {
    var data = new byte[508];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i & 0xFF);
    var file = new NokiaNlmFile { PixelData = data };
    var bytes = NokiaNlmWriter.ToBytes(file);
    Assert.That(bytes, Is.EqualTo(data));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_AllBytes_Preserved() {
    var original = new byte[508];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i * 7 & 0xFF);
    var file = NokiaNlmReader.FromBytes(original);
    var written = NokiaNlmWriter.ToBytes(file);
    Assert.That(written, Is.EqualTo(original));
  }

  [Test]
  public void RoundTrip_ViaFile() {
    var original = new byte[508];
    var tmp = Path.GetTempFileName();
    try {
      File.WriteAllBytes(tmp, original);
      var file = NokiaNlmReader.FromFile(new FileInfo(tmp));
      var written = NokiaNlmWriter.ToBytes(file);
      Assert.That(written, Is.EqualTo(original));
    } finally {
      File.Delete(tmp);
    }
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void FixedWidth_Is84()
    => Assert.That(NokiaNlmFile.FixedWidth, Is.EqualTo(84));

  [Test]
  public void FixedHeight_Is48()
    => Assert.That(NokiaNlmFile.FixedHeight, Is.EqualTo(48));

  [Test]
  public void FileSize_Is508()
    => Assert.That(NokiaNlmFile.FileSize, Is.EqualTo(508));

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NokiaNlmFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NokiaNlmFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 84, Height = 48, Format = PixelFormat.Rgb24, PixelData = new byte[84 * 48 * 3] };
    Assert.Throws<ArgumentException>(() => NokiaNlmFile.FromRawImage(raw));
  }
}
