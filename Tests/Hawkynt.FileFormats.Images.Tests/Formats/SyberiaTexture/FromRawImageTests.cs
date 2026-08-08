using System;
using System.IO;
using FileFormat.Core;
using FileFormat.SyberiaTexture;

namespace FileFormat.SyberiaTexture.Tests;

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
    var decoded = SyberiaTextureFile.ToRawImage(SyberiaTextureReader.FromBytes(SyberiaTextureWriter.ToBytes(SyberiaTextureFile.FromRawImage(source))));

    Assert.That((decoded.Width, decoded.Height), Is.EqualTo((source.Width, source.Height)));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CutsExactlyTheTenBytesTheFormatLeavesOut() {
    var full = SyberiaTextureFile.FromRawImage(_Picture()).Restored;
    var written = SyberiaTextureWriter.ToBytes(new() { Restored = full });

    Assert.That(written.Length, Is.EqualTo(full.Length - SyberiaTextureFile.MissingHead.Length));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_APictureThatIsNotAJfif_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => SyberiaTextureWriter.ToBytes(new() { Restored = new byte[32] }));
}
