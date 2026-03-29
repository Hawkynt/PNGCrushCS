using System;
using FileFormat.Cineon;

namespace FileFormat.Cineon.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_PreservesDimensions() {
    var pixelData = new byte[8 * 4 * 4]; // 8x4, one 32-bit word per pixel
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new CineonFile {
      Width = 8,
      Height = 4,
      BitsPerSample = 10,
      PixelData = pixelData
    };

    var bytes = CineonWriter.ToBytes(original);
    var restored = CineonReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.BitsPerSample, Is.EqualTo(original.BitsPerSample));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PreservesPixelData() {
    var pixelData = new byte[4 * 2 * 4]; // 4x2
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new CineonFile {
      Width = 4,
      Height = 2,
      BitsPerSample = 10,
      PixelData = pixelData
    };

    var bytes = CineonWriter.ToBytes(original);
    var restored = CineonReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PreservesOrientation() {
    var original = new CineonFile {
      Width = 2,
      Height = 2,
      BitsPerSample = 10,
      Orientation = 1,
      PixelData = new byte[2 * 2 * 4]
    };

    var bytes = CineonWriter.ToBytes(original);
    var restored = CineonReader.FromBytes(bytes);

    Assert.That(restored.Orientation, Is.EqualTo(original.Orientation));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargeImage() {
    var width = 1920;
    var height = 1080;
    var pixelData = new byte[width * height * 4];
    var rng = new Random(42);
    rng.NextBytes(pixelData);

    var original = new CineonFile {
      Width = width,
      Height = height,
      BitsPerSample = 10,
      PixelData = pixelData
    };

    var bytes = CineonWriter.ToBytes(original);
    var restored = CineonReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(width));
      Assert.That(restored.Height, Is.EqualTo(height));
      Assert.That(restored.PixelData, Is.EqualTo(pixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_EmptyPixelData() {
    var original = new CineonFile {
      Width = 0,
      Height = 0,
      BitsPerSample = 10,
      PixelData = []
    };

    var bytes = CineonWriter.ToBytes(original);
    var restored = CineonReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(0));
      Assert.That(restored.Height, Is.EqualTo(0));
      Assert.That(restored.PixelData, Is.Empty);
    });
  }
}
