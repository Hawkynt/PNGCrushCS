using System;
using System.IO;
using FileFormat.Core;
using FileFormat.TilezTexture;

namespace FileFormat.TilezTexture.Tests;

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
  public void RoundTrip_ThePictureComesBack() {
    var source = _Picture();
    var decoded = TilezTextureFile.ToRawImage(TilezTextureReader.FromBytes(TilezTextureWriter.ToBytes(TilezTextureFile.FromRawImage(source))));

    Assert.That((decoded.Width, decoded.Height), Is.EqualTo((source.Width, source.Height)));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StatesThePicturesLength() {
    var bytes = TilezTextureWriter.ToBytes(TilezTextureFile.FromRawImage(_Picture()));

    Assert.Multiple(() => {
      Assert.That(bytes[..4], Is.EqualTo(TilezTextureFile.Magic.ToArray()));
      Assert.That(BitConverter.ToInt32(bytes, 4), Is.EqualTo(bytes.Length - TilezTextureFile.HeaderSize));
    });
  }
}
