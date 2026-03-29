using System;
using System.Text;
using FileFormat.Fits;

namespace FileFormat.Fits.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_UInt8() {
    var pixelData = new byte[4 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var original = new FitsFile {
      Width = 4,
      Height = 3,
      Bitpix = FitsBitpix.UInt8,
      PixelData = pixelData
    };

    var bytes = FitsWriter.ToBytes(original);
    var restored = FitsReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Bitpix, Is.EqualTo(FitsBitpix.UInt8));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Int16() {
    var width = 4;
    var height = 2;
    var bytesPerPixel = 2;
    var pixelData = new byte[width * height * bytesPerPixel];

    // Write big-endian Int16 values
    for (var i = 0; i < width * height; ++i) {
      var value = (short)(i * 1000 - 3000);
      pixelData[i * 2] = (byte)(value >> 8);
      pixelData[i * 2 + 1] = (byte)(value & 0xFF);
    }

    var original = new FitsFile {
      Width = width,
      Height = height,
      Bitpix = FitsBitpix.Int16,
      PixelData = pixelData
    };

    var bytes = FitsWriter.ToBytes(original);
    var restored = FitsReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Bitpix, Is.EqualTo(FitsBitpix.Int16));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Float32() {
    var width = 2;
    var height = 2;
    var bytesPerPixel = 4;
    var pixelData = new byte[width * height * bytesPerPixel];

    // Write big-endian Float32 values
    var values = new[] { 1.0f, -2.5f, 0.0f, 100.125f };
    for (var i = 0; i < values.Length; ++i) {
      var floatBytes = BitConverter.GetBytes(values[i]);
      if (BitConverter.IsLittleEndian) {
        pixelData[i * 4] = floatBytes[3];
        pixelData[i * 4 + 1] = floatBytes[2];
        pixelData[i * 4 + 2] = floatBytes[1];
        pixelData[i * 4 + 3] = floatBytes[0];
      } else {
        Array.Copy(floatBytes, 0, pixelData, i * 4, 4);
      }
    }

    var original = new FitsFile {
      Width = width,
      Height = height,
      Bitpix = FitsBitpix.Float32,
      PixelData = pixelData
    };

    var bytes = FitsWriter.ToBytes(original);
    var restored = FitsReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Bitpix, Is.EqualTo(FitsBitpix.Float32));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_CustomKeywordsPreserved() {
    var keywords = new[] {
      new FitsKeyword("OBJECT", "TestStar", "target name"),
      new FitsKeyword("EXPTIME", "30", "exposure time")
    };

    var original = new FitsFile {
      Width = 2,
      Height = 2,
      Bitpix = FitsBitpix.UInt8,
      Keywords = keywords,
      PixelData = new byte[4]
    };

    var bytes = FitsWriter.ToBytes(original);
    var restored = FitsReader.FromBytes(bytes);

    var objectKw = _FindKeyword(restored.Keywords, "OBJECT");
    Assert.That(objectKw, Is.Not.Null);
    Assert.That(objectKw!.Value, Does.Contain("TestStar"));
  }

  private static FitsKeyword? _FindKeyword(System.Collections.Generic.IReadOnlyList<FitsKeyword> keywords, string name) {
    for (var i = 0; i < keywords.Count; ++i)
      if (keywords[i].Name == name)
        return keywords[i];

    return null;
  }
}
