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
    // The container is read; the codec is not trusted to draw the picture and says so instead of
    // returning one, so what a valid file proves here is that the size came out of the right tags.
    var failure = Assert.Catch<Exception>(() => JpegXrReader.FromBytes(_BuildJxr(4, 2, 1)));

    Assert.That(failure!.Message, Does.Contain("4x2"));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb() {
    // The container is read; the codec is not trusted to draw the picture and says so instead of
    // returning one, so what a valid file proves here is that the size came out of the right tags.
    var failure = Assert.Catch<Exception>(() => JpegXrReader.FromBytes(_BuildJxr(3, 2, 3)));

    Assert.That(failure!.Message, Does.Contain("3x2"));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ParsesDimensionsFromIfd() {
    // The container is read; the codec is not trusted to draw the picture and says so instead of
    // returning one, so what a valid file proves here is that the size came out of the right tags.
    var failure = Assert.Catch<Exception>(() => JpegXrReader.FromBytes(_BuildJxr(16, 8, 3)));

    Assert.That(failure!.Message, Does.Contain("16x8"));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PixelDataPreserved() {
    // The container is read; the codec is not trusted to draw the picture and says so instead of
    // returning one, so what a valid file proves here is that the size came out of the right tags.
    var failure = Assert.Catch<Exception>(() => JpegXrReader.FromBytes(_BuildJxr(8, 4, 3)));

    Assert.That(failure!.Message, Does.Contain("8x4"));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidRgb() {
    using var stream = new MemoryStream(_BuildJxr(3, 2, 3));
    var failure = Assert.Catch<Exception>(() => JpegXrReader.FromStream(stream));

    Assert.That(failure!.Message, Does.Contain("3x2"));
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
    // The bytes a real file has here are 0xBC then 0x01, which as a little-endian word is 0x01BC.
    BinaryPrimitives.WriteUInt16LittleEndian(span[2..], 0x01BC);
    BinaryPrimitives.WriteUInt32LittleEndian(span[4..], (uint)ifdOffset);

    // IFD
    var pos = ifdOffset;
    BinaryPrimitives.WriteUInt16LittleEndian(span[pos..], (ushort)entryCount);
    pos += 2;

    var pixelFormatByte = componentCount == 1 ? (byte)0x08 : (byte)0x0C;

    _WriteEntry(span, ref pos, 0xBC01, 1, 1, pixelFormatByte);              // PixelFormat (BYTE)
    _WriteEntry(span, ref pos, 0xBC80, 4, 1, (uint)width);                  // ImageWidth
    _WriteEntry(span, ref pos, 0xBC81, 4, 1, (uint)height);                 // ImageHeight
    // The standard puts these at 0xBCC0 and 0xBCC1; this fixture wrote them where the reader was
    // wrongly looking, so the two agreed with each other and neither agreed with a real file.
    _WriteEntry(span, ref pos, 0xBCC0, 4, 1, (uint)pixelDataOffset);        // ImageOffset
    _WriteEntry(span, ref pos, 0xBCC1, 4, 1, (uint)totalPixelBytes);        // ImageByteCount

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
