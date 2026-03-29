using System;
using FileFormat.Doodle;

namespace FileFormat.Doodle.Tests;

[TestFixture]
public sealed class DoodleWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_NullFile_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DoodleWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_OutputIsExactly9218Bytes() {
    var file = new DoodleFile {
      LoadAddress = 0x5C00,
      BitmapData = new byte[8000],
      ScreenRam = new byte[1000],
    };

    var bytes = DoodleWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(DoodleFile.ExpectedFileSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LoadAddress_WrittenAsLittleEndian() {
    var file = new DoodleFile {
      LoadAddress = 0x5C00,
      BitmapData = new byte[8000],
      ScreenRam = new byte[1000],
    };

    var bytes = DoodleWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x00));
    Assert.That(bytes[1], Is.EqualTo(0x5C));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BitmapDataOffset_StartsAtByte2() {
    var file = new DoodleFile {
      LoadAddress = 0x5C00,
      BitmapData = new byte[8000],
      ScreenRam = new byte[1000],
    };
    file.BitmapData[0] = 0xAA;
    file.BitmapData[7999] = 0xBB;

    var bytes = DoodleWriter.ToBytes(file);

    Assert.That(bytes[2], Is.EqualTo(0xAA));
    Assert.That(bytes[8001], Is.EqualTo(0xBB));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ScreenRamOffset_StartsAtByte8002() {
    var file = new DoodleFile {
      LoadAddress = 0x5C00,
      BitmapData = new byte[8000],
      ScreenRam = new byte[1000],
    };
    file.ScreenRam[0] = 0xCC;
    file.ScreenRam[999] = 0xDD;

    var bytes = DoodleWriter.ToBytes(file);

    Assert.That(bytes[8002], Is.EqualTo(0xCC));
    Assert.That(bytes[9001], Is.EqualTo(0xDD));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PaddingIsZeros() {
    var file = new DoodleFile {
      LoadAddress = 0x5C00,
      BitmapData = new byte[8000],
      ScreenRam = new byte[1000],
    };

    var bytes = DoodleWriter.ToBytes(file);

    for (var i = 9002; i < 9218; ++i)
      Assert.That(bytes[i], Is.EqualTo(0), $"Padding byte at offset {i} should be zero");
  }
}
