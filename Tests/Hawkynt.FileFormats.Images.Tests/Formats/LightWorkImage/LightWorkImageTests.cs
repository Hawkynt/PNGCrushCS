using System;
using System.IO;
using System.Linq;
using FileFormat.Core;
using FileFormat.LightWorkImage;

namespace FileFormat.LightWorkImage.Tests;

[TestFixture]
public sealed class LightWorkImageTests {

  private static RawImage _Picture(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      pixels[i * 3] = (byte)(i * 7);
      pixels[i * 3 + 1] = (byte)(i / width * 3);
      pixels[i * 3 + 2] = (byte)(i % width);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => LightWorkImageReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongMagic_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => LightWorkImageReader.FromBytes(new byte[256]));

  [Test]
  [Category("Unit")]
  public void FromBytes_ReadsTheSizeAndThePixels() {
    var file = LightWorkImageReader.FromBytes(LightWorkImageWriter.ToBytes(LightWorkImageFile.FromRawImage(_Picture(9, 4))));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(9));
      Assert.That(file.Height, Is.EqualTo(4));
      Assert.That(file.Pixels, Has.Length.EqualTo(9 * 4 * 3));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ARunStreamCutShort_ThrowsInvalidDataException() {
    var data = LightWorkImageWriter.ToBytes(LightWorkImageFile.FromRawImage(_Picture(16, 8)));
    // Take the last run and the closing records away: the runs no longer reach the stated size.
    Array.Resize(ref data, data.Length - 14);

    Assert.Throws<InvalidDataException>(() => LightWorkImageReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RubbishAfterThePixels_ThrowsInvalidDataException() {
    var data = LightWorkImageWriter.ToBytes(LightWorkImageFile.FromRawImage(_Picture(4, 4)));
    var longer = new byte[data.Length + 1];
    data.CopyTo(longer, 0);
    longer[^1] = 0x7F;

    Assert.Throws<InvalidDataException>(() => LightWorkImageReader.FromBytes(longer));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_KeepsTheNamesTheFileCarried() {
    var written = LightWorkImageWriter.ToBytes(LightWorkImageFile.FromRawImage(_Picture(4, 4)) with {
      Creator = "ppmtolwi",
      Author = "woll",
      Source = "stdin",
      Date = "Fri_Sep_18_12:29:22_1992",
    });

    var back = LightWorkImageReader.FromBytes(written);

    Assert.Multiple(() => {
      Assert.That(back.Creator, Is.EqualTo("ppmtolwi"));
      Assert.That(back.Author, Is.EqualTo("woll"));
      Assert.That(back.Source, Is.EqualTo("stdin"));
      Assert.That(back.Date, Is.EqualTo("Fri_Sep_18_12:29:22_1992"));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ThePixelsComeBackByteForByte() {
    var source = _Picture(37, 11);
    var decoded = LightWorkImageFile.ToRawImage(
      LightWorkImageReader.FromBytes(LightWorkImageWriter.ToBytes(LightWorkImageFile.FromRawImage(source))));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(37));
      Assert.That(decoded.Height, Is.EqualTo(11));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(decoded.PixelData.SequenceEqual(source.PixelData), Is.True, "every pixel survives the runs");
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ARunOfOneColourLongerThanACountByte() {
    var flat = new RawImage {
      Width = 300, Height = 2, Format = PixelFormat.Rgb24, PixelData = new byte[300 * 2 * 3],
    };

    var decoded = LightWorkImageFile.ToRawImage(
      LightWorkImageReader.FromBytes(LightWorkImageWriter.ToBytes(LightWorkImageFile.FromRawImage(flat))));

    Assert.That(decoded.PixelData.SequenceEqual(flat.PixelData), Is.True, "a 600-pixel run splits and comes back whole");
  }
}
