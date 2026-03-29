using System;
using System.IO;
using FileFormat.PublicPainter;

namespace FileFormat.PublicPainter.Tests;

[TestFixture]
public sealed class PublicPainterReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PublicPainterReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PublicPainterReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cmp"));
    Assert.Throws<FileNotFoundException>(() => PublicPainterReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PublicPainterReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[1];
    Assert.Throws<InvalidDataException>(() => PublicPainterReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidCompressed_ParsesCorrectly() {
    var original = new byte[PublicPainterFile.DecompressedSize];
    var compressed = PublicPainterCompressor.Compress(original);
    var result = PublicPainterReader.FromBytes(compressed);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(PublicPainterFile.ImageWidth));
      Assert.That(result.Height, Is.EqualTo(PublicPainterFile.ImageHeight));
      Assert.That(result.PixelData, Has.Length.EqualTo(PublicPainterFile.DecompressedSize));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidCompressed_ParsesCorrectly() {
    var original = new byte[PublicPainterFile.DecompressedSize];
    var compressed = PublicPainterCompressor.Compress(original);
    using var stream = new MemoryStream(compressed);
    var result = PublicPainterReader.FromStream(stream);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(PublicPainterFile.ImageWidth));
      Assert.That(result.Height, Is.EqualTo(PublicPainterFile.ImageHeight));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AllOnes_DecompressesCorrectly() {
    var original = new byte[PublicPainterFile.DecompressedSize];
    for (var i = 0; i < original.Length; ++i)
      original[i] = 0xFF;

    var compressed = PublicPainterCompressor.Compress(original);
    var result = PublicPainterReader.FromBytes(compressed);

    for (var i = 0; i < PublicPainterFile.DecompressedSize; ++i)
      Assert.That(result.PixelData[i], Is.EqualTo(0xFF), $"Mismatch at byte {i}");
  }
}
