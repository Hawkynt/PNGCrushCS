using System;
using FileFormat.Core;
using FileFormat.Hpi;

namespace FileFormat.Hpi.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private static RawImage _Picture(int width = 17, int height = 9) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i)
      pixels[i * 3] = pixels[i * 3 + 1] = pixels[i * 3 + 2] = (byte)(i * 5);

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ThePictureComesBack() {
    var decoded = HpiFile.ToRawImage(HpiReader.FromBytes(HpiWriter.ToBytes(HpiFile.FromRawImage(_Picture()))));

    Assert.That((decoded.Width, decoded.Height), Is.EqualTo((17, 9)));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TheTableStatesWhereThePictureIs() {
    // The reader takes the offset from the table rather than assuming one, so a table that lied
    // would send it somewhere there is no picture.
    var bytes = HpiWriter.ToBytes(HpiFile.FromRawImage(_Picture()));
    var stated = (int)BitConverter.ToUInt32(bytes, HpiFile.JpegOffsetField);

    Assert.Multiple(() => {
      Assert.That(bytes[..8], Is.EqualTo(HpiFile.Magic.ToArray()));
      Assert.That(bytes[stated], Is.EqualTo(0xFF));
      Assert.That(bytes[stated + 1], Is.EqualTo(0xD8));
    });
  }
}
