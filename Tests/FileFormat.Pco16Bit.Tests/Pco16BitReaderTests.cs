using System;
using System.IO;
using FileFormat.Pco16Bit;

namespace FileFormat.Pco16Bit.Tests;

[TestFixture]
public sealed class Pco16BitReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Pco16BitReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Pco16BitReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".b16"));
    Assert.Throws<FileNotFoundException>(() => Pco16BitReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Pco16BitReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => Pco16BitReader.FromBytes(new byte[4]));

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildValidB16(4, 2);
    var result = Pco16BitReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(4));
      Assert.That(result.Height, Is.EqualTo(2));
      Assert.That(result.PixelData.Length, Is.EqualTo(16));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_RoundTrip_PreservesData() {
    var original = _BuildValidB16(3, 3);
    var file = Pco16BitReader.FromBytes(original);
    var written = Pco16BitWriter.ToBytes(file);
    var reread = Pco16BitReader.FromBytes(written);

    Assert.Multiple(() => {
      Assert.That(reread.Width, Is.EqualTo(file.Width));
      Assert.That(reread.Height, Is.EqualTo(file.Height));
      Assert.That(reread.PixelData, Is.EqualTo(file.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromStream_ValidParsesCorrectly() {
    var data = _BuildValidB16(2, 2);
    using var ms = new MemoryStream(data);
    var result = Pco16BitReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(2));
  }

  private static byte[] _BuildValidB16(int width, int height) {
    var pixelDataSize = width * height * 2;
    var data = new byte[Pco16BitFile.HeaderSize + pixelDataSize];

    BitConverter.TryWriteBytes(new Span<byte>(data, 0, 4), width);
    BitConverter.TryWriteBytes(new Span<byte>(data, 4, 4), height);

    for (var i = 0; i < pixelDataSize; ++i)
      data[Pco16BitFile.HeaderSize + i] = (byte)(i % 256);

    return data;
  }
}
