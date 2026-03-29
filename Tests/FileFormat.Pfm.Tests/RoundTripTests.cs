using System;
using FileFormat.Pfm;

namespace FileFormat.Pfm.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb_PreservesPixelData() {
    var pixelData = new float[3 * 2 * 3]; // 3x2, RGB
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (i + 1) * 0.1f;

    var original = new PfmFile {
      Width = 3,
      Height = 2,
      ColorMode = PfmColorMode.Rgb,
      Scale = 1.0f,
      IsLittleEndian = true,
      PixelData = pixelData
    };

    var bytes = PfmWriter.ToBytes(original);
    var restored = PfmReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.ColorMode, Is.EqualTo(PfmColorMode.Rgb));
    Assert.That(restored.Scale, Is.EqualTo(1.0f));
    Assert.That(restored.IsLittleEndian, Is.True);
    Assert.That(restored.PixelData, Has.Length.EqualTo(original.PixelData.Length));
    for (var i = 0; i < original.PixelData.Length; ++i)
      Assert.That(restored.PixelData[i], Is.EqualTo(original.PixelData[i]).Within(1e-6f));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grayscale_PreservesPixelData() {
    var pixelData = new float[4 * 3]; // 4x3, grayscale
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = i * 0.25f;

    var original = new PfmFile {
      Width = 4,
      Height = 3,
      ColorMode = PfmColorMode.Grayscale,
      Scale = 1.0f,
      IsLittleEndian = true,
      PixelData = pixelData
    };

    var bytes = PfmWriter.ToBytes(original);
    var restored = PfmReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.ColorMode, Is.EqualTo(PfmColorMode.Grayscale));
    Assert.That(restored.PixelData, Has.Length.EqualTo(original.PixelData.Length));
    for (var i = 0; i < original.PixelData.Length; ++i)
      Assert.That(restored.PixelData[i], Is.EqualTo(original.PixelData[i]).Within(1e-6f));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_FloatPrecision_PreservedExactly() {
    var pixelData = new[] { float.MinValue, float.MaxValue, float.Epsilon, 0f, -0f, 3.14159265f };

    var original = new PfmFile {
      Width = 2,
      Height = 1,
      ColorMode = PfmColorMode.Rgb,
      Scale = 1.0f,
      IsLittleEndian = true,
      PixelData = pixelData
    };

    var bytes = PfmWriter.ToBytes(original);
    var restored = PfmReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Has.Length.EqualTo(pixelData.Length));
    for (var i = 0; i < pixelData.Length; ++i)
      Assert.That(restored.PixelData[i], Is.EqualTo(pixelData[i]));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_BigEndian_PreservesPixelData() {
    var pixelData = new float[] { 1.0f, 0.5f, 0.25f, 0.75f, 0.125f, 0.875f };

    var original = new PfmFile {
      Width = 2,
      Height = 1,
      ColorMode = PfmColorMode.Rgb,
      Scale = 2.0f,
      IsLittleEndian = false,
      PixelData = pixelData
    };

    var bytes = PfmWriter.ToBytes(original);
    var restored = PfmReader.FromBytes(bytes);

    Assert.That(restored.IsLittleEndian, Is.False);
    Assert.That(restored.Scale, Is.EqualTo(2.0f));
    for (var i = 0; i < pixelData.Length; ++i)
      Assert.That(restored.PixelData[i], Is.EqualTo(pixelData[i]));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ScalePreserved() {
    var original = new PfmFile {
      Width = 1,
      Height = 1,
      ColorMode = PfmColorMode.Grayscale,
      Scale = 0.5f,
      IsLittleEndian = true,
      PixelData = new float[] { 42.0f }
    };

    var bytes = PfmWriter.ToBytes(original);
    var restored = PfmReader.FromBytes(bytes);

    Assert.That(restored.Scale, Is.EqualTo(0.5f));
  }
}
