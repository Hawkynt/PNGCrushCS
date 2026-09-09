using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.Mrw;

namespace FileFormat.Mrw.Tests;

[TestFixture]
public sealed class MrwTests {

  private static void _WriteBlockHeader(Stream target, ReadOnlySpan<byte> name, int length) {
    target.Write(name);
    Span<byte> word = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(word, (uint)length);
    target.Write(word);
  }

  /// <summary>A picture block stating the sensor and the picture cut out of it.</summary>
  private static byte[] _PictureBlock(int sensorWidth, int sensorHeight, int width, int height, byte bits = 12) {
    var block = new byte[MrwFile.PictureBlockSize];
    BinaryPrimitives.WriteUInt16BigEndian(block.AsSpan(8), (ushort)sensorHeight);
    BinaryPrimitives.WriteUInt16BigEndian(block.AsSpan(10), (ushort)sensorWidth);
    BinaryPrimitives.WriteUInt16BigEndian(block.AsSpan(12), (ushort)height);
    BinaryPrimitives.WriteUInt16BigEndian(block.AsSpan(14), (ushort)width);
    block[16] = bits;
    block[17] = bits;
    return block;
  }

  /// <summary>A whole file: the header, the blocks, and twelve-bit samples filling the sensor.</summary>
  private static byte[] _File(int sensorWidth, int sensorHeight, int width, int height, byte bits = 12, int extra = 0) {
    var picture = _PictureBlock(sensorWidth, sensorHeight, width, height, bits);

    using var blocks = new MemoryStream();
    _WriteBlockHeader(blocks, MrwFile.PictureBlock, picture.Length);
    blocks.Write(picture);

    // Red, the first green, the second green, blue.
    var balance = new byte[12];
    BinaryPrimitives.WriteUInt16BigEndian(balance.AsSpan(4), 341);
    BinaryPrimitives.WriteUInt16BigEndian(balance.AsSpan(6), 256);
    BinaryPrimitives.WriteUInt16BigEndian(balance.AsSpan(8), 256);
    BinaryPrimitives.WriteUInt16BigEndian(balance.AsSpan(10), 539);
    _WriteBlockHeader(blocks, MrwFile.WhiteBalanceBlock, balance.Length);
    blocks.Write(balance);

    var body = blocks.ToArray();

    using var ms = new MemoryStream();
    ms.Write(MrwFile.Magic);
    Span<byte> word = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(word, (uint)body.Length);
    ms.Write(word);
    ms.Write(body);
    ms.Write(new byte[sensorWidth * sensorHeight / 2 * 3 + extra]);

    return ms.ToArray();
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => MrwReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongMagic_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => MrwReader.FromBytes(new byte[256]));

  [Test]
  [Category("Unit")]
  public void FromBytes_NoPictureBlock_ThrowsInvalidDataException() {
    var data = _File(16, 8, 16, 8);
    data[MrwFile.HeaderSize + 1] = (byte)'X';

    Assert.Throws<InvalidDataException>(() => MrwReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TheSensorMustAccountForTheRestOfTheFile()
    => Assert.Throws<InvalidDataException>(() => MrwReader.FromBytes(_File(16, 8, 16, 8, extra: 1)));

  [Test]
  [Category("Unit")]
  public void FromBytes_ADepthThisDoesNotRead_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => MrwReader.FromBytes(_File(16, 8, 16, 8, bits: 14)));

  [Test]
  [Category("Unit")]
  public void FromBytes_APictureLargerThanItsSensor_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => MrwReader.FromBytes(_File(16, 8, 32, 8)));

