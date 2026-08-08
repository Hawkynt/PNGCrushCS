using System;
using FileFormat.Core;

namespace FileFormat.BodyPaint3D.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private static RawImage _Gradient(int width, int height) {
    var data = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      data[i * 3] = (byte)(i * 7);
      data[i * 3 + 1] = (byte)(i * 13);
      data[i * 3 + 2] = (byte)(i * 29);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Colour_ReproducesEveryPixel() {
    var source = _Gradient(37, 11);
    var decoded = BodyPaint3DFile.ToRawImage(BodyPaint3DReader.FromBytes(BodyPaint3DWriter.ToBytes(BodyPaint3DFile.FromRawImage(source))));

    Assert.Multiple(() => {
      Assert.That((decoded.Width, decoded.Height), Is.EqualTo((37, 11)));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_KeepsWhateverSizeItIsGiven() {
    var wide = BodyPaint3DFile.FromRawImage(_Gradient(200, 3));
    var tall = BodyPaint3DFile.FromRawImage(_Gradient(3, 200));

    Assert.Multiple(() => {
      Assert.That((wide.Width, wide.Height), Is.EqualTo((200, 3)));
      Assert.That((tall.Width, tall.Height), Is.EqualTo((3, 200)));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grey_StaysOneChannel() {
    var pixels = new byte[37 * 11];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 5);

    var file = BodyPaint3DFile.FromRawImage(new() { Width = 37, Height = 11, Format = PixelFormat.Gray8, PixelData = pixels });
    var decoded = BodyPaint3DReader.FromBytes(BodyPaint3DWriter.ToBytes(file));

    Assert.Multiple(() => {
      Assert.That(decoded.Planes, Is.EqualTo(BodyPaint3DFile.GrayPlanes));
      Assert.That(decoded.PixelData, Is.EqualTo(pixels));
    });
  }

  /// <summary>
  /// The scanlines arrive interleaved by channel — the red, green and blue rows of picture row zero,
  /// then the three of row one — rather than in planes. A writer that emitted planes would round-trip
  /// through a reader that read planes and produce a file with its channels shuffled by row.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToBytes_InterleavesTheScanlinesByChannel() {
    var pixels = new byte[2 * 2 * 3];
    for (var i = 0; i < 4; ++i) {
      pixels[i * 3] = 10;
      pixels[i * 3 + 1] = 20;
      pixels[i * 3 + 2] = 30;
    }

    var bytes = BodyPaint3DWriter.ToBytes(BodyPaint3DFile.FromRawImage(
      new() { Width = 2, Height = 2, Format = PixelFormat.Rgb24, PixelData = pixels }));

    // Each scanline is a tag, a method byte, an array tag, four bytes of length and then the packed
    // row; a row of two equal bytes packs as a literal of two.
    var expected = new byte[] { 10, 20, 30, 10, 20, 30 };
    var seen = new System.Collections.Generic.List<byte>();
    for (var at = 0; at + 8 < bytes.Length; ++at)
      if (bytes[at] == BodyPaint3DFile.TagScanline && bytes[at + 1] == BodyPaint3DFile.MethodPackBits
          && bytes[at + 2] == BodyPaint3DFile.TagByteArray)
        seen.Add(bytes[at + 8]);

    Assert.Multiple(() => {
      Assert.That(seen, Has.Count.EqualTo(6), "two rows in three channels");
      Assert.That(seen, Is.EqualTo(expected), "the three channels of a row come before the next row");
    });
  }
}
