using System;
using System.Buffers.Binary;
using FileFormat.Analyze;

namespace FileFormat.Analyze.Tests;

[TestFixture]
public sealed class AnalyzeWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => AnalyzeWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_SizeofHdrField_Is348() {
    var file = new AnalyzeFile {
      Width = 2,
      Height = 2,
      DataType = AnalyzeDataType.UInt8,
      BitsPerPixel = 8,
      PixelData = new byte[4]
    };

    var bytes = AnalyzeWriter.ToBytes(file);
    var sizeofHdr = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0));

    Assert.That(sizeofHdr, Is.EqualTo(348));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DatatypeField() {
    var file = new AnalyzeFile {
      Width = 2,
      Height = 2,
      DataType = AnalyzeDataType.Rgb24,
      BitsPerPixel = 24,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = AnalyzeWriter.ToBytes(file);
    var datatype = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(70));

    Assert.That(datatype, Is.EqualTo((short)AnalyzeDataType.Rgb24));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Dimensions() {
    var file = new AnalyzeFile {
      Width = 320,
      Height = 240,
      DataType = AnalyzeDataType.UInt8,
      BitsPerPixel = 8,
      PixelData = new byte[320 * 240]
    };

    var bytes = AnalyzeWriter.ToBytes(file);
    var width = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(42));
    var height = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(44));

    Assert.That(width, Is.EqualTo(320));
    Assert.That(height, Is.EqualTo(240));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BitsPerPixelField() {
    var file = new AnalyzeFile {
      Width = 2,
      Height = 2,
      DataType = AnalyzeDataType.Rgb24,
      BitsPerPixel = 24,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = AnalyzeWriter.ToBytes(file);
    var bitpix = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(72));

    Assert.That(bitpix, Is.EqualTo(24));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataFollowsHeader() {
    var pixels = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
    var file = new AnalyzeFile {
      Width = 2,
      Height = 2,
      DataType = AnalyzeDataType.UInt8,
      BitsPerPixel = 8,
      PixelData = pixels
    };

    var bytes = AnalyzeWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(348 + 4));
    Assert.That(bytes[348], Is.EqualTo(0xAA));
    Assert.That(bytes[349], Is.EqualTo(0xBB));
    Assert.That(bytes[350], Is.EqualTo(0xCC));
    Assert.That(bytes[351], Is.EqualTo(0xDD));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DimCountField_Is3() {
    var file = new AnalyzeFile {
      Width = 1,
      Height = 1,
      DataType = AnalyzeDataType.UInt8,
      BitsPerPixel = 8,
      PixelData = new byte[1]
    };

    var bytes = AnalyzeWriter.ToBytes(file);
    var dimCount = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(40));

    Assert.That(dimCount, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TotalSize() {
    var w = 4;
    var h = 3;
    var bpp = 1;
    var file = new AnalyzeFile {
      Width = w,
      Height = h,
      DataType = AnalyzeDataType.UInt8,
      BitsPerPixel = 8,
      PixelData = new byte[w * h * bpp]
    };

    var bytes = AnalyzeWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(348 + w * h * bpp));
  }
}
