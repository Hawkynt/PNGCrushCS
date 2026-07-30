using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Psb;

namespace FileFormat.Psb.Tests;

[TestFixture]
public sealed class PsbReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PsbReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PsbReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".psb"));
    Assert.Throws<FileNotFoundException>(() => PsbReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PsbReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[10];
    Assert.Throws<InvalidDataException>(() => PsbReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidSignature_ThrowsInvalidDataException() {
    var bad = new byte[26];
    bad[0] = (byte)'X';
    bad[1] = (byte)'Y';
    bad[2] = (byte)'Z';
    bad[3] = (byte)'W';
    Assert.Throws<InvalidDataException>(() => PsbReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Version1_ThrowsInvalidDataException() {
    var data = _BuildMinimalPsbBytes(2, 2, 3, version: 1);
    Assert.Throws<InvalidDataException>(() => PsbReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb8_ParsesCorrectly() {
    var data = _BuildMinimalPsbBytes(4, 3, 3);
    var result = PsbReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
    Assert.That(result.Channels, Is.EqualTo(3));
    Assert.That(result.Depth, Is.EqualTo(8));
    Assert.That(result.ColorMode, Is.EqualTo(PsbColorMode.RGB));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb8_ParsesPixelData() {
    var data = _BuildMinimalPsbBytes(4, 3, 3);
    var result = PsbReader.FromBytes(data);

    Assert.That(result.PixelData, Has.Length.EqualTo(4 * 3 * 3));
    for (var i = 0; i < result.PixelData.Length; ++i)
      Assert.That(result.PixelData[i], Is.EqualTo((byte)(i * 7 % 256)));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidPsb_HasVersion2() {
    var data = _BuildMinimalPsbBytes(2, 2, 3);
    var span = data.AsSpan();
    var version = BinaryPrimitives.ReadInt16BigEndian(span[4..]);
    Assert.That(version, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Uses8ByteLayerMaskInfoLength() {
    var layerMaskInfo = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
    var data = _BuildMinimalPsbBytesWithLayerMaskInfo(2, 2, 3, layerMaskInfo);
    var result = PsbReader.FromBytes(data);

    Assert.That(result.LayerMaskInfo, Is.Not.Null);
    Assert.That(result.LayerMaskInfo, Is.EqualTo(layerMaskInfo));
  }

  internal static byte[] _BuildMinimalPsbBytes(int width, int height, int channels, short version = 2) {
    var pixelDataSize = width * height * channels;
    using var ms = new MemoryStream();

    // Header (26 bytes, big-endian)
    ms.Write([(byte)'8', (byte)'B', (byte)'P', (byte)'S']);
    _WriteInt16BE(ms, version);
    ms.Write(new byte[6]);
    _WriteInt16BE(ms, (short)channels);
    _WriteInt32BE(ms, height);
    _WriteInt32BE(ms, width);
    _WriteInt16BE(ms, 8);  // depth
    _WriteInt16BE(ms, 3);  // color mode: RGB

    // Color Mode Data (empty)
    _WriteInt32BE(ms, 0);

    // Image Resources (empty)
    _WriteInt32BE(ms, 0);

    // Layer and Mask Info (PSB: 8-byte length, empty)
    _WriteInt64BE(ms, 0);

    // Image Data: Raw compression
    _WriteInt16BE(ms, 0);

    // Channel-planar pixel data
    var pixelData = new byte[pixelDataSize];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);
    ms.Write(pixelData);

    return ms.ToArray();
  }

  internal static byte[] _BuildMinimalPsbBytesWithLayerMaskInfo(int width, int height, int channels, byte[] layerMaskInfo) {
    var pixelDataSize = width * height * channels;
    using var ms = new MemoryStream();

    ms.Write([(byte)'8', (byte)'B', (byte)'P', (byte)'S']);
    _WriteInt16BE(ms, 2);
    ms.Write(new byte[6]);
    _WriteInt16BE(ms, (short)channels);
    _WriteInt32BE(ms, height);
    _WriteInt32BE(ms, width);
    _WriteInt16BE(ms, 8);
    _WriteInt16BE(ms, 3);

    // Color Mode Data (empty)
    _WriteInt32BE(ms, 0);

    // Image Resources (empty)
    _WriteInt32BE(ms, 0);

    // Layer and Mask Info (PSB: 8-byte length)
    _WriteInt64BE(ms, layerMaskInfo.Length);
    ms.Write(layerMaskInfo);

    // Image Data: Raw compression
    _WriteInt16BE(ms, 0);
    ms.Write(new byte[pixelDataSize]);

    return ms.ToArray();
  }

  internal static void _WriteInt16BE(MemoryStream ms, short value) {
    Span<byte> buf = stackalloc byte[2];
    BinaryPrimitives.WriteInt16BigEndian(buf, value);
    ms.Write(buf);
  }

  internal static void _WriteInt32BE(MemoryStream ms, int value) {
    Span<byte> buf = stackalloc byte[4];
    BinaryPrimitives.WriteInt32BigEndian(buf, value);
    ms.Write(buf);
  }

  internal static void _WriteInt64BE(MemoryStream ms, long value) {
    Span<byte> buf = stackalloc byte[8];
    BinaryPrimitives.WriteInt64BigEndian(buf, value);
    ms.Write(buf);
  }
}
