using System;
using System.IO;
using FileFormat.Cals;

namespace FileFormat.Cals.Tests;

[TestFixture]
public sealed class CalsReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CalsReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CalsReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cal"));
    Assert.Throws<FileNotFoundException>(() => CalsReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => CalsReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidImage() {
    var file = new CalsFile {
      Width = 16,
      Height = 4,
      Dpi = 300,
      Orientation = "portrait",
      PixelData = new byte[8] // 2 bytes per row * 4 rows
    };
    for (var i = 0; i < file.PixelData.Length; ++i)
      file.PixelData[i] = (byte)(0xA0 + i);

    var bytes = CalsWriter.ToBytes(file);
    var result = CalsReader.FromBytes(bytes);

    Assert.That(result.Width, Is.EqualTo(16));
    Assert.That(result.Height, Is.EqualTo(4));
    Assert.That(result.Dpi, Is.EqualTo(300));
    Assert.That(result.Orientation, Is.EqualTo("portrait"));
    Assert.That(result.PixelData.Length, Is.EqualTo(8));
    Assert.That(result.PixelData, Is.EqualTo(file.PixelData));
  }
}
