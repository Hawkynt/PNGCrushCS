using System;
using System.IO;
using FileFormat.AccessFax;

namespace FileFormat.AccessFax.Tests;

[TestFixture]
public sealed class AccessFaxReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => AccessFaxReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => AccessFaxReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".g4"));
    Assert.Throws<FileNotFoundException>(() => AccessFaxReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => AccessFaxReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => AccessFaxReader.FromBytes(new byte[4]));

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildValidAccessFax(16, 4);
    var result = AccessFaxReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(16));
      Assert.That(result.Height, Is.EqualTo(4));
      Assert.That(result.PixelData.Length, Is.EqualTo(8));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_RoundTrip_PreservesData() {
    var original = _BuildValidAccessFax(24, 3);
    var file = AccessFaxReader.FromBytes(original);
    var written = AccessFaxWriter.ToBytes(file);
    var reread = AccessFaxReader.FromBytes(written);

    Assert.Multiple(() => {
      Assert.That(reread.Width, Is.EqualTo(file.Width));
      Assert.That(reread.Height, Is.EqualTo(file.Height));
      Assert.That(reread.PixelData, Is.EqualTo(file.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromStream_ValidParsesCorrectly() {
    var data = _BuildValidAccessFax(8, 2);
    using var ms = new MemoryStream(data);
    var result = AccessFaxReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(8));
  }

  private static byte[] _BuildValidAccessFax(int width, int height) {
    var bytesPerRow = (width + 7) / 8;
    var pixelDataSize = bytesPerRow * height;
    var data = new byte[AccessFaxFile.HeaderSize + pixelDataSize];

    data[0] = 0x00;
    data[1] = 0x00;
    BitConverter.TryWriteBytes(new Span<byte>(data, 2, 2), (ushort)width);
    BitConverter.TryWriteBytes(new Span<byte>(data, 4, 2), (ushort)height);
    // flags = 0

    for (var i = 0; i < pixelDataSize; ++i)
      data[AccessFaxFile.HeaderSize + i] = (byte)(i % 256);

    return data;
  }
}
