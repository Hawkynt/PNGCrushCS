using System;
using System.IO;
using FileFormat.ZxBorderMulticolor;

namespace FileFormat.ZxBorderMulticolor.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new ZxBorderMulticolorFile {
      BitmapData = new byte[ZxBorderMulticolorReader.BitmapSize],
      AttributeData = new byte[ZxBorderMulticolorReader.AttributeSize],
      BorderData = new byte[ZxBorderMulticolorReader.BorderSize],
    };

    var bytes = ZxBorderMulticolorWriter.ToBytes(original);
    var restored = ZxBorderMulticolorReader.FromBytes(bytes);

    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.AttributeData, Is.EqualTo(original.AttributeData));
    Assert.That(restored.BorderData, Is.EqualTo(original.BorderData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllOnes() {
    var bitmap = new byte[ZxBorderMulticolorReader.BitmapSize];
    var attributes = new byte[ZxBorderMulticolorReader.AttributeSize];
    var border = new byte[ZxBorderMulticolorReader.BorderSize];
    Array.Fill(bitmap, (byte)0xFF);
    Array.Fill(attributes, (byte)0xFF);
    Array.Fill(border, (byte)0xFF);

    var original = new ZxBorderMulticolorFile {
      BitmapData = bitmap,
      AttributeData = attributes,
      BorderData = border,
    };

    var bytes = ZxBorderMulticolorWriter.ToBytes(original);
    var restored = ZxBorderMulticolorReader.FromBytes(bytes);

    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.AttributeData, Is.EqualTo(original.AttributeData));
    Assert.That(restored.BorderData, Is.EqualTo(original.BorderData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PatternData() {
    var bitmap = new byte[ZxBorderMulticolorReader.BitmapSize];
    var attributes = new byte[ZxBorderMulticolorReader.AttributeSize];
    var border = new byte[ZxBorderMulticolorReader.BorderSize];

    for (var i = 0; i < bitmap.Length; ++i)
      bitmap[i] = (byte)(i % 256);
    for (var i = 0; i < attributes.Length; ++i)
      attributes[i] = (byte)((i * 7) % 256);
    for (var i = 0; i < border.Length; ++i)
      border[i] = (byte)((i * 13) % 256);

    var original = new ZxBorderMulticolorFile {
      BitmapData = bitmap,
      AttributeData = attributes,
      BorderData = border,
    };

    var bytes = ZxBorderMulticolorWriter.ToBytes(original);
    var restored = ZxBorderMulticolorReader.FromBytes(bytes);

    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.AttributeData, Is.EqualTo(original.AttributeData));
    Assert.That(restored.BorderData, Is.EqualTo(original.BorderData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var bitmap = new byte[ZxBorderMulticolorReader.BitmapSize];
    var attributes = new byte[ZxBorderMulticolorReader.AttributeSize];
    var border = new byte[ZxBorderMulticolorReader.BorderSize];
    bitmap[0] = 0xAA;
    attributes[0] = 0x47;
    border[0] = 0xBB;

    var original = new ZxBorderMulticolorFile {
      BitmapData = bitmap,
      AttributeData = attributes,
      BorderData = border,
    };

    var tmpPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bmc4");
    try {
      var bytes = ZxBorderMulticolorWriter.ToBytes(original);
      File.WriteAllBytes(tmpPath, bytes);

      var restored = ZxBorderMulticolorReader.FromFile(new FileInfo(tmpPath));
      Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
      Assert.That(restored.AttributeData, Is.EqualTo(original.AttributeData));
      Assert.That(restored.BorderData, Is.EqualTo(original.BorderData));
    } finally {
      if (File.Exists(tmpPath))
        File.Delete(tmpPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ToRawImage_ProducesRgb24() {
    var file = new ZxBorderMulticolorFile {
      BitmapData = new byte[ZxBorderMulticolorReader.BitmapSize],
      AttributeData = new byte[ZxBorderMulticolorReader.AttributeSize],
      BorderData = new byte[ZxBorderMulticolorReader.BorderSize],
    };

    var raw = ZxBorderMulticolorFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Rgb24));
    Assert.That(raw.Width, Is.EqualTo(256));
    Assert.That(raw.Height, Is.EqualTo(192));
    Assert.That(raw.PixelData.Length, Is.EqualTo(256 * 192 * 3));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_InkPixel_Uses8x4Attribution() {
    // Set a single ink pixel at (0,0) and an attribute in cell (0,0)
    var bitmap = new byte[ZxBorderMulticolorReader.BitmapSize];
    var attributes = new byte[ZxBorderMulticolorReader.AttributeSize];
    var border = new byte[ZxBorderMulticolorReader.BorderSize];

    bitmap[0] = 0x80; // top-left pixel is ink (bit 7 set)
    attributes[0] = 0x47; // bright=1, paper=0(black), ink=7(white)

    var file = new ZxBorderMulticolorFile {
      BitmapData = bitmap,
      AttributeData = attributes,
      BorderData = border,
    };

    var raw = ZxBorderMulticolorFile.ToRawImage(file);

    // Pixel (0,0) should be bright white (ink=7, bright=1 => 0xFF,0xFF,0xFF)
    Assert.That(raw.PixelData[0], Is.EqualTo(0xFF));
    Assert.That(raw.PixelData[1], Is.EqualTo(0xFF));
    Assert.That(raw.PixelData[2], Is.EqualTo(0xFF));
  }
}
