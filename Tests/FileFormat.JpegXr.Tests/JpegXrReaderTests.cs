using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.JpegXr;

namespace FileFormat.JpegXr.Tests;

[TestFixture]
public sealed class JpegXrReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => JpegXrReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => JpegXrReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jxr"));
    Assert.Throws<FileNotFoundException>(() => JpegXrReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => JpegXrReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[8];
    Assert.Throws<InvalidDataException>(() => JpegXrReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidByteOrder_ThrowsInvalidDataException() {
    var data = new byte[14];
    data[0] = (byte)'M';
    data[1] = (byte)'M';
    Assert.Throws<InvalidDataException>(() => JpegXrReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[14];
    data[0] = (byte)'I';
    data[1] = (byte)'I';
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 42); // TIFF magic, not JXR
    Assert.Throws<InvalidDataException>(() => JpegXrReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidGrayscale() {
    var jxr = _BuildJxr(4, 2, 1);
    var result = JpegXrReader.FromBytes(jxr);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.ComponentCount, Is.EqualTo(1));
    Assert.That(result.PixelData.Length, Is.EqualTo(4 * 2));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb() {
    var jxr = _BuildJxr(3, 2, 3);
    var result = JpegXrReader.FromBytes(jxr);

    Assert.That(result.Width, Is.EqualTo(3));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.ComponentCount, Is.EqualTo(3));
    Assert.That(result.PixelData.Length, Is.EqualTo(3 * 2 * 3));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ParsesDimensionsFromIfd() {
    var jxr = _BuildJxr(16, 8, 3);
    var result = JpegXrReader.FromBytes(jxr);

    Assert.That(result.Width, Is.EqualTo(16));
    Assert.That(result.Height, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PixelDataPreserved() {
    var pixelData = new byte[2 * 2 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var file = new JpegXrFile {
      Width = 2,
      Height = 2,
      ComponentCount = 3,
      PixelData = pixelData
    };

    var jxr = JpegXrWriter.ToBytes(file);
    var result = JpegXrReader.FromBytes(jxr);

    Assert.That(result.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidRgb() {
    var jxr = _BuildJxr(2, 2, 3);
    using var ms = new MemoryStream(jxr);
    var result = JpegXrReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.ComponentCount, Is.EqualTo(3));
  }

  /// <summary>Builds a minimal JPEG XR file with the given dimensions and component count.</summary>
  private static byte[] _BuildJxr(int width, int height, int componentCount) {
    var pixelData = new byte[width * height * componentCount];
    return _BuildJxrWithPixels(width, height, componentCount, pixelData);
  }

  /// <summary>Builds a minimal JPEG XR file with the given pixel data.</summary>
  private static byte[] _BuildJxrWithPixels(int width, int height, int componentCount, byte[] pixelData) {
    // We build a file manually (not using the writer) to test the reader independently
    var entryCount = 5;
    var ifdOffset = 8;
    var ifdSize = 2 + entryCount * 12 + 4;
    var pixelDataOffset = ifdOffset + ifdSize;
    var totalPixelBytes = pixelData.Length;
    var fileSize = pixelDataOffset + totalPixelBytes;

    var data = new byte[fileSize];
    var span = data.AsSpan();

    // Header
    data[0] = (byte)'I';
    data[1] = (byte)'I';
    BinaryPrimitives.WriteUInt16LittleEndian(span[2..], 0xBC01);
    BinaryPrimitives.WriteUInt32LittleEndian(span[4..], (uint)ifdOffset);

    // IFD
    var pos = ifdOffset;
    BinaryPrimitives.WriteUInt16LittleEndian(span[pos..], (ushort)entryCount);
    pos += 2;

    var pixelFormatByte = componentCount == 1 ? (byte)0x08 : (byte)0x0C;

    _WriteEntry(span, ref pos, 0xBC01, 1, 1, pixelFormatByte);              // PixelFormat (BYTE)
    _WriteEntry(span, ref pos, 0xBC80, 4, 1, (uint)width);                  // ImageWidth
    _WriteEntry(span, ref pos, 0xBC81, 4, 1, (uint)height);                 // ImageHeight
    _WriteEntry(span, ref pos, 0xBCE0, 4, 1, (uint)pixelDataOffset);        // ImageOffset
    _WriteEntry(span, ref pos, 0xBCE1, 4, 1, (uint)totalPixelBytes);        // ImageByteCount

    // Next IFD = 0
    BinaryPrimitives.WriteUInt32LittleEndian(span[pos..], 0);

    Array.Copy(pixelData, 0, data, pixelDataOffset, totalPixelBytes);

    return data;
  }

  private static void _WriteEntry(Span<byte> span, ref int pos, ushort tag, ushort type, uint count, uint value) {
    BinaryPrimitives.WriteUInt16LittleEndian(span[pos..], tag);
    BinaryPrimitives.WriteUInt16LittleEndian(span[(pos + 2)..], type);
    BinaryPrimitives.WriteUInt32LittleEndian(span[(pos + 4)..], count);
    if (type == 1 && count == 1) // BYTE
      span[pos + 8] = (byte)value;
    else if (type == 3 && count == 1) // SHORT
      BinaryPrimitives.WriteUInt16LittleEndian(span[(pos + 8)..], (ushort)value);
    else
      BinaryPrimitives.WriteUInt32LittleEndian(span[(pos + 8)..], value);
    pos += 12;
  }
}
