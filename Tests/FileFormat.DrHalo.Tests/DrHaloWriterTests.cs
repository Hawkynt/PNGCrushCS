using System;
using System.Buffers.Binary;
using FileFormat.DrHalo;

namespace FileFormat.DrHalo.Tests;

[TestFixture]
public sealed class DrHaloWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidData_WritesCorrectWidth() {
    var file = new DrHaloFile {
      Width = 320,
      Height = 200,
      PixelData = new byte[320 * 200]
    };

    var bytes = DrHaloWriter.ToBytes(file);
    var width = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(0));

    Assert.That(width, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidData_WritesCorrectHeight() {
    var file = new DrHaloFile {
      Width = 320,
      Height = 200,
      PixelData = new byte[320 * 200]
    };

    var bytes = DrHaloWriter.ToBytes(file);
    var height = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(2));

    Assert.That(height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidData_ReservedIsZero() {
    var file = new DrHaloFile {
      Width = 10,
      Height = 5,
      PixelData = new byte[10 * 5]
    };

    var bytes = DrHaloWriter.ToBytes(file);
    var reserved = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(4));

    Assert.That(reserved, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidData_OutputLargerThanHeader() {
    var file = new DrHaloFile {
      Width = 4,
      Height = 2,
      PixelData = new byte[4 * 2]
    };

    var bytes = DrHaloWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.GreaterThan(DrHaloHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DrHaloWriter.ToBytes(null!));
  }
}
