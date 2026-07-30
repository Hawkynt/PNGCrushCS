using System;
using System.IO;
using FileFormat.SpeccyExtended;

namespace FileFormat.SpeccyExtended.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new SpeccyExtendedFile {
      BitmapData = new byte[6144],
      AttributeData = new byte[768],
      ExtendedAttributeData = new byte[768],
    };

    var bytes = SpeccyExtendedWriter.ToBytes(original);
    var restored = SpeccyExtendedReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(256));
    Assert.That(restored.Height, Is.EqualTo(192));
    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.AttributeData, Is.EqualTo(original.AttributeData));
    Assert.That(restored.ExtendedAttributeData, Is.EqualTo(original.ExtendedAttributeData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_KnownPattern() {
    var bitmap = new byte[6144];
    for (var i = 0; i < bitmap.Length; ++i)
      bitmap[i] = (byte)(i * 7 % 256);

    var attributes = new byte[768];
    for (var i = 0; i < attributes.Length; ++i)
      attributes[i] = (byte)(i * 13 % 256);

    var extAttributes = new byte[768];
    for (var i = 0; i < extAttributes.Length; ++i)
      extAttributes[i] = (byte)(i * 17 % 256);

    var original = new SpeccyExtendedFile {
      BitmapData = bitmap,
      AttributeData = attributes,
      ExtendedAttributeData = extAttributes,
    };

    var bytes = SpeccyExtendedWriter.ToBytes(original);
    var restored = SpeccyExtendedReader.FromBytes(bytes);

    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.AttributeData, Is.EqualTo(original.AttributeData));
    Assert.That(restored.ExtendedAttributeData, Is.EqualTo(original.ExtendedAttributeData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllOnes() {
    var bitmap = new byte[6144];
    Array.Fill(bitmap, (byte)0xFF);

    var attributes = new byte[768];
    Array.Fill(attributes, (byte)0xFF);

    var extAttributes = new byte[768];
    Array.Fill(extAttributes, (byte)0xFF);

    var original = new SpeccyExtendedFile {
      BitmapData = bitmap,
      AttributeData = attributes,
      ExtendedAttributeData = extAttributes,
    };

    var bytes = SpeccyExtendedWriter.ToBytes(original);
    var restored = SpeccyExtendedReader.FromBytes(bytes);

    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.AttributeData, Is.EqualTo(original.AttributeData));
    Assert.That(restored.ExtendedAttributeData, Is.EqualTo(original.ExtendedAttributeData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_VersionPreserved() {
    var original = new SpeccyExtendedFile {
      Version = 42,
      BitmapData = new byte[6144],
      AttributeData = new byte[768],
      ExtendedAttributeData = new byte[768],
    };

    var bytes = SpeccyExtendedWriter.ToBytes(original);
    var restored = SpeccyExtendedReader.FromBytes(bytes);

    Assert.That(restored.Version, Is.EqualTo(42));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_InterleaveVerification_Row1() {
    var bitmap = new byte[6144];
    var row1LinearOffset = 1 * 32;
    bitmap[row1LinearOffset] = 0xBE;
    bitmap[row1LinearOffset + 1] = 0xEF;

    var original = new SpeccyExtendedFile {
      BitmapData = bitmap,
      AttributeData = new byte[768],
      ExtendedAttributeData = new byte[768],
    };

    var bytes = SpeccyExtendedWriter.ToBytes(original);

    Assert.That(bytes[SpeccyExtendedReader.HeaderSize + 256], Is.EqualTo(0xBE));

    var restored = SpeccyExtendedReader.FromBytes(bytes);
    Assert.That(restored.BitmapData[row1LinearOffset], Is.EqualTo(0xBE));
    Assert.That(restored.BitmapData[row1LinearOffset + 1], Is.EqualTo(0xEF));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var bitmap = new byte[6144];
    bitmap[0] = 0xDE;
    bitmap[6143] = 0xAD;

    var original = new SpeccyExtendedFile {
      BitmapData = bitmap,
      AttributeData = new byte[768],
      ExtendedAttributeData = new byte[768],
    };

    var tmpPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sxg");
    try {
      var bytes = SpeccyExtendedWriter.ToBytes(original);
      File.WriteAllBytes(tmpPath, bytes);

      var restored = SpeccyExtendedReader.FromFile(new FileInfo(tmpPath));
      Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    } finally {
      if (File.Exists(tmpPath))
        File.Delete(tmpPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_DimensionsAlwaysFixed() {
    var original = new SpeccyExtendedFile {
      BitmapData = new byte[6144],
      AttributeData = new byte[768],
      ExtendedAttributeData = new byte[768],
    };

    var bytes = SpeccyExtendedWriter.ToBytes(original);
    var restored = SpeccyExtendedReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(256));
    Assert.That(restored.Height, Is.EqualTo(192));
  }
}
