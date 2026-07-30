using System;
using System.IO;
using FileFormat.GemImg;

namespace FileFormat.GemImg.Tests;

[TestFixture]
public sealed class GemImgReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GemImgReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GemImgReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".img"));
    Assert.Throws<FileNotFoundException>(() => GemImgReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GemImgReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[10];
    Assert.Throws<InvalidDataException>(() => GemImgReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidMonochrome_ParsesCorrectly() {
    var data = _BuildMinimalMonochromeImg(16, 4);
    var result = GemImgReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(16));
    Assert.That(result.Height, Is.EqualTo(4));
    Assert.That(result.NumPlanes, Is.EqualTo(1));
    Assert.That(result.Version, Is.EqualTo(1));
  }

  private static byte[] _BuildMinimalMonochromeImg(int width, int height) {
    var bytesPerRow = (width + 7) / 8;
    var headerLengthInWords = GemImgHeader.StructSize / 2;

    using var ms = new MemoryStream();

    var headerBytes = new byte[GemImgHeader.StructSize];
    var header = new GemImgHeader(1, (short)headerLengthInWords, 1, 1, 85, 85, (short)width, (short)height);
    header.WriteTo(headerBytes);
    ms.Write(headerBytes, 0, headerBytes.Length);

    for (var row = 0; row < height; ++row) {
      ms.WriteByte(0x80); // bit string
      ms.WriteByte((byte)bytesPerRow);
      for (var b = 0; b < bytesPerRow; ++b)
        ms.WriteByte((byte)(row * 10 + b));
    }

    return ms.ToArray();
  }
}
