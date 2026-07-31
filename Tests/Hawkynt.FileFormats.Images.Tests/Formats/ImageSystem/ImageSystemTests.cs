using System;
using System.IO;
using FileFormat.Core;
using FileFormat.ImageSystem;

namespace FileFormat.ImageSystem.Tests;

/// <summary>
/// Image System's two file layouts, which are told apart by length alone and put their sections in
/// different places — the high-resolution one spreads the bitmap over eight whole pages, the
/// multicolour one leads with the colour RAM.
/// </summary>
[TestFixture]
public sealed class ImageSystemTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ImageSystemReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_UnknownSize_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => ImageSystemReader.FromBytes(new byte[9009]));

  [Test]
  [Category("Unit")]
  public void Hires_EachSectionReadFromItsOwnOffset() {
    var data = new byte[ImageSystemFile.HiresFileSize];
    data[ImageSystemFile.HiresBitmapOffset] = 0xAB;
    data[ImageSystemFile.HiresVideoMatrixOffset] = 0x12;

    var result = ImageSystemReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.IsHires, Is.True);
      Assert.That(result.Width, Is.EqualTo(320));
      Assert.That(result.Height, Is.EqualTo(200));
      Assert.That(result.BitmapData[0], Is.EqualTo(0xAB));
      Assert.That(result.VideoMatrix[0], Is.EqualTo(0x12));
    });
  }

  [Test]
  [Category("Unit")]
  public void Multicolor_EachSectionReadFromItsOwnOffset() {
    var data = new byte[ImageSystemFile.MulticolorFileSize];
    data[ImageSystemFile.MulticolorBitmapOffset] = 0xAB;
    data[ImageSystemFile.MulticolorVideoMatrixOffset] = 0x12;
    data[ImageSystemFile.MulticolorColorRamOffset] = 0x34;
    data[ImageSystemFile.MulticolorBackgroundOffset] = 0x06;

    var result = ImageSystemReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.IsHires, Is.False);
      Assert.That(result.Width, Is.EqualTo(160));
      Assert.That(result.BitmapData[0], Is.EqualTo(0xAB));
      Assert.That(result.VideoMatrix[0], Is.EqualTo(0x12));
      Assert.That(result.ColorRam![0], Is.EqualTo(0x34));
      Assert.That(result.BackgroundColor, Is.EqualTo(0x06));
    });
  }

  [TestCase(true)]
  [TestCase(false)]
  [Category("Unit")]
  public void RoundTrip_PreservesEverySection(bool hires) {
    var size = hires ? ImageSystemFile.HiresFileSize : ImageSystemFile.MulticolorFileSize;
    var data = new byte[size];
    data[0] = 0x00;
    data[1] = 0x40;

    var bitmapOffset = hires ? ImageSystemFile.HiresBitmapOffset : ImageSystemFile.MulticolorBitmapOffset;
    for (var i = 0; i < ImageSystemFile.BitmapDataSize; ++i)
      data[bitmapOffset + i] = (byte)(i % 251);

    var original = ImageSystemReader.FromBytes(data);
    var reread = ImageSystemReader.FromBytes(ImageSystemWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(reread.IsHires, Is.EqualTo(original.IsHires));
      Assert.That(reread.LoadAddress, Is.EqualTo(original.LoadAddress));
      Assert.That(reread.BitmapData, Is.EqualTo(original.BitmapData));
      Assert.That(reread.VideoMatrix, Is.EqualTo(original.VideoMatrix));
      Assert.That(reread.BackgroundColor, Is.EqualTo(original.BackgroundColor));
    });
  }

  /// <summary>Writing a picture gives the multicolour form, which is the one holding more colour.</summary>
  [Test]
  [Category("Unit")]
  public void FromRawImage_WritesTheMulticolorForm() {
    var rgb = new byte[160 * 200 * 3];
    for (var i = 0; i < rgb.Length; i += 3) {
      var color = Commodore64Graphics.HexColors[i / 3 % 16];
      rgb[i] = (byte)(color >> 16);
      rgb[i + 1] = (byte)(color >> 8);
      rgb[i + 2] = (byte)color;
    }

    var source = new RawImage { Width = 160, Height = 200, Format = PixelFormat.Rgb24, PixelData = rgb };

    var written = ImageSystemWriter.ToBytes(ImageSystemFile.FromRawImage(source));

    Assert.That(written, Has.Length.EqualTo(ImageSystemFile.MulticolorFileSize));
    Assert.That(ImageSystemReader.FromBytes(written).IsHires, Is.False);
  }
}
