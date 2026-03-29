using System;
using System.IO;
using FileFormat.Pkm;

namespace FileFormat.Pkm.Tests;

[TestFixture]
public sealed class PkmReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PkmReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PkmReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var file = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pkm"));
    Assert.Throws<FileNotFoundException>(() => PkmReader.FromFile(file));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var data = new byte[8];
    Assert.Throws<InvalidDataException>(() => PkmReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[16];
    data[0] = (byte)'X';
    data[1] = (byte)'Y';
    data[2] = (byte)'Z';
    data[3] = (byte)'!';
    Assert.Throws<InvalidDataException>(() => PkmReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidV10_ParsesCorrectly() {
    var data = _BuildPkmBytes("10", PkmFormat.Etc1Rgb, 64, 32, 16, 16, new byte[8]);

    var result = PkmReader.FromBytes(data);

    Assert.That(result.Version, Is.EqualTo("10"));
    Assert.That(result.Format, Is.EqualTo(PkmFormat.Etc1Rgb));
    Assert.That(result.PaddedWidth, Is.EqualTo(64));
    Assert.That(result.PaddedHeight, Is.EqualTo(32));
    Assert.That(result.Width, Is.EqualTo(16));
    Assert.That(result.Height, Is.EqualTo(16));
    Assert.That(result.CompressedData.Length, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidV20_ParsesCorrectly() {
    var data = _BuildPkmBytes("20", PkmFormat.Etc2Rgba8, 128, 128, 100, 100, new byte[64]);

    var result = PkmReader.FromBytes(data);

    Assert.That(result.Version, Is.EqualTo("20"));
    Assert.That(result.Format, Is.EqualTo(PkmFormat.Etc2Rgba8));
    Assert.That(result.Width, Is.EqualTo(100));
    Assert.That(result.Height, Is.EqualTo(100));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = _BuildPkmBytes("10", PkmFormat.Etc1Rgb, 4, 4, 4, 4, new byte[8]);
    using var ms = new MemoryStream(data);

    var result = PkmReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(4));
    Assert.That(result.Version, Is.EqualTo("10"));
  }

  private static byte[] _BuildPkmBytes(string version, PkmFormat format, ushort paddedWidth, ushort paddedHeight, ushort width, ushort height, byte[] compressedData) {
    var result = new byte[16 + compressedData.Length];
    result[0] = (byte)'P';
    result[1] = (byte)'K';
    result[2] = (byte)'M';
    result[3] = (byte)' ';
    result[4] = (byte)version[0];
    result[5] = (byte)version[1];
    _WriteUInt16BE(result, 6, (ushort)format);
    _WriteUInt16BE(result, 8, paddedWidth);
    _WriteUInt16BE(result, 10, paddedHeight);
    _WriteUInt16BE(result, 12, width);
    _WriteUInt16BE(result, 14, height);
    Array.Copy(compressedData, 0, result, 16, compressedData.Length);
    return result;
  }

  private static void _WriteUInt16BE(byte[] buffer, int offset, ushort value) {
    buffer[offset] = (byte)(value >> 8);
    buffer[offset + 1] = (byte)(value & 0xFF);
  }
}
