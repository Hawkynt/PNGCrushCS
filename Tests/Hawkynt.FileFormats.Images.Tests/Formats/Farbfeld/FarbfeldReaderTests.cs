using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Farbfeld;

namespace FileFormat.Farbfeld.Tests;

[TestFixture]
public sealed class FarbfeldReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FarbfeldReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FarbfeldReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ff"));
    Assert.Throws<FileNotFoundException>(() => FarbfeldReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FarbfeldReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[10];
    Assert.Throws<InvalidDataException>(() => FarbfeldReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidSignature_ThrowsInvalidDataException() {
    var bad = new byte[16];
    bad[0] = (byte)'X';
    Assert.Throws<InvalidDataException>(() => FarbfeldReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildMinimalFarbfeld(2, 3);
    var result = FarbfeldReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(2));
      Assert.That(result.Height, Is.EqualTo(3));
      Assert.That(result.PixelData.Length, Is.EqualTo(2 * 3 * 8));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PixelDataTooSmall_ThrowsInvalidDataException() {
    var data = new byte[16]; // header only, claims 1x1 but no pixel data
    var magic = "farbfeld"u8;
    magic.CopyTo(data.AsSpan());
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), 1);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(12), 1);

    Assert.Throws<InvalidDataException>(() => FarbfeldReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidParsesCorrectly() {
    var data = _BuildMinimalFarbfeld(1, 1);
    using var ms = new MemoryStream(data);
    var result = FarbfeldReader.FromStream(ms);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(1));
      Assert.That(result.Height, Is.EqualTo(1));
    });
  }

  private static byte[] _BuildMinimalFarbfeld(int width, int height) {
    var pixelDataSize = width * height * 8;
    var fileSize = 16 + pixelDataSize;
    var data = new byte[fileSize];

    // Magic
    var magic = "farbfeld"u8;
    magic.CopyTo(data.AsSpan());

    // Width and Height (big-endian)
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), (uint)width);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(12), (uint)height);

    // Fill pixel data with a recognizable pattern
    for (var i = 0; i < pixelDataSize; ++i)
      data[16 + i] = (byte)(i * 7 % 256);

    return data;
  }
}
