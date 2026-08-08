using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.DivGameMap.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private static RawImage _TwoHundredFiftySixColours(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      var value = (byte)(i % 256);
      pixels[i * 3] = value;
      pixels[i * 3 + 1] = (byte)(value * 2 % 256);
      pixels[i * 3 + 2] = (byte)(255 - value);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PictureWithinThePaletteSize_ReturnsEveryPixelUnchanged() {
    var source = _TwoHundredFiftySixColours(16, 16);

    var restored = DivGameMapFile.ToRawImage(DivGameMapReader.FromBytes(DivGameMapWriter.ToBytes(DivGameMapFile.FromRawImage(source))));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(16));
      Assert.That(restored.Height, Is.EqualTo(16));
      Assert.That(restored.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  /// <summary>The reader skips four bytes per control point before it reads a pixel, so an entry
  /// claiming points it does not have would put the body out of step.</summary>
  [Test]
  [Category("Integration")]
  public void RoundTrip_SingleEntry_DeclaresNoControlPoints() {
    var bytes = DivGameMapWriter.ToBytes(DivGameMapFile.FromRawImage(_TwoHundredFiftySixColours(4, 4)));

    var pointCount = BinaryPrimitives.ReadInt32LittleEndian(
      bytes.AsSpan(DivGameMapFile.MinFileSize + 4 + 4 + 32 + 12 + 4 + 4));

    Assert.That(pointCount, Is.Zero);
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_OddSizes_AreKept([Values(1, 7, 63)] int width) {
    Assert.That(DivGameMapFile.FromRawImage(_TwoHundredFiftySixColours(width, 2)).Width, Is.EqualTo(width));
  }
}
