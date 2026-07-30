using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Psd;

namespace FileFormat.Psd.Tests;

[TestFixture]
public sealed class PsdReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PsdReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PsdReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".psd"));
    Assert.Throws<FileNotFoundException>(() => PsdReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PsdReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[10];
    Assert.Throws<InvalidDataException>(() => PsdReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidSignature_ThrowsInvalidDataException() {
    var bad = new byte[26];
    bad[0] = (byte)'X';
    bad[1] = (byte)'Y';
    bad[2] = (byte)'Z';
    bad[3] = (byte)'W';
    Assert.Throws<InvalidDataException>(() => PsdReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb8_ParsesCorrectly() {
    var data = _BuildMinimalRgb8Psd(4, 3, 3);
    var result = PsdReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
    Assert.That(result.Channels, Is.EqualTo(3));
    Assert.That(result.Depth, Is.EqualTo(8));
    Assert.That(result.ColorMode, Is.EqualTo(PsdColorMode.RGB));
  }

  internal static byte[] _BuildMinimalRgb8Psd(int width, int height, int channels) {
    var pixelDataSize = width * height * channels;
    using var ms = new MemoryStream();

    // Header (26 bytes, big-endian)
    ms.Write(new byte[] { (byte)'8', (byte)'B', (byte)'P', (byte)'S' }); // signature
    _WriteInt16BE(ms, 1);           // version
    ms.Write(new byte[6]);          // reserved
    _WriteInt16BE(ms, (short)channels);
    _WriteInt32BE(ms, height);
    _WriteInt32BE(ms, width);
    _WriteInt16BE(ms, 8);           // depth
    _WriteInt16BE(ms, 3);           // color mode: RGB

    // Color Mode Data (empty)
    _WriteInt32BE(ms, 0);

    // Image Resources (empty)
    _WriteInt32BE(ms, 0);

    // Layer and Mask Info (empty)
    _WriteInt32BE(ms, 0);

    // Image Data: Raw compression
    _WriteInt16BE(ms, 0);

    // Channel-planar pixel data
    var pixelData = new byte[pixelDataSize];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);
    ms.Write(pixelData);

    return ms.ToArray();
  }

  private static void _WriteInt16BE(MemoryStream ms, short value) {
    Span<byte> buf = stackalloc byte[2];
    BinaryPrimitives.WriteInt16BigEndian(buf, value);
    ms.Write(buf);
  }

  private static void _WriteInt32BE(MemoryStream ms, int value) {
    Span<byte> buf = stackalloc byte[4];
    BinaryPrimitives.WriteInt32BigEndian(buf, value);
    ms.Write(buf);
  }
}
