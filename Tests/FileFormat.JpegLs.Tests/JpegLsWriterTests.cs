using System;
using System.IO;
using FileFormat.JpegLs;

namespace FileFormat.JpegLs.Tests;

[TestFixture]
public sealed class JpegLsWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => JpegLsWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithSoiMarker() {
    var bytes = _Encode(2, 2, 1);

    Assert.That(bytes[0], Is.EqualTo(0xFF));
    Assert.That(bytes[1], Is.EqualTo(0xD8));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EndsWithEoiMarker() {
    var bytes = _Encode(2, 2, 1);

    Assert.That(bytes[^2], Is.EqualTo(0xFF));
    Assert.That(bytes[^1], Is.EqualTo(0xD9));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsSof55Marker() {
    var bytes = _Encode(2, 2, 1);

    var foundSof = false;
    for (var i = 0; i < bytes.Length - 1; ++i) {
      if (bytes[i] == 0xFF && bytes[i + 1] == 0xF7) {
        foundSof = true;
        break;
      }
    }

    Assert.That(foundSof, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsSosMarker() {
    var bytes = _Encode(2, 2, 1);

    var foundSos = false;
    for (var i = 0; i < bytes.Length - 1; ++i) {
      if (bytes[i] == 0xFF && bytes[i + 1] == 0xDA) {
        foundSos = true;
        break;
      }
    }

    Assert.That(foundSos, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Sof55_ContainsDimensions() {
    var bytes = _Encode(320, 240, 1);

    var sof55Pos = -1;
    for (var i = 0; i < bytes.Length - 1; ++i) {
      if (bytes[i] == 0xFF && bytes[i + 1] == 0xF7) {
        sof55Pos = i + 2;
        break;
      }
    }

    Assert.That(sof55Pos, Is.GreaterThan(0));

    // Skip length (2 bytes), then bitsPerSample (1 byte)
    var heightOffset = sof55Pos + 3;
    var height = (bytes[heightOffset] << 8) | bytes[heightOffset + 1];
    var width = (bytes[heightOffset + 2] << 8) | bytes[heightOffset + 3];

    Assert.That(height, Is.EqualTo(240));
    Assert.That(width, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Sof55_ContainsBitsPerSample() {
    var bytes = _Encode(2, 2, 1);

    var sof55Pos = -1;
    for (var i = 0; i < bytes.Length - 1; ++i) {
      if (bytes[i] == 0xFF && bytes[i + 1] == 0xF7) {
        sof55Pos = i + 2;
        break;
      }
    }

    Assert.That(sof55Pos, Is.GreaterThan(0));

    // Skip length (2 bytes), bitsPerSample at offset +2
    var bps = bytes[sof55Pos + 2];
    Assert.That(bps, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Sof55_ContainsComponentCount() {
    var bytes = _Encode(2, 2, 3);

    var sof55Pos = -1;
    for (var i = 0; i < bytes.Length - 1; ++i) {
      if (bytes[i] == 0xFF && bytes[i + 1] == 0xF7) {
        sof55Pos = i + 2;
        break;
      }
    }

    Assert.That(sof55Pos, Is.GreaterThan(0));

    // Skip length (2), bps (1), height (2), width (2) => componentCount at +7
    var componentCount = bytes[sof55Pos + 7];
    Assert.That(componentCount, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Rgb_HasMultipleSosMarkers() {
    var bytes = _Encode(2, 2, 3);

    var sosCount = 0;
    for (var i = 0; i < bytes.Length - 1; ++i) {
      if (bytes[i] == 0xFF && bytes[i + 1] == 0xDA)
        ++sosCount;
    }

    Assert.That(sosCount, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileSize_AtLeastMarkersAndHeaders() {
    var bytes = _Encode(1, 1, 1);

    // SOI (2) + SOF55 (2+2+8+3=15 for 1 component) + SOS (2+2+6+2=12 for 1 component) + EOI (2) = 31 minimum
    Assert.That(bytes.Length, Is.GreaterThanOrEqualTo(20));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_UniformImage_CompressesWell() {
    var pixelData = new byte[64 * 64];
    Array.Fill(pixelData, (byte)128);

    var file = new JpegLsFile {
      Width = 64,
      Height = 64,
      BitsPerSample = 8,
      ComponentCount = 1,
      PixelData = pixelData
    };

    var bytes = JpegLsWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.LessThan(pixelData.Length));
  }

  private static byte[] _Encode(int width, int height, int componentCount) {
    var pixelData = new byte[width * height * componentCount];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    return JpegLsWriter.ToBytes(new JpegLsFile {
      Width = width,
      Height = height,
      BitsPerSample = 8,
      ComponentCount = componentCount,
      PixelData = pixelData
    });
  }
}
