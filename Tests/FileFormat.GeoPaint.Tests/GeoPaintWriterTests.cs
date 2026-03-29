using System;
using FileFormat.GeoPaint;

namespace FileFormat.GeoPaint.Tests;

[TestFixture]
public sealed class GeoPaintWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GeoPaintWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_AllZeros_ProducesCompressedOutput() {
    var file = new GeoPaintFile {
      Height = 1,
      PixelData = new byte[80]
    };

    var bytes = GeoPaintWriter.ToBytes(file);

    // Should be smaller than 80 raw bytes (compressed zero runs) + end markers
    Assert.That(bytes.Length, Is.LessThan(80 + 1));
    Assert.That(bytes.Length, Is.GreaterThan(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EndsWithEndMarkerPerScanline() {
    var file = new GeoPaintFile {
      Height = 1,
      PixelData = new byte[80]
    };

    var bytes = GeoPaintWriter.ToBytes(file);

    // Last byte of the single scanline should be the end marker 0xFF
    Assert.That(bytes[^1], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MultipleScanlines_EndsWithEndMarkers() {
    var file = new GeoPaintFile {
      Height = 3,
      PixelData = new byte[80 * 3]
    };

    var bytes = GeoPaintWriter.ToBytes(file);

    // Count 0xFF end markers -- should be at least 3 (one per scanline)
    var endCount = 0;
    foreach (var b in bytes)
      if (b == 0xFF)
        ++endCount;

    Assert.That(endCount, Is.GreaterThanOrEqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_AllOnes_ProducesCompressedOutput() {
    var pixelData = new byte[80];
    Array.Fill(pixelData, (byte)0xFF);
    var file = new GeoPaintFile {
      Height = 1,
      PixelData = pixelData
    };

    var bytes = GeoPaintWriter.ToBytes(file);

    // Repeat run of 0xFF for 64 bytes + repeat for 16 = much smaller than 80
    Assert.That(bytes.Length, Is.LessThan(80));
    Assert.That(bytes.Length, Is.GreaterThan(0));
  }
}
