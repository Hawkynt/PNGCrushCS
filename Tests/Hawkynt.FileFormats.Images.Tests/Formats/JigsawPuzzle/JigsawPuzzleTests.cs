using System;
using System.IO;
using FileFormat.Core;
using FileFormat.JigsawPuzzle;

namespace FileFormat.JigsawPuzzle.Tests;

[TestFixture]
public sealed class JigsawPuzzleTests {

  private static RawImage _Picture(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      pixels[i * 3] = (byte)(i * 7);
      pixels[i * 3 + 1] = (byte)(i * 3);
      pixels[i * 3 + 2] = (byte)i;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => JigsawPuzzleReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongMagic_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => JigsawPuzzleReader.FromBytes(new byte[64]));

  [Test]
  [Category("Unit")]
  public void FromBytes_ABitmapIsNotOneOfThese() {
    var data = JigsawPuzzleWriter.ToBytes(JigsawPuzzleFile.FromRawImage(_Picture(8, 4)));
    data[0] = (byte)'B';
    data[1] = (byte)'M';

    Assert.Throws<InvalidDataException>(() => JigsawPuzzleReader.FromBytes(data));
  }

  /// <summary>The stated length must be the pixel offset plus the padded rows, which is what tells one
  /// of these from anything else that happens to begin with the same two letters.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_StatedLengthMustAccountForThePixels() {
    var data = JigsawPuzzleWriter.ToBytes(JigsawPuzzleFile.FromRawImage(_Picture(8, 4)));
    data[JigsawPuzzleFile.BitmapLengthAt] ^= 0x20;

    Assert.Throws<InvalidDataException>(() => JigsawPuzzleReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ThePuzzleAfterTheBitmapIsKept() {
    var written = JigsawPuzzleWriter.ToBytes(JigsawPuzzleFile.FromRawImage(_Picture(8, 4)));
    var withPuzzle = new byte[written.Length + 5];
    written.CopyTo(withPuzzle, 0);
    "pieces"u8[..5].CopyTo(withPuzzle.AsSpan(written.Length));

    var file = JigsawPuzzleReader.FromBytes(withPuzzle);

    Assert.Multiple(() => {
      Assert.That(file.Puzzle, Has.Length.EqualTo(5), "what follows the bitmap is kept");
      Assert.That(file.Embedded, Has.Length.EqualTo(written.Length), "and is not read as picture");
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_OpensWithTheTwoLettersTheseUse() {
    var bytes = JigsawPuzzleWriter.ToBytes(JigsawPuzzleFile.FromRawImage(_Picture(8, 4)));

    Assert.That(bytes[..2], Is.EqualTo(new[] { (byte)'J', (byte)'G' }));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ThePictureComesBackExactly() {
    var original = _Picture(16, 9);
    var decoded = JigsawPuzzleFile.ToRawImage(
      JigsawPuzzleReader.FromBytes(JigsawPuzzleWriter.ToBytes(JigsawPuzzleFile.FromRawImage(original))));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(16));
      Assert.That(decoded.Height, Is.EqualTo(9));
      Assert.That(decoded.ToRgb24(), Is.EqualTo(original.PixelData));
    });
  }
}
