using System;
using FileFormat.Dpx;

namespace FileFormat.Dpx.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_BigEndian_PreservesDimensions() {
    var pixelData = new byte[8 * 4 * 4]; // 8x4, one 32-bit word per pixel
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new DpxFile {
      Width = 8,
      Height = 4,
      BitsPerElement = 10,
      Descriptor = DpxDescriptor.Rgb,
      Packing = DpxPacking.FilledA,
      Transfer = DpxTransfer.Linear,
      IsBigEndian = true,
      PixelData = pixelData
    };

    var bytes = DpxWriter.ToBytes(original);
    var restored = DpxReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.BitsPerElement, Is.EqualTo(original.BitsPerElement));
      Assert.That(restored.IsBigEndian, Is.True);
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LittleEndian_PreservesDimensions() {
    var pixelData = new byte[4 * 2 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var original = new DpxFile {
      Width = 4,
      Height = 2,
      BitsPerElement = 10,
      Descriptor = DpxDescriptor.Rgb,
      IsBigEndian = false,
      PixelData = pixelData
    };

    var bytes = DpxWriter.ToBytes(original);
    var restored = DpxReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.BitsPerElement, Is.EqualTo(original.BitsPerElement));
      Assert.That(restored.IsBigEndian, Is.False);
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PreservesPixelData() {
    var pixelData = new byte[4 * 2 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new DpxFile {
      Width = 4,
      Height = 2,
      BitsPerElement = 10,
      Descriptor = DpxDescriptor.Rgb,
      IsBigEndian = true,
      PixelData = pixelData
    };

    var bytes = DpxWriter.ToBytes(original);
    var restored = DpxReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PreservesDescriptor() {
    var original = new DpxFile {
      Width = 2,
      Height = 2,
      BitsPerElement = 10,
      Descriptor = DpxDescriptor.Rgba,
      Transfer = DpxTransfer.Logarithmic,
      Packing = DpxPacking.FilledB,
      IsBigEndian = true,
      PixelData = new byte[2 * 2 * 4]
    };

    var bytes = DpxWriter.ToBytes(original);
    var restored = DpxReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Descriptor, Is.EqualTo(original.Descriptor));
      Assert.That(restored.Transfer, Is.EqualTo(original.Transfer));
      Assert.That(restored.Packing, Is.EqualTo(original.Packing));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargeImage() {
    var width = 1920;
    var height = 1080;
    var pixelData = new byte[width * height * 4];
    var rng = new Random(42);
    rng.NextBytes(pixelData);

    var original = new DpxFile {
      Width = width,
      Height = height,
      BitsPerElement = 10,
      Descriptor = DpxDescriptor.Rgb,
      IsBigEndian = true,
      PixelData = pixelData
    };

    var bytes = DpxWriter.ToBytes(original);
    var restored = DpxReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(width));
      Assert.That(restored.Height, Is.EqualTo(height));
      Assert.That(restored.PixelData, Is.EqualTo(pixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_EmptyPixelData() {
    var original = new DpxFile {
      Width = 0,
      Height = 0,
      BitsPerElement = 10,
      Descriptor = DpxDescriptor.Rgb,
      IsBigEndian = true,
      PixelData = []
    };

    var bytes = DpxWriter.ToBytes(original);
    var restored = DpxReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(0));
      Assert.That(restored.Height, Is.EqualTo(0));
      Assert.That(restored.PixelData, Is.Empty);
    });
  }
}
