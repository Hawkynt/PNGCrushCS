using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.DrHalo;

namespace FileFormat.MicroDesignCut.Tests;

[TestFixture]
public sealed class MicroDesignCutConformanceTests {

  [Test]
  [Category("Unit")]
  public void Reader_DecodesCodesAndPreservesWholeStoredRows() {
    var data = new byte[] {
      0x01, 0x00, // height code 1 -> 2 rows
      0x06, 0x00, // width code 6 -> 8 pixels
      0x80, 0x55, // second byte is wholly unused because width is exactly 8
      0x01, 0xAA,
    };

    var file = MicroDesignCutReader.FromBytes(data);
    var raw = MicroDesignCutFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(file.HeightCode, Is.EqualTo(1));
      Assert.That(file.WidthCode, Is.EqualTo(6));
      Assert.That(file.Width, Is.EqualTo(8));
      Assert.That(file.Height, Is.EqualTo(2));
      Assert.That(MicroDesignCutFile.GetRowStride(file.Width), Is.EqualTo(2));
      Assert.That(file.RasterData, Is.EqualTo(new byte[] { 0x80, 0x55, 0x01, 0xAA }));
      Assert.That(raw.PixelData.AsSpan(0, 8).ToArray(), Is.EqualTo(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }));
      Assert.That(raw.PixelData.AsSpan(8, 8).ToArray(), Is.EqualTo(new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 }));
      Assert.That(raw.Palette, Is.EqualTo(new byte[] { 0, 0, 0, 255, 255, 255 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void Writer_PreservesRawHeightCodeAndPaddingBytesExactly() {
    var file = new MicroDesignCutFile {
      HeightCode = 2,
      WidthCode = 6,
      RasterData = [0xF0, 0x5A, 0x0F, 0xA5],
    };

    var encoded = MicroDesignCutWriter.ToBytes(file);

    Assert.That(encoded, Is.EqualTo(new byte[] {
      0x02, 0x00,
      0x06, 0x00,
      0xF0, 0x5A,
      0x0F, 0xA5,
    }));
  }

  [Test]
  [Category("Unit")]
  public void HeightCode_IntegerRule_HasHistoricalAliases() {
    Assert.Multiple(() => {
      Assert.That(MicroDesignCutFile.GetHeight(1), Is.EqualTo(2));
      Assert.That(MicroDesignCutFile.GetHeight(2), Is.EqualTo(2));
      Assert.That(MicroDesignCutFile.GetHeight(3), Is.EqualTo(3));
      Assert.That(MicroDesignCutFile.GetHeight(4), Is.EqualTo(3));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_RequiresCallerSelectedMatchingHeightCode() {
    var pixels = new byte[8 * 2 * 3];
    Array.Fill(pixels, (byte)255, 0, 8 * 3);
    var raw = new RawImage {
      Width = 8,
      Height = 2,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };

    var file = MicroDesignCutFile.FromRawImage(raw, heightCode: 2);

    Assert.Multiple(() => {
      Assert.That(file.HeightCode, Is.EqualTo(2));
      Assert.That(file.WidthCode, Is.EqualTo(6));
      Assert.That(file.RasterData, Is.EqualTo(new byte[] { 0xFF, 0x00, 0x00, 0x00 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_MismatchedHeightCode_IsRejected() {
    var raw = new RawImage {
      Width = 8,
      Height = 2,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[8 * 2 * 3],
    };

    Assert.Throws<ArgumentException>(() => MicroDesignCutFile.FromRawImage(raw, heightCode: 3));
  }

  [Test]
  [Category("Unit")]
  public void WriterReader_RoundTripsRawCodesAndRaster() {
    var file = new MicroDesignCutFile {
      HeightCode = 4,
      WidthCode = 7,
      RasterData = [
        0xAA, 0x80,
        0x55, 0x00,
        0xF0, 0x00,
      ],
    };

    var decoded = MicroDesignCutReader.FromBytes(MicroDesignCutWriter.ToBytes(file));

    Assert.Multiple(() => {
      Assert.That(decoded.HeightCode, Is.EqualTo(file.HeightCode));
      Assert.That(decoded.WidthCode, Is.EqualTo(file.WidthCode));
      Assert.That(decoded.Width, Is.EqualTo(9));
      Assert.That(decoded.Height, Is.EqualTo(3));
      Assert.That(decoded.RasterData, Is.EqualTo(file.RasterData));
    });
  }

  [Test]
  [Category("Unit")]
  public void Reader_TruncatedHeader_IsRejected() {
    Assert.Throws<InvalidDataException>(() => MicroDesignCutReader.FromBytes(new byte[3]));
  }

  [Test]
  [Category("Unit")]
  public void Reader_TruncatedRaster_IsRejected() {
    var data = new byte[] { 0x01, 0x00, 0x06, 0x00, 0x80, 0x00, 0x01 };
    var exception = Assert.Throws<InvalidDataException>(() => MicroDesignCutReader.FromBytes(data));
    Assert.That(exception!.Message, Does.Contain("Truncated"));
  }

  [Test]
  [Category("Unit")]
  public void Reader_TrailingData_IsRejected() {
    var data = new byte[] {
      0x00, 0x00, 0x00, 0x00, // 2x1 => one stored byte
      0x80,
      0x00,
    };

    var exception = Assert.Throws<InvalidDataException>(() => MicroDesignCutReader.FromBytes(data));
    Assert.That(exception!.Message, Does.Contain("trailing"));
  }

  [Test]
  [Category("Unit")]
  public void Reader_ExcessiveDimensions_AreRejectedBeforeAllocation() {
    var data = new byte[4];
    BinaryPrimitives.WriteUInt16LittleEndian(data, ushort.MaxValue);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), ushort.MaxValue);

    var exception = Assert.Throws<InvalidDataException>(() => MicroDesignCutReader.FromBytes(data));
    Assert.That(exception!.Message, Does.Contain("safety limit"));
  }

  [Test]
  [Category("Unit")]
  public void Writer_RasterLengthMustMatchStoredStrideExactly() {
    var file = new MicroDesignCutFile {
      HeightCode = 1,
      WidthCode = 6,
      RasterData = [0x80, 0x00, 0x01],
    };

    Assert.Throws<ArgumentException>(() => MicroDesignCutWriter.ToBytes(file));
  }

  [Test]
  [Category("Unit")]
  public void ValidMicroDesignCut_IsNotAcceptedAsDrHaloCut() {
    var data = new byte[] {
      0x01, 0x00,
      0x06, 0x00,
      0x80, 0x55,
      0x01, 0xAA,
    };

    Assert.That(MicroDesignCutReader.FromBytes(data).Width, Is.EqualTo(8));
    Assert.Throws<InvalidDataException>(() => DrHaloReader.FromBytes(data));
  }
}
