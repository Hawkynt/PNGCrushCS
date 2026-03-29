using System;
using System.IO;
using FileFormat.Vic20;
using FileFormat.Core;

namespace FileFormat.Vic20.Tests;

[TestFixture]
public class Vic20ReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Vic20Reader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => Vic20Reader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Vic20Reader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => Vic20Reader.FromBytes(new byte[1]));

  [Test]
  public void FromBytes_ExactSize_Succeeds() {
    var data = new byte[4096];
    var result = Vic20Reader.FromBytes(data);
    Assert.That(result.Width, Is.EqualTo(176));
    Assert.That(result.Height, Is.EqualTo(184));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Vic20Reader.FromStream(null!));
}

[TestFixture]
public class Vic20WriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Vic20Writer.ToBytes(null!));

  [Test]
  public void ToBytes_OutputSize_Is4096() {
    var file = new Vic20File { PixelData = new byte[4096] };
    var bytes = Vic20Writer.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(4096));
  }

  [Test]
  public void ToBytes_DataPreserved() {
    var data = new byte[4096];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i & 0xFF);
    var file = new Vic20File { PixelData = data };
    var bytes = Vic20Writer.ToBytes(file);
    Assert.That(bytes, Is.EqualTo(data));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_AllBytes_Preserved() {
    var original = new byte[4096];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i * 7 & 0xFF);
    var file = Vic20Reader.FromBytes(original);
    var written = Vic20Writer.ToBytes(file);
    Assert.That(written, Is.EqualTo(original));
  }

  [Test]
  public void RoundTrip_ViaFile() {
    var original = new byte[4096];
    var tmp = Path.GetTempFileName();
    try {
      File.WriteAllBytes(tmp, original);
      var file = Vic20Reader.FromFile(new FileInfo(tmp));
      var written = Vic20Writer.ToBytes(file);
      Assert.That(written, Is.EqualTo(original));
    } finally {
      File.Delete(tmp);
    }
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void FixedWidth_Is176()
    => Assert.That(Vic20File.FixedWidth, Is.EqualTo(176));

  [Test]
  public void FixedHeight_Is184()
    => Assert.That(Vic20File.FixedHeight, Is.EqualTo(184));

  [Test]
  public void FileSize_Is4096()
    => Assert.That(Vic20File.FileSize, Is.EqualTo(4096));

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Vic20File.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Vic20File.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 176, Height = 184, Format = PixelFormat.Rgb24, PixelData = new byte[176 * 184 * 3] };
    Assert.Throws<ArgumentException>(() => Vic20File.FromRawImage(raw));
  }
}
