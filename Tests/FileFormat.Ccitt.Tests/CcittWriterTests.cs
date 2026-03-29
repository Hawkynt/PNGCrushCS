using System;
using FileFormat.Ccitt;

namespace FileFormat.Ccitt.Tests;

[TestFixture]
public sealed class CcittWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CcittWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_AllWhite_ProducesNonEmptyOutput() {
    var file = new CcittFile {
      Width = 8,
      Height = 1,
      Format = CcittFormat.Group3_1D,
      PixelData = [0x00]
    };

    var bytes = CcittWriter.ToBytes(file);

    Assert.That(bytes, Is.Not.Empty);
    Assert.That(bytes.Length, Is.GreaterThan(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_AllBlack_ProducesNonEmptyOutput() {
    var file = new CcittFile {
      Width = 8,
      Height = 1,
      Format = CcittFormat.Group3_1D,
      PixelData = [0xFF]
    };

    var bytes = CcittWriter.ToBytes(file);

    Assert.That(bytes, Is.Not.Empty);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MultiRow_ProducesCompressedOutput() {
    var file = new CcittFile {
      Width = 16,
      Height = 2,
      Format = CcittFormat.Group3_1D,
      PixelData = [0x00, 0x00, 0xFF, 0xFF]
    };

    var bytes = CcittWriter.ToBytes(file);

    Assert.That(bytes, Is.Not.Empty);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_UnsupportedFormat_ThrowsNotSupportedException() {
    var file = new CcittFile {
      Width = 8,
      Height = 1,
      Format = CcittFormat.Group3_2D,
      PixelData = [0x00]
    };

    Assert.Throws<NotSupportedException>(() => CcittWriter.ToBytes(file));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Group4_ProducesNonEmptyOutput() {
    var file = new CcittFile {
      Width = 8,
      Height = 1,
      Format = CcittFormat.Group4,
      PixelData = [0x00]
    };

    var bytes = CcittWriter.ToBytes(file);

    Assert.That(bytes, Is.Not.Empty);
  }
}
