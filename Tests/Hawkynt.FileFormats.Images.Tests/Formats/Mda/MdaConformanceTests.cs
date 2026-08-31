using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.Mda.Tests;

[TestFixture]
public sealed class MdaConformanceTests {

  [Test]
  [Category("Unit")]
  public void Reader_Area2ZeroCount_Expands256BytesAcrossRows() {
    var header = _Header(MdaVersion.Area2, height: 4, widthBytes: 64);
    var data = _Concat(header, [0x00, 0x00]);

    var file = MdaReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(512));
      Assert.That(file.Height, Is.EqualTo(4));
      Assert.That(file.Version, Is.EqualTo(MdaVersion.Area2));
      Assert.That(file.RasterData, Is.EqualTo(new byte[256]));
    });
  }

  [Test]
  [Category("Unit")]
  public void Writer_Area2_KnownVector_MatchesSpecifiedRunEncoding() {
    var file = new MdaFile {
      Width = 16,
      Height = 4,
      Version = MdaVersion.Area2,
      SerialNumber = "1234567",
      RasterData = [0x0F, 0xCC, 0xF0, 0x00, 0x3F, 0xFF, 0xFF, 0xF0],
    };

    var encoded = MdaWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(encoded.AsSpan(0, 4).ToArray(), Is.EqualTo(".MDA"u8.ToArray()));
      Assert.That(encoded.AsSpan(4, 14).ToArray(), Is.EqualTo("MicroDesignPCW"u8.ToArray()));
      Assert.That(encoded.AsSpan(18, 5).ToArray(), Is.EqualTo("v1.00"u8.ToArray()));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan(128, 2)), Is.EqualTo(4));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan(130, 2)), Is.EqualTo(2));
      Assert.That(encoded.AsSpan(132).ToArray(), Is.EqualTo(new byte[] {
        0x0F, 0xCC, 0xF0, 0x00, 0x01, 0x3F, 0xFF, 0x02, 0xF0,
      }));
    });
  }

  [Test]
  [Category("Unit")]
  public void Reader_Area3_DecodesDataRepeatDifferenceAndSameLineTypes() {
    var header = _Header(MdaVersion.Area3, height: 4, widthBytes: 4);
    var payload = new byte[] {
      0x01, 0x03, 0x0F, 0xCC, 0xF0, 0x00,
      0x01, 0xFD, 0xAA,
      0x02, 0x03, 0x00, 0xFF, 0x00, 0xFF,
      0x00, 0xFF,
    };

    var file = MdaReader.FromBytes(_Concat(header, payload));

    Assert.That(file.RasterData, Is.EqualTo(new byte[] {
      0x0F, 0xCC, 0xF0, 0x00,
      0xAA, 0xAA, 0xAA, 0xAA,
      0xAA, 0x55, 0xAA, 0x55,
      0xFF, 0xFF, 0xFF, 0xFF,
    }));
  }

  [Test]
  [Category("Unit")]
  public void Reader_Area3Negative127Control_Expands128Bytes() {
    var header = _Header(MdaVersion.Area3, height: 4, widthBytes: 128);
    var payload = new byte[] {
      0x01, 0x81, 0x5A,
      0x00, 0x00,
      0x00, 0xFF,
      0x00, 0x11,
    };

    var file = MdaReader.FromBytes(_Concat(header, payload));

    Assert.Multiple(() => {
      Assert.That(file.RasterData.AsSpan(0, 128).ToArray(), Is.EqualTo(Enumerable.Repeat((byte)0x5A, 128).ToArray()));
      Assert.That(file.RasterData.AsSpan(128, 128).ToArray(), Is.EqualTo(new byte[128]));
      Assert.That(file.RasterData.AsSpan(256, 128).ToArray(), Is.EqualTo(Enumerable.Repeat((byte)0xFF, 128).ToArray()));
      Assert.That(file.RasterData.AsSpan(384, 128).ToArray(), Is.EqualTo(Enumerable.Repeat((byte)0x11, 128).ToArray()));
    });
  }

  [Test]
  [Category("Unit")]
  public void Writer_Area3_DeterministicallyChoosesSameDataAndDifferenceLines() {
    var file = new MdaFile {
      Width = 32,
      Height = 4,
      Version = MdaVersion.Area3,
      SerialNumber = "7654321",
      RasterData = [
        0xFF, 0xFF, 0xFF, 0xFF,
        0x00, 0x01, 0x02, 0x03,
        0x00, 0x01, 0x02, 0x03,
        0x00, 0x00, 0x00, 0x00,
      ],
    };

    var encoded = MdaWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(encoded.AsSpan(18, 5).ToArray(), Is.EqualTo("v1.30"u8.ToArray()));
      Assert.That(encoded.AsSpan(25, 7).ToArray(), Is.EqualTo("7654321"u8.ToArray()));
      Assert.That(encoded.AsSpan(132).ToArray(), Is.EqualTo(new byte[] {
        0x00, 0xFF,
        0x01, 0x03, 0x00, 0x01, 0x02, 0x03,
        0x02, 0xFD, 0x00,
        0x00, 0x00,
      }));
    });
  }

  [TestCase(MdaVersion.Area2)]
  [TestCase(MdaVersion.Area3)]
  [Category("Unit")]
  public void WriterReader_RoundTripsRasterAndSerial(MdaVersion version) {
    var file = new MdaFile {
      Width = 24,
      Height = 4,
      Version = version,
      SerialNumber = "A1B2C3D",
      RasterData = [
        0x00, 0xFF, 0x55,
        0x00, 0xFF, 0x55,
        0x12, 0x34, 0x56,
        0xFF, 0xFF, 0xFF,
      ],
    };

    var decoded = MdaReader.FromBytes(MdaWriter.ToBytes(file));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(file.Width));
      Assert.That(decoded.Height, Is.EqualTo(file.Height));
      Assert.That(decoded.Version, Is.EqualTo(version));
      Assert.That(decoded.SerialNumber, Is.EqualTo(file.SerialNumber));
      Assert.That(decoded.RasterData, Is.EqualTo(file.RasterData));
    });
  }

  [Test]
  [Category("Unit")]
  public void RawImageConversion_UsesMsbLeftAndOneMeansWhite() {
    var row = new byte[] {
      255, 255, 255,
      0, 0, 0,
      255, 255, 255,
      0, 0, 0,
      0, 0, 0,
      255, 255, 255,
      0, 0, 0,
      255, 255, 255,
    };
    var raw = new RawImage {
      Width = 8,
      Height = 4,
      Format = PixelFormat.Rgb24,
      PixelData = Enumerable.Repeat(row, 4).SelectMany(static bytes => bytes).ToArray(),
    };

    var file = MdaFile.FromRawImage(raw);
    var decoded = MdaFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(file.Version, Is.EqualTo(MdaVersion.Area3));
      Assert.That(file.SerialNumber, Is.EqualTo(MdaFile.DefaultSerialNumber));
      Assert.That(file.RasterData, Is.EqualTo(new byte[] { 0xA5, 0xA5, 0xA5, 0xA5 }));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Indexed8));
      Assert.That(decoded.Palette, Is.EqualTo(new byte[] { 0, 0, 0, 255, 255, 255 }));
      Assert.That(decoded.PixelData.AsSpan(0, 8).ToArray(), Is.EqualTo(new byte[] { 1, 0, 1, 0, 0, 1, 0, 1 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void Reader_FirstArea3LineCannotBeDifferenceEncoded() {
    var data = _Concat(_Header(MdaVersion.Area3, height: 4, widthBytes: 1), [
      0x02, 0x00, 0x00,
      0x00, 0x00,
      0x00, 0x00,
      0x00, 0x00,
    ]);

    var exception = Assert.Throws<InvalidDataException>(() => MdaReader.FromBytes(data));
    Assert.That(exception!.Message, Does.Contain("first line"));
  }

  [Test]
  [Category("Unit")]
  public void Reader_Area3ReservedControl80_IsRejected() {
    var data = _Concat(_Header(MdaVersion.Area3, height: 4, widthBytes: 1), [
      0x01, 0x80,
      0x00, 0x00,
      0x00, 0x00,
      0x00, 0x00,
    ]);

    var exception = Assert.Throws<InvalidDataException>(() => MdaReader.FromBytes(data));
    Assert.That(exception!.Message, Does.Contain("0x80"));
  }

  [Test]
  [Category("Unit")]
  public void Reader_Area3LiteralOverrun_IsRejected() {
    var data = _Concat(_Header(MdaVersion.Area3, height: 4, widthBytes: 1), [
      0x01, 0x01, 0x11, 0x22,
      0x00, 0x00,
      0x00, 0x00,
      0x00, 0x00,
    ]);

    var exception = Assert.Throws<InvalidDataException>(() => MdaReader.FromBytes(data));
    Assert.That(exception!.Message, Does.Contain("overruns"));
  }

  [Test]
  [Category("Unit")]
  public void Reader_TruncatedArea2Run_IsRejected() {
    var data = _Concat(_Header(MdaVersion.Area2, height: 4, widthBytes: 1), [0x00]);

    var exception = Assert.Throws<InvalidDataException>(() => MdaReader.FromBytes(data));
    Assert.That(exception!.Message, Does.Contain("run"));
  }

  [Test]
  [Category("Unit")]
  public void Reader_TrailingCompressedData_IsRejected() {
    var data = _Concat(_Header(MdaVersion.Area2, height: 4, widthBytes: 1), [
      0x11, 0x22, 0x33, 0x44, 0x55,
    ]);

    var exception = Assert.Throws<InvalidDataException>(() => MdaReader.FromBytes(data));
    Assert.That(exception!.Message, Does.Contain("trailing"));
  }

  [Test]
  [Category("Unit")]
  public void Reader_InvalidProgramIdentifier_IsRejected() {
    var data = _Header(MdaVersion.Area2, height: 4, widthBytes: 1);
    data[4] = (byte)'X';

    var exception = Assert.Throws<InvalidDataException>(() => MdaReader.FromBytes(data));
    Assert.That(exception!.Message, Does.Contain("program identifier"));
  }

  [Test]
  [Category("Unit")]
  public void Reader_NonZeroReservedStampData_IsRejected() {
    var data = _Header(MdaVersion.Area2, height: 4, widthBytes: 1);
    data[34] = 1;

    var exception = Assert.Throws<InvalidDataException>(() => MdaReader.FromBytes(data));
    Assert.That(exception!.Message, Does.Contain("reserved"));
  }

  [Test]
  [Category("Unit")]
  public void Reader_ExcessiveDimensions_AreRejectedBeforeAllocation() {
    var data = _Header(MdaVersion.Area2, height: 65532, widthBytes: 65535);

    var exception = Assert.Throws<InvalidDataException>(() => MdaReader.FromBytes(data));
    Assert.That(exception!.Message, Does.Contain("safety limit"));
  }

  [TestCase(7, 4)]
  [TestCase(8, 3)]
  [Category("Unit")]
  public void FromRawImage_UnrepresentableDimensions_AreRejected(int width, int height) {
    var raw = new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[width * height * 3],
    };

    Assert.Throws<ArgumentOutOfRangeException>(() => MdaFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void Writer_SerialMustBeExactlySevenPrintableAsciiCharacters() {
    var file = new MdaFile {
      Width = 8,
      Height = 4,
      Version = MdaVersion.Area2,
      SerialNumber = "short",
      RasterData = [0, 0, 0, 0],
    };

    Assert.Throws<ArgumentException>(() => MdaWriter.ToBytes(file));
  }

  private static byte[] _Header(MdaVersion version, ushort height, ushort widthBytes) {
    var result = new byte[132];
    ".MDA"u8.CopyTo(result);
    "MicroDesignPCW"u8.CopyTo(result.AsSpan(4));
    if (version == MdaVersion.Area2)
      "v1.00"u8.CopyTo(result.AsSpan(18));
    else
      "v1.30"u8.CopyTo(result.AsSpan(18));
    result[23] = 13;
    result[24] = 10;
    "1234567"u8.CopyTo(result.AsSpan(25));
    result[32] = 13;
    result[33] = 10;
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(128, 2), height);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(130, 2), widthBytes);
    return result;
  }

  private static byte[] _Concat(byte[] prefix, byte[] payload) {
    var result = new byte[prefix.Length + payload.Length];
    prefix.CopyTo(result, 0);
    payload.CopyTo(result, prefix.Length);
    return result;
  }
}
