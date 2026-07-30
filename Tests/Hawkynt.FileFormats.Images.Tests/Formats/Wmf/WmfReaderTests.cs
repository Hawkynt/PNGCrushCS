using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Wmf;

namespace FileFormat.Wmf.Tests;

[TestFixture]
public sealed class WmfReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => WmfReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => WmfReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wmf"));
    Assert.Throws<FileNotFoundException>(() => WmfReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => WmfReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[10];
    Assert.Throws<InvalidDataException>(() => WmfReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[100];
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0), 0x12345678);
    Assert.Throws<InvalidDataException>(() => WmfReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_NoStretchDib_OnlyEof_ThrowsInvalidDataException() {
    // Build a minimal WMF with only a META_EOF record (no image data)
    var data = _BuildMinimalWmfWithEofOnly();
    Assert.Throws<InvalidDataException>(() => WmfReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidWithEmbeddedDib() {
    // Build a valid WMF via writer, then read it back
    var width = 2;
    var height = 2;
    var pixelData = new byte[width * height * 3];
    pixelData[0] = 0xFF; // R
    pixelData[1] = 0x00; // G
    pixelData[2] = 0x00; // B
    pixelData[3] = 0x00;
    pixelData[4] = 0xFF;
    pixelData[5] = 0x00;
    pixelData[6] = 0x00;
    pixelData[7] = 0x00;
    pixelData[8] = 0xFF;
    pixelData[9] = 0xAA;
    pixelData[10] = 0xBB;
    pixelData[11] = 0xCC;

    var wmfFile = new WmfFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };

    var bytes = WmfWriter.ToBytes(wmfFile);
    var result = WmfReader.FromBytes(bytes);

    Assert.That(result.Width, Is.EqualTo(width));
    Assert.That(result.Height, Is.EqualTo(height));
    Assert.That(result.PixelData.Length, Is.EqualTo(width * height * 3));
    Assert.That(result.PixelData[0], Is.EqualTo(0xFF));
    Assert.That(result.PixelData[9], Is.EqualTo(0xAA));
  }

  private static byte[] _BuildMinimalWmfWithEofOnly() {
    // 22 placeable + 18 standard + 6 EOF = 46 bytes
    var data = new byte[46];
    var span = data.AsSpan();

    // Placeable header
    BinaryPrimitives.WriteUInt32LittleEndian(span, 0x9AC6CDD7);
    BinaryPrimitives.WriteUInt16LittleEndian(span[4..], 0);
    BinaryPrimitives.WriteInt16LittleEndian(span[6..], 0);
    BinaryPrimitives.WriteInt16LittleEndian(span[8..], 0);
    BinaryPrimitives.WriteInt16LittleEndian(span[10..], 1);
    BinaryPrimitives.WriteInt16LittleEndian(span[12..], 1);
    BinaryPrimitives.WriteUInt16LittleEndian(span[14..], 1440);
    BinaryPrimitives.WriteUInt32LittleEndian(span[16..], 0);
    ushort checksum = 0;
    for (var i = 0; i < 10; ++i)
      checksum ^= BinaryPrimitives.ReadUInt16LittleEndian(span[(i * 2)..]);
    BinaryPrimitives.WriteUInt16LittleEndian(span[20..], checksum);

    // Standard header
    BinaryPrimitives.WriteUInt16LittleEndian(span[22..], 1);
    BinaryPrimitives.WriteUInt16LittleEndian(span[24..], 9);
    BinaryPrimitives.WriteUInt16LittleEndian(span[26..], 0x0300);
    BinaryPrimitives.WriteUInt32LittleEndian(span[28..], 12); // file size in words = (18+6)/2 = 12
    BinaryPrimitives.WriteUInt16LittleEndian(span[32..], 0);
    BinaryPrimitives.WriteUInt32LittleEndian(span[34..], 3);
    BinaryPrimitives.WriteUInt16LittleEndian(span[38..], 0);

    // META_EOF
    BinaryPrimitives.WriteUInt32LittleEndian(span[40..], 3);
    BinaryPrimitives.WriteUInt16LittleEndian(span[44..], 0);

    return data;
  }
}
