using System;
using System.IO;
using System.Text;
using FileFormat.CsvImage;

namespace FileFormat.CsvImage.Tests;

[TestFixture]
public sealed class CsvImageReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => CsvImageReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => CsvImageReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv"));
    Assert.Throws<FileNotFoundException>(() => CsvImageReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => CsvImageReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => CsvImageReader.FromBytes(new byte[2]));

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidHeader_ThrowsInvalidDataException() {
    var data = Encoding.ASCII.GetBytes("not_a_header\n0,0,0\n");
    Assert.Throws<InvalidDataException>(() => CsvImageReader.FromBytes(data));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildValid(4, 2);
    var result = CsvImageReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(4));
      Assert.That(result.Height, Is.EqualTo(2));
      Assert.That(result.PixelData.Length, Is.EqualTo(8));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_RoundTrip_PreservesData() {
    var original = _BuildValid(3, 2);
    var file = CsvImageReader.FromBytes(original);
    var written = CsvImageWriter.ToBytes(file);
    var reread = CsvImageReader.FromBytes(written);

    Assert.Multiple(() => {
      Assert.That(reread.Width, Is.EqualTo(file.Width));
      Assert.That(reread.Height, Is.EqualTo(file.Height));
      Assert.That(reread.PixelData, Is.EqualTo(file.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromStream_ValidParsesCorrectly() {
    var data = _BuildValid(2, 2);
    using var ms = new MemoryStream(data);
    var result = CsvImageReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(2));
  }

  private static byte[] _BuildValid(int width, int height) {
    var sb = new StringBuilder();
    sb.Append(width);
    sb.Append(',');
    sb.Append(height);
    sb.Append('\n');

    for (var y = 0; y < height; ++y) {
      for (var x = 0; x < width; ++x) {
        if (x > 0)
          sb.Append(',');

        sb.Append((y * width + x) % 256);
      }

      sb.Append('\n');
    }

    return Encoding.ASCII.GetBytes(sb.ToString());
  }
}
