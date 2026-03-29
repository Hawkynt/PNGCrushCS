using System;
using FileFormat.Jbig2;

namespace FileFormat.Jbig2.Tests;

[TestFixture]
public sealed class Jbig2WriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Jbig2Writer.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsFileMagic() {
    var file = new Jbig2File {
      Width = 8,
      Height = 1,
      PixelData = [0x00],
    };

    var bytes = Jbig2Writer.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x97));
    Assert.That(bytes[1], Is.EqualTo(0x4A));
    Assert.That(bytes[2], Is.EqualTo(0x42));
    Assert.That(bytes[3], Is.EqualTo(0x32));
    Assert.That(bytes[4], Is.EqualTo(0x0D));
    Assert.That(bytes[5], Is.EqualTo(0x0A));
    Assert.That(bytes[6], Is.EqualTo(0x1A));
    Assert.That(bytes[7], Is.EqualTo(0x0A));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsPageInfoSegment() {
    var file = new Jbig2File {
      Width = 8,
      Height = 1,
      PixelData = [0x00],
    };

    var bytes = Jbig2Writer.ToBytes(file);

    // After file header (8 magic + 1 flags + 4 page count = 13), first segment starts
    // Segment 0: 4 bytes number + 1 flags + 1 referred + 1 page assoc + 4 data length = 11
    // Flags byte at offset 17 should be PageInformation (48)
    Assert.That(bytes[17], Is.EqualTo((byte)Jbig2SegmentType.PageInformation));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsGenericRegionSegment() {
    var file = new Jbig2File {
      Width = 8,
      Height = 1,
      PixelData = [0x00],
    };

    var bytes = Jbig2Writer.ToBytes(file);

    // Find ImmediateLosslessGenericRegion (39) in the output
    var found = false;
    for (var i = 13; i < bytes.Length - 6; ++i)
      if (bytes[i + 4] == (byte)Jbig2SegmentType.ImmediateLosslessGenericRegion) {
        found = true;
        break;
      }

    Assert.That(found, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsEndOfFileSegment() {
    var file = new Jbig2File {
      Width = 8,
      Height = 1,
      PixelData = [0x00],
    };

    var bytes = Jbig2Writer.ToBytes(file);

    // Find EndOfFile (51) segment type in the output
    var found = false;
    for (var i = 13; i < bytes.Length - 6; ++i)
      if (bytes[i + 4] == (byte)Jbig2SegmentType.EndOfFile) {
        found = true;
        break;
      }

    Assert.That(found, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_SequentialFlagSet() {
    var file = new Jbig2File {
      Width = 8,
      Height = 1,
      PixelData = [0x00],
    };

    var bytes = Jbig2Writer.ToBytes(file);

    // Flags byte at offset 8: bit 0 should be 1 (sequential)
    Assert.That(bytes[8] & 0x01, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PageCountIsOne() {
    var file = new Jbig2File {
      Width = 8,
      Height = 1,
      PixelData = [0x00],
    };

    var bytes = Jbig2Writer.ToBytes(file);

    // Page count at offset 9-12 (4 bytes BE)
    var pageCount = (bytes[9] << 24) | (bytes[10] << 16) | (bytes[11] << 8) | bytes[12];
    Assert.That(pageCount, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_OutputLargerThanMagic() {
    var file = new Jbig2File {
      Width = 8,
      Height = 1,
      PixelData = [0xFF],
    };

    var bytes = Jbig2Writer.ToBytes(file);

    // Must have at least magic + flags + page count + segments
    Assert.That(bytes.Length, Is.GreaterThan(13));
  }
}
