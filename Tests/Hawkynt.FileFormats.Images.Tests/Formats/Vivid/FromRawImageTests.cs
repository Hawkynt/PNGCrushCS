using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Vivid;

namespace FileFormat.Vivid.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private static RawImage _Picture(int width = 17, int height = 9) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      pixels[i * 3] = (byte)(i * 7);
      pixels[i * 3 + 1] = (byte)(i * 13);
      pixels[i * 3 + 2] = (byte)(i * 31);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_EveryPixelComesBack() {
    var source = _Picture();
    var decoded = VividFile.ToRawImage(VividReader.FromBytes(VividWriter.ToBytes(VividFile.FromRawImage(source))));

    Assert.Multiple(() => {
      Assert.That((decoded.Width, decoded.Height), Is.EqualTo((source.Width, source.Height)));
      Assert.That(PixelConverter.Convert(decoded, PixelFormat.Rgb24).PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EachRowIsNumberedAndSplitIntoPlanes() {
    // Interleaving the row instead would give the same length and the wrong picture.
    var bytes = VividWriter.ToBytes(VividFile.FromRawImage(_Picture(4, 3)));
    var stride = VividFile.RowNumberSize + 4 * 3;

    Assert.Multiple(() => {
      Assert.That(bytes.Length, Is.EqualTo(VividFile.HeaderSize + stride * 3));
      Assert.That(BitConverter.ToUInt16(bytes, VividFile.HeaderSize), Is.EqualTo(0));
      Assert.That(BitConverter.ToUInt16(bytes, VividFile.HeaderSize + stride), Is.EqualTo(1));
      Assert.That(BitConverter.ToUInt16(bytes, VividFile.HeaderSize + stride * 2), Is.EqualTo(2));
    });
  }
}
