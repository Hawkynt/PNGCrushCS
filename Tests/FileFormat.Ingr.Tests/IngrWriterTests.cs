using System;
using FileFormat.Ingr;

namespace FileFormat.Ingr.Tests;

[TestFixture]
public sealed class IngrWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IngrWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderIs512Bytes() {
    var file = new IngrFile {
      Width = 1,
      Height = 1,
      DataType = IngrDataType.Rgb24,
      PixelData = new byte[3]
    };

    var bytes = IngrWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(512 + 3));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderTypeIsCorrect() {
    var file = new IngrFile {
      Width = 1,
      Height = 1,
      DataType = IngrDataType.Rgb24,
      PixelData = new byte[3]
    };

    var bytes = IngrWriter.ToBytes(file);
    var headerType = BitConverter.ToUInt16(bytes, 0);

    Assert.That(headerType, Is.EqualTo(0x0809));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DataTypeFieldIsCorrect() {
    var file = new IngrFile {
      Width = 1,
      Height = 1,
      DataType = IngrDataType.ByteData,
      PixelData = new byte[1]
    };

    var bytes = IngrWriter.ToBytes(file);
    var dataType = BitConverter.ToUInt16(bytes, 2);

    Assert.That(dataType, Is.EqualTo((ushort)IngrDataType.ByteData));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelsPerLineAtOffset184() {
    var file = new IngrFile {
      Width = 320,
      Height = 240,
      DataType = IngrDataType.Rgb24,
      PixelData = new byte[320 * 240 * 3]
    };

    var bytes = IngrWriter.ToBytes(file);
    var pixelsPerLine = BitConverter.ToInt32(bytes, 184);

    Assert.That(pixelsPerLine, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_NumberOfLinesAtOffset188() {
    var file = new IngrFile {
      Width = 320,
      Height = 240,
      DataType = IngrDataType.Rgb24,
      PixelData = new byte[320 * 240 * 3]
    };

    var bytes = IngrWriter.ToBytes(file);
    var numberOfLines = BitConverter.ToInt32(bytes, 188);

    Assert.That(numberOfLines, Is.EqualTo(240));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_XExtentAtOffset8() {
    var file = new IngrFile {
      Width = 100,
      Height = 50,
      DataType = IngrDataType.ByteData,
      PixelData = new byte[100 * 50]
    };

    var bytes = IngrWriter.ToBytes(file);
    var xExtent = BitConverter.ToInt16(bytes, 8);

    Assert.That(xExtent, Is.EqualTo(100));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_YExtentAtOffset10() {
    var file = new IngrFile {
      Width = 100,
      Height = 50,
      DataType = IngrDataType.ByteData,
      PixelData = new byte[100 * 50]
    };

    var bytes = IngrWriter.ToBytes(file);
    var yExtent = BitConverter.ToInt16(bytes, 10);

    Assert.That(yExtent, Is.EqualTo(50));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataStartsAtOffset512() {
    var pixels = new byte[] { 0xAA, 0xBB, 0xCC };
    var file = new IngrFile {
      Width = 1,
      Height = 1,
      DataType = IngrDataType.Rgb24,
      PixelData = pixels
    };

    var bytes = IngrWriter.ToBytes(file);

    Assert.That(bytes[512], Is.EqualTo(0xAA));
    Assert.That(bytes[513], Is.EqualTo(0xBB));
    Assert.That(bytes[514], Is.EqualTo(0xCC));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Rgb24_TotalSize() {
    var width = 4;
    var height = 3;
    var file = new IngrFile {
      Width = width,
      Height = height,
      DataType = IngrDataType.Rgb24,
      PixelData = new byte[width * height * 3]
    };

    var bytes = IngrWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(512 + width * height * 3));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ByteData_TotalSize() {
    var width = 8;
    var height = 4;
    var file = new IngrFile {
      Width = width,
      Height = height,
      DataType = IngrDataType.ByteData,
      PixelData = new byte[width * height]
    };

    var bytes = IngrWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(512 + width * height));
  }
}
