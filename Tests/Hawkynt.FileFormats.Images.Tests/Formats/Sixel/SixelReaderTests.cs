using System;
using System.IO;
using System.Text;
using FileFormat.Sixel;

namespace FileFormat.Sixel.Tests;

[TestFixture]
public sealed class SixelReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SixelReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SixelReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".six"));
    Assert.Throws<FileNotFoundException>(() => SixelReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[2];
    Assert.Throws<InvalidDataException>(() => SixelReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidDcs_ThrowsInvalidDataException() {
    var invalid = Encoding.ASCII.GetBytes("INVALID");
    Assert.Throws<InvalidDataException>(() => SixelReader.FromBytes(invalid));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidSixel_ParsesCorrectly() {
    var sixel = "\x1BP0;0;0q#0;2;100;0;0~\x1B\\";
    var data = Encoding.ASCII.GetBytes(sixel);
    var result = SixelReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.GreaterThan(0));
      Assert.That(result.Height, Is.GreaterThan(0));
      Assert.That(result.PixelData, Is.Not.Null);
      Assert.That(result.Palette, Is.Not.Null);
    });
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidSixel_ParsesCorrectly() {
    var sixel = "\x1BP0;0;0q#0;2;100;0;0~\x1B\\";
    using var stream = new MemoryStream(Encoding.ASCII.GetBytes(sixel));
    var result = SixelReader.FromStream(stream);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.GreaterThan(0));
      Assert.That(result.Height, Is.GreaterThan(0));
    });
  }
}
