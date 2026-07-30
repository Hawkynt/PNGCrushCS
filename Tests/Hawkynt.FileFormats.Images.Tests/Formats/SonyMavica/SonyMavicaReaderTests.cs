using System;
using System.IO;
using FileFormat.SonyMavica;

namespace FileFormat.SonyMavica.Tests;

[TestFixture]
public sealed class SonyMavicaReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SonyMavicaReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SonyMavicaReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".411"));
    Assert.Throws<FileNotFoundException>(() => SonyMavicaReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SonyMavicaReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => SonyMavicaReader.FromBytes(new byte[4]));

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[SonyMavicaFile.MinFileSize + 12];
    Assert.Throws<InvalidDataException>(() => SonyMavicaReader.FromBytes(data));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildValid(4, 2);
    var result = SonyMavicaReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(4));
      Assert.That(result.Height, Is.EqualTo(2));
      Assert.That(result.PixelData.Length, Is.EqualTo(4 * 2 * 3));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_RoundTrip_PreservesData() {
    var original = _BuildValid(8, 3);
    var file = SonyMavicaReader.FromBytes(original);
    var written = SonyMavicaWriter.ToBytes(file);
    var reread = SonyMavicaReader.FromBytes(written);

    Assert.Multiple(() => {
      Assert.That(reread.Width, Is.EqualTo(file.Width));
      Assert.That(reread.Height, Is.EqualTo(file.Height));
      Assert.That(reread.PixelData, Is.EqualTo(file.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromStream_ValidParsesCorrectly() {
    var data = _BuildValid(4, 2);
    using var ms = new MemoryStream(data);
    var result = SonyMavicaReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(4));
  }

  private static byte[] _BuildValid(int width, int height) {
    var pixelDataSize = width * height * 3;
    var data = new byte[SonyMavicaFile.HeaderSize + pixelDataSize];

    data[0] = 0x4D; // 'M'
    data[1] = 0x56; // 'V'
    BitConverter.TryWriteBytes(new Span<byte>(data, 2, 2), (ushort)width);
    BitConverter.TryWriteBytes(new Span<byte>(data, 4, 2), (ushort)height);

    for (var i = 0; i < pixelDataSize; ++i)
      data[SonyMavicaFile.HeaderSize + i] = (byte)(i % 256);

    return data;
  }
}
