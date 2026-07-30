using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Nie;

namespace FileFormat.Nie.Tests;

[TestFixture]
public sealed class NieReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NieReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NieReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".nie"));
    Assert.Throws<FileNotFoundException>(() => NieReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NieReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[8];
    Assert.Throws<InvalidDataException>(() => NieReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[NieFile.HeaderSize + 16];
    data[0] = 0xFF;
    Assert.Throws<InvalidDataException>(() => NieReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidPixelConfig_ThrowsInvalidDataException() {
    var data = _BuildNieHeader(2, 2, 0xFF);
    Assert.Throws<InvalidDataException>(() => NieReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidBgra8_ParsesCorrectly() {
    var data = _BuildNie(3, 2, NiePixelConfig.Bgra8);
    var result = NieReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(3));
      Assert.That(result.Height, Is.EqualTo(2));
      Assert.That(result.PixelConfig, Is.EqualTo(NiePixelConfig.Bgra8));
      Assert.That(result.PixelData, Has.Length.EqualTo(3 * 2 * 4));
      Assert.That(result.Is16Bit, Is.False);
      Assert.That(result.IsPremultiplied, Is.False);
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidBgra16_ParsesCorrectly() {
    var data = _BuildNie(2, 2, NiePixelConfig.Bgra16);
    var result = NieReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(2));
      Assert.That(result.Height, Is.EqualTo(2));
      Assert.That(result.PixelConfig, Is.EqualTo(NiePixelConfig.Bgra16));
      Assert.That(result.PixelData, Has.Length.EqualTo(2 * 2 * 8));
      Assert.That(result.Is16Bit, Is.True);
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_BgraPremul8_Detected() {
    var data = _BuildNie(2, 2, NiePixelConfig.BgraPremul8);
    var result = NieReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.PixelConfig, Is.EqualTo(NiePixelConfig.BgraPremul8));
      Assert.That(result.IsPremultiplied, Is.True);
      Assert.That(result.Is16Bit, Is.False);
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_BgraPremul16_Detected() {
    var data = _BuildNie(2, 2, NiePixelConfig.BgraPremul16);
    var result = NieReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.PixelConfig, Is.EqualTo(NiePixelConfig.BgraPremul16));
      Assert.That(result.IsPremultiplied, Is.True);
      Assert.That(result.Is16Bit, Is.True);
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_DataTooSmall_ThrowsInvalidDataException() {
    var data = new byte[NieFile.HeaderSize];
    data[0] = 0x6E; data[1] = 0xC3; data[2] = 0xAF; data[3] = 0x45;
    data[4] = (byte)NiePixelConfig.Bgra8;
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 100);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), 100);
    Assert.Throws<InvalidDataException>(() => NieReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ViaStream_Equivalent() {
    var data = _BuildNie(2, 2, NiePixelConfig.Bgra8);
    var fromBytes = NieReader.FromBytes(data);

    using var ms = new MemoryStream(data);
    var fromStream = NieReader.FromStream(ms);

    Assert.Multiple(() => {
      Assert.That(fromStream.Width, Is.EqualTo(fromBytes.Width));
      Assert.That(fromStream.Height, Is.EqualTo(fromBytes.Height));
      Assert.That(fromStream.PixelConfig, Is.EqualTo(fromBytes.PixelConfig));
    });
  }

  private static byte[] _BuildNieHeader(int width, int height, byte configByte) {
    var bpp = configByte is (byte)NiePixelConfig.Bgra16 or (byte)NiePixelConfig.BgraPremul16 ? 8 : 4;
    var data = new byte[NieFile.HeaderSize + width * height * bpp];
    data[0] = 0x6E; data[1] = 0xC3; data[2] = 0xAF; data[3] = 0x45;
    data[4] = configByte;
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), (uint)width);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), (uint)height);
    return data;
  }

  private static byte[] _BuildNie(int width, int height, NiePixelConfig config) {
    var bpp = config is NiePixelConfig.Bgra16 or NiePixelConfig.BgraPremul16 ? 8 : 4;
    var data = new byte[NieFile.HeaderSize + width * height * bpp];
    data[0] = 0x6E; data[1] = 0xC3; data[2] = 0xAF; data[3] = 0x45;
    data[4] = (byte)config;
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), (uint)width);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), (uint)height);
    for (var i = NieFile.HeaderSize; i < data.Length; ++i)
      data[i] = (byte)(i & 0xFF);
    return data;
  }
}
