using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Dng;

namespace FileFormat.Dng.Tests;

[TestFixture]
public sealed class DngReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DngReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DngReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dng"));
    Assert.Throws<FileNotFoundException>(() => DngReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DngReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[8];
    Assert.Throws<InvalidDataException>(() => DngReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidSignature_ThrowsInvalidDataException() {
    var data = new byte[14];
    data[0] = (byte)'X';
    data[1] = (byte)'X';
    Assert.Throws<InvalidDataException>(() => DngReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[14];
    data[0] = (byte)'I';
    data[1] = (byte)'I';
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 99);
    Assert.Throws<InvalidDataException>(() => DngReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_NoDngVersionTag_ThrowsInvalidDataException() {
    // Build a valid TIFF but without DNGVersion tag
    var data = _BuildTiffWithoutDngVersion(2, 2, 1, 8, 1);
    Assert.Throws<InvalidDataException>(() => DngReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidGrayscale() {
    var dng = _BuildMinimalDng(4, 2, 1, 8, 1);
    var result = DngReader.FromBytes(dng);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.SamplesPerPixel, Is.EqualTo(1));
    Assert.That(result.BitsPerSample, Is.EqualTo(8));
    Assert.That(result.PixelData.Length, Is.EqualTo(4 * 2 * 1));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb() {
    var dng = _BuildMinimalDng(3, 2, 3, 8, 2);
    var result = DngReader.FromBytes(dng);

    Assert.That(result.Width, Is.EqualTo(3));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.SamplesPerPixel, Is.EqualTo(3));
    Assert.That(result.BitsPerSample, Is.EqualTo(8));
    Assert.That(result.PixelData.Length, Is.EqualTo(3 * 2 * 3));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_BigEndian_ValidRgb() {
    var dng = _BuildMinimalDngBigEndian(2, 2, 3, 8, 2);
    var result = DngReader.FromBytes(dng);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.SamplesPerPixel, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidGrayscale() {
    var dng = _BuildMinimalDng(2, 2, 1, 8, 1);
    using var ms = new MemoryStream(dng);
    var result = DngReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.SamplesPerPixel, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PixelDataPreserved() {
    var pixelData = new byte[2 * 2 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var dng = _BuildMinimalDngWithPixels(2, 2, 3, 8, 2, pixelData);
    var result = DngReader.FromBytes(dng);

    Assert.That(result.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_DngVersionPreserved() {
    var dng = _BuildMinimalDng(2, 2, 1, 8, 1, dngVersion: [1, 6, 0, 0]);
    var result = DngReader.FromBytes(dng);

    Assert.That(result.DngVersion, Is.EqualTo(new byte[] { 1, 6, 0, 0 }));
  }

  // --- helpers ---

  /// <summary>Builds a minimal DNG (LE) with DNGVersion tag.</summary>
  private static byte[] _BuildMinimalDng(int width, int height, int samplesPerPixel, int bitsPerSample, ushort photometric, byte[]? dngVersion = null) {
    var pixelData = new byte[width * height * samplesPerPixel * (bitsPerSample / 8)];
    return _BuildMinimalDngWithPixels(width, height, samplesPerPixel, bitsPerSample, photometric, pixelData, dngVersion);
  }

  private static byte[] _BuildMinimalDngWithPixels(int width, int height, int samplesPerPixel, int bitsPerSample, ushort photometric, byte[] pixelData, byte[]? dngVersion = null) {
    dngVersion ??= [1, 4, 0, 0];
    var entryCount = 10; // 9 standard + DNGVersion
    var ifdOffset = 8;
    var ifdSize = 2 + entryCount * 12 + 4;
    var bpsExternalSize = samplesPerPixel > 2 ? samplesPerPixel * 2 : 0;
    var extraDataOffset = ifdOffset + ifdSize;
    var pixelDataOffset = extraDataOffset + bpsExternalSize;
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

    if (samplesPerPixel <= 2) {
      _WriteEntry(span, ref pos, 258, 3, (uint)samplesPerPixel, (uint)bitsPerSample);
    } else {
      _WriteEntry(span, ref pos, 258, 3, (uint)samplesPerPixel, (uint)extraDataOffset);
      for (var i = 0; i < samplesPerPixel; ++i)
        BinaryPrimitives.WriteUInt16LittleEndian(span[(extraDataOffset + i * 2)..], (ushort)bitsPerSample);
    }

    _WriteEntry(span, ref pos, 259, 3, 1, 1);
    _WriteEntry(span, ref pos, 262, 3, 1, photometric);
    _WriteEntry(span, ref pos, 273, 4, 1, (uint)pixelDataOffset);
    _WriteEntry(span, ref pos, 277, 3, 1, (uint)samplesPerPixel);
    _WriteEntry(span, ref pos, 278, 4, 1, (uint)height);
    _WriteEntry(span, ref pos, 279, 4, 1, (uint)totalPixelBytes);

    // DNGVersion (tag 50706, type BYTE=1, count 4)
    var versionValue = (uint)(dngVersion[0] | (dngVersion[1] << 8) | (dngVersion[2] << 16) | (dngVersion[3] << 24));
    _WriteEntry(span, ref pos, 50706, 1, 4, versionValue);

    BinaryPrimitives.WriteUInt32LittleEndian(span[pos..], 0);

    Array.Copy(pixelData, 0, data, pixelDataOffset, totalPixelBytes);

    return data;
  }

  private static byte[] _BuildMinimalDngBigEndian(int width, int height, int samplesPerPixel, int bitsPerSample, ushort photometric) {
    var entryCount = 10;
    var ifdOffset = 8;
    var ifdSize = 2 + entryCount * 12 + 4;
    var bpsExternalSize = samplesPerPixel > 2 ? samplesPerPixel * 2 : 0;
    var extraDataOffset = ifdOffset + ifdSize;
    var pixelDataOffset = extraDataOffset + bpsExternalSize;
    var totalPixelBytes = width * height * samplesPerPixel * (bitsPerSample / 8);
    var fileSize = pixelDataOffset + totalPixelBytes;

    var data = new byte[fileSize];
    var span = data.AsSpan();

    data[0] = (byte)'M';
    data[1] = (byte)'M';
    BinaryPrimitives.WriteUInt16BigEndian(span[2..], 42);
    BinaryPrimitives.WriteUInt32BigEndian(span[4..], (uint)ifdOffset);

    var pos = ifdOffset;
    BinaryPrimitives.WriteUInt16BigEndian(span[pos..], (ushort)entryCount);
    pos += 2;

    _WriteEntryBE(span, ref pos, 256, 4, 1, (uint)width);
    _WriteEntryBE(span, ref pos, 257, 4, 1, (uint)height);

    if (samplesPerPixel <= 2) {
      _WriteEntryBE(span, ref pos, 258, 3, (uint)samplesPerPixel, (uint)bitsPerSample);
    } else {
      _WriteEntryBE(span, ref pos, 258, 3, (uint)samplesPerPixel, (uint)extraDataOffset);
      for (var i = 0; i < samplesPerPixel; ++i)
        BinaryPrimitives.WriteUInt16BigEndian(span[(extraDataOffset + i * 2)..], (ushort)bitsPerSample);
    }

    _WriteEntryBE(span, ref pos, 259, 3, 1, 1);
    _WriteEntryBE(span, ref pos, 262, 3, 1, photometric);
    _WriteEntryBE(span, ref pos, 273, 4, 1, (uint)pixelDataOffset);
    _WriteEntryBE(span, ref pos, 277, 3, 1, (uint)samplesPerPixel);
    _WriteEntryBE(span, ref pos, 278, 4, 1, (uint)height);
    _WriteEntryBE(span, ref pos, 279, 4, 1, (uint)totalPixelBytes);

    // DNGVersion
    var versionValue = (uint)((1 << 24) | (4 << 16) | (0 << 8) | 0); // BE: [1,4,0,0]
    _WriteEntryBE(span, ref pos, 50706, 1, 4, versionValue);

    BinaryPrimitives.WriteUInt32BigEndian(span[pos..], 0);

    return data;
  }

  /// <summary>Builds a valid TIFF without DNGVersion tag (not a valid DNG).</summary>
  private static byte[] _BuildTiffWithoutDngVersion(int width, int height, int samplesPerPixel, int bitsPerSample, ushort photometric) {
    var entryCount = 9;
    var ifdOffset = 8;
    var ifdSize = 2 + entryCount * 12 + 4;
    var bpsExternalSize = samplesPerPixel > 2 ? samplesPerPixel * 2 : 0;
    var extraDataOffset = ifdOffset + ifdSize;
    var pixelDataOffset = extraDataOffset + bpsExternalSize;
    var totalPixelBytes = width * height * samplesPerPixel * (bitsPerSample / 8);
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

    if (samplesPerPixel <= 2) {
      _WriteEntry(span, ref pos, 258, 3, (uint)samplesPerPixel, (uint)bitsPerSample);
    } else {
      _WriteEntry(span, ref pos, 258, 3, (uint)samplesPerPixel, (uint)extraDataOffset);
      for (var i = 0; i < samplesPerPixel; ++i)
        BinaryPrimitives.WriteUInt16LittleEndian(span[(extraDataOffset + i * 2)..], (ushort)bitsPerSample);
    }

    _WriteEntry(span, ref pos, 259, 3, 1, 1);
    _WriteEntry(span, ref pos, 262, 3, 1, photometric);
    _WriteEntry(span, ref pos, 273, 4, 1, (uint)pixelDataOffset);
    _WriteEntry(span, ref pos, 277, 3, 1, (uint)samplesPerPixel);
    _WriteEntry(span, ref pos, 278, 4, 1, (uint)height);
    _WriteEntry(span, ref pos, 279, 4, 1, (uint)totalPixelBytes);

    BinaryPrimitives.WriteUInt32LittleEndian(span[pos..], 0);

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
