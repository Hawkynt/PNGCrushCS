using System;
using System.IO;
using FileFormat.JupiterAce;
using FileFormat.Core;

namespace FileFormat.JupiterAce.Tests;

[TestFixture]
public class JupiterAceReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => JupiterAceReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => JupiterAceReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => JupiterAceReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => JupiterAceReader.FromBytes(new byte[1]));

  [Test]
  public void FromBytes_ExactSize_Succeeds() {
    var data = new byte[1536];
    var result = JupiterAceReader.FromBytes(data);
    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(192));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => JupiterAceReader.FromStream(null!));
}

[TestFixture]
public class JupiterAceWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => JupiterAceWriter.ToBytes(null!));

  [Test]
  public void ToBytes_OutputSize_Is1536() {
    var file = new JupiterAceFile { PixelData = new byte[1536] };
    var bytes = JupiterAceWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(1536));
  }

  [Test]
  public void ToBytes_DataPreserved() {
    var data = new byte[1536];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i & 0xFF);
    var file = new JupiterAceFile { PixelData = data };
    var bytes = JupiterAceWriter.ToBytes(file);
    Assert.That(bytes, Is.EqualTo(data));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_AllBytes_Preserved() {
    var original = new byte[1536];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i * 7 & 0xFF);
    var file = JupiterAceReader.FromBytes(original);
    var written = JupiterAceWriter.ToBytes(file);
    Assert.That(written, Is.EqualTo(original));
  }

  [Test]
  public void RoundTrip_ViaFile() {
    var original = new byte[1536];
    var tmp = Path.GetTempFileName();
    try {
      File.WriteAllBytes(tmp, original);
      var file = JupiterAceReader.FromFile(new FileInfo(tmp));
      var written = JupiterAceWriter.ToBytes(file);
      Assert.That(written, Is.EqualTo(original));
    } finally {
      File.Delete(tmp);
    }
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void FixedWidth_Is256()
    => Assert.That(JupiterAceFile.FixedWidth, Is.EqualTo(256));

  [Test]
  public void FixedHeight_Is192()
    => Assert.That(JupiterAceFile.FixedHeight, Is.EqualTo(192));

  [Test]
  public void FileSize_Is1536()
    => Assert.That(JupiterAceFile.FileSize, Is.EqualTo(1536));

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => JupiterAceFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => JupiterAceFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 256, Height = 192, Format = PixelFormat.Rgb24, PixelData = new byte[256 * 192 * 3] };
    Assert.Throws<ArgumentException>(() => JupiterAceFile.FromRawImage(raw));
  }
}
