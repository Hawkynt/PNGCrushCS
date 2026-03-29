using System;
using System.IO;
using FileFormat.GeoPaint;

namespace FileFormat.GeoPaint.Tests;

[TestFixture]
public sealed class GeoPaintReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GeoPaintReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GeoPaintReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".geo"));
    Assert.Throws<FileNotFoundException>(() => GeoPaintReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GeoPaintReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Empty_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => GeoPaintReader.FromBytes([]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_SingleEndMarker_ThrowsInvalidDataException() {
    // A single 0xFF end marker means zero scanlines were decompressed
    Assert.Throws<InvalidDataException>(() => GeoPaintReader.FromBytes([0xFF]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_OneScanline_Parsed() {
    // Create one scanline: 80 zero bytes via a zero-run encoding
    // 0xC0 + 63 - 1 = 0xFE => 63 zeros, then 0xC0 + 17 - 1 = 0xD0 => 17 zeros = total 80 zeros
    var data = new byte[] { 0xFE, 0xD0, 0xFF };

    var result = GeoPaintReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(640));
    Assert.That(result.Height, Is.EqualTo(1));
    Assert.That(result.PixelData.Length, Is.EqualTo(80));
    Assert.That(result.PixelData, Is.All.EqualTo((byte)0));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_LiteralScanline_Parsed() {
    // Encode 80 bytes as literals: two chunks of 64 (0x3F) and 16 (0x0F)
    // Layout: 1 code + 64 data + 1 code + 16 data + 1 end marker = 83 bytes
    var data = new byte[1 + 64 + 1 + 16 + 1];
    var offset = 0;

    // First literal chunk: code 0x3F = 64 bytes
    data[offset++] = 0x3F;
    for (var i = 0; i < 64; ++i)
      data[offset++] = (byte)(i + 1);

    // Second literal chunk: code 0x0F = 16 bytes
    data[offset++] = 0x0F;
    for (var i = 0; i < 16; ++i)
      data[offset++] = (byte)(65 + i);

    // End marker
    data[offset] = 0xFF;

    var result = GeoPaintReader.FromBytes(data);

    Assert.That(result.Height, Is.EqualTo(1));
    Assert.That(result.PixelData[0], Is.EqualTo(1));
    Assert.That(result.PixelData[63], Is.EqualTo(64));
    Assert.That(result.PixelData[64], Is.EqualTo(65));
    Assert.That(result.PixelData[79], Is.EqualTo(80));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_OneScanline_Parsed() {
    var data = new byte[] { 0xFE, 0xD0, 0xFF };
    using var ms = new MemoryStream(data);
    var result = GeoPaintReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(640));
    Assert.That(result.Height, Is.EqualTo(1));
  }
}
