using System;
using System.Buffers.Binary;
using FileFormat.Dcx;
using FileFormat.Pcx;

namespace FileFormat.Dcx.Tests;

[TestFixture]
public sealed class DcxWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithMagic() {
    var file = new DcxFile {
      Pages = [_CreateSimplePcxFile()]
    };

    var bytes = DcxWriter.ToBytes(file);

    var magic = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    Assert.That(magic, Is.EqualTo(0x3ADE68B1u));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PageTableWritten() {
    var file = new DcxFile {
      Pages = [_CreateSimplePcxFile(), _CreateSimplePcxFile()]
    };

    var bytes = DcxWriter.ToBytes(file);

    // Two pages: header = 4 + 3*4 = 16 bytes
    var offset1 = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4));
    var offset2 = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8));
    var terminator = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12));

    Assert.That(offset1, Is.EqualTo(16u), "First page offset should be right after header");
    Assert.That(offset2, Is.GreaterThan(offset1), "Second page offset must follow first page");
    Assert.That(terminator, Is.EqualTo(0u), "Page table must be zero-terminated");
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PageCountCorrect() {
    var file = new DcxFile {
      Pages = [_CreateSimplePcxFile(), _CreateSimplePcxFile(), _CreateSimplePcxFile()]
    };

    var bytes = DcxWriter.ToBytes(file);

    // Count non-zero offsets after magic
    var count = 0;
    var pos = 4;
    while (pos + 4 <= bytes.Length) {
      var offset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(pos));
      if (offset == 0)
        break;
      ++count;
      pos += 4;
    }

    Assert.That(count, Is.EqualTo(3));
  }

  private static PcxFile _CreateSimplePcxFile() {
    var pixelData = new byte[4 * 4 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7);

    return new PcxFile {
      Width = 4,
      Height = 4,
      BitsPerPixel = 8,
      PixelData = pixelData,
      ColorMode = PcxColorMode.Rgb24,
      PlaneConfig = PcxPlaneConfig.SeparatePlanes
    };
  }
}
