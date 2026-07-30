using System;
using FileFormat.Tim;

namespace FileFormat.Tim.Tests;

[TestFixture]
public sealed class TimWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithMagic() {
    var file = new TimFile {
      Width = 4,
      Height = 2,
      Bpp = TimBpp.Bpp16,
      HasClut = false,
      PixelData = new byte[4 * 2 * 2]
    };

    var bytes = TimWriter.ToBytes(file);

    Assert.That(BitConverter.ToUInt32(bytes, 0), Is.EqualTo(0x10u));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_16bpp_FlagsCorrect() {
    var file = new TimFile {
      Width = 4,
      Height = 2,
      Bpp = TimBpp.Bpp16,
      HasClut = false,
      PixelData = new byte[4 * 2 * 2]
    };

    var bytes = TimWriter.ToBytes(file);
    var flags = BitConverter.ToUInt32(bytes, 4);

    Assert.That(flags & 0x03, Is.EqualTo((uint)TimBpp.Bpp16));
    Assert.That(flags & 0x08, Is.EqualTo(0u));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_8bppWithClut_FlagsHasClutBit() {
    var file = new TimFile {
      Width = 4,
      Height = 2,
      Bpp = TimBpp.Bpp8,
      HasClut = true,
      ClutData = new byte[256 * 2],
      ClutWidth = 256,
      ClutHeight = 1,
      PixelData = new byte[4 / 2 * 2 * 2]
    };

    var bytes = TimWriter.ToBytes(file);
    var flags = BitConverter.ToUInt32(bytes, 4);

    Assert.That(flags & 0x08, Is.EqualTo(8u));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WithClut_ClutBlockWritten() {
    var clutData = new byte[16 * 2];
    for (var i = 0; i < clutData.Length; ++i)
      clutData[i] = (byte)(i & 0xFF);

    var file = new TimFile {
      Width = 8,
      Height = 2,
      Bpp = TimBpp.Bpp4,
      HasClut = true,
      ClutData = clutData,
      ClutX = 10,
      ClutY = 20,
      ClutWidth = 16,
      ClutHeight = 1,
      PixelData = new byte[8 / 4 * 2 * 2]
    };

    var bytes = TimWriter.ToBytes(file);

    var clutBlockSize = BitConverter.ToUInt32(bytes, TimHeader.StructSize);
    Assert.That(clutBlockSize, Is.EqualTo((uint)(TimBlockHeader.StructSize + clutData.Length)));
    Assert.That(BitConverter.ToUInt16(bytes, TimHeader.StructSize + 4), Is.EqualTo(10));
    Assert.That(BitConverter.ToUInt16(bytes, TimHeader.StructSize + 6), Is.EqualTo(20));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ImageBlockWritten() {
    var pixelData = new byte[4 * 2 * 2];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var file = new TimFile {
      Width = 4,
      Height = 2,
      Bpp = TimBpp.Bpp16,
      HasClut = false,
      ImageX = 5,
      ImageY = 15,
      PixelData = pixelData
    };

    var bytes = TimWriter.ToBytes(file);

    var imageBlockOffset = TimHeader.StructSize;
    var imageBlockSize = BitConverter.ToUInt32(bytes, imageBlockOffset);
    Assert.That(imageBlockSize, Is.EqualTo((uint)(TimBlockHeader.StructSize + pixelData.Length)));
    Assert.That(BitConverter.ToUInt16(bytes, imageBlockOffset + 4), Is.EqualTo(5));
    Assert.That(BitConverter.ToUInt16(bytes, imageBlockOffset + 6), Is.EqualTo(15));
  }
}
