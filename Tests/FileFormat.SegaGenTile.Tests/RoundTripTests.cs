using System;
using System.IO;
using FileFormat.SegaGenTile;
using FileFormat.Core;

namespace FileFormat.SegaGenTile.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  public void RoundTrip_AllZeros_Preserves() {
    var data = new byte[32 * 16];
    var file = SegaGenTileReader.FromBytes(data);
    var output = SegaGenTileWriter.ToBytes(file);
    Assert.That(output, Is.EqualTo(data));
  }

  [Test]
  public void RoundTrip_KnownNibbles_Preserved() {
    var data = new byte[32];
    data[0] = 0xF0;
    var file = SegaGenTileReader.FromBytes(data);
    Assert.That(file.PixelData[0], Is.EqualTo(15));
    Assert.That(file.PixelData[1], Is.EqualTo(0));
    var output = SegaGenTileWriter.ToBytes(file);
    Assert.That(output[0], Is.EqualTo(0xF0));
  }

  [Test]
  public void RoundTrip_ViaRawImage_Preserved() {
    var data = new byte[32 * 16];
    data[0] = 0x55;
    var file = SegaGenTileReader.FromBytes(data);
    var raw = SegaGenTileFile.ToRawImage(file);
    var file2 = SegaGenTileFile.FromRawImage(raw);
    var output = SegaGenTileWriter.ToBytes(file2);
    Assert.That(output, Is.EqualTo(data));
  }

  [Test]
  public void RoundTrip_ViaFile_Preserved() {
    var data = new byte[32 * 16];
    data[0] = 0xAB;
    var tempPath = Path.Combine(Path.GetTempPath(), $"gen_test_{Guid.NewGuid()}.gen");
    try {
      File.WriteAllBytes(tempPath, data);
      var file = SegaGenTileReader.FromFile(new FileInfo(tempPath));
      var output = SegaGenTileWriter.ToBytes(file);
      Assert.That(output, Is.EqualTo(data));
    } finally {
      File.Delete(tempPath);
    }
  }

  [Test]
  public void RoundTrip_AllOnes_Preserved() {
    var data = new byte[32 * 16];
    for (var i = 0; i < data.Length; ++i)
      data[i] = 0xFF;
    var file = SegaGenTileReader.FromBytes(data);
    var output = SegaGenTileWriter.ToBytes(file);
    Assert.That(output, Is.EqualTo(data));
  }

  [Test]
  public void RoundTrip_Gradient_Preserved() {
    var data = new byte[32 * 16];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i & 0xFF);
    var file = SegaGenTileReader.FromBytes(data);
    var output = SegaGenTileWriter.ToBytes(file);
    Assert.That(output, Is.EqualTo(data));
  }

  [Test]
  public void RoundTrip_ViaRawImage_PalettePreserved() {
    var data = new byte[32 * 16];
    var file = SegaGenTileReader.FromBytes(data);
    var raw = SegaGenTileFile.ToRawImage(file);
    Assert.Multiple(() => {
      Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
      Assert.That(raw.PaletteCount, Is.EqualTo(16));
      Assert.That(raw.Palette, Is.Not.Null);
      Assert.That(raw.Palette!.Length, Is.EqualTo(48));
    });
  }

  [Test]
  public void RoundTrip_MultipleTileRows_Preserved() {
    var data = new byte[32 * 32];
    data[0] = 0x12;
    data[32 * 16] = 0x34;
    var file = SegaGenTileReader.FromBytes(data);
    var output = SegaGenTileWriter.ToBytes(file);
    Assert.That(output, Is.EqualTo(data));
  }
}
