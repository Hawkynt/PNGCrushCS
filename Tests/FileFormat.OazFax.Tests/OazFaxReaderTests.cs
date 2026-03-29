using System;
using System.IO;
using FileFormat.OazFax;

namespace FileFormat.OazFax.Tests;

[TestFixture]
public sealed class OazFaxReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => OazFaxReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => OazFaxReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".oaz"));
    Assert.Throws<FileNotFoundException>(() => OazFaxReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => OazFaxReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => OazFaxReader.FromBytes(new byte[4]));

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[OazFaxFile.HeaderSize + 8];
    Assert.Throws<InvalidDataException>(() => OazFaxReader.FromBytes(data));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildValidOaz(16, 4);
    var result = OazFaxReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(16));
      Assert.That(result.Height, Is.EqualTo(4));
      Assert.That(result.PixelData.Length, Is.EqualTo(8));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_RoundTrip_PreservesData() {
    var original = _BuildValidOaz(24, 3);
    var file = OazFaxReader.FromBytes(original);
    var written = OazFaxWriter.ToBytes(file);
    var reread = OazFaxReader.FromBytes(written);

    Assert.Multiple(() => {
      Assert.That(reread.Width, Is.EqualTo(file.Width));
      Assert.That(reread.Height, Is.EqualTo(file.Height));
      Assert.That(reread.PixelData, Is.EqualTo(file.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromStream_ValidParsesCorrectly() {
    var data = _BuildValidOaz(8, 2);
    using var ms = new MemoryStream(data);
    var result = OazFaxReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(8));
  }

  private static byte[] _BuildValidOaz(int width, int height) {
    var bytesPerRow = (width + 7) / 8;
    var pixelDataSize = bytesPerRow * height;
    var data = new byte[OazFaxFile.HeaderSize + pixelDataSize];

    data[0] = 0x4F; // 'O'
    data[1] = 0x41; // 'A'
    data[2] = 0x5A; // 'Z'
    data[3] = 0x46; // 'F'
    BitConverter.TryWriteBytes(new Span<byte>(data, 4, 2), (ushort)1); // version
    BitConverter.TryWriteBytes(new Span<byte>(data, 6, 2), (ushort)width);
    BitConverter.TryWriteBytes(new Span<byte>(data, 8, 2), (ushort)height);
    // encoding = 0

    for (var i = 0; i < pixelDataSize; ++i)
      data[OazFaxFile.HeaderSize + i] = (byte)(i % 256);

    return data;
  }
}
