using System;
using System.IO;
using FileFormat.SciFax;

namespace FileFormat.SciFax.Tests;

[TestFixture]
public sealed class SciFaxReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SciFaxReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SciFaxReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".scf"));
    Assert.Throws<FileNotFoundException>(() => SciFaxReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SciFaxReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => SciFaxReader.FromBytes(new byte[4]));

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[SciFaxFile.HeaderSize + 8];
    Assert.Throws<InvalidDataException>(() => SciFaxReader.FromBytes(data));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildValidScf(16, 4);
    var result = SciFaxReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(16));
      Assert.That(result.Height, Is.EqualTo(4));
      Assert.That(result.PixelData.Length, Is.EqualTo(8));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_RoundTrip_PreservesData() {
    var original = _BuildValidScf(24, 3);
    var file = SciFaxReader.FromBytes(original);
    var written = SciFaxWriter.ToBytes(file);
    var reread = SciFaxReader.FromBytes(written);

    Assert.Multiple(() => {
      Assert.That(reread.Width, Is.EqualTo(file.Width));
      Assert.That(reread.Height, Is.EqualTo(file.Height));
      Assert.That(reread.PixelData, Is.EqualTo(file.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromStream_ValidParsesCorrectly() {
    var data = _BuildValidScf(8, 2);
    using var ms = new MemoryStream(data);
    var result = SciFaxReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(8));
  }

  private static byte[] _BuildValidScf(int width, int height) {
    var bytesPerRow = (width + 7) / 8;
    var pixelDataSize = bytesPerRow * height;
    var data = new byte[SciFaxFile.HeaderSize + pixelDataSize];

    data[0] = 0x53; // 'S'
    data[1] = 0x46; // 'F'
    BitConverter.TryWriteBytes(new Span<byte>(data, 2, 2), (ushort)1); // version
    BitConverter.TryWriteBytes(new Span<byte>(data, 4, 2), (ushort)width);
    BitConverter.TryWriteBytes(new Span<byte>(data, 6, 2), (ushort)height);

    for (var i = 0; i < pixelDataSize; ++i)
      data[SciFaxFile.HeaderSize + i] = (byte)(i % 256);

    return data;
  }
}
