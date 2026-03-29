using System;
using System.Buffers.Binary;
using System.Text;
using FileFormat.Wal;

namespace FileFormat.Wal.Tests;

[TestFixture]
public sealed class WalWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => WalWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderAt100Bytes() {
    var file = new WalFile {
      Name = "test",
      Width = 4,
      Height = 4,
      PixelData = new byte[16]
    };

    var bytes = WalWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.GreaterThanOrEqualTo(WalHeader.StructSize));
    Assert.That(bytes.Length, Is.EqualTo(WalHeader.StructSize + 16));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_NamePreserved() {
    var file = new WalFile {
      Name = "e1u1/wall03",
      Width = 4,
      Height = 4,
      PixelData = new byte[16]
    };

    var bytes = WalWriter.ToBytes(file);

    var name = Encoding.ASCII.GetString(bytes, 0, 32).TrimEnd('\0');
    Assert.That(name, Is.EqualTo("e1u1/wall03"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_NextFrameNamePreserved() {
    var file = new WalFile {
      Name = "wall01",
      Width = 4,
      Height = 4,
      NextFrameName = "wall02",
      PixelData = new byte[16]
    };

    var bytes = WalWriter.ToBytes(file);

    var nextName = Encoding.ASCII.GetString(bytes, 56, 32).TrimEnd('\0');
    Assert.That(nextName, Is.EqualTo("wall02"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MipOffsetsCorrect_WithMips() {
    var file = new WalFile {
      Name = "wall",
      Width = 8,
      Height = 8,
      PixelData = new byte[64],
      MipMaps = [new byte[16], new byte[4], new byte[1]]
    };

    var bytes = WalWriter.ToBytes(file);

    var mip0 = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(40));
    var mip1 = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(44));
    var mip2 = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(48));
    var mip3 = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(52));

    Assert.That(mip0, Is.EqualTo(100u));        // header size
    Assert.That(mip1, Is.EqualTo(100u + 64u));  // + 8*8
    Assert.That(mip2, Is.EqualTo(100u + 64u + 16u)); // + 4*4
    Assert.That(mip3, Is.EqualTo(100u + 64u + 16u + 4u)); // + 2*2
  }
}
