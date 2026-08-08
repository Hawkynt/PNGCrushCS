using System;
using System.IO;
using FileFormat.Core;
using FileFormat.InterleafImage;

namespace FileFormat.InterleafImage.Tests;

[TestFixture]
public sealed class InterleafImageTests {

  private static RawImage _Picture(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      pixels[i * 3] = (byte)(i * 11);
      pixels[i * 3 + 1] = (byte)(i * 5);
      pixels[i * 3 + 2] = (byte)(i * 2);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => InterleafImageReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongMagic_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => InterleafImageReader.FromBytes(new byte[64]));

  /// <summary>The header's size times its depth, plus the header, is the length of the file — which is
  /// the whole of the evidence that the size is read where the format keeps it.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_TheStatedSizeMustAccountForTheFile() {
    var data = InterleafImageWriter.ToBytes(InterleafImageFile.FromRawImage(_Picture(8, 4)));
    Array.Resize(ref data, data.Length - 1);

    Assert.Throws<InvalidDataException>(() => InterleafImageReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ADepthOtherThanTwentyFourIsRefused() {
    var data = InterleafImageWriter.ToBytes(InterleafImageFile.FromRawImage(_Picture(8, 4)));
    data[InterleafImageFile.BitsPerPixelAt + 1] = 8;

    Assert.Throws<InvalidDataException>(() => InterleafImageReader.FromBytes(data));
  }

  /// <summary>A row of red, then that row's green, then its blue — not three bytes to a pixel.</summary>
  [Test]
  [Category("Unit")]
  public void ToBytes_ThePlanesAreInterleavedByLine() {
    var image = _Picture(4, 2);
    var bytes = InterleafImageWriter.ToBytes(InterleafImageFile.FromRawImage(image));
    var body = bytes[InterleafImageFile.HeaderSize..];

    Assert.Multiple(() => {
      for (var x = 0; x < 4; ++x) {
        Assert.That(body[x], Is.EqualTo(image.PixelData[x * 3]), $"red of pixel {x}");
        Assert.That(body[4 + x], Is.EqualTo(image.PixelData[x * 3 + 1]), $"green of pixel {x}");
        Assert.That(body[8 + x], Is.EqualTo(image.PixelData[x * 3 + 2]), $"blue of pixel {x}");
      }
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ThePictureComesBackExactly() {
    var original = _Picture(16, 9);
    var decoded = InterleafImageFile.ToRawImage(
      InterleafImageReader.FromBytes(InterleafImageWriter.ToBytes(InterleafImageFile.FromRawImage(original))));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(16));
      Assert.That(decoded.Height, Is.EqualTo(9));
      Assert.That(decoded.PixelData, Is.EqualTo(original.PixelData));
    });
  }
}
