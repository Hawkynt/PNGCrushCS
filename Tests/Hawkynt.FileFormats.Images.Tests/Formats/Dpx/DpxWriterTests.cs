using System;
using System.Buffers.Binary;
using FileFormat.Dpx;

namespace FileFormat.Dpx.Tests;

[TestFixture]
public sealed class DpxWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_BigEndian_StartsWithSdpxMagic() {
    var file = new DpxFile {
      Width = 4,
      Height = 2,
      BitsPerElement = 10,
      Descriptor = DpxDescriptor.Rgb,
      IsBigEndian = true,
      PixelData = new byte[4 * 2 * 4]
    };

    var bytes = DpxWriter.ToBytes(file);

    var magic = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(0));
    Assert.That(magic, Is.EqualTo(DpxHeader.MagicBigEndian));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LittleEndian_StartsWithXpdsMagic() {
    var file = new DpxFile {
      Width = 4,
      Height = 2,
      BitsPerElement = 10,
      Descriptor = DpxDescriptor.Rgb,
      IsBigEndian = false,
      PixelData = new byte[4 * 2 * 4]
    };

    var bytes = DpxWriter.ToBytes(file);

    var magic = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(0));
    Assert.That(magic, Is.EqualTo(DpxHeader.MagicLittleEndian));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DataOffset_Is2048() {
    var file = new DpxFile {
      Width = 2,
      Height = 2,
      BitsPerElement = 10,
      Descriptor = DpxDescriptor.Rgb,
      IsBigEndian = true,
      PixelData = new byte[2 * 2 * 4]
    };

    var bytes = DpxWriter.ToBytes(file);

    var dataOffset = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(4));
    Assert.That(dataOffset, Is.EqualTo(2048));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Dimensions_MatchInput() {
    var file = new DpxFile {
      Width = 320,
      Height = 240,
      BitsPerElement = 10,
      Descriptor = DpxDescriptor.Rgb,
      IsBigEndian = true,
      PixelData = new byte[320 * 240 * 4]
    };

    var bytes = DpxWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(772)), Is.EqualTo(320));
      Assert.That(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(776)), Is.EqualTo(240));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileSize_MatchesActualLength() {
    var pixelData = new byte[64];
    var file = new DpxFile {
      Width = 4,
      Height = 4,
      BitsPerElement = 10,
      Descriptor = DpxDescriptor.Rgb,
      IsBigEndian = true,
      PixelData = pixelData
    };

    var bytes = DpxWriter.ToBytes(file);

    var fileSizeField = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16));
    Assert.That(fileSizeField, Is.EqualTo(bytes.Length));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Descriptor_StoredInHeader() {
    var file = new DpxFile {
      Width = 2,
      Height = 2,
      BitsPerElement = 10,
      Descriptor = DpxDescriptor.Rgba,
      IsBigEndian = true,
      PixelData = new byte[2 * 2 * 4]
    };

    var bytes = DpxWriter.ToBytes(file);

    Assert.That(bytes[800], Is.EqualTo((byte)DpxDescriptor.Rgba));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BitsPerElement_StoredInHeader() {
    var file = new DpxFile {
      Width = 2,
      Height = 2,
      BitsPerElement = 16,
      Descriptor = DpxDescriptor.Rgb,
      IsBigEndian = true,
      PixelData = new byte[2 * 2 * 6]
    };

    var bytes = DpxWriter.ToBytes(file);

    Assert.That(bytes[803], Is.EqualTo(16));
  }
}
