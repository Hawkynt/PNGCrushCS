using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.JpegXr;

namespace FileFormat.JpegXr.Tests;

[TestFixture]
public sealed class JpegXrReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => JpegXrReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => JpegXrReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jxr"));
    Assert.Throws<FileNotFoundException>(() => JpegXrReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => JpegXrReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => JpegXrReader.FromBytes(new byte[8]));

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
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), 42);
    Assert.Throws<InvalidDataException>(() => JpegXrReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidGrayscale() {
    var bytes = JpegXrWriter.ToBytes(new JpegXrFile {
      Width = 4,
      Height = 2,
      ComponentCount = 1,
      PixelData = [0, 32, 64, 96, 128, 160, 192, 255],
    });

    var decoded = JpegXrReader.FromBytes(bytes);
    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(4));
      Assert.That(decoded.Height, Is.EqualTo(2));
      Assert.That(decoded.ComponentCount, Is.EqualTo(1));
      Assert.That(decoded.PixelData, Is.EqualTo(new byte[] { 0, 32, 64, 96, 128, 160, 192, 255 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb() {
    var pixels = new byte[] {
      255, 0, 0, 0, 255, 0, 0, 0, 255,
      255, 255, 255, 17, 31, 47, 73, 91, 113,
    };
    var bytes = JpegXrWriter.ToBytes(new JpegXrFile { Width = 3, Height = 2, ComponentCount = 3, PixelData = pixels });

    var decoded = JpegXrReader.FromBytes(bytes);
    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(3));
      Assert.That(decoded.Height, Is.EqualTo(2));
      Assert.That(decoded.ComponentCount, Is.EqualTo(3));
      Assert.That(decoded.PixelData, Is.EqualTo(pixels));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidRgb() {
    var pixels = new byte[] { 1, 2, 3, 10, 20, 30, 100, 110, 120, 200, 210, 220 };
    var bytes = JpegXrWriter.ToBytes(new JpegXrFile { Width = 2, Height = 2, ComponentCount = 3, PixelData = pixels });
    using var stream = new MemoryStream(bytes);

    var decoded = JpegXrReader.FromStream(stream);
    Assert.That(decoded.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsLegacyOneBytePixelFormatPseudoContainer() {
    // BC01 is a 16-byte WIC GUID in T.833. The old implementation accepted a private one-byte
    // discriminator and could therefore only read files produced by itself.
    const int ifdOffset = 8;
    const int entryCount = 5;
    var ifdSize = 2 + entryCount * 12 + 4;
    var data = new byte[ifdOffset + ifdSize + 16];
    var span = data.AsSpan();
    data[0] = (byte)'I'; data[1] = (byte)'I';
    BinaryPrimitives.WriteUInt16LittleEndian(span[2..], 0x01BC);
    BinaryPrimitives.WriteUInt32LittleEndian(span[4..], ifdOffset);
    var pos = ifdOffset;
    BinaryPrimitives.WriteUInt16LittleEndian(span[pos..], entryCount); pos += 2;
    _WriteEntry(span, ref pos, 0xBC01, 1, 1, 0x0D);
    _WriteEntry(span, ref pos, 0xBC80, 4, 1, 1);
    _WriteEntry(span, ref pos, 0xBC81, 4, 1, 1);
    _WriteEntry(span, ref pos, 0xBCC0, 4, 1, (uint)(ifdOffset + ifdSize));
    _WriteEntry(span, ref pos, 0xBCC1, 4, 1, 16);

    Assert.Throws<InvalidOperationException>(() => JpegXrReader.FromBytes(data));
  }

  private static void _WriteEntry(Span<byte> span, ref int pos, ushort tag, ushort type, uint count, uint value) {
    BinaryPrimitives.WriteUInt16LittleEndian(span[pos..], tag);
    BinaryPrimitives.WriteUInt16LittleEndian(span[(pos + 2)..], type);
    BinaryPrimitives.WriteUInt32LittleEndian(span[(pos + 4)..], count);
    if (type == JpegXrIfd.TYPE_BYTE && count == 1)
      span[pos + 8] = (byte)value;
    else
      BinaryPrimitives.WriteUInt32LittleEndian(span[(pos + 8)..], value);
    pos += 12;
  }
}
