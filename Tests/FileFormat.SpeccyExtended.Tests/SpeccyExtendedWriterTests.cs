using System;
using FileFormat.SpeccyExtended;

namespace FileFormat.SpeccyExtended.Tests;

[TestFixture]
public sealed class SpeccyExtendedWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SpeccyExtendedWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_OutputIsExactFileSize() {
    var file = new SpeccyExtendedFile {
      BitmapData = new byte[6144],
      AttributeData = new byte[768],
      ExtendedAttributeData = new byte[768],
    };

    var bytes = SpeccyExtendedWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(SpeccyExtendedReader.FileSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MagicBytesWritten() {
    var file = new SpeccyExtendedFile {
      BitmapData = new byte[6144],
      AttributeData = new byte[768],
      ExtendedAttributeData = new byte[768],
    };

    var bytes = SpeccyExtendedWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x53)); // 'S'
    Assert.That(bytes[1], Is.EqualTo(0x58)); // 'X'
    Assert.That(bytes[2], Is.EqualTo(0x47)); // 'G'
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_VersionByteWritten() {
    var file = new SpeccyExtendedFile {
      Version = 7,
      BitmapData = new byte[6144],
      AttributeData = new byte[768],
      ExtendedAttributeData = new byte[768],
    };

    var bytes = SpeccyExtendedWriter.ToBytes(file);

    Assert.That(bytes[3], Is.EqualTo(7));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StandardAttributeDataWritten() {
    var attributes = new byte[768];
    attributes[0] = 0x47;
    attributes[767] = 0x38;

    var file = new SpeccyExtendedFile {
      BitmapData = new byte[6144],
      AttributeData = attributes,
      ExtendedAttributeData = new byte[768],
    };

    var bytes = SpeccyExtendedWriter.ToBytes(file);

    var stdAttrOffset = SpeccyExtendedReader.HeaderSize + SpeccyExtendedReader.BitmapSize;
    Assert.That(bytes[stdAttrOffset], Is.EqualTo(0x47));
    Assert.That(bytes[stdAttrOffset + 767], Is.EqualTo(0x38));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ExtendedAttributeDataWritten() {
    var extAttributes = new byte[768];
    extAttributes[0] = 0xAA;
    extAttributes[767] = 0xBB;

    var file = new SpeccyExtendedFile {
      BitmapData = new byte[6144],
      AttributeData = new byte[768],
      ExtendedAttributeData = extAttributes,
    };

    var bytes = SpeccyExtendedWriter.ToBytes(file);

    var extAttrOffset = SpeccyExtendedReader.HeaderSize + SpeccyExtendedReader.BitmapSize + SpeccyExtendedReader.AttributeSize;
    Assert.That(bytes[extAttrOffset], Is.EqualTo(0xAA));
    Assert.That(bytes[extAttrOffset + 767], Is.EqualTo(0xBB));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BitmapDataInterleavedCorrectly_Row0() {
    var bitmap = new byte[6144];
    bitmap[0] = 0xFF;

    var file = new SpeccyExtendedFile {
      BitmapData = bitmap,
      AttributeData = new byte[768],
      ExtendedAttributeData = new byte[768],
    };

    var bytes = SpeccyExtendedWriter.ToBytes(file);

    Assert.That(bytes[SpeccyExtendedReader.HeaderSize], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BitmapDataInterleavedCorrectly_Row1() {
    var bitmap = new byte[6144];
    var row1Offset = 1 * 32;
    bitmap[row1Offset] = 0xAA;

    var file = new SpeccyExtendedFile {
      BitmapData = bitmap,
      AttributeData = new byte[768],
      ExtendedAttributeData = new byte[768],
    };

    var bytes = SpeccyExtendedWriter.ToBytes(file);

    Assert.That(bytes[SpeccyExtendedReader.HeaderSize + 256], Is.EqualTo(0xAA));
  }
}
