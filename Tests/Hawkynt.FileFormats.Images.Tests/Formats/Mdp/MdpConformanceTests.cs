using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Mdp.Tests;

[TestFixture]
public sealed class MdpConformanceTests {

  [Test]
  [Category("Unit")]
  public void Reader_KnownVector_DecodesPageMetadataAndArea3Raster() {
    var data = _Concat(
      _Header(MdpResolution.Dpi360, MdpPageFormat.A4Landscape, pageRamBlocks: 42, height: 4, widthBytes: 2),
      [
        0x00, 0xFF,
        0x00, 0x00,
        0x01, 0x01, 0xA5, 0x5A,
        0x02, 0x01, 0x00, 0xFF,
      ]
    );

    var file = MdpReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(16));
      Assert.That(file.Height, Is.EqualTo(4));
      Assert.That(file.SerialNumber, Is.EqualTo("1234567"));
      Assert.That(file.Resolution, Is.EqualTo(MdpResolution.Dpi360));
      Assert.That(file.PageFormat, Is.EqualTo(MdpPageFormat.A4Landscape));
      Assert.That(file.PageRamBlocks, Is.EqualTo(42));
      Assert.That(file.RasterData, Is.EqualTo(new byte[] {
        0xFF, 0xFF,
        0x00, 0x00,
        0xA5, 0x5A,
        0xA5, 0xA5,
      }));
    });
  }

  [Test]
  [Category("Unit")]
  public void Writer_KnownVector_UsesMdpStampAndArea3Compression() {
    var file = new MdpFile {
      Width = 32,
      Height = 4,
      SerialNumber = "7654321",
      Resolution = MdpResolution.Dpi300,
      PageFormat = MdpPageFormat.A5LandscapeHighResolution,
      PageRamBlocks = 17,
      RasterData = [
        0xFF, 0xFF, 0xFF, 0xFF,
        0x00, 0x01, 0x02, 0x03,
        0x00, 0x01, 0x02, 0x03,
        0x00, 0x00, 0x00, 0x00,
      ],
    };

    var encoded = MdpWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(encoded.AsSpan(0, 4).ToArray(), Is.EqualTo(".MDP"u8.ToArray()));
      Assert.That(encoded.AsSpan(4, 14).ToArray(), Is.EqualTo("MicroDesignPCW"u8.ToArray()));
      Assert.That(encoded.AsSpan(18, 5).ToArray(), Is.EqualTo("v1.30"u8.ToArray()));
      Assert.That(encoded.AsSpan(25, 7).ToArray(), Is.EqualTo("7654321"u8.ToArray()));
      Assert.That(encoded[34], Is.EqualTo((byte)MdpResolution.Dpi300));
      Assert.That(encoded[35], Is.EqualTo((byte)MdpPageFormat.A5LandscapeHighResolution));
      Assert.That(encoded[36], Is.EqualTo(17));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan(128, 2)), Is.EqualTo(4));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan(130, 2)), Is.EqualTo(4));
      Assert.That(encoded.AsSpan(132).ToArray(), Is.EqualTo(new byte[] {
        0x00, 0xFF,
        0x01, 0x03, 0x00, 0x01, 0x02, 0x03,
        0x02, 0xFD, 0x00,
        0x00, 0x00,
      }));
    });
  }

  [Test]
  [Category("Unit")]
  public void WriterReader_RoundTripsPageMetadataWithoutChangingRaster() {
    var file = new MdpFile {
      Width = 24,
      Height = 4,
      SerialNumber = "A1B2C3D",
      Resolution = MdpResolution.Dpi240,
      PageFormat = MdpPageFormat.A5PortraitHighResolution,
      PageRamBlocks = 255,
      RasterData = [
        0x00, 0xFF, 0x55,
        0x00, 0xFF, 0x55,
        0x12, 0x34, 0x56,
        0xFF, 0xFF, 0xFF,
      ],
    };

    var decoded = MdpReader.FromBytes(MdpWriter.ToBytes(file));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(file.Width));
      Assert.That(decoded.Height, Is.EqualTo(file.Height));
      Assert.That(decoded.SerialNumber, Is.EqualTo(file.SerialNumber));
      Assert.That(decoded.Resolution, Is.EqualTo(file.Resolution));
      Assert.That(decoded.PageFormat, Is.EqualTo(file.PageFormat));
      Assert.That(decoded.PageRamBlocks, Is.EqualTo(file.PageRamBlocks));
      Assert.That(decoded.RasterData, Is.EqualTo(file.RasterData));
    });
  }

  [Test]
  [Category("Unit")]
  public void RawImageFactory_RequiresAndPreservesCallerSuppliedPageMetadata() {
    var raw = new RawImage {
      Width = 8,
      Height = 4,
      Format = PixelFormat.Rgb24,
      PixelData = [
        255,255,255, 0,0,0, 255,255,255, 0,0,0, 0,0,0, 255,255,255, 0,0,0, 255,255,255,
        255,255,255, 0,0,0, 255,255,255, 0,0,0, 0,0,0, 255,255,255, 0,0,0, 255,255,255,
        255,255,255, 0,0,0, 255,255,255, 0,0,0, 0,0,0, 255,255,255, 0,0,0, 255,255,255,
        255,255,255, 0,0,0, 255,255,255, 0,0,0, 0,0,0, 255,255,255, 0,0,0, 255,255,255,
      ],
    };

    var file = MdpFile.FromRawImage(
      raw,
      MdpResolution.Dpi360,
      MdpPageFormat.A4Portrait,
      pageRamBlocks: 31,
      serialNumber: "RAWTEST"
    );
    var decoded = MdpFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(file.Resolution, Is.EqualTo(MdpResolution.Dpi360));
      Assert.That(file.PageFormat, Is.EqualTo(MdpPageFormat.A4Portrait));
      Assert.That(file.PageRamBlocks, Is.EqualTo(31));
      Assert.That(file.SerialNumber, Is.EqualTo("RAWTEST"));
      Assert.That(file.RasterData, Is.EqualTo(new byte[] { 0xA5, 0xA5, 0xA5, 0xA5 }));
      Assert.That(decoded.PixelData.AsSpan(0, 8).ToArray(), Is.EqualTo(new byte[] { 1, 0, 1, 0, 0, 1, 0, 1 }));
    });
  }

  [TestCase(3)]
  [TestCase(255)]
  [Category("Unit")]
  public void Reader_InvalidResolutionCode_IsRejected(int code) {
    var data = _Header(MdpResolution.Dpi240, MdpPageFormat.A5Portrait, 1, 4, 1);
    data[34] = (byte)code;

    var exception = Assert.Throws<InvalidDataException>(() => MdpReader.FromBytes(data));
    Assert.That(exception!.Message, Does.Contain("resolution"));
  }

  [TestCase(6)]
  [TestCase(255)]
  [Category("Unit")]
  public void Reader_InvalidPageFormatCode_IsRejected(int code) {
    var data = _Header(MdpResolution.Dpi240, MdpPageFormat.A5Portrait, 1, 4, 1);
    data[35] = (byte)code;

    var exception = Assert.Throws<InvalidDataException>(() => MdpReader.FromBytes(data));
    Assert.That(exception!.Message, Does.Contain("format code"));
  }

  [Test]
  [Category("Unit")]
  public void Reader_Area2Version_IsRejected() {
    var data = _Header(MdpResolution.Dpi240, MdpPageFormat.A5Portrait, 1, 4, 1);
    "v1.00"u8.CopyTo(data.AsSpan(18));

    var exception = Assert.Throws<InvalidDataException>(() => MdpReader.FromBytes(data));
    Assert.That(exception!.Message, Does.Contain("v1.30"));
  }

  [Test]
  [Category("Unit")]
  public void Reader_NonZeroReservedStampByte_IsRejectedBySharedArea3Validation() {
    var data = _Concat(
      _Header(MdpResolution.Dpi240, MdpPageFormat.A5Portrait, 1, 4, 1),
      [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]
    );
    data[37] = 1;

    var exception = Assert.Throws<InvalidDataException>(() => MdpReader.FromBytes(data));
    Assert.That(exception!.Message, Does.Contain("reserved"));
  }

  [Test]
  [Category("Unit")]
  public void Reader_MalformedArea3Payload_IsRejectedBySharedCodec() {
    var data = _Concat(
      _Header(MdpResolution.Dpi240, MdpPageFormat.A5Portrait, 1, 4, 1),
      [0x02, 0x00, 0x00]
    );

    var exception = Assert.Throws<InvalidDataException>(() => MdpReader.FromBytes(data));
    Assert.That(exception!.Message, Does.Contain("first line"));
  }

  [Test]
  [Category("Unit")]
  public void Writer_InvalidMetadataEnum_IsRejected() {
    var file = new MdpFile {
      Width = 8,
      Height = 4,
      SerialNumber = "1234567",
      Resolution = (MdpResolution)99,
      PageFormat = MdpPageFormat.A5Portrait,
      PageRamBlocks = 1,
      RasterData = [0, 0, 0, 0],
    };

    Assert.Throws<ArgumentOutOfRangeException>(() => MdpWriter.ToBytes(file));
  }

  private static byte[] _Header(
    MdpResolution resolution,
    MdpPageFormat pageFormat,
    byte pageRamBlocks,
    ushort height,
    ushort widthBytes
  ) {
    var result = new byte[132];
    ".MDP"u8.CopyTo(result);
    "MicroDesignPCW"u8.CopyTo(result.AsSpan(4));
    "v1.30"u8.CopyTo(result.AsSpan(18));
    result[23] = 13;
    result[24] = 10;
    "1234567"u8.CopyTo(result.AsSpan(25));
    result[32] = 13;
    result[33] = 10;
    result[34] = (byte)resolution;
    result[35] = (byte)pageFormat;
    result[36] = pageRamBlocks;
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
