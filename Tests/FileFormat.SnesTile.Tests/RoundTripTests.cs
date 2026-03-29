using System;
using System.IO;
using FileFormat.SnesTile;
using FileFormat.Core;

namespace FileFormat.SnesTile.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  public void RoundTrip_AllZeros_Preserves() {
    var data = new byte[32 * 16];
    var file = SnesTileReader.FromBytes(data);
    var output = SnesTileWriter.ToBytes(file);
    Assert.That(output, Is.EqualTo(data));
  }

  [Test]
  public void RoundTrip_PixelValues_Preserved() {
    var data = new byte[32];
    // Set plane0 bit 7 of row 0 -> pixel (0,0) bit 0 = 1
    data[0] = 0x80;
    var file = SnesTileReader.FromBytes(data);
    Assert.That(file.PixelData[0], Is.EqualTo(1));
    var output = SnesTileWriter.ToBytes(file);
    Assert.That(output[0], Is.EqualTo(0x80));
  }

  [Test]
  public void RoundTrip_AllPlanes_Preserved() {
    // Use a full row of 16 tiles so writer output size matches input
    var data = new byte[32 * 16];
    // Set all 4 planes for pixel (0,0) of tile 0 -> value 15
    data[0] = 0x80;   // plane0
    data[1] = 0x80;   // plane1
    data[16] = 0x80;  // plane2
    data[17] = 0x80;  // plane3
    var file = SnesTileReader.FromBytes(data);
    Assert.That(file.PixelData[0], Is.EqualTo(15));
    var output = SnesTileWriter.ToBytes(file);
    Assert.That(output, Is.EqualTo(data));
  }

  [Test]
  public void RoundTrip_ViaRawImage_Preserved() {
    var data = new byte[32 * 16];
    data[0] = 0xFF;
    var file = SnesTileReader.FromBytes(data);
    var raw = SnesTileFile.ToRawImage(file);
    var file2 = SnesTileFile.FromRawImage(raw);
    var output = SnesTileWriter.ToBytes(file2);
    Assert.That(output, Is.EqualTo(data));
  }

  [Test]
  public void RoundTrip_ViaFile_Preserved() {
    var data = new byte[32 * 16];
    data[0] = 0xAA;
    var tempPath = Path.Combine(Path.GetTempPath(), $"snes_test_{Guid.NewGuid()}.sfc");
    try {
      File.WriteAllBytes(tempPath, data);
      var file = SnesTileReader.FromFile(new FileInfo(tempPath));
      var output = SnesTileWriter.ToBytes(file);
      Assert.That(output, Is.EqualTo(data));
    } finally {
      File.Delete(tempPath);
    }
  }
}