  [Test]
  [Category("Unit")]
  public void FromBytes_ThePictureIsCutOutOfTheSensorArray() {
    // The sensor is wider and taller than the picture; what comes back is the picture's size and not
    // the array's.
    var decoded = MrwFile.ToRawImage(MrwReader.FromBytes(_File(24, 12, 16, 8)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(16));
      Assert.That(decoded.Height, Is.EqualTo(8));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(decoded.PixelData, Has.Length.EqualTo(16 * 8 * 3));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TwelveBitSamplesUnpackTwoToEveryThreeBytes() {
    // 0xAB 0xCD 0xEF is the pair 0xABC and 0xDEF, so the two Bayer greens of the first row are given
    // very different values and the demosaic cannot come back flat.
    var data = _File(8, 4, 8, 4);
    var sensorAt = data.Length - 8 * 4 / 2 * 3;
    for (var i = sensorAt; i + 2 < data.Length; i += 3) {
      data[i] = 0xAB;
      data[i + 1] = 0xCD;
      data[i + 2] = 0xEF;
    }

    var file = MrwReader.FromBytes(data);

    Assert.That(file.PixelData, Has.Some.Not.EqualTo(file.PixelData[0]), "the unpacked samples are not all one value");
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesCanonicalPackedRggbBlocksAndAlignsTheSensor() {
    var data = MrwWriter.ToBytes(new() {
      Width = 8,
      Height = 4,
      PixelData = new byte[8 * 4 * 3],
    });

    Assert.Multiple(() => {
      Assert.That(data[..4], Is.EqualTo(MrwFile.Magic.ToArray()));
      Assert.That(BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4)), Is.EqualTo(504));
      Assert.That(data[8..12], Is.EqualTo(MrwFile.PictureBlock.ToArray()));
      Assert.That(BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(12)), Is.EqualTo(24));
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(24)), Is.EqualTo(4));
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(26)), Is.EqualTo(8));
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(28)), Is.EqualTo(4));
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(30)), Is.EqualTo(8));
      Assert.That(data[32], Is.EqualTo(12));
      Assert.That(data[33], Is.EqualTo(12));
      Assert.That(data[34], Is.EqualTo(0x59));
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(38)), Is.EqualTo(1));
      Assert.That(data[40..44], Is.EqualTo(MrwFile.WhiteBalanceBlock.ToArray()));
      Assert.That(data[48..52], Is.EqualTo(new byte[] { 2, 2, 2, 2 }));
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(52)), Is.EqualTo(256));
      Assert.That(data[60..64], Is.EqualTo(new byte[] { 0, (byte)'P', (byte)'A', (byte)'D' }));
      Assert.That(BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(64)), Is.EqualTo(444));
      Assert.That(data.Length, Is.EqualTo(512 + 8 * 4 / 2 * 3));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PacksRgbIntoRggbTwelveBitSamples() {
    var data = MrwWriter.ToBytes(new() {
      Width = 2,
      Height = 2,
      PixelData = [
        0xAB, 0x01, 0x02,  0x03, 0xCD, 0x04,
        0x05, 0x12, 0x06,  0x07, 0x08, 0x34,
      ],
    });

    Assert.That(data[512..], Is.EqualTo(new byte[] { 0xAB, 0xAC, 0xDC, 0x12, 0x13, 0x43 }));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ResultIsReadableByMrwReader() {
    var source = new RawImage {
      Width = 8,
      Height = 4,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[8 * 4 * 3],
    };

    var encoded = MrwWriter.ToBytes(MrwFile.FromRawImage(source));
    var decoded = MrwReader.FromBytes(encoded);

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(source.Width));
      Assert.That(decoded.Height, Is.EqualTo(source.Height));
      Assert.That(decoded.PixelData, Has.Length.EqualTo(source.PixelData.Length));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_OddWidth_ThrowsArgumentException()
    => Assert.Throws<ArgumentException>(() => MrwWriter.ToBytes(new() {
      Width = 3,
      Height = 2,
      PixelData = new byte[3 * 2 * 3],
    }));

  [Test]
  [Category("Unit")]
  public void ToBytes_WrongRgbByteCount_ThrowsArgumentException()
    => Assert.Throws<ArgumentException>(() => MrwWriter.ToBytes(new() {
      Width = 4,
      Height = 2,
      PixelData = new byte[4 * 2 * 3 - 1],
    }));
}
