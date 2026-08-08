using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.Jpeg;
using FileFormat.Kqp;

namespace FileFormat.Kqp.Tests;

[TestFixture]
public sealed class KqpTests {

  private const int _PaletteBytes = 252 * 4;

  private static RawImage _Picture(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      pixels[i * 3] = (byte)(i * 3);
      pixels[i * 3 + 1] = (byte)(i * 5);
      pixels[i * 3 + 2] = (byte)(i * 7);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  /// <summary>A JPEG with the tables taken out of it, which is how these files store one.</summary>
  private static byte[] _StripTables(byte[] jpeg) {
    using var ms = new MemoryStream();
    ms.Write(jpeg, 0, 2);

    var at = 2;
    while (at + 4 <= jpeg.Length) {
      var marker = jpeg[at + 1];
      if (marker == 0xDA) {
        ms.Write(jpeg, at, jpeg.Length - at);
        break;
      }

      var length = BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(at + 2));
      if (marker is not (0xDB or 0xC4))
        ms.Write(jpeg, at, 2 + length);

      at += 2 + length;
    }

    return ms.ToArray();
  }

  /// <summary>The bitmap wrapper Konica put around that stream.</summary>
  private static byte[] _File(byte[] stream, int width, int height, int infoHeader = KqpFile.InfoHeaderSize, string compression = "JPEG") {
    var offset = KqpFile.FileHeaderSize + KqpFile.InfoHeaderSize + _PaletteBytes;
    var data = new byte[offset + stream.Length];

    KqpFile.Magic.CopyTo(data);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(KqpFile.DataOffsetField), offset);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(KqpFile.FileHeaderSize), infoHeader);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(KqpFile.FileHeaderSize + 4), width);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(KqpFile.FileHeaderSize + 8), -height);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(KqpFile.FileHeaderSize + 12), 1);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(KqpFile.FileHeaderSize + 14), 24);
    System.Text.Encoding.ASCII.GetBytes(compression).CopyTo(data.AsSpan(KqpFile.FileHeaderSize + 16));
    stream.CopyTo(data.AsSpan(offset));

    return data;
  }

  private static byte[] _Sample(int width, int height)
    => _File(_StripTables(JpegWriter.ToBytes(JpegFile.FromRawImage(_Picture(width, height)))), width, height);

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => KqpReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongMagic_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => KqpReader.FromBytes(new byte[256]));

  [Test]
  [Category("Unit")]
  public void FromBytes_AnOrdinaryBitmapHeaderIsNotThisFormat() {
    var jpeg = _StripTables(JpegWriter.ToBytes(JpegFile.FromRawImage(_Picture(16, 16))));

    Assert.Throws<InvalidDataException>(() => KqpReader.FromBytes(_File(jpeg, 16, 16, infoHeader: 40)));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ACompressionOtherThanJpeg_ThrowsInvalidDataException() {
    var jpeg = _StripTables(JpegWriter.ToBytes(JpegFile.FromRawImage(_Picture(16, 16))));

    Assert.Throws<InvalidDataException>(() => KqpReader.FromBytes(_File(jpeg, 16, 16, compression: "PNG ")));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AStreamThatCarriesItsOwnTablesIsNotThisFormat() {
    // These files are stored without tables and this reader supplies them, so a stream holding a set
    // of its own would be defining them twice.
    var jpeg = JpegWriter.ToBytes(JpegFile.FromRawImage(_Picture(16, 16)));

    Assert.Throws<InvalidDataException>(() => KqpReader.FromBytes(_File(jpeg, 16, 16)));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_NoJpegAtTheStatedOffset_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => KqpReader.FromBytes(_File(new byte[64], 16, 16)));

  [Test]
  [Category("Unit")]
  public void FromBytes_TheHeaderAndTheFrameMustAgreeOnTheSize() {
    var jpeg = _StripTables(JpegWriter.ToBytes(JpegFile.FromRawImage(_Picture(16, 16))));

    Assert.Throws<InvalidDataException>(() => KqpReader.FromBytes(_File(jpeg, 32, 16)));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_ATablelessStreamStillDecodesAtItsSize() {
    var decoded = KqpFile.ToRawImage(KqpReader.FromBytes(_Sample(32, 16)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(32));
      Assert.That(decoded.Height, Is.EqualTo(16));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(decoded.PixelData, Has.Length.EqualTo(32 * 16 * 3));
    });
  }
}
