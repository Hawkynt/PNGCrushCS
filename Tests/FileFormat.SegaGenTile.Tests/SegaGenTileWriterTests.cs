using System;
using FileFormat.SegaGenTile;

namespace FileFormat.SegaGenTile.Tests;

[TestFixture]
public sealed class SegaGenTileWriterTests {

  [Test]
  public void ToBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SegaGenTileWriter.ToBytes(null!));

  [Test]
  public void ToBytes_OneTileRow_OutputSize() {
    var file = new SegaGenTileFile {
      Width = 128,
      Height = 8,
      PixelData = new byte[128 * 8],
    };
    var result = SegaGenTileWriter.ToBytes(file);
    Assert.That(result.Length, Is.EqualTo(16 * 32));
  }

  [Test]
  public void ToBytes_PacksNibbles_HighBitIsLeftPixel() {
    var pixels = new byte[128 * 8];
    pixels[0] = 0x0A;
    pixels[1] = 0x05;
    var file = new SegaGenTileFile {
      Width = 128,
      Height = 8,
      PixelData = pixels,
    };
    var result = SegaGenTileWriter.ToBytes(file);
    Assert.That(result[0], Is.EqualTo(0xA5));
  }

  [Test]
  public void ToBytes_MasksToLower4Bits() {
    var pixels = new byte[128 * 8];
    pixels[0] = 0xFF;
    pixels[1] = 0xF3;
    var file = new SegaGenTileFile {
      Width = 128,
      Height = 8,
      PixelData = pixels,
    };
    var result = SegaGenTileWriter.ToBytes(file);
    Assert.That(result[0], Is.EqualTo(0xF3));
  }

  [Test]
  public void ToBytes_SecondRowOfTile_CorrectOffset() {
    var pixels = new byte[128 * 8];
    var py = 1;
    pixels[py * 128] = 0x0B;
    pixels[py * 128 + 1] = 0x0C;
    var file = new SegaGenTileFile {
      Width = 128,
      Height = 8,
      PixelData = pixels,
    };
    var result = SegaGenTileWriter.ToBytes(file);
    Assert.That(result[4], Is.EqualTo(0xBC));
  }

  [Test]
  public void ToBytes_TwoTileRows_CorrectTotalSize() {
    var file = new SegaGenTileFile {
      Width = 128,
      Height = 16,
      PixelData = new byte[128 * 16],
    };
    var result = SegaGenTileWriter.ToBytes(file);
    Assert.That(result.Length, Is.EqualTo(32 * 32));
  }
}
