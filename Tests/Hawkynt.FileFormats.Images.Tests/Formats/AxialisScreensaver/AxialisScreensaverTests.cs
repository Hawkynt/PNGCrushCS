using System;
using System.IO;
using System.Text;
using FileFormat.AxialisScreensaver;
using FileFormat.Core;
using FileFormat.Png;

namespace FileFormat.AxialisScreensaver.Tests;

[TestFixture]
public sealed class AxialisScreensaverTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => AxialisScreensaverReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => AxialisScreensaverReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(
      () => AxialisScreensaverReader.FromFile(new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ssp"))));

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => AxialisScreensaverReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => AxialisScreensaverReader.FromBytes(new byte[4]));

  [Test]
  [Category("Unit")]
  public void FromBytes_ForeignFile_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => AxialisScreensaverReader.FromBytes(_Png(2, 2)));

  [Test]
  [Category("Unit")]
  public void FromBytes_NoVersionBehindTheSignature_ThrowsInvalidDataException() {
    var data = new byte[64];
    AxialisScreensaverFile.Magic.CopyTo(data);
    data[5] = (byte)'x';
    Assert.Throws<InvalidDataException>(() => AxialisScreensaverReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PictureWithoutItsLengthInFront_ThrowsInvalidDataException() {
    // The same picture, with the word before it saying something other than how long it is.
    var picture = _Png(3, 2);
    var data = _Build([picture], statedLength: picture.Length + 1);
    Assert.Throws<InvalidDataException>(() => AxialisScreensaverReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_FindsEveryEmbeddedPicture() {
    var first = _Png(4, 3);
    var second = _Png(6, 5);
    var file = AxialisScreensaverReader.FromBytes(_Build([first, second]));

    Assert.That(file.Version, Is.EqualTo("0400"));
    Assert.That(AxialisScreensaverFile.ImageCount(file), Is.EqualTo(2));
    Assert.That(file.Embedded[0], Is.EqualTo(first));
    Assert.That(file.Embedded[1], Is.EqualTo(second));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_TakesTheOneAsked_NotTheFirst() {
    var file = AxialisScreensaverReader.FromBytes(_Build([_Png(4, 3), _Png(6, 5)]));

    var second = AxialisScreensaverFile.ToRawImage(file, 1);
    Assert.That(second.Width, Is.EqualTo(6));
    Assert.That(second.Height, Is.EqualTo(5));

    Assert.Throws<ArgumentOutOfRangeException>(() => AxialisScreensaverFile.ToRawImage(file, 2));
  }

  /// <summary>The signature, a name of sorts, then each picture behind the length it states.</summary>
  private static byte[] _Build(byte[][] pictures, int? statedLength = null) {
    using var ms = new MemoryStream();
    ms.Write(Encoding.ASCII.GetBytes("AXSSP0400"));
    ms.Write(new byte[16], 0, 16);

    foreach (var picture in pictures) {
      var stated = statedLength ?? picture.Length;
      ms.WriteByte((byte)stated);
      ms.WriteByte((byte)(stated >> 8));
      ms.WriteByte((byte)(stated >> 16));
      ms.WriteByte((byte)(stated >> 24));
      ms.Write(picture, 0, picture.Length);
    }

    return ms.ToArray();
  }

  internal static byte[] _Png(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 11 % 256);

    return PngWriter.ToBytes(PngFile.FromRawImage(new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    }));
  }
}
