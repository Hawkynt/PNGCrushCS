using System;
using FileFormat.HiresBitmap;

namespace FileFormat.HiresBitmap.Tests;

[TestFixture]
public sealed class HiresBitmapWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_NullFile_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HiresBitmapWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MinimalFile_ReturnsMinSize() {
    var file = new HiresBitmapFile {
      BitmapData = new byte[HiresBitmapFile.BitmapDataSize],
      ScreenData = new byte[HiresBitmapFile.ScreenDataSize],
      TrailingData = [],
    };

    var bytes = HiresBitmapWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(HiresBitmapFile.MinFileSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LoadAddress_LittleEndian() {
    var file = new HiresBitmapFile {
      LoadAddress = 0x1234,
      BitmapData = new byte[HiresBitmapFile.BitmapDataSize],
      ScreenData = new byte[HiresBitmapFile.ScreenDataSize],
    };

    var bytes = HiresBitmapWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x34));
    Assert.That(bytes[1], Is.EqualTo(0x12));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BitmapDataAtCorrectOffset() {
    var bitmapData = new byte[HiresBitmapFile.BitmapDataSize];
    bitmapData[0] = 0xDE;
    bitmapData[7999] = 0xAD;

    var file = new HiresBitmapFile {
      BitmapData = bitmapData,
      ScreenData = new byte[HiresBitmapFile.ScreenDataSize],
    };

    var bytes = HiresBitmapWriter.ToBytes(file);

    Assert.That(bytes[2], Is.EqualTo(0xDE));
    Assert.That(bytes[2 + 7999], Is.EqualTo(0xAD));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ScreenDataAtCorrectOffset() {
    var screenData = new byte[HiresBitmapFile.ScreenDataSize];
    screenData[0] = 0xBE;
    screenData[999] = 0xEF;

    var file = new HiresBitmapFile {
      BitmapData = new byte[HiresBitmapFile.BitmapDataSize],
      ScreenData = screenData,
    };

    var bytes = HiresBitmapWriter.ToBytes(file);

    Assert.That(bytes[8002], Is.EqualTo(0xBE));
    Assert.That(bytes[8002 + 999], Is.EqualTo(0xEF));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TrailingDataAppended() {
    var trailing = new byte[] { 0x01, 0x02, 0x03 };
    var file = new HiresBitmapFile {
      BitmapData = new byte[HiresBitmapFile.BitmapDataSize],
      ScreenData = new byte[HiresBitmapFile.ScreenDataSize],
      TrailingData = trailing,
    };

    var bytes = HiresBitmapWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(HiresBitmapFile.MinFileSize + 3));
    Assert.That(bytes[9002], Is.EqualTo(0x01));
    Assert.That(bytes[9003], Is.EqualTo(0x02));
    Assert.That(bytes[9004], Is.EqualTo(0x03));
  }
}
