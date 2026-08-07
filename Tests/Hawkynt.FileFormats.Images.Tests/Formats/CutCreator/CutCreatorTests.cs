using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.CutCreator.Tests;

/// <summary>
/// A Cut Creator picture: 96 by 99, one bit a pixel, and nothing else in the file.
/// </summary>
/// <remarks>
/// <c>.cut</c> was claimed only by Dr. Halo, which means something else by the name and refused
/// these for having no usable dimensions in them — because there are none to have.
/// </remarks>
[TestFixture]
public sealed class CutCreatorTests {

  private static byte[] _ValidFile() {
    var data = new byte[CutCreatorFile.FileSize];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i % 3 == 0 ? 0b1010_1010 : 0);

    return data;
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => CutCreatorReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FileSize_IsTheOnlyThingThatIdentifiesOne() {
    // 12 bytes a row, 99 rows. Nothing in the file says what it is, so a reader that took anything
    // longer would claim every 1188-byte file of any format at all.
    Assert.That(CutCreatorFile.FileSize, Is.EqualTo(1188));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AnyOtherLength_ThrowsInvalidDataException() {
    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => CutCreatorReader.FromBytes(new byte[CutCreatorFile.FileSize - 1]));
      Assert.Throws<InvalidDataException>(() => CutCreatorReader.FromBytes(new byte[CutCreatorFile.FileSize + 1]));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_IsNinetySixByNinetyNine() {
    var picture = CutCreatorFile.ToRawImage(CutCreatorReader.FromBytes(_ValidFile()));

    Assert.Multiple(() => {
      Assert.That(picture.Width, Is.EqualTo(96));
      Assert.That(picture.Height, Is.EqualTo(99));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_TakesASetBitAsLitAndTheTopBitAsLeftmost() {
    var picture = CutCreatorFile.ToRawImage(CutCreatorReader.FromBytes(_ValidFile()));
    var rgb = PixelConverter.Convert(picture, PixelFormat.Rgb24).PixelData;

    // 0xEE and not white: a colour byte carries its luminance in the low nibble and the chip
    // ignores that nibble's bottom bit, so 15 is not a level the hardware can show.
    Assert.Multiple(() => {
      Assert.That(rgb[0], Is.EqualTo(0xEE), "0b1010_1010 starts with a set bit, which is lit");
      Assert.That(rgb[3], Is.EqualTo(0), "and the next is clear");
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_TheBitmapComesBack() {
    var original = CutCreatorReader.FromBytes(_ValidFile());

    var restored = CutCreatorReader.FromBytes(CutCreatorWriter.ToBytes(original));

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void FromRawImage_ProducesAWholeFile() {
    var pixels = new byte[96 * 99 * 3];
    for (var i = 0; i < 96 * 99; ++i)
      pixels[i * 3] = pixels[i * 3 + 1] = pixels[i * 3 + 2] = (byte)(i % 5 == 0 ? 255 : 0);

    var file = CutCreatorFile.FromRawImage(new() {
      Width = 96, Height = 99, Format = PixelFormat.Rgb24, PixelData = pixels,
    });

    Assert.That(CutCreatorWriter.ToBytes(file), Has.Length.EqualTo(CutCreatorFile.FileSize));
  }
}
