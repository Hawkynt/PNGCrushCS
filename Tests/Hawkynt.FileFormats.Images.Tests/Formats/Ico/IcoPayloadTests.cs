using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Ico;

namespace FileFormat.Ico.Tests;

/// <summary>
/// The conversion between an entry's payload and the standalone image file holding the same
/// picture, in both directions.
/// </summary>
[TestFixture]
public sealed class IcoPayloadTests {

  private const int _FileHeaderSize = 14;
  private const int _InfoHeaderSize = 40;

  /// <summary>A BMP file of the given size and depth, its colours a recognisable ramp.</summary>
  private static byte[] _Bmp(int width, int height, int bitsPerPixel = 32, int paletteEntries = 0) {
    var stride = (width * bitsPerPixel + 31) / 32 * 4;
    var colourBytes = stride * height;
    var paletteBytes = paletteEntries * 4;
    var coloursAt = _FileHeaderSize + _InfoHeaderSize + paletteBytes;
    var data = new byte[coloursAt + colourBytes];

    data[0] = (byte)'B';
    data[1] = (byte)'M';
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(2), (uint)data.Length);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(10), (uint)coloursAt);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(14), _InfoHeaderSize);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(18), width);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(22), height);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(26), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(28), (ushort)bitsPerPixel);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(34), (uint)colourBytes);
    if (paletteEntries > 0)
      BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(46), (uint)paletteEntries);

    for (var i = 0; i < colourBytes; ++i)
      data[coloursAt + i] = (byte)(i % 251);

    return data;
  }

  /// <summary>A BMP with the twelve-byte header that states its sizes in sixteen bits.</summary>
  private static byte[] _CoreHeaderBmp(int width, int height) {
    const int coreHeaderSize = 12;
    var stride = (width * 32 + 31) / 32 * 4;
    var colourBytes = stride * height;
    var coloursAt = _FileHeaderSize + coreHeaderSize;
    var data = new byte[coloursAt + colourBytes];

    data[0] = (byte)'B';
    data[1] = (byte)'M';
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(2), (uint)data.Length);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(10), (uint)coloursAt);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(14), coreHeaderSize);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(18), (short)width);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(20), (short)height);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(22), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(24), 32);

    for (var i = 0; i < colourBytes; ++i)
      data[coloursAt + i] = (byte)(i % 251);

    return data;
  }

  private static byte[] _MinimalPng(int width, int height, byte bitDepth = 8, byte colourType = 6) {
    static byte[] Chunk(string type, byte[] body) {
      var chunk = new byte[4 + 4 + body.Length + 4];
      BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(0), (uint)body.Length);
      for (var i = 0; i < 4; ++i)
        chunk[4 + i] = (byte)type[i];
      body.CopyTo(chunk.AsSpan(8));
      return chunk;
    }

    var ihdr = new byte[13];
    BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0), (uint)width);
    BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)height);
    ihdr[8] = bitDepth;
    ihdr[9] = colourType;

    using var ms = new MemoryStream();
    ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
    ms.Write(Chunk("IHDR", ihdr));
    ms.Write(Chunk("IEND", []));
    return ms.ToArray();
  }

  // ── The bitmap an entry carries, made into a file a viewer opens ──────────

  [Test]
  [Category("Unit")]
  public void IconDibToBmpFile_StartsWithTheSignatureThatNamesABmp() {
    var dib = IcoPayload.BmpFileToIconDib(_Bmp(16, 16), out var width, out var height, out var bitsPerPixel);

    var bmp = IcoPayload.IconDibToBmpFile(dib, width, height, bitsPerPixel);

    Assert.That(bmp[0], Is.EqualTo((byte)'B'));
    Assert.That(bmp[1], Is.EqualTo((byte)'M'));
  }

  [Test]
  [Category("Unit")]
  public void IconDibToBmpFile_StatesThePicturesOwnHeightRatherThanTheDoubledOne() {
    var dib = IcoPayload.BmpFileToIconDib(_Bmp(16, 16), out var width, out var height, out var bitsPerPixel);
    Assert.That(BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(8)), Is.EqualTo(32), "the entry states the doubled height");

    var bmp = IcoPayload.IconDibToBmpFile(dib, width, height, bitsPerPixel);

    Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(_FileHeaderSize + 8)), Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void IconDibToBmpFile_DropsTheMaskTheDoubledHeightWasCovering() {
    var source = _Bmp(16, 16);
    var dib = IcoPayload.BmpFileToIconDib(source, out var width, out var height, out var bitsPerPixel);

    var bmp = IcoPayload.IconDibToBmpFile(dib, width, height, bitsPerPixel);

    // Every colour byte survives and not one mask byte does, so the file is exactly as long as the
    // one it started as.
    Assert.That(bmp.Length, Is.EqualTo(source.Length));
    Assert.That(bmp.AsSpan(_FileHeaderSize).SequenceEqual(source.AsSpan(_FileHeaderSize)), Is.False,
      "the stated height differs, so the headers cannot be byte-identical");
    Assert.That(bmp.AsSpan(_FileHeaderSize + _InfoHeaderSize).SequenceEqual(source.AsSpan(_FileHeaderSize + _InfoHeaderSize)), Is.True,
      "the colours are unchanged");
  }

  [Test]
  [Category("Unit")]
  public void IconDibToBmpFile_PointsAtTheColoursPastThePalette() {
    var source = _Bmp(8, 8, bitsPerPixel: 4, paletteEntries: 16);
    var dib = IcoPayload.BmpFileToIconDib(source, out var width, out var height, out var bitsPerPixel);
    Assert.That(bitsPerPixel, Is.EqualTo(4));

    var bmp = IcoPayload.IconDibToBmpFile(dib, width, height, bitsPerPixel);

    var coloursAt = BinaryPrimitives.ReadUInt32LittleEndian(bmp.AsSpan(10));
    Assert.That(coloursAt, Is.EqualTo((uint)(_FileHeaderSize + _InfoHeaderSize + 16 * 4)));
  }

  [Test]
  [Category("Boundary")]
  public void IconDibToBmpFile_KeepsWhatArrivedOfATruncatedBitmap() {
    var dib = IcoPayload.BmpFileToIconDib(_Bmp(16, 16), out var width, out var height, out var bitsPerPixel);
    var truncated = dib.AsSpan(0, _InfoHeaderSize + 64).ToArray();

    var bmp = IcoPayload.IconDibToBmpFile(truncated, width, height, bitsPerPixel);

    Assert.That(bmp.Length, Is.EqualTo(_FileHeaderSize + _InfoHeaderSize + 64));
    Assert.That(bmp[0], Is.EqualTo((byte)'B'));
  }

  [Test]
  [Category("Exceptional")]
  public void IconDibToBmpFile_ImpossibleHeaderSize_Throws() {
    var dib = new byte[64];
    BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(0), 4);

    Assert.Throws<InvalidDataException>(() => IcoPayload.IconDibToBmpFile(dib, 8, 8, 32));
  }

  // ── A file a viewer produced, made into the bitmap an entry carries ───────

  [Test]
  [Category("Unit")]
  public void BmpFileToIconDib_StatesTwiceTheHeightAndAppendsAMaskThatLong() {
    var source = _Bmp(16, 16);

    var dib = IcoPayload.BmpFileToIconDib(source, out var width, out var height, out var bitsPerPixel);

    Assert.That(width, Is.EqualTo(16));
    Assert.That(height, Is.EqualTo(16));
    Assert.That(bitsPerPixel, Is.EqualTo(32));
    Assert.That(BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(8)), Is.EqualTo(32));
    Assert.That(dib.Length, Is.EqualTo(_InfoHeaderSize + 16 * 16 * 4 + (16 + 31) / 32 * 4 * 16));
  }

  [Test]
  [Category("Unit")]
  public void BmpFileToIconDib_LeavesTheMaskClearSoEveryPixelIsDrawn() {
    var dib = IcoPayload.BmpFileToIconDib(_Bmp(16, 16), out _, out _, out _);

    var maskAt = _InfoHeaderSize + 16 * 16 * 4;
    foreach (var b in dib.AsSpan(maskAt))
      Assert.That(b, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void BmpFileToIconDib_StatedByteCountCoversTheMaskToo() {
    var dib = IcoPayload.BmpFileToIconDib(_Bmp(16, 16), out _, out _, out _);

    var stated = BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(20));
    Assert.That(stated, Is.EqualTo((uint)(16 * 16 * 4 + (16 + 31) / 32 * 4 * 16)));
  }

  [Test]
  [Category("Exceptional")]
  public void BmpFileToIconDib_TopDown_ThrowsNotSupported() {
    var source = _Bmp(16, 16);
    BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(22), -16);

    Assert.Throws<NotSupportedException>(() => IcoPayload.BmpFileToIconDib(source, out _, out _, out _));
  }

  [Test]
  [Category("Exceptional")]
  public void BmpFileToIconDib_Truncated_ThrowsInvalidData() {
    Assert.Throws<InvalidDataException>(() => IcoPayload.BmpFileToIconDib(new byte[] { (byte)'B', (byte)'M' }, out _, out _, out _));
  }

  // ── The oldest information header, which is twelve bytes of sixteen-bit fields ──

  [Test]
  [Category("Boundary")]
  public void CoreHeaderBmp_RoundTripsThroughAnEntryPayload() {
    var source = _CoreHeaderBmp(8, 8);

    var dib = IcoPayload.BmpFileToIconDib(source, out var width, out var height, out var bitsPerPixel);

    Assert.That(width, Is.EqualTo(8));
    Assert.That(height, Is.EqualTo(8));
    Assert.That(bitsPerPixel, Is.EqualTo(32));
    // The doubled height goes at +6 as sixteen bits, not at +8 as thirty-two, which would land on
    // top of the depth.
    Assert.That(BinaryPrimitives.ReadInt16LittleEndian(dib.AsSpan(6)), Is.EqualTo((short)16));
    Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(dib.AsSpan(10)), Is.EqualTo((ushort)32));

    var bmp = IcoPayload.IconDibToBmpFile(dib, width, height, bitsPerPixel);
    Assert.That(BinaryPrimitives.ReadInt16LittleEndian(bmp.AsSpan(_FileHeaderSize + 6)), Is.EqualTo((short)8));
  }

  // ── PNG headers ──────────────────────────────────────────────────────────

  [Test]
  [Category("Unit")]
  public void ReadPngHeader_TakesSizeAndDepthFromTheImageHeader() {
    var (width, height, bitsPerPixel) = IcoPayload.ReadPngHeader(_MinimalPng(48, 24));

    Assert.That(width, Is.EqualTo(48));
    Assert.That(height, Is.EqualTo(24));
    Assert.That(bitsPerPixel, Is.EqualTo(32));
  }

  [TestCase((byte)0, 1)]
  [TestCase((byte)2, 3)]
  [TestCase((byte)3, 1)]
  [TestCase((byte)4, 2)]
  [TestCase((byte)6, 4)]
  [Category("Unit")]
  public void ReadPngHeader_CountsSamplesByColourType(byte colourType, int samples) {
    var (_, _, bitsPerPixel) = IcoPayload.ReadPngHeader(_MinimalPng(8, 8, bitDepth: 8, colourType: colourType));

    Assert.That(bitsPerPixel, Is.EqualTo(8 * samples));
  }

  [Test]
  [Category("Exceptional")]
  public void ReadPngHeader_FirstChunkIsNotTheImageHeader_Throws() {
    var png = _MinimalPng(16, 16);
    png[12] = (byte)'g';
    png[13] = (byte)'A';
    png[14] = (byte)'M';
    png[15] = (byte)'A';

    Assert.Throws<InvalidDataException>(() => IcoPayload.ReadPngHeader(png));
  }

  [Test]
  [Category("Exceptional")]
  public void ReadPngHeader_TooShort_Throws() {
    Assert.Throws<InvalidDataException>(() => IcoPayload.ReadPngHeader(new byte[20]));
  }

  // ── Recognising the two things a payload can be ──────────────────────────

  [Test]
  [Category("Unit")]
  public void IsPng_RequiresAllEightSignatureBytes() {
    var png = _MinimalPng(8, 8);
    Assert.That(IcoPayload.IsPng(png), Is.True);

    // The four bytes after the name exist to catch a file mangled in transit.
    png[4] = 0x0A;
    Assert.That(IcoPayload.IsPng(png), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void IsBmpFile_RecognisesTheFileHeaderAnEntryDoesNotHave() {
    Assert.That(IcoPayload.IsBmpFile(_Bmp(8, 8)), Is.True);
    Assert.That(IcoPayload.IsBmpFile(IcoPayload.BmpFileToIconDib(_Bmp(8, 8), out _, out _, out _)), Is.False);
  }

  // ── Encoding a whole file into a payload ─────────────────────────────────

  [Test]
  [Category("Unit")]
  public void Encode_KeepsAPngVerbatim() {
    var png = _MinimalPng(32, 32);

    var (payload, width, height, bitsPerPixel, format) = IcoPayload.Encode(png);

    Assert.That(format, Is.EqualTo(IcoImageFormat.Png));
    Assert.That(payload, Is.EqualTo(png).AsCollection);
    Assert.That(width, Is.EqualTo(32));
    Assert.That(height, Is.EqualTo(32));
    Assert.That(bitsPerPixel, Is.EqualTo(32));
  }

  [Test]
  [Category("Unit")]
  public void Encode_ConvertsABmp() {
    var (payload, width, height, bitsPerPixel, format) = IcoPayload.Encode(_Bmp(16, 16));

    Assert.That(format, Is.EqualTo(IcoImageFormat.Bmp));
    Assert.That(IcoPayload.IsBmpFile(payload), Is.False, "a payload has no file header");
    Assert.That(width, Is.EqualTo(16));
    Assert.That(height, Is.EqualTo(16));
    Assert.That(bitsPerPixel, Is.EqualTo(32));
  }

  [Test]
  [Category("Exceptional")]
  public void Encode_NeitherPngNorBmp_ThrowsArgumentException() {
    Assert.Throws<ArgumentException>(() => IcoPayload.Encode(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }));
  }
}
