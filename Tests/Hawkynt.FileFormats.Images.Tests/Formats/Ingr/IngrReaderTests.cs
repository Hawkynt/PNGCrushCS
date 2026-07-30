using System;
using System.IO;
using FileFormat.Ingr;

namespace FileFormat.Ingr.Tests;

[TestFixture]
public sealed class IngrReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IngrReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IngrReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cit"));
    Assert.Throws<FileNotFoundException>(() => IngrReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IngrReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var data = new byte[100];
    Assert.Throws<InvalidDataException>(() => IngrReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_UnsupportedDataType_ThrowsInvalidDataException() {
    var data = new byte[512];
    BitConverter.TryWriteBytes(data.AsSpan(0), (ushort)0x0809);
    BitConverter.TryWriteBytes(data.AsSpan(2), (ushort)99);
    BitConverter.TryWriteBytes(data.AsSpan(184), 4);
    BitConverter.TryWriteBytes(data.AsSpan(188), 4);

    Assert.Throws<InvalidDataException>(() => IngrReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb24_ParsesDimensions() {
    var width = 4;
    var height = 3;
    var pixelData = new byte[width * height * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var data = new byte[512 + pixelData.Length];
    BitConverter.TryWriteBytes(data.AsSpan(0), (ushort)0x0809);
    BitConverter.TryWriteBytes(data.AsSpan(2), (ushort)IngrDataType.Rgb24);
    BitConverter.TryWriteBytes(data.AsSpan(8), (short)width);
    BitConverter.TryWriteBytes(data.AsSpan(10), (short)height);
    BitConverter.TryWriteBytes(data.AsSpan(184), width);
    BitConverter.TryWriteBytes(data.AsSpan(188), height);
    Array.Copy(pixelData, 0, data, 512, pixelData.Length);

    var result = IngrReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(width));
    Assert.That(result.Height, Is.EqualTo(height));
    Assert.That(result.DataType, Is.EqualTo(IngrDataType.Rgb24));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb24_ParsesPixelData() {
    var width = 2;
    var height = 1;
    var pixelData = new byte[] { 255, 0, 0, 0, 255, 0 };

    var data = new byte[512 + pixelData.Length];
    BitConverter.TryWriteBytes(data.AsSpan(0), (ushort)0x0809);
    BitConverter.TryWriteBytes(data.AsSpan(2), (ushort)IngrDataType.Rgb24);
    BitConverter.TryWriteBytes(data.AsSpan(184), width);
    BitConverter.TryWriteBytes(data.AsSpan(188), height);
    Array.Copy(pixelData, 0, data, 512, pixelData.Length);

    var result = IngrReader.FromBytes(data);

    Assert.That(result.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidByteData_ParsesGrayscale() {
    var width = 3;
    var height = 2;
    var pixelData = new byte[] { 10, 20, 30, 40, 50, 60 };

    var data = new byte[512 + pixelData.Length];
    BitConverter.TryWriteBytes(data.AsSpan(0), (ushort)0x0809);
    BitConverter.TryWriteBytes(data.AsSpan(2), (ushort)IngrDataType.ByteData);
    BitConverter.TryWriteBytes(data.AsSpan(184), width);
    BitConverter.TryWriteBytes(data.AsSpan(188), height);
    Array.Copy(pixelData, 0, data, 512, pixelData.Length);

    var result = IngrReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(width));
    Assert.That(result.Height, Is.EqualTo(height));
    Assert.That(result.DataType, Is.EqualTo(IngrDataType.ByteData));
    Assert.That(result.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidRgb24_Parses() {
    var width = 2;
    var height = 2;
    var pixelData = new byte[width * height * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var data = new byte[512 + pixelData.Length];
    BitConverter.TryWriteBytes(data.AsSpan(0), (ushort)0x0809);
    BitConverter.TryWriteBytes(data.AsSpan(2), (ushort)IngrDataType.Rgb24);
    BitConverter.TryWriteBytes(data.AsSpan(184), width);
    BitConverter.TryWriteBytes(data.AsSpan(188), height);
    Array.Copy(pixelData, 0, data, 512, pixelData.Length);

    using var ms = new MemoryStream(data);
    var result = IngrReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(width));
    Assert.That(result.Height, Is.EqualTo(height));
    Assert.That(result.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_FallsBackToXExtent_WhenPixelsPerLineIsZero() {
    var data = new byte[512];
    BitConverter.TryWriteBytes(data.AsSpan(0), (ushort)0x0809);
    BitConverter.TryWriteBytes(data.AsSpan(2), (ushort)IngrDataType.ByteData);
    BitConverter.TryWriteBytes(data.AsSpan(8), (short)10);
    BitConverter.TryWriteBytes(data.AsSpan(10), (short)5);
    BitConverter.TryWriteBytes(data.AsSpan(184), 0);
    BitConverter.TryWriteBytes(data.AsSpan(188), 0);

    var result = IngrReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(10));
    Assert.That(result.Height, Is.EqualTo(5));
  }
}
