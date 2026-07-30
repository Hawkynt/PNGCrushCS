using System;
using System.IO;
using FileFormat.ZxPaintbrush;

namespace FileFormat.ZxPaintbrush.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new ZxPaintbrushFile {
      BitmapData = new byte[6144],
      AttributeData = new byte[768]
    };

    var bytes = ZxPaintbrushWriter.ToBytes(original);
    var restored = ZxPaintbrushReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(256));
    Assert.That(restored.Height, Is.EqualTo(192));
    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.AttributeData, Is.EqualTo(original.AttributeData));
    Assert.That(restored.ExtraData.Length, Is.EqualTo(0));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_KnownBitmapPattern() {
    var bitmap = new byte[6144];
    for (var i = 0; i < bitmap.Length; ++i)
      bitmap[i] = (byte)(i * 7 % 256);

    var attributes = new byte[768];
    for (var i = 0; i < attributes.Length; ++i)
      attributes[i] = (byte)(i * 13 % 256);

    var original = new ZxPaintbrushFile {
      BitmapData = bitmap,
      AttributeData = attributes,
    };

    var bytes = ZxPaintbrushWriter.ToBytes(original);
    var restored = ZxPaintbrushReader.FromBytes(bytes);

    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.AttributeData, Is.EqualTo(original.AttributeData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllOnes() {
    var bitmap = new byte[6144];
    Array.Fill(bitmap, (byte)0xFF);

    var attributes = new byte[768];
    Array.Fill(attributes, (byte)0xFF);

    var original = new ZxPaintbrushFile {
      BitmapData = bitmap,
      AttributeData = attributes
    };

    var bytes = ZxPaintbrushWriter.ToBytes(original);
    var restored = ZxPaintbrushReader.FromBytes(bytes);

    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.AttributeData, Is.EqualTo(original.AttributeData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WithExtraData_Preserved() {
    var extra = new byte[200];
    for (var i = 0; i < extra.Length; ++i)
      extra[i] = (byte)(i * 3 % 256);

    var original = new ZxPaintbrushFile {
      BitmapData = new byte[6144],
      AttributeData = new byte[768],
      ExtraData = extra,
    };

    var bytes = ZxPaintbrushWriter.ToBytes(original);
    var restored = ZxPaintbrushReader.FromBytes(bytes);

    Assert.That(restored.ExtraData, Is.EqualTo(original.ExtraData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_InterleaveVerification_Row1() {
    var bitmap = new byte[6144];
    var row1LinearOffset = 1 * 32;
    bitmap[row1LinearOffset] = 0xBE;
    bitmap[row1LinearOffset + 1] = 0xEF;

    var original = new ZxPaintbrushFile {
      BitmapData = bitmap,
      AttributeData = new byte[768]
    };

    var bytes = ZxPaintbrushWriter.ToBytes(original);

    Assert.That(bytes[256], Is.EqualTo(0xBE));
    Assert.That(bytes[257], Is.EqualTo(0xEF));

    var restored = ZxPaintbrushReader.FromBytes(bytes);
    Assert.That(restored.BitmapData[row1LinearOffset], Is.EqualTo(0xBE));
    Assert.That(restored.BitmapData[row1LinearOffset + 1], Is.EqualTo(0xEF));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SinglePixelPerRow_AllRows() {
    var bitmap = new byte[6144];
    for (var row = 0; row < 192; ++row)
      bitmap[row * 32] = (byte)(row % 256);

    var original = new ZxPaintbrushFile {
      BitmapData = bitmap,
      AttributeData = new byte[768]
    };

    var bytes = ZxPaintbrushWriter.ToBytes(original);
    var restored = ZxPaintbrushReader.FromBytes(bytes);

    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_DimensionsAlwaysFixed() {
    var original = new ZxPaintbrushFile {
      BitmapData = new byte[6144],
      AttributeData = new byte[768]
    };

    var bytes = ZxPaintbrushWriter.ToBytes(original);
    var restored = ZxPaintbrushReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(256));
    Assert.That(restored.Height, Is.EqualTo(192));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var bitmap = new byte[6144];
    bitmap[0] = 0xDE;
    bitmap[6143] = 0xAD;

    var original = new ZxPaintbrushFile {
      BitmapData = bitmap,
      AttributeData = new byte[768]
    };

    var tmpPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zxp");
    try {
      var bytes = ZxPaintbrushWriter.ToBytes(original);
      File.WriteAllBytes(tmpPath, bytes);

      var restored = ZxPaintbrushReader.FromFile(new FileInfo(tmpPath));
      Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    } finally {
      if (File.Exists(tmpPath))
        File.Delete(tmpPath);
    }
  }
}
