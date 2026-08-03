using System;
using System.IO;
using FileFormat.Core;
using FileFormat.FunPhotor;
using FileFormat.Png;

namespace FileFormat.FunPhotor.Tests;

/// <summary>
/// What a FunPhotor frame is.
/// </summary>
/// <remarks>
/// These used to describe a Commodore 64 screen in exactly 10050 bytes, because that is what the
/// reader expected. A .fpr is four bytes of length and then an ordinary PNG, so the tests and the
/// reader agreed with each other and with no real file.
/// </remarks>
[TestFixture]
public sealed class FunPhotorReaderTests {

  /// <summary>Builds a frame: four bytes of length, then the smallest PNG that decodes.</summary>
  private static byte[] _BuildValidFile() {
    var png = PngWriter.ToBytes(PngFile.FromRawImage(new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Rgb24,
      PixelData = [255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255],
    }));

    var result = new byte[FunPhotorFile.HeaderSize + png.Length];
    png.CopyTo(result, FunPhotorFile.HeaderSize);
    return result;
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => FunPhotorReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => FunPhotorReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".fpr"));
    Assert.Throws<FileNotFoundException>(() => FunPhotorReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => FunPhotorReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => FunPhotorReader.FromBytes(new byte[8]));

  [Test]
  [Category("Unit")]
  public void FromBytes_WithoutAPngFourBytesIn_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => FunPhotorReader.FromBytes(new byte[10051]));

  [Test]
  [Category("Integration")]
  public void FromBytes_HandsBackThePngItWraps() {
    var result = FunPhotorReader.FromBytes(_BuildValidFile());
    var image = FunPhotorFile.ToRawImage(result);

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(2));
      Assert.That(image.Height, Is.EqualTo(2));
    });
  }
}
