using FileFormat.PublicPainter;

namespace FileFormat.PublicPainter.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void PublicPainterFile_DecompressedSize_Is32000() {
    Assert.That(PublicPainterFile.DecompressedSize, Is.EqualTo(32000));
  }

  [Test]
  [Category("Unit")]
  public void PublicPainterFile_ImageWidth_Is640() {
    Assert.That(PublicPainterFile.ImageWidth, Is.EqualTo(640));
  }

  [Test]
  [Category("Unit")]
  public void PublicPainterFile_ImageHeight_Is400() {
    Assert.That(PublicPainterFile.ImageHeight, Is.EqualTo(400));
  }

  [Test]
  [Category("Unit")]
  public void PublicPainterFile_DefaultWidth_Is640() {
    var file = new PublicPainterFile();
    Assert.That(file.Width, Is.EqualTo(640));
  }

  [Test]
  [Category("Unit")]
  public void PublicPainterFile_DefaultHeight_Is400() {
    var file = new PublicPainterFile();
    Assert.That(file.Height, Is.EqualTo(400));
  }

  [Test]
  [Category("Unit")]
  public void PublicPainterFile_DefaultPixelData_Has32000Bytes() {
    var file = new PublicPainterFile();
    Assert.That(file.PixelData, Has.Length.EqualTo(32000));
  }
}
