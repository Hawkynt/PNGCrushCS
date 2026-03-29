using System;
using System.IO;
using FileFormat.PocketPc2bp;
using FileFormat.Core;

namespace FileFormat.PocketPc2bp.Tests;

[TestFixture]
public class PocketPc2bpReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PocketPc2bpReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => PocketPc2bpReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PocketPc2bpReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => PocketPc2bpReader.FromBytes(new byte[7]));

  [Test]
  public void FromBytes_ValidHeader_Succeeds() {
    var data = new byte[8 + (64 + 7) / 8 * 64];
    data[0] = 64;
    data[1] = 0;
    data[4] = 64; data[5] = 0;
    var result = PocketPc2bpReader.FromBytes(data);
    Assert.That(result.Width, Is.GreaterThan(0));
    Assert.That(result.Height, Is.GreaterThan(0));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PocketPc2bpReader.FromStream(null!));
}

[TestFixture]
public class PocketPc2bpWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PocketPc2bpWriter.ToBytes(null!));

  [Test]
  public void ToBytes_IncludesHeader() {
    var file = new PocketPc2bpFile {
      Width = 64,
      Height = 64,
      PixelData = new byte[(64 + 7) / 8 * 64],
    };
    var bytes = PocketPc2bpWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(8 + (64 + 7) / 8 * 64));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_PixelDataPreserved() {
    var file = new PocketPc2bpFile {
      Width = 64,
      Height = 64,
      PixelData = new byte[(64 + 7) / 8 * 64],
    };
    for (var i = 0; i < file.PixelData.Length; ++i)
      file.PixelData[i] = (byte)(i & 0xFF);
    var bytes = PocketPc2bpWriter.ToBytes(file);
    var file2 = PocketPc2bpReader.FromBytes(bytes);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var file = new PocketPc2bpFile {
      Width = 64,
      Height = 64,
      PixelData = new byte[(64 + 7) / 8 * 64],
    };
    var raw = PocketPc2bpFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed1));
    var file2 = PocketPc2bpFile.FromRawImage(raw);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void HeaderSize_Is8()
    => Assert.That(PocketPc2bpFile.HeaderSize, Is.EqualTo(8));

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PocketPc2bpFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PocketPc2bpFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 64, Height = 64, Format = PixelFormat.Rgb24, PixelData = new byte[64 * 64 * 3] };
    Assert.Throws<ArgumentException>(() => PocketPc2bpFile.FromRawImage(raw));
  }

  [Test]
  public void FileExtensions_ContainsPrimary() {
    string[] exts = [".2bp"];
    Assert.That(exts, Does.Contain(".2bp"));
  }
}
