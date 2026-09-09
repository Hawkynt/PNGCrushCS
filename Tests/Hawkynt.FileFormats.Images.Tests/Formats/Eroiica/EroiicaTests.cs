using System;
using System.IO;
using System.Linq;
using FileFormat.Core;
using FileFormat.Eroiica;
using FileFormat.Tiff;

namespace FileFormat.Eroiica.Tests;

/// <summary>
/// The fixtures wrap a TIFF this library writes in the eight bytes an Eroiica document opens with,
/// which is the shape the one real sample has: complete TIFF streams standing inside the document,
/// each with its own offsets.
/// </summary>
[TestFixture]
public sealed class EroiicaTests {

  private static RawImage _Picture(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 7 % 251);

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };
  }

  private static byte[] _Tiff(int width, int height)
    => TiffWriter.ToBytes(TiffFile.FromRawImage(_Picture(width, height)));

  private static byte[] _Build(params byte[][] pages)
    => EroiicaFile.Magic.ToArray().Concat(pages.SelectMany(x => x)).ToArray();

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => EroiicaReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_WithoutTheOpeningBytesIsRefused()
    => Assert.Throws<InvalidDataException>(() => EroiicaReader.FromBytes(_Tiff(4, 4)));

  /// <summary>A document whose body holds nothing that walks like a TIFF is refused, not drawn blank.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_WithNoPageInItIsRefused()
    => Assert.Throws<InvalidDataException>(() => EroiicaReader.FromBytes(_Build(new byte[512])));

  [Test]
  [Category("Unit")]
  public void FromBytes_FindsThePageAndReadsIt() {
    var file = EroiicaReader.FromBytes(_Build(new byte[64], _Tiff(5, 3)));

    Assert.That(EroiicaFile.ImageCount(file), Is.EqualTo(1));
    var image = EroiicaFile.ToRawImage(file);
    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(5));
      Assert.That(image.Height, Is.EqualTo(3));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_FindsEveryPageAndKeepsTheirOrder() {
    var file = EroiicaReader.FromBytes(_Build(new byte[16], _Tiff(5, 3), new byte[8], _Tiff(7, 2)));

    Assert.That(EroiicaFile.ImageCount(file), Is.EqualTo(2));
    Assert.Multiple(() => {
      Assert.That(EroiicaFile.ToRawImage(file, 0).Width, Is.EqualTo(5));
      Assert.That(EroiicaFile.ToRawImage(file, 1).Width, Is.EqualTo(7));
    });
  }

  /// <summary>
  /// The three letters a TIFF opens with turn up inside pixel data often enough that finding them is
  /// not finding a page. What makes one is the directory reaching a strip that ends inside the file.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_ARunThatMerelyStartsLikeATiffIsNotAPage() {
    var noise = new byte[256];
    noise[10] = (byte)'I';
    noise[11] = (byte)'I';
    noise[12] = 42;
    noise[13] = 0;
    noise[14] = 0xFF;
    noise[15] = 0xFF;
    noise[16] = 0xFF;
    noise[17] = 0x7F;

    Assert.Throws<InvalidDataException>(() => EroiicaReader.FromBytes(_Build(noise)));
  }

  [Test]
  [Category("Unit")]
  public void Writer_FromRawImage_RoundTripsThePicture() {
    var source = _Picture(7, 5);

    var bytes = EroiicaWriter.ToBytes(EroiicaFile.FromRawImage(source));
    var decoded = EroiicaFile.ToRawImage(EroiicaReader.FromBytes(bytes));

    Assert.Multiple(() => {
      Assert.That(bytes.AsSpan(0, EroiicaFile.Magic.Length).SequenceEqual(EroiicaFile.Magic), Is.True);
      Assert.That(decoded.Width, Is.EqualTo(source.Width));
      Assert.That(decoded.Height, Is.EqualTo(source.Height));
      Assert.That(decoded.Format, Is.EqualTo(source.Format));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void Writer_PreservesEveryTiffPageAndItsOrder() {
    var first = _Tiff(5, 3);
    var second = _Tiff(7, 2);

    var bytes = EroiicaWriter.ToBytes(new() { Pages = [first, second] });
    var decoded = EroiicaReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(decoded.Pages, Has.Count.EqualTo(2));
      Assert.That(decoded.Pages[0], Is.EqualTo(first));
      Assert.That(decoded.Pages[1], Is.EqualTo(second));
      Assert.That(EroiicaFile.ToRawImage(decoded, 0).Width, Is.EqualTo(5));
      Assert.That(EroiicaFile.ToRawImage(decoded, 1).Width, Is.EqualTo(7));
    });
  }

  [Test]
  [Category("Unit")]
  public void Writer_WithoutPages_IsRefused()
    => Assert.Throws<ArgumentException>(() => EroiicaWriter.ToBytes(new()));

  [Test]
  [Category("Unit")]
  public void Writer_WithNonTiffPage_IsRefused()
    => Assert.Throws<InvalidDataException>(() => EroiicaWriter.ToBytes(new() { Pages = [new byte[32]] }));
}
