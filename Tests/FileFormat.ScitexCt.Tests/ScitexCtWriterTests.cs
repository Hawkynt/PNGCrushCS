using System;
using System.Text;
using FileFormat.ScitexCt;

namespace FileFormat.ScitexCt.Tests;

[TestFixture]
public sealed class ScitexCtWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Signature_IsCT() {
    var file = new ScitexCtFile {
      Width = 2,
      Height = 2,
      BitsPerComponent = 8,
      ColorMode = ScitexCtColorMode.Grayscale,
      PixelData = new byte[4]
    };

    var bytes = ScitexCtWriter.ToBytes(file);
    var signature = Encoding.ASCII.GetString(bytes, 0, 2);

    Assert.That(signature, Is.EqualTo("CT"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderSize80() {
    var file = new ScitexCtFile {
      Width = 2,
      Height = 2,
      BitsPerComponent = 8,
      ColorMode = ScitexCtColorMode.Grayscale,
      PixelData = new byte[4]
    };

    var bytes = ScitexCtWriter.ToBytes(file);
    var headerSize = Encoding.ASCII.GetString(bytes, 2, 6).Trim();

    Assert.That(headerSize, Is.EqualTo("000080"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ScitexCtWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileSizeCorrect() {
    var file = new ScitexCtFile {
      Width = 4,
      Height = 3,
      BitsPerComponent = 8,
      ColorMode = ScitexCtColorMode.Cmyk,
      PixelData = new byte[4 * 3 * 4]
    };

    var bytes = ScitexCtWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(ScitexCtHeader.StructSize + 4 * 3 * 4));
  }
}
