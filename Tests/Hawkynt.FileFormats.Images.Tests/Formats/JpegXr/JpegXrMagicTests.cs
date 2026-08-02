using System;
using FileFormat.Core;

namespace FileFormat.JpegXr.Tests;

/// <summary>
/// The two bytes that follow the byte order, which say the container is a JPEG XR one.
/// </summary>
/// <remarks>
/// A real file has 0xBC then 0x01, and the container states itself little-endian — so read as a word
/// it is 0x01BC. It was written here as 0xBC01, putting the bytes the other way round, and the
/// writer used the same constant: the pair agreed with each other and no file from anywhere else
/// would open.
/// <para/>
/// The value here was taken from a real file out of a public archive of format samples, which is
/// also what the byte assertion below reproduces.
/// </remarks>
[TestFixture]
public sealed class JpegXrMagicTests {

  [Test]
  [Category("Unit")]
  public void Written_HasTheBytesARealFileHas() {
    var pixels = new byte[16 * 16 * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 251);

    var image = new RawImage { Width = 16, Height = 16, Format = PixelFormat.Rgb24, PixelData = pixels };
    var bytes = JpegXrWriter.ToBytes(JpegXrFile.FromRawImage(image));

    Assert.Multiple(() => {
      Assert.That(bytes[0], Is.EqualTo((byte)'I'));
      Assert.That(bytes[1], Is.EqualTo((byte)'I'));
      Assert.That(bytes[2], Is.EqualTo(0xBC), "a real file has 0xBC here, not 0x01");
      Assert.That(bytes[3], Is.EqualTo(0x01));
    });
  }
}
