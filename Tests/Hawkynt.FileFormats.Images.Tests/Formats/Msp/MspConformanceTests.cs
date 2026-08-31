using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Msp.Tests;

[TestFixture]
public sealed class MspConformanceTests {

  [Test]
  public void Version1_PublishedDanMSignatureAndMetadata_ArePreserved() {
    var header = _BuildHeader(MspVersion.V1, 8, 2);
    var bytes = _Join(header, [0x80, 0x01]);

    var file = MspReader.FromSpan(bytes);

    Assert.That(file.Version, Is.EqualTo(MspVersion.V1));
    Assert.That(file.XAspect, Is.EqualTo(101));
    Assert.That(file.YAspect, Is.EqualTo(102));
    Assert.That(file.XAspectPrinter, Is.EqualTo(103));
    Assert.That(file.YAspectPrinter, Is.EqualTo(104));
    Assert.That(file.PrinterWidth, Is.EqualTo(105));
    Assert.That(file.PrinterHeight, Is.EqualTo(106));
    Assert.That(file.XAspectCorr, Is.EqualTo(107));
    Assert.That(file.YAspectCorr, Is.EqualTo(108));
    Assert.That(file.PixelData, Is.EqualTo(new byte[] { 0x80, 0x01 }));

    var image = MspFile.ToRawImage(file);
    Assert.That(image.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void Version1_Writer_ProducesExactHeaderChecksumAndRaster() {
    var file = _CreateFile(MspVersion.V1, 8, 2, [0x80, 0x01]);
    Assert.That(MspWriter.ToBytes(file), Is.EqualTo(_Join(_BuildHeader(MspVersion.V1, 8, 2), [0x80, 0x01])));
  }

  [Test]
  public void Version2_HandAuthoredLiteralAndRepeatPackets_Decode() {
    var bytes = _Join(_BuildHeader(MspVersion.V2, 24, 2), [
      0x04, 0x00,
      0x03, 0x00,
      0x03, 0xAA, 0x55, 0x0F,
      0x00, 0x03, 0xFF,
    ]);

    var file = MspReader.FromSpan(bytes);

    Assert.That(file.PixelData, Is.EqualTo(new byte[] { 0xAA, 0x55, 0x0F, 0xFF, 0xFF, 0xFF }));
  }

  [Test]
  public void Version2_Writer_ProducesDeterministicMapAndPackets() {
    var file = _CreateFile(MspVersion.V2, 24, 2, [0xAA, 0x55, 0x0F, 0xFF, 0xFF, 0xFF]);
    var expected = _Join(_BuildHeader(MspVersion.V2, 24, 2), [
      0x04, 0x00,
      0x03, 0x00,
      0x03, 0xAA, 0x55, 0x0F,
      0x00, 0x03, 0xFF,
    ]);

    Assert.That(MspWriter.ToBytes(file), Is.EqualTo(expected));
  }

  [TestCase(MspVersion.V1)]
  [TestCase(MspVersion.V2)]
  public void RoundTrip_PreservesRasterAndHeaderMetadata(MspVersion version) {
    var original = _CreateFile(version, 17, 3, [
      0x80, 0x00, 0x7F,
      0xFF, 0xFF, 0xFF,
      0x12, 0x34, 0x56,
    ]);

    var decoded = MspReader.FromBytes(MspWriter.ToBytes(original));

    Assert.That(decoded.Version, Is.EqualTo(original.Version));
    Assert.That(decoded.Width, Is.EqualTo(original.Width));
    Assert.That(decoded.Height, Is.EqualTo(original.Height));
    Assert.That(decoded.XAspect, Is.EqualTo(original.XAspect));
    Assert.That(decoded.YAspect, Is.EqualTo(original.YAspect));
    Assert.That(decoded.XAspectPrinter, Is.EqualTo(original.XAspectPrinter));
    Assert.That(decoded.YAspectPrinter, Is.EqualTo(original.YAspectPrinter));
    Assert.That(decoded.PrinterWidth, Is.EqualTo(original.PrinterWidth));
    Assert.That(decoded.PrinterHeight, Is.EqualTo(original.PrinterHeight));
    Assert.That(decoded.XAspectCorr, Is.EqualTo(original.XAspectCorr));
    Assert.That(decoded.YAspectCorr, Is.EqualTo(original.YAspectCorr));
    Assert.That(decoded.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  public void BadHeaderChecksum_IsRejected() {
    var bytes = _Join(_BuildHeader(MspVersion.V1, 8, 1), [0x00]);
    bytes[8] ^= 1;
    Assert.Throws<InvalidDataException>(() => MspReader.FromBytes(bytes));
  }

  [Test]
  public void NonZeroReservedPadding_IsRejected() {
    var header = _BuildHeader(MspVersion.V1, 8, 1);
    header[26] = 1;
    Assert.Throws<InvalidDataException>(() => MspReader.FromBytes(_Join(header, [0x00])));
  }

  [Test]
  public void Version1_TruncatedOrTrailingRaster_IsRejected() {
    var header = _BuildHeader(MspVersion.V1, 16, 1);
    Assert.Throws<InvalidDataException>(() => MspReader.FromBytes(_Join(header, [0x00])));
    Assert.Throws<InvalidDataException>(() => MspReader.FromBytes(_Join(header, [0x00, 0x00, 0x00])));
  }

  [Test]
  public void Version2_TruncatedLiteral_IsRejected() {
    var bytes = _Join(_BuildHeader(MspVersion.V2, 16, 1), [
      0x02, 0x00,
      0x02, 0xAA,
    ]);
    Assert.Throws<InvalidDataException>(() => MspReader.FromBytes(bytes));
  }

  [Test]
  public void Version2_RepeatOverrun_IsRejected() {
    var bytes = _Join(_BuildHeader(MspVersion.V2, 16, 1), [
      0x03, 0x00,
      0x00, 0x03, 0xFF,
    ]);
    Assert.Throws<InvalidDataException>(() => MspReader.FromBytes(bytes));
  }

  [Test]
  public void Version2_ZeroRepeatCount_IsRejected() {
    var bytes = _Join(_BuildHeader(MspVersion.V2, 8, 1), [
      0x03, 0x00,
      0x00, 0x00, 0xFF,
    ]);
    Assert.Throws<InvalidDataException>(() => MspReader.FromBytes(bytes));
  }

  [Test]
  public void ExcessiveDimensions_AreRejectedBeforeAllocation() {
    var bytes = _BuildHeader(MspVersion.V1, ushort.MaxValue, ushort.MaxValue);
    Assert.Throws<InvalidDataException>(() => MspReader.FromBytes(bytes));
  }

  [Test]
  public void Writer_RejectsShortPixelDataInsteadOfPaddingSilently() {
    var file = _CreateFile(MspVersion.V1, 16, 1, [0x00]);
    Assert.Throws<ArgumentException>(() => MspWriter.ToBytes(file));
  }

  private static MspFile _CreateFile(MspVersion version, int width, int height, byte[] pixelData) => new() {
    Version = version,
    Width = width,
    Height = height,
    XAspect = 101,
    YAspect = 102,
    XAspectPrinter = 103,
    YAspectPrinter = 104,
    PrinterWidth = 105,
    PrinterHeight = 106,
    XAspectCorr = 107,
    YAspectCorr = 108,
    PixelData = pixelData,
  };

  private static byte[] _BuildHeader(MspVersion version, ushort width, ushort height) {
    var header = new byte[MspHeader.StructSize];
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(0), version == MspVersion.V1 ? MspHeader.V1Key1 : MspHeader.V2Key1);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(2), version == MspVersion.V1 ? MspHeader.V1Key2 : MspHeader.V2Key2);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), width);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), height);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8), 101);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(10), 102);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), 103);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(14), 104);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(16), 105);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(18), 106);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(20), 107);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(22), 108);

    ushort checksum = 0;
    for (var offset = 0; offset < 24; offset += 2)
      checksum ^= BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(offset));
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(24), checksum);
    return header;
  }

  private static byte[] _Join(byte[] first, byte[] second) {
    var result = new byte[first.Length + second.Length];
    first.CopyTo(result, 0);
    second.CopyTo(result, first.Length);
    return result;
  }
}
