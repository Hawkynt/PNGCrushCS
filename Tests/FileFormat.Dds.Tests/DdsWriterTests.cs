using System;
using System.Buffers.Binary;
using FileFormat.Dds;

namespace FileFormat.Dds.Tests;

[TestFixture]
public sealed class DdsWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidRgba_StartsWithDdsMagic() {
    var file = new DdsFile {
      Width = 4,
      Height = 4,
      MipMapCount = 1,
      Format = DdsFormat.Rgba,
      Surfaces = [new DdsSurface { Width = 4, Height = 4, MipLevel = 0, Data = new byte[4 * 4 * 4] }]
    };

    var bytes = DdsWriter.ToBytes(file);

    var magic = BinaryPrimitives.ReadInt32LittleEndian(bytes);
    Assert.That(magic, Is.EqualTo(0x20534444));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderSize_Is124() {
    var file = new DdsFile {
      Width = 2,
      Height = 2,
      MipMapCount = 1,
      Format = DdsFormat.Rgba,
      Surfaces = [new DdsSurface { Width = 2, Height = 2, MipLevel = 0, Data = new byte[2 * 2 * 4] }]
    };

    var bytes = DdsWriter.ToBytes(file);

    var headerSize = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4));
    Assert.That(headerSize, Is.EqualTo(124));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelFormatSize_Is32() {
    var file = new DdsFile {
      Width = 2,
      Height = 2,
      MipMapCount = 1,
      Format = DdsFormat.Rgba,
      Surfaces = [new DdsSurface { Width = 2, Height = 2, MipLevel = 0, Data = new byte[2 * 2 * 4] }]
    };

    var bytes = DdsWriter.ToBytes(file);

    // PixelFormat starts at offset 4 (magic) + 72 (header offset) = 76
    var pfSize = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(76));
    Assert.That(pfSize, Is.EqualTo(32));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Dxt1_SetsFourCC() {
    var file = new DdsFile {
      Width = 4,
      Height = 4,
      MipMapCount = 1,
      Format = DdsFormat.Dxt1,
      Surfaces = [new DdsSurface { Width = 4, Height = 4, MipLevel = 0, Data = new byte[8] }]
    };

    var bytes = DdsWriter.ToBytes(file);

    // FourCC at offset 4 + 72 + 8 = 84
    var fourCC = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(84));
    var expected = 'D' | ('X' << 8) | ('T' << 16) | ('1' << 24);
    Assert.That(fourCC, Is.EqualTo(expected));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Dx10_IncludesExtendedHeader() {
    var file = new DdsFile {
      Width = 4,
      Height = 4,
      MipMapCount = 1,
      Format = DdsFormat.Dx10,
      HasDx10Header = true,
      Surfaces = [new DdsSurface { Width = 4, Height = 4, MipLevel = 0, Data = new byte[4 * 4 * 4] }]
    };

    var bytes = DdsWriter.ToBytes(file);

    // File should be: 4 (magic) + 124 (header) + 20 (dx10) + surface data
    Assert.That(bytes.Length, Is.EqualTo(4 + 124 + 20 + 4 * 4 * 4));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DimensionsPreserved() {
    var file = new DdsFile {
      Width = 16,
      Height = 8,
      MipMapCount = 1,
      Format = DdsFormat.Rgba,
      Surfaces = [new DdsSurface { Width = 16, Height = 8, MipLevel = 0, Data = new byte[16 * 8 * 4] }]
    };

    var bytes = DdsWriter.ToBytes(file);

    var height = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12));
    var width = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(16));
    Assert.That(height, Is.EqualTo(8));
    Assert.That(width, Is.EqualTo(16));
  }
}
