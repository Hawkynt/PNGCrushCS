using System;
using FileFormat.Core;
using FileFormat.Ico;

namespace FileFormat.Ico.Tests;

/// <summary>
/// Building an icon out of an arbitrary picture.
/// </summary>
/// <remarks>
/// The entry an icon carries is not a BMP. What it holds was checked against ImageMagick and
/// IrfanView, both of which read the result back to the same pixels including its transparency; the
/// facts they confirmed are pinned here so that a change breaking them fails without needing either
/// tool present.
/// </remarks>
[TestFixture]
public class IcoAuthoringTests {

  /// <summary>A picture whose corner is transparent and whose middle is not.</summary>
  private static RawImage _Picture(int width, int height) {
    var pixels = new byte[width * height * 4];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var at = (y * width + x) * 4;
        pixels[at] = (byte)(x * 8);          // blue
        pixels[at + 1] = (byte)(y * 8);      // green
        pixels[at + 2] = 200;                // red
        pixels[at + 3] = x == 0 && y == 0 ? (byte)0 : (byte)255;
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Bgra32, PixelData = pixels };
  }

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => IcoFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_LargerThanAnEntryCanStateIsBroughtDownToFit() {
    // Each side is one byte in the directory, nought standing for 256, so nothing bigger can be
    // described. Refusing outright made the format unwritable through the registry, which hands a
    // writer whatever picture it has.
    var tooBig = new RawImage { Width = 320, Height = 200, Format = PixelFormat.Bgra32, PixelData = new byte[320 * 200 * 4] };

    var entry = IcoFile.FromRawImage(tooBig).Images[0];

    Assert.Multiple(() => {
      Assert.That(entry.Width, Is.EqualTo(256));
      Assert.That(entry.Height, Is.EqualTo(200), "a side already within reach is left alone");
    });
  }

  [Test]
  public void FromRawImage_NoSize_ThrowsNotSupportedException() {
    var empty = new RawImage { Width = 0, Height = 0, Format = PixelFormat.Bgra32, PixelData = [] };

    Assert.Throws<NotSupportedException>(() => IcoFile.FromRawImage(empty));
  }

  [Test]
  public void FromRawImage_MakesOneEntryAtTheOriginalSize() {
    var icon = IcoFile.FromRawImage(_Picture(16, 16));

    Assert.Multiple(() => {
      Assert.That(icon.Images, Has.Count.EqualTo(1));
      Assert.That(icon.Images[0].Width, Is.EqualTo(16));
      Assert.That(icon.Images[0].Height, Is.EqualTo(16));
      Assert.That(icon.Images[0].BitsPerPixel, Is.EqualTo(32));
      Assert.That(icon.Images[0].Format, Is.EqualTo(IcoImageFormat.Bmp));
    });
  }

  [Test]
  public void FromRawImage_StatesTwiceTheHeightInTheInformationHeader() {
    // The colours and the mask are stacked, and the header counts both. A reader trusting the header
    // rather than the directory entry draws the picture squashed into its top half without this.
    var entry = IcoFile.FromRawImage(_Picture(16, 16)).Images[0];

    Assert.Multiple(() => {
      Assert.That(BitConverter.ToInt32(entry.Data, 4), Is.EqualTo(16), "the width");
      Assert.That(BitConverter.ToInt32(entry.Data, 8), Is.EqualTo(32), "the height, doubled");
    });
  }

  [Test]
  public void FromRawImage_HoldsTheSizeAnEntryOfThisShapeTakes() {
    // Forty bytes of header, four a pixel, and a mask row padded out to a whole four bytes.
    var entry = IcoFile.FromRawImage(_Picture(16, 16)).Images[0];

    Assert.That(entry.Data.Length, Is.EqualTo(40 + 16 * 16 * 4 + 4 * 16));
  }

  [Test]
  public void FromRawImage_WritesTheColoursBottomUp() {
    var entry = IcoFile.FromRawImage(_Picture(4, 4)).Images[0];

    // The last row of the picture is the first in the file. Green counts up with the row, so the
    // first row stored carries the green of row three.
    Assert.That(entry.Data[40 + 1], Is.EqualTo(24), "green of the bottom row");
  }

  [Test]
  public void FromRawImage_SetsTheMaskWhereThePictureIsTransparent() {
    var entry = IcoFile.FromRawImage(_Picture(8, 8)).Images[0];
    var maskAt = 40 + 8 * 8 * 4;

    // The transparent pixel is the top left, which is the last row of a bottom-up mask.
    Assert.Multiple(() => {
      Assert.That(entry.Data[maskAt + 7 * 4], Is.EqualTo(0x80), "the top-left pixel is not drawn");
      Assert.That(entry.Data[maskAt], Is.EqualTo(0x00), "the bottom row is drawn throughout");
    });
  }

  [Test]
  public void RoundTrip_ThroughBytesKeepsEveryPixelAndItsAlpha() {
    var source = _Picture(16, 16);

    var bytes = IcoWriter.ToBytes(IcoFile.FromRawImage(source));
    var restored = IcoFile.ToRawImage(IcoReader.FromBytes(bytes));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(16));
      Assert.That(restored.Height, Is.EqualTo(16));
      Assert.That(restored.ToBgra32(), Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  public void RoundTrip_AcceptsAPictureThatIsNotAlreadyFourBytesAPixel() {
    // A writer the registry can reach is handed whatever a caller has, not only the one layout that
    // needs no conversion.
    var grey = new RawImage {
      Width = 4,
      Height = 4,
      Format = PixelFormat.Gray8,
      PixelData = [.. new byte[] { 0, 40, 80, 120, 160, 200, 240, 255, 10, 20, 30, 40, 50, 60, 70, 80 }],
    };

    var restored = IcoFile.ToRawImage(IcoReader.FromBytes(IcoWriter.ToBytes(IcoFile.FromRawImage(grey))));
    var back = restored.ToBgra32();

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(4));
      Assert.That(back[0], Is.EqualTo(0), "the first pixel is black");
      Assert.That(back[3], Is.EqualTo(255), "and opaque");
      Assert.That(back[4], Is.EqualTo(40), "the second is its own grey");
    });
  }
}
