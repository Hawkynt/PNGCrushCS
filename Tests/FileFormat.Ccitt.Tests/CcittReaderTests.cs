using System;
using System.IO;
using FileFormat.Ccitt;

namespace FileFormat.Ccitt.Tests;

[TestFixture]
public sealed class CcittReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CcittReader.FromBytes(null!, 8, 1, CcittFormat.Group3_1D));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CcittReader.FromFile(null!, 8, 1, CcittFormat.Group3_1D));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".g3"));
    Assert.Throws<FileNotFoundException>(() => CcittReader.FromFile(missing, 8, 1, CcittFormat.Group3_1D));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CcittReader.FromStream(null!, 8, 1, CcittFormat.Group3_1D));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var empty = Array.Empty<byte>();
    Assert.Throws<InvalidDataException>(() => CcittReader.FromBytes(empty, 8, 1, CcittFormat.Group3_1D));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidWidth_ThrowsArgumentOutOfRangeException() {
    Assert.Throws<ArgumentOutOfRangeException>(() => CcittReader.FromBytes([0xFF], 0, 1, CcittFormat.Group3_1D));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidHeight_ThrowsArgumentOutOfRangeException() {
    Assert.Throws<ArgumentOutOfRangeException>(() => CcittReader.FromBytes([0xFF], 8, 0, CcittFormat.Group3_1D));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_UnsupportedFormat_ThrowsNotSupportedException() {
    Assert.Throws<NotSupportedException>(() => CcittReader.FromBytes([0xFF], 8, 1, CcittFormat.Group3_2D));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidGroup3_1D_ReturnsCorrectDimensions() {
    // Encode a simple all-white 8x1 image, then decode
    var original = new CcittFile {
      Width = 8,
      Height = 1,
      Format = CcittFormat.Group3_1D,
      PixelData = [0x00]
    };

    var compressed = CcittWriter.ToBytes(original);
    var result = CcittReader.FromBytes(compressed, 8, 1, CcittFormat.Group3_1D);

    Assert.That(result.Width, Is.EqualTo(8));
    Assert.That(result.Height, Is.EqualTo(1));
    Assert.That(result.Format, Is.EqualTo(CcittFormat.Group3_1D));
  }
}
