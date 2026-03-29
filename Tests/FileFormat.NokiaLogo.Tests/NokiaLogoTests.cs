using System;
using System.IO;
using FileFormat.NokiaLogo;
using FileFormat.Core;

namespace FileFormat.NokiaLogo.Tests;

[TestFixture]
public class NokiaLogoReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NokiaLogoReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => NokiaLogoReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NokiaLogoReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => NokiaLogoReader.FromBytes(new byte[1]));

  [Test]
  public void FromBytes_ExactSize_Succeeds() {
    var data = new byte[131];
    var result = NokiaLogoReader.FromBytes(data);
    Assert.That(result.Width, Is.EqualTo(72));
    Assert.That(result.Height, Is.EqualTo(14));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NokiaLogoReader.FromStream(null!));
}

[TestFixture]
public class NokiaLogoWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NokiaLogoWriter.ToBytes(null!));

  [Test]
  public void ToBytes_OutputSize_Is131() {
    var file = new NokiaLogoFile { PixelData = new byte[131] };
    var bytes = NokiaLogoWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(131));
  }

  [Test]
  public void ToBytes_DataPreserved() {
    var data = new byte[131];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i & 0xFF);
    var file = new NokiaLogoFile { PixelData = data };
    var bytes = NokiaLogoWriter.ToBytes(file);
    Assert.That(bytes, Is.EqualTo(data));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_AllBytes_Preserved() {
    var original = new byte[131];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i * 7 & 0xFF);
    var file = NokiaLogoReader.FromBytes(original);
    var written = NokiaLogoWriter.ToBytes(file);
    Assert.That(written, Is.EqualTo(original));
  }

  [Test]
  public void RoundTrip_ViaFile() {
    var original = new byte[131];
    var tmp = Path.GetTempFileName();
    try {
      File.WriteAllBytes(tmp, original);
      var file = NokiaLogoReader.FromFile(new FileInfo(tmp));
      var written = NokiaLogoWriter.ToBytes(file);
      Assert.That(written, Is.EqualTo(original));
    } finally {
      File.Delete(tmp);
    }
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void FixedWidth_Is72()
    => Assert.That(NokiaLogoFile.FixedWidth, Is.EqualTo(72));

  [Test]
  public void FixedHeight_Is14()
    => Assert.That(NokiaLogoFile.FixedHeight, Is.EqualTo(14));

  [Test]
  public void FileSize_Is131()
    => Assert.That(NokiaLogoFile.FileSize, Is.EqualTo(131));

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NokiaLogoFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NokiaLogoFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 72, Height = 14, Format = PixelFormat.Rgb24, PixelData = new byte[72 * 14 * 3] };
    Assert.Throws<ArgumentException>(() => NokiaLogoFile.FromRawImage(raw));
  }
}
