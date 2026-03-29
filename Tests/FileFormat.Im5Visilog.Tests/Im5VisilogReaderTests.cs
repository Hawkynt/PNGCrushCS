using System;
using System.IO;
using FileFormat.Im5Visilog;

namespace FileFormat.Im5Visilog.Tests;

[TestFixture]
public sealed class Im5VisilogReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Im5VisilogReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Im5VisilogReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".im5"));
    Assert.Throws<FileNotFoundException>(() => Im5VisilogReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Im5VisilogReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => Im5VisilogReader.FromBytes(new byte[4]));

  [Test]
  [Category("Integration")]
  public void FromBytes_Valid8BitParsesCorrectly() {
    var data = _BuildValidIm5(4, 2, 8);
    var result = Im5VisilogReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(4));
      Assert.That(result.Height, Is.EqualTo(2));
      Assert.That(result.Depth, Is.EqualTo(8));
      Assert.That(result.PixelData.Length, Is.EqualTo(8));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_Valid16BitParsesCorrectly() {
    var data = _BuildValidIm5(4, 2, 16);
    var result = Im5VisilogReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(4));
      Assert.That(result.Height, Is.EqualTo(2));
      Assert.That(result.Depth, Is.EqualTo(16));
      Assert.That(result.PixelData.Length, Is.EqualTo(16));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_RoundTrip_PreservesData() {
    var original = _BuildValidIm5(3, 3, 8);
    var file = Im5VisilogReader.FromBytes(original);
    var written = Im5VisilogWriter.ToBytes(file);
    var reread = Im5VisilogReader.FromBytes(written);

    Assert.Multiple(() => {
      Assert.That(reread.Width, Is.EqualTo(file.Width));
      Assert.That(reread.Height, Is.EqualTo(file.Height));
      Assert.That(reread.Depth, Is.EqualTo(file.Depth));
      Assert.That(reread.PixelData, Is.EqualTo(file.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromStream_ValidParsesCorrectly() {
    var data = _BuildValidIm5(2, 2, 8);
    using var ms = new MemoryStream(data);
    var result = Im5VisilogReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(2));
  }

  private static byte[] _BuildValidIm5(int width, int height, int depth) {
    var bytesPerPixel = depth / 8;
    var pixelDataSize = width * height * bytesPerPixel;
    var data = new byte[Im5VisilogFile.HeaderSize + pixelDataSize];

    BitConverter.TryWriteBytes(new Span<byte>(data, 0, 4), width);
    BitConverter.TryWriteBytes(new Span<byte>(data, 4, 4), height);
    BitConverter.TryWriteBytes(new Span<byte>(data, 8, 4), depth);

    for (var i = 0; i < pixelDataSize; ++i)
      data[Im5VisilogFile.HeaderSize + i] = (byte)(i % 256);

    return data;
  }
}
