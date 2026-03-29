using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Analyze;

namespace FileFormat.Analyze.Tests;

[TestFixture]
public sealed class AnalyzeReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AnalyzeReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AnalyzeReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".hdr"));
    Assert.Throws<FileNotFoundException>(() => AnalyzeReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AnalyzeReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => AnalyzeReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidSizeofHdr_ThrowsInvalidDataException() {
    var data = new byte[348];
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0), 999);
    Assert.Throws<InvalidDataException>(() => AnalyzeReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidGrayscale() {
    var data = _BuildAnalyzeBytes(4, 3, AnalyzeDataType.UInt8, 8);
    var result = AnalyzeReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
    Assert.That(result.DataType, Is.EqualTo(AnalyzeDataType.UInt8));
    Assert.That(result.BitsPerPixel, Is.EqualTo(8));
    Assert.That(result.PixelData.Length, Is.EqualTo(4 * 3));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb() {
    var data = _BuildAnalyzeBytes(2, 2, AnalyzeDataType.Rgb24, 24);
    var result = AnalyzeReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.DataType, Is.EqualTo(AnalyzeDataType.Rgb24));
    Assert.That(result.BitsPerPixel, Is.EqualTo(24));
    Assert.That(result.PixelData.Length, Is.EqualTo(2 * 2 * 3));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PixelDataPreserved() {
    var pixelData = new byte[4 * 2];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 31 % 256);

    var data = _BuildAnalyzeBytesWithPixels(4, 2, AnalyzeDataType.UInt8, 8, pixelData);
    var result = AnalyzeReader.FromBytes(data);

    Assert.That(result.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidGrayscale() {
    var data = _BuildAnalyzeBytes(2, 2, AnalyzeDataType.UInt8, 8);
    using var ms = new MemoryStream(data);
    var result = AnalyzeReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.DataType, Is.EqualTo(AnalyzeDataType.UInt8));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_HeaderOnly_EmptyPixelData() {
    var data = new byte[348];
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0), 348);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(42), 2);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(44), 2);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(70), (short)AnalyzeDataType.UInt8);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(72), 8);

    var result = AnalyzeReader.FromBytes(data);

    Assert.That(result.PixelData, Is.Empty);
  }

  // --- helpers ---

  internal static byte[] _BuildAnalyzeBytes(int width, int height, AnalyzeDataType dataType, int bitpix) {
    var bytesPerPixel = bitpix / 8;
    var pixelData = new byte[width * height * bytesPerPixel];
    return _BuildAnalyzeBytesWithPixels(width, height, dataType, bitpix, pixelData);
  }

  internal static byte[] _BuildAnalyzeBytesWithPixels(int width, int height, AnalyzeDataType dataType, int bitpix, byte[] pixelData) {
    var result = new byte[348 + pixelData.Length];
    var span = result.AsSpan();

    BinaryPrimitives.WriteInt32LittleEndian(span, 348);
    BinaryPrimitives.WriteInt16LittleEndian(span[40..], 3);
    BinaryPrimitives.WriteInt16LittleEndian(span[42..], (short)width);
    BinaryPrimitives.WriteInt16LittleEndian(span[44..], (short)height);
    BinaryPrimitives.WriteInt16LittleEndian(span[46..], 1);
    BinaryPrimitives.WriteInt16LittleEndian(span[70..], (short)dataType);
    BinaryPrimitives.WriteInt16LittleEndian(span[72..], (short)bitpix);

    Array.Copy(pixelData, 0, result, 348, pixelData.Length);

    return result;
  }
}
