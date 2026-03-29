using System;
using System.Buffers.Binary;
using FileFormat.Rla;

namespace FileFormat.Rla.Tests;

[TestFixture]
public sealed class RlaWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => RlaWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderIs740Bytes() {
    var file = new RlaFile {
      Width = 2,
      Height = 2,
      NumChannels = 3,
      NumMatte = 0,
      NumBits = 8,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = RlaWriter.ToBytes(file);

    // At minimum the file must contain 740-byte header + offset table
    Assert.That(bytes.Length, Is.GreaterThanOrEqualTo(RlaHeader.StructSize + 2 * 4));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsOffsetTable() {
    var file = new RlaFile {
      Width = 4,
      Height = 3,
      NumChannels = 3,
      NumMatte = 0,
      NumBits = 8,
      PixelData = new byte[4 * 3 * 3]
    };

    var bytes = RlaWriter.ToBytes(file);

    // Each offset in the table should be >= start of data area
    var offsetTableStart = RlaHeader.StructSize;
    var dataStart = offsetTableStart + 3 * 4;
    for (var i = 0; i < 3; ++i) {
      var offset = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offsetTableStart + i * 4));
      Assert.That(offset, Is.GreaterThanOrEqualTo(dataStart));
    }
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ActiveWindowMatchesDimensions() {
    var file = new RlaFile {
      Width = 10,
      Height = 20,
      NumChannels = 3,
      NumMatte = 0,
      NumBits = 8,
      PixelData = new byte[10 * 20 * 3]
    };

    var bytes = RlaWriter.ToBytes(file);

    var activeRight = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(10));
    var activeTop = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(14));
    Assert.That(activeRight, Is.EqualTo(9));
    Assert.That(activeTop, Is.EqualTo(19));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ChannelCountWrittenToHeader() {
    var file = new RlaFile {
      Width = 2,
      Height = 2,
      NumChannels = 3,
      NumMatte = 1,
      NumBits = 8,
      PixelData = new byte[2 * 2 * 4]
    };

    var bytes = RlaWriter.ToBytes(file);

    var numChannels = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(20));
    var numMatte = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(22));
    Assert.That(numChannels, Is.EqualTo(3));
    Assert.That(numMatte, Is.EqualTo(1));
  }
}
