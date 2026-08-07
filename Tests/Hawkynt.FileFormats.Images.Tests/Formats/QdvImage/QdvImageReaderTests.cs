using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.QdvImage.Tests;

/// <summary>
/// A QDV picture: five bytes of size, a 256-entry palette, then one byte a pixel.
/// </summary>
/// <remarks>
/// What was tested before was an invention — four bytes reading "QDV\0" and a twelve-byte header
/// carrying a depth and a flags word. No file has that, and the one real sample was refused by it.
/// </remarks>
[TestFixture]
public sealed class QdvImageReaderTests {

  private const int Width = 4;
  private const int Height = 3;

  private static byte[] _ValidFile() {
    var data = new byte[QdvImageFile.PixelOffset + Width * Height];
    data[0] = 0; data[1] = Width;
    data[2] = 0; data[3] = Height;
    data[4] = 2;

    // Entry 1 red, entry 2 green.
    data[QdvImageFile.HeaderSize + 3] = 0xFF;
    data[QdvImageFile.HeaderSize + 7] = 0xFF;

    for (var i = 0; i < Width * Height; ++i)
      data[QdvImageFile.PixelOffset + i] = (byte)(i % 3);

    return data;
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => QdvImageReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => QdvImageReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => QdvImageReader.FromBytes(new byte[QdvImageFile.MinFileSize - 1]));

  [Test]
  [Category("Unit")]
  public void PixelOffset_IsTheHeaderAndThePalette() {
    Assert.Multiple(() => {
      Assert.That(QdvImageFile.HeaderSize, Is.EqualTo(5));
      Assert.That(QdvImageFile.PaletteSize, Is.EqualTo(768));
      Assert.That(QdvImageFile.PixelOffset, Is.EqualTo(773));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TakesTheSizeAsBigEndianWords() {
    var file = QdvImageReader.FromBytes(_ValidFile());

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(Width));
      Assert.That(file.Height, Is.EqualTo(Height));
      Assert.That(file.HighestIndex, Is.EqualTo(2));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AnyOtherLength_ThrowsInvalidDataException() {
    // Nothing in the file says what it is, so the size stated has to account for the whole of it.
    var data = new byte[QdvImageFile.PixelOffset + Width * Height + 1];
    data[1] = Width;
    data[3] = Height;

    Assert.Throws<InvalidDataException>(() => QdvImageReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_DrawsThroughThePalette() {
    var picture = QdvImageFile.ToRawImage(QdvImageReader.FromBytes(_ValidFile()));
    var rgb = PixelConverter.Convert(picture, PixelFormat.Rgb24).PixelData;

    Assert.Multiple(() => {
      Assert.That(picture.Width, Is.EqualTo(Width));
      Assert.That(rgb[0], Is.EqualTo(0), "index 0 is black");
      Assert.That(rgb[3], Is.EqualTo(0xFF), "index 1 is red");
      Assert.That(rgb[7], Is.EqualTo(0xFF), "index 2 is green");
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ThePaletteAndThePictureComeBack() {
    var original = QdvImageReader.FromBytes(_ValidFile());

    var restored = QdvImageReader.FromBytes(QdvImageWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }
}
