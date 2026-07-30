using System;
using System.IO;
using FileFormat.Grs16;

namespace FileFormat.Grs16.Tests;

[TestFixture]
public sealed class Grs16ReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Grs16Reader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Grs16Reader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".g16"));
    Assert.Throws<FileNotFoundException>(() => Grs16Reader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Grs16Reader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => Grs16Reader.FromBytes(new byte[1]));

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildValid(4, 4);
    var result = Grs16Reader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.GreaterThan(0));
      Assert.That(result.Height, Is.GreaterThan(0));
      Assert.That(result.PixelData.Length, Is.EqualTo(data.Length));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_RoundTrip_PreservesData() {
    var original = _BuildValid(4, 2);
    var file = Grs16Reader.FromBytes(original);
    var written = Grs16Writer.ToBytes(file);
    var reread = Grs16Reader.FromBytes(written);

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
    var result = Grs16Reader.FromStream(ms);

    Assert.That(result.PixelData.Length, Is.GreaterThan(0));
  }

  private static byte[] _BuildValid(int width, int height) {
    var pixelCount = width * height;
    var data = new byte[pixelCount * 2];

    for (var i = 0; i < pixelCount; ++i) {
      data[i * 2] = (byte)(i % 256);
      data[i * 2 + 1] = (byte)((i * 3) % 256);
    }

    return data;
  }
}
