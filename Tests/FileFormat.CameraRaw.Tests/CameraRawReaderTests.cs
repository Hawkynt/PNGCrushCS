using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.CameraRaw;

namespace FileFormat.CameraRaw.Tests;

[TestFixture]
public sealed class CameraRawReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CameraRawReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CameraRawReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cr2"));
    Assert.Throws<FileNotFoundException>(() => CameraRawReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CameraRawReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[8];
    Assert.Throws<InvalidDataException>(() => CameraRawReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidSignature_ThrowsInvalidDataException() {
    var data = new byte[14];
    data[0] = (byte)'X';
    data[1] = (byte)'X';
    Assert.Throws<InvalidDataException>(() => CameraRawReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[14];
    data[0] = (byte)'I';
    data[1] = (byte)'I';
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 99);
    Assert.Throws<InvalidDataException>(() => CameraRawReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb24_DimensionsCorrect() {
    var raw = _BuildMinimalTiffRaw(4, 3);
    var result = CameraRawReader.FromBytes(raw);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb24_PixelDataLength() {
    var raw = _BuildMinimalTiffRaw(4, 3);
    var result = CameraRawReader.FromBytes(raw);

    Assert.That(result.PixelData.Length, Is.EqualTo(4 * 3 * 3));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PixelDataPreserved() {
    var pixelData = new byte[2 * 2 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var raw = _BuildMinimalTiffRawWithPixels(2, 2, pixelData);
    var result = CameraRawReader.FromBytes(raw);

    Assert.That(result.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidRgb24() {
    var raw = _BuildMinimalTiffRaw(3, 2);
    using var ms = new MemoryStream(raw);
    var result = CameraRawReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(3));
    Assert.That(result.Height, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ManufacturerDetection_Canon() {
    var raw = _BuildMinimalTiffRawWithMake(2, 2, "Canon");
    var result = CameraRawReader.FromBytes(raw);

    Assert.That(result.Manufacturer, Is.EqualTo(CameraRawManufacturer.Canon));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ManufacturerDetection_Nikon() {
    var raw = _BuildMinimalTiffRawWithMake(2, 2, "NIKON CORPORATION");
    var result = CameraRawReader.FromBytes(raw);

    Assert.That(result.Manufacturer, Is.EqualTo(CameraRawManufacturer.Nikon));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ModelString_Preserved() {
    var raw = _BuildMinimalTiffRawWithMakeAndModel(2, 2, "Canon", "EOS R5");
    var result = CameraRawReader.FromBytes(raw);

    Assert.That(result.Model, Is.EqualTo("EOS R5"));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_BigEndian_ValidRgb24() {
    var raw = _BuildMinimalTiffRawBigEndian(2, 2);
    var result = CameraRawReader.FromBytes(raw);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
  }

  /// <summary>Builds a minimal TIFF-based raw file with RGB24 pixel data (LE).</summary>
  private static byte[] _BuildMinimalTiffRaw(int width, int height) {
    var pixelData = new byte[width * height * 3];
    return _BuildMinimalTiffRawWithPixels(width, height, pixelData);
  }

  private static byte[] _BuildMinimalTiffRawWithPixels(int width, int height, byte[] pixelData) {
    const int entryCount = 9;
    const int ifdOffset = 8;
    var ifdSize = 2 + entryCount * 12 + 4;
    var bpsExternalOffset = ifdOffset + ifdSize;
    const int bpsExternalSize = 3 * 2; // 3 channels x 2 bytes each
    var pixelDataOffset = bpsExternalOffset + bpsExternalSize;
    var totalPixelBytes = pixelData.Length;
    var fileSize = pixelDataOffset + totalPixelBytes;

    var data = new byte[fileSize];
    var span = data.AsSpan();

    data[0] = (byte)'I';
    data[1] = (byte)'I';
    BinaryPrimitives.WriteUInt16LittleEndian(span[2..], 42);
    BinaryPrimitives.WriteUInt32LittleEndian(span[4..], (uint)ifdOffset);

    var pos = ifdOffset;
    BinaryPrimitives.WriteUInt16LittleEndian(span[pos..], entryCount);
    pos += 2;

    _WriteEntry(span, ref pos, 256, 4, 1, (uint)width);
    _WriteEntry(span, ref pos, 257, 4, 1, (uint)height);
    _WriteEntry(span, ref pos, 258, 3, 3, (uint)bpsExternalOffset);
    for (var i = 0; i < 3; ++i)
      BinaryPrimitives.WriteUInt16LittleEndian(span[(bpsExternalOffset + i * 2)..], 8);
    _WriteEntry(span, ref pos, 259, 3, 1, 1);
    _WriteEntry(span, ref pos, 262, 3, 1, 2);
    _WriteEntry(span, ref pos, 273, 4, 1, (uint)pixelDataOffset);
    _WriteEntry(span, ref pos, 277, 3, 1, 3);
    _WriteEntry(span, ref pos, 278, 4, 1, (uint)height);
    _WriteEntry(span, ref pos, 279, 4, 1, (uint)totalPixelBytes);

    BinaryPrimitives.WriteUInt32LittleEndian(span[pos..], 0);

    Array.Copy(pixelData, 0, data, pixelDataOffset, totalPixelBytes);

    return data;
  }

  private static byte[] _BuildMinimalTiffRawWithMake(int width, int height, string make) {
    return _BuildMinimalTiffRawWithMakeAndModel(width, height, make, "");
  }

  private static byte[] _BuildMinimalTiffRawWithMakeAndModel(int width, int height, string make, string model) {
    var pixelData = new byte[width * height * 3];
    var makeBytes = System.Text.Encoding.ASCII.GetBytes(make + "\0");
    var modelBytes = model.Length > 0 ? System.Text.Encoding.ASCII.GetBytes(model + "\0") : [];

    var entryCount = 9 + 1 + (modelBytes.Length > 0 ? 1 : 0); // base + Make + optional Model
    const int ifdOffset = 8;
    var ifdSize = 2 + entryCount * 12 + 4;
    var bpsExternalOffset = ifdOffset + ifdSize;
    const int bpsExternalSize = 3 * 2;
    var makeOffset = bpsExternalOffset + bpsExternalSize;
    var modelOffset = makeOffset + makeBytes.Length;
    var pixelDataOffset = modelOffset + modelBytes.Length;
    var totalPixelBytes = pixelData.Length;
    var fileSize = pixelDataOffset + totalPixelBytes;

    var data = new byte[fileSize];
    var span = data.AsSpan();

    data[0] = (byte)'I';
    data[1] = (byte)'I';
    BinaryPrimitives.WriteUInt16LittleEndian(span[2..], 42);
    BinaryPrimitives.WriteUInt32LittleEndian(span[4..], (uint)ifdOffset);

    var pos = ifdOffset;
    BinaryPrimitives.WriteUInt16LittleEndian(span[pos..], (ushort)entryCount);
    pos += 2;

    _WriteEntry(span, ref pos, 256, 4, 1, (uint)width);
    _WriteEntry(span, ref pos, 257, 4, 1, (uint)height);
    _WriteEntry(span, ref pos, 258, 3, 3, (uint)bpsExternalOffset);
    for (var i = 0; i < 3; ++i)
      BinaryPrimitives.WriteUInt16LittleEndian(span[(bpsExternalOffset + i * 2)..], 8);
    _WriteEntry(span, ref pos, 259, 3, 1, 1);
    _WriteEntry(span, ref pos, 262, 3, 1, 2);

    // Make tag (271) - ASCII
    _WriteEntry(span, ref pos, 271, 2, (uint)makeBytes.Length, (uint)makeOffset);
    Array.Copy(makeBytes, 0, data, makeOffset, makeBytes.Length);

    // Model tag (272) - ASCII (if present)
    if (modelBytes.Length > 0) {
      _WriteEntry(span, ref pos, 272, 2, (uint)modelBytes.Length, (uint)modelOffset);
      Array.Copy(modelBytes, 0, data, modelOffset, modelBytes.Length);
    }

    _WriteEntry(span, ref pos, 273, 4, 1, (uint)pixelDataOffset);
    _WriteEntry(span, ref pos, 277, 3, 1, 3);
    _WriteEntry(span, ref pos, 278, 4, 1, (uint)height);
    _WriteEntry(span, ref pos, 279, 4, 1, (uint)totalPixelBytes);

    BinaryPrimitives.WriteUInt32LittleEndian(span[pos..], 0);

    Array.Copy(pixelData, 0, data, pixelDataOffset, totalPixelBytes);

    return data;
  }

  private static byte[] _BuildMinimalTiffRawBigEndian(int width, int height) {
    var pixelData = new byte[width * height * 3];
    const int entryCount = 9;
    const int ifdOffset = 8;
    var ifdSize = 2 + entryCount * 12 + 4;
    var bpsExternalOffset = ifdOffset + ifdSize;
    const int bpsExternalSize = 3 * 2;
    var pixelDataOffset = bpsExternalOffset + bpsExternalSize;
    var totalPixelBytes = pixelData.Length;
    var fileSize = pixelDataOffset + totalPixelBytes;

    var data = new byte[fileSize];
    var span = data.AsSpan();

    data[0] = (byte)'M';
    data[1] = (byte)'M';
    BinaryPrimitives.WriteUInt16BigEndian(span[2..], 42);
    BinaryPrimitives.WriteUInt32BigEndian(span[4..], (uint)ifdOffset);

    var pos = ifdOffset;
    BinaryPrimitives.WriteUInt16BigEndian(span[pos..], entryCount);
    pos += 2;

    _WriteEntryBE(span, ref pos, 256, 4, 1, (uint)width);
    _WriteEntryBE(span, ref pos, 257, 4, 1, (uint)height);
    _WriteEntryBE(span, ref pos, 258, 3, 3, (uint)bpsExternalOffset);
    for (var i = 0; i < 3; ++i)
      BinaryPrimitives.WriteUInt16BigEndian(span[(bpsExternalOffset + i * 2)..], 8);
    _WriteEntryBE(span, ref pos, 259, 3, 1, 1);
    _WriteEntryBE(span, ref pos, 262, 3, 1, 2);
    _WriteEntryBE(span, ref pos, 273, 4, 1, (uint)pixelDataOffset);
    _WriteEntryBE(span, ref pos, 277, 3, 1, 3);
    _WriteEntryBE(span, ref pos, 278, 4, 1, (uint)height);
    _WriteEntryBE(span, ref pos, 279, 4, 1, (uint)totalPixelBytes);

    BinaryPrimitives.WriteUInt32BigEndian(span[pos..], 0);

    return data;
  }

  private static void _WriteEntry(Span<byte> span, ref int pos, ushort tag, ushort type, uint count, uint value) {
    BinaryPrimitives.WriteUInt16LittleEndian(span[pos..], tag);
    BinaryPrimitives.WriteUInt16LittleEndian(span[(pos + 2)..], type);
    BinaryPrimitives.WriteUInt32LittleEndian(span[(pos + 4)..], count);
    if (type == 3 && count == 1)
      BinaryPrimitives.WriteUInt16LittleEndian(span[(pos + 8)..], (ushort)value);
    else
      BinaryPrimitives.WriteUInt32LittleEndian(span[(pos + 8)..], value);
    pos += 12;
  }

  private static void _WriteEntryBE(Span<byte> span, ref int pos, ushort tag, ushort type, uint count, uint value) {
    BinaryPrimitives.WriteUInt16BigEndian(span[pos..], tag);
    BinaryPrimitives.WriteUInt16BigEndian(span[(pos + 2)..], type);
    BinaryPrimitives.WriteUInt32BigEndian(span[(pos + 4)..], count);
    if (type == 3 && count == 1)
      BinaryPrimitives.WriteUInt16BigEndian(span[(pos + 8)..], (ushort)value);
    else
      BinaryPrimitives.WriteUInt32BigEndian(span[(pos + 8)..], value);
    pos += 12;
  }
}
