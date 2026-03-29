using System;
using FileFormat.Hdr;

namespace FileFormat.Hdr.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_UniformPixels_PreservedWithinRgbePrecision() {
    var pixels = new float[4 * 3 * 3]; // 4x3 image
    for (var i = 0; i < pixels.Length; i += 3) {
      pixels[i] = 1.0f;
      pixels[i + 1] = 0.5f;
      pixels[i + 2] = 0.25f;
    }

    var original = new HdrFile {
      Width = 4,
      Height = 3,
      PixelData = pixels
    };

    var bytes = HdrWriter.ToBytes(original);
    var restored = HdrReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData.Length, Is.EqualTo(original.PixelData.Length));

    for (var i = 0; i < original.PixelData.Length; ++i) {
      var expected = original.PixelData[i];
      var tolerance = expected < 1e-6f ? 1e-6f : expected * 0.02f;
      Assert.That(restored.PixelData[i], Is.EqualTo(expected).Within(tolerance),
        $"Pixel float at index {i} differs beyond RGBE precision.");
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_BrightPixels_PreservedWithinRgbePrecision() {
    var pixels = new float[2 * 2 * 3];
    pixels[0] = 100.0f; pixels[1] = 200.0f; pixels[2] = 50.0f;
    pixels[3] = 10.0f;  pixels[4] = 20.0f;  pixels[5] = 30.0f;
    pixels[6] = 0.5f;   pixels[7] = 0.3f;   pixels[8] = 0.1f;
    pixels[9] = 500.0f; pixels[10] = 250.0f; pixels[11] = 100.0f;

    var original = new HdrFile {
      Width = 2,
      Height = 2,
      PixelData = pixels
    };

    var bytes = HdrWriter.ToBytes(original);
    var restored = HdrReader.FromBytes(bytes);

    for (var i = 0; i < original.PixelData.Length; ++i) {
      var expected = original.PixelData[i];
      var tolerance = expected < 1e-6f ? 1e-6f : expected * 0.02f;
      Assert.That(restored.PixelData[i], Is.EqualTo(expected).Within(tolerance),
        $"Pixel float at index {i} differs beyond RGBE precision.");
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_BlackPixels_StayBlack() {
    var pixels = new float[2 * 1 * 3]; // all zeros

    var original = new HdrFile {
      Width = 2,
      Height = 1,
      PixelData = pixels
    };

    var bytes = HdrWriter.ToBytes(original);
    var restored = HdrReader.FromBytes(bytes);

    for (var i = 0; i < restored.PixelData.Length; ++i)
      Assert.That(restored.PixelData[i], Is.EqualTo(0f));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Exposure_Preserved() {
    var pixels = new float[2 * 2 * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = 0.5f;

    var original = new HdrFile {
      Width = 2,
      Height = 2,
      Exposure = 3.5f,
      PixelData = pixels
    };

    var bytes = HdrWriter.ToBytes(original);
    var restored = HdrReader.FromBytes(bytes);

    Assert.That(restored.Exposure, Is.EqualTo(3.5f).Within(0.01f));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Dimensions_Preserved() {
    var pixels = new float[10 * 5 * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = 1.0f;

    var original = new HdrFile {
      Width = 10,
      Height = 5,
      PixelData = pixels
    };

    var bytes = HdrWriter.ToBytes(original);
    var restored = HdrReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(10));
    Assert.That(restored.Height, Is.EqualTo(5));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SmallWidth_UsesOldStyleEncoding() {
    // Width < 8 forces old-style (non-adaptive RLE) encoding
    var pixels = new float[4 * 2 * 3];
    for (var i = 0; i < pixels.Length; i += 3) {
      pixels[i] = 2.0f;
      pixels[i + 1] = 1.0f;
      pixels[i + 2] = 0.5f;
    }

    var original = new HdrFile {
      Width = 4,
      Height = 2,
      PixelData = pixels
    };

    var bytes = HdrWriter.ToBytes(original);
    var restored = HdrReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(2));

    for (var i = 0; i < original.PixelData.Length; ++i) {
      var expected = original.PixelData[i];
      var tolerance = expected < 1e-6f ? 1e-6f : expected * 0.02f;
      Assert.That(restored.PixelData[i], Is.EqualTo(expected).Within(tolerance));
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargeWidth_UsesAdaptiveRle() {
    // Width >= 8 triggers adaptive RLE
    var pixels = new float[16 * 2 * 3];
    for (var i = 0; i < pixels.Length; i += 3) {
      pixels[i] = 3.0f;
      pixels[i + 1] = 1.5f;
      pixels[i + 2] = 0.75f;
    }

    var original = new HdrFile {
      Width = 16,
      Height = 2,
      PixelData = pixels
    };

    var bytes = HdrWriter.ToBytes(original);
    var restored = HdrReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(16));
    Assert.That(restored.Height, Is.EqualTo(2));

    for (var i = 0; i < original.PixelData.Length; ++i) {
      var expected = original.PixelData[i];
      var tolerance = expected < 1e-6f ? 1e-6f : expected * 0.02f;
      Assert.That(restored.PixelData[i], Is.EqualTo(expected).Within(tolerance));
    }
  }
}
