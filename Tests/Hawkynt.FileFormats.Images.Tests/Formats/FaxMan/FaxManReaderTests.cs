using System;
using System.IO;
using FileFormat.FaxMan;

namespace FileFormat.FaxMan.Tests;

[TestFixture]
public sealed class FaxManReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => FaxManReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => FaxManReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".fmf"));
    Assert.Throws<FileNotFoundException>(() => FaxManReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => FaxManReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => FaxManReader.FromBytes(new byte[4]));

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[FaxManFile.HeaderSize + 8];
    Assert.Throws<InvalidDataException>(() => FaxManReader.FromBytes(data));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildValidFmf(16, 4);
    var result = FaxManReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(16));
      Assert.That(result.Height, Is.EqualTo(4));
      Assert.That(result.PixelData.Length, Is.EqualTo(8));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_RoundTrip_PreservesData() {
    var original = _BuildValidFmf(24, 3);
    var file = FaxManReader.FromBytes(original);
    var written = FaxManWriter.ToBytes(file);
    var reread = FaxManReader.FromBytes(written);

    Assert.Multiple(() => {
      Assert.That(reread.Width, Is.EqualTo(file.Width));
      Assert.That(reread.Height, Is.EqualTo(file.Height));
      Assert.That(reread.PixelData, Is.EqualTo(file.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromStream_ValidParsesCorrectly() {
    var data = _BuildValidFmf(8, 2);
    using var ms = new MemoryStream(data);
    var result = FaxManReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(8));
  }

  private static byte[] _BuildValidFmf(int width, int height) {
    var bytesPerRow = (width + 7) / 8;
    var pixelDataSize = bytesPerRow * height;
    var data = new byte[FaxManFile.HeaderSize + pixelDataSize];

    data[0] = 0x46; // 'F'
    data[1] = 0x4D; // 'M'
    BitConverter.TryWriteBytes(new Span<byte>(data, 2, 2), (ushort)width);
    BitConverter.TryWriteBytes(new Span<byte>(data, 4, 2), (ushort)height);
    BitConverter.TryWriteBytes(new Span<byte>(data, 6, 2), (ushort)1); // version
    // flags = 0

    for (var i = 0; i < pixelDataSize; ++i)
      data[FaxManFile.HeaderSize + i] = (byte)(i % 256);

    return data;
  }
}
