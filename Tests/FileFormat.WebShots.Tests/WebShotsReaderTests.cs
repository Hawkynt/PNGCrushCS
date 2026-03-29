using System;
using System.IO;
using FileFormat.WebShots;

namespace FileFormat.WebShots.Tests;

[TestFixture]
public sealed class WebShotsReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => WebShotsReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => WebShotsReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wb1"));
    Assert.Throws<FileNotFoundException>(() => WebShotsReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => WebShotsReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => WebShotsReader.FromBytes(new byte[4]));

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[WebShotsFile.HeaderSize + 8];
    Assert.Throws<InvalidDataException>(() => WebShotsReader.FromBytes(data));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildValid(16, 4, 1, 24);
    var result = WebShotsReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(16));
      Assert.That(result.Height, Is.EqualTo(4));
      Assert.That(result.Version, Is.EqualTo(1));
      Assert.That(result.Bpp, Is.EqualTo(24));
      Assert.That(result.PixelData.Length, Is.EqualTo(16 * 4 * 3));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_RoundTrip_PreservesData() {
    var original = _BuildValid(8, 2, 2, 24);
    var file = WebShotsReader.FromBytes(original);
    var written = WebShotsWriter.ToBytes(file);
    var reread = WebShotsReader.FromBytes(written);

    Assert.Multiple(() => {
      Assert.That(reread.Width, Is.EqualTo(file.Width));
      Assert.That(reread.Height, Is.EqualTo(file.Height));
      Assert.That(reread.Version, Is.EqualTo(file.Version));
      Assert.That(reread.Bpp, Is.EqualTo(file.Bpp));
      Assert.That(reread.PixelData, Is.EqualTo(file.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromStream_ValidParsesCorrectly() {
    var data = _BuildValid(8, 2, 1, 24);
    using var ms = new MemoryStream(data);
    var result = WebShotsReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(8));
  }

  private static byte[] _BuildValid(int width, int height, ushort version, ushort bpp) {
    var pixelDataSize = width * height * 3;
    var data = new byte[WebShotsFile.HeaderSize + pixelDataSize];

    data[0] = 0x57; // 'W'
    data[1] = 0x42; // 'B'
    data[2] = 0x53; // 'S'
    data[3] = 0x54; // 'T'
    BitConverter.TryWriteBytes(new Span<byte>(data, 4, 2), version);
    BitConverter.TryWriteBytes(new Span<byte>(data, 6, 2), (ushort)width);
    BitConverter.TryWriteBytes(new Span<byte>(data, 8, 2), (ushort)height);
    BitConverter.TryWriteBytes(new Span<byte>(data, 10, 2), bpp);
    // bytes 12-15 reserved

    for (var i = 0; i < pixelDataSize; ++i)
      data[WebShotsFile.HeaderSize + i] = (byte)(i % 256);

    return data;
  }
}
