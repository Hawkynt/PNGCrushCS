using System;
using FileFormat.PublicPainter;

namespace FileFormat.PublicPainter.Tests;

[TestFixture]
public sealed class PublicPainterWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PublicPainterWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_AllZeros_ProducesCompressedOutput() {
    var file = new PublicPainterFile {
      PixelData = new byte[PublicPainterFile.DecompressedSize]
    };

    var bytes = PublicPainterWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.LessThan(PublicPainterFile.DecompressedSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_AllOnes_ProducesCompressedOutput() {
    var pixelData = new byte[PublicPainterFile.DecompressedSize];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = 0xFF;

    var file = new PublicPainterFile {
      PixelData = pixelData
    };

    var bytes = PublicPainterWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.LessThan(PublicPainterFile.DecompressedSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ProducesNonEmptyOutput() {
    var file = new PublicPainterFile {
      PixelData = new byte[PublicPainterFile.DecompressedSize]
    };

    var bytes = PublicPainterWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.GreaterThan(0));
  }
}
