using System;
using System.IO;
using FileFormat.HomeworldLif;

namespace FileFormat.HomeworldLif.Tests;

[TestFixture]
public sealed class HomeworldLifReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => HomeworldLifReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => HomeworldLifReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".lif"));
    Assert.Throws<FileNotFoundException>(() => HomeworldLifReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => HomeworldLifReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => HomeworldLifReader.FromBytes(new byte[4]));

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[HomeworldLifFile.HeaderSize + 16];
    Assert.Throws<InvalidDataException>(() => HomeworldLifReader.FromBytes(data));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildValidLif(2, 2, 1);
    var result = HomeworldLifReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(2));
      Assert.That(result.Height, Is.EqualTo(2));
      Assert.That(result.Version, Is.EqualTo(1));
      Assert.That(result.PixelData.Length, Is.EqualTo(16));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_RoundTrip_PreservesData() {
    var original = _BuildValidLif(3, 2, 1);
    var file = HomeworldLifReader.FromBytes(original);
    var written = HomeworldLifWriter.ToBytes(file);
    var reread = HomeworldLifReader.FromBytes(written);

    Assert.Multiple(() => {
      Assert.That(reread.Width, Is.EqualTo(file.Width));
      Assert.That(reread.Height, Is.EqualTo(file.Height));
      Assert.That(reread.Version, Is.EqualTo(file.Version));
      Assert.That(reread.PixelData, Is.EqualTo(file.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromStream_ValidParsesCorrectly() {
    var data = _BuildValidLif(2, 2, 1);
    using var ms = new MemoryStream(data);
    var result = HomeworldLifReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(2));
  }

  private static byte[] _BuildValidLif(int width, int height, int version) {
    var pixelDataSize = width * height * 4;
    var data = new byte[HomeworldLifFile.HeaderSize + pixelDataSize];

    data[0] = 0x4C; // 'L'
    data[1] = 0x69; // 'i'
    data[2] = 0x66; // 'f'
    data[3] = 0x20; // ' '
    BitConverter.TryWriteBytes(new Span<byte>(data, 4, 4), version);
    BitConverter.TryWriteBytes(new Span<byte>(data, 8, 4), width);
    BitConverter.TryWriteBytes(new Span<byte>(data, 12, 4), height);

    for (var i = 0; i < pixelDataSize; ++i)
      data[HomeworldLifFile.HeaderSize + i] = (byte)(i % 256);

    return data;
  }
}
