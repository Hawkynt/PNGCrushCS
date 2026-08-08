using System;
using System.IO;
using FileFormat.BodyPaint3D;
using FileFormat.Core;

namespace FileFormat.BodyPaint3D.Tests;

[TestFixture]
public sealed class BodyPaint3DTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => BodyPaint3DReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => BodyPaint3DReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(
      () => BodyPaint3DReader.FromFile(new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".b3d"))));

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => BodyPaint3DReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => BodyPaint3DReader.FromBytes(new byte[4]));

  [Test]
  [Category("Unit")]
  public void FromBytes_ForeignFile_ThrowsInvalidDataException() {
    var png = new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13 };
    Assert.Throws<InvalidDataException>(() => BodyPaint3DReader.FromBytes(png));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_SignatureButNothingBehindIt_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => BodyPaint3DReader.FromBytes(BodyPaint3DFile.Magic.ToArray()));

  [Test]
  [Category("Unit")]
  public void FromBytes_UnknownTag_ThrowsInvalidDataException() {
    var data = new byte[BodyPaint3DFile.Magic.Length + 1];
    BodyPaint3DFile.Magic.CopyTo(data);
    data[^1] = 0x77;
    Assert.Throws<InvalidDataException>(() => BodyPaint3DReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_MinimalTexture_ReadsThePicture() {
    var data = _Build(2, 2, planes: 3);
    var file = BodyPaint3DReader.FromBytes(data);

    Assert.That(file.Width, Is.EqualTo(2));
    Assert.That(file.Height, Is.EqualTo(2));
    Assert.That(file.Planes, Is.EqualTo(3));

    var raw = BodyPaint3DFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.PixelData.Length, Is.EqualTo(2 * 2 * 3));

    // Row k of the stream is channel k mod planes of picture row k div planes, so the first pixel
    // takes its red from scanline 0, its green from scanline 1 and its blue from scanline 2.
    Assert.That(raw.PixelData[0], Is.EqualTo(0));
    Assert.That(raw.PixelData[1], Is.EqualTo(1));
    Assert.That(raw.PixelData[2], Is.EqualTo(2));
    Assert.That(raw.PixelData[6], Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ScanlineShorterThanTheWidth_ThrowsInvalidDataException() {
    var data = _Build(2, 2, planes: 1, scanlinePixels: 1);
    Assert.Throws<InvalidDataException>(() => BodyPaint3DReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_UnknownCompression_ThrowsInvalidDataException() {
    var data = _Build(2, 2, planes: 1, method: 9);
    Assert.Throws<InvalidDataException>(() => BodyPaint3DReader.FromBytes(data));
  }

  /// <summary>
  /// A texture in the tag stream the format uses: the header record, then a bitmap record whose
  /// scanlines are PackBits literal runs carrying the scanline's own index.
  /// </summary>
  private static byte[] _Build(int width, int height, int planes, int? scanlinePixels = null, byte method = BodyPaint3DFile.MethodPackBits) {
    using var ms = new MemoryStream();
    ms.Write(BodyPaint3DFile.Magic);

    _Begin(ms, BodyPaint3DFile.ClassTexture, 1);
    _Int(ms, width);
    _Int(ms, height);
    _Int(ms, planes == 1 ? 2 : 4);
    ms.WriteByte(BodyPaint3DFile.TagEnd);

    _Begin(ms, BodyPaint3DFile.ClassBitmap, 1);
    _Int(ms, 0);
    _Int(ms, 0);
    _Int(ms, width);
    _Int(ms, height);
    _Int(ms, planes);

    var count = scanlinePixels ?? width;
    for (var row = 0; row < height * planes; ++row) {
      ms.WriteByte(BodyPaint3DFile.TagScanline);
      ms.WriteByte(method);
      ms.WriteByte(BodyPaint3DFile.TagByteArray);
      _UInt32(ms, (uint)(1 + count));
      ms.WriteByte((byte)(count - 1));
      for (var x = 0; x < count; ++x)
        ms.WriteByte((byte)row);
    }

    ms.WriteByte(BodyPaint3DFile.TagEnd);
    return ms.ToArray();
  }

  private static void _Begin(Stream stream, uint klass, uint subtype) {
    stream.WriteByte(BodyPaint3DFile.TagBegin);
    _UInt32(stream, klass);
    _UInt32(stream, subtype);
  }

  private static void _Int(Stream stream, int value) {
    stream.WriteByte(BodyPaint3DFile.TagInt32);
    _UInt32(stream, (uint)value);
  }

  private static void _UInt32(Stream stream, uint value) {
    stream.WriteByte((byte)(value >> 24));
    stream.WriteByte((byte)(value >> 16));
    stream.WriteByte((byte)(value >> 8));
    stream.WriteByte((byte)value);
  }
}
