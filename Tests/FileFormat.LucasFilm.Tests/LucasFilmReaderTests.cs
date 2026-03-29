using System;
using System.IO;
using FileFormat.LucasFilm;

namespace FileFormat.LucasFilm.Tests;

[TestFixture]
public sealed class LucasFilmReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => LucasFilmReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => LucasFilmReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".lff"));
    Assert.Throws<FileNotFoundException>(() => LucasFilmReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => LucasFilmReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => LucasFilmReader.FromBytes(new byte[4]));

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[LucasFilmFile.HeaderSize + 8];
    Assert.Throws<InvalidDataException>(() => LucasFilmReader.FromBytes(data));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildValid(4, 2);
    var result = LucasFilmReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(4));
      Assert.That(result.Height, Is.EqualTo(2));
      Assert.That(result.PixelData.Length, Is.EqualTo(24));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_RoundTrip_PreservesData() {
    var original = _BuildValid(3, 2);
    var file = LucasFilmReader.FromBytes(original);
    var written = LucasFilmWriter.ToBytes(file);
    var reread = LucasFilmReader.FromBytes(written);

    Assert.Multiple(() => {
      Assert.That(reread.Width, Is.EqualTo(file.Width));
      Assert.That(reread.Height, Is.EqualTo(file.Height));
      Assert.That(reread.PixelData, Is.EqualTo(file.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromStream_ValidParsesCorrectly() {
    var data = _BuildValid(8, 2);
    using var ms = new MemoryStream(data);
    var result = LucasFilmReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(8));
  }

  private static byte[] _BuildValid(int width, int height) {
    var pixelDataSize = width * height * 3;
    var data = new byte[LucasFilmFile.HeaderSize + pixelDataSize];

    data[0] = 0x4C; // 'L'
    data[1] = 0x46; // 'F'
    data[2] = 0x46; // 'F'
    data[3] = 0x00; // '\0'
    BitConverter.TryWriteBytes(new Span<byte>(data, 4, 2), (ushort)width);
    BitConverter.TryWriteBytes(new Span<byte>(data, 6, 2), (ushort)height);
    BitConverter.TryWriteBytes(new Span<byte>(data, 8, 2), (ushort)24); // bpp
    BitConverter.TryWriteBytes(new Span<byte>(data, 10, 2), (ushort)3); // channels
    // reserved = 0

    for (var i = 0; i < pixelDataSize; ++i)
      data[LucasFilmFile.HeaderSize + i] = (byte)(i % 256);

    return data;
  }
}
