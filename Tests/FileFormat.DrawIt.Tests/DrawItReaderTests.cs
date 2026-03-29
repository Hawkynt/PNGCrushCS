using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.DrawIt;

namespace FileFormat.DrawIt.Tests;

[TestFixture]
public sealed class DrawItReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DrawItReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DrawItReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dit"));
    Assert.Throws<FileNotFoundException>(() => DrawItReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DrawItReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[2];
    Assert.Throws<InvalidDataException>(() => DrawItReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroWidth_ThrowsInvalidDataException() {
    var data = new byte[4 + 768 + 100];
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), 0);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 10);
    Assert.Throws<InvalidDataException>(() => DrawItReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroHeight_ThrowsInvalidDataException() {
    var data = new byte[4 + 768 + 100];
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), 10);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 0);
    Assert.Throws<InvalidDataException>(() => DrawItReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TruncatedPixelData_ThrowsInvalidDataException() {
    var data = new byte[4 + 768 + 5]; // width=10 height=10 needs 100 pixel bytes
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), 10);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 10);
    Assert.Throws<InvalidDataException>(() => DrawItReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesCorrectly() {
    var data = _BuildDrawIt(16, 8);
    var result = DrawItReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(16));
      Assert.That(result.Height, Is.EqualTo(8));
      Assert.That(result.Palette, Has.Length.EqualTo(768));
      Assert.That(result.PixelData, Has.Length.EqualTo(128));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PreservesPaletteValues() {
    var data = _BuildDrawIt(4, 4);
    data[4] = 255; // R of entry 0
    data[5] = 128; // G of entry 0
    data[6] = 64;  // B of entry 0

    var result = DrawItReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Palette[0], Is.EqualTo(255));
      Assert.That(result.Palette[1], Is.EqualTo(128));
      Assert.That(result.Palette[2], Is.EqualTo(64));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PreservesPixelData() {
    var data = _BuildDrawIt(4, 4);
    var pixelStart = 4 + 768;
    data[pixelStart] = 42;
    data[pixelStart + 1] = 99;

    var result = DrawItReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.PixelData[0], Is.EqualTo(42));
      Assert.That(result.PixelData[1], Is.EqualTo(99));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = _BuildDrawIt(8, 8);
    using var ms = new MemoryStream(data);
    var result = DrawItReader.FromStream(ms);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(8));
      Assert.That(result.Height, Is.EqualTo(8));
    });
  }

  private static byte[] _BuildDrawIt(int width, int height) {
    var size = 4 + 768 + width * height;
    var data = new byte[size];
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0), (ushort)width);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), (ushort)height);

    for (var i = 0; i < 768; ++i)
      data[4 + i] = (byte)(i % 256);

    for (var i = 0; i < width * height; ++i)
      data[4 + 768 + i] = (byte)(i % 256);

    return data;
  }
}
