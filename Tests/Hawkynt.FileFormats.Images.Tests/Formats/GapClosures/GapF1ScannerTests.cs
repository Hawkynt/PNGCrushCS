using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Ccitt;
using FileFormat.Core;
using FileFormat.MicroDynamicsMars;
using FileFormat.RicohFax;
using FileFormat.RicohIs30;
using FileFormat.Skantek;
using FileFormat.SmartFax;
using FileFormat.XionicsSmp;

namespace FileFormat.GapClosures.Tests;

/// <summary>
/// Six scanner and fax formats whose layouts were recovered from XnView's own converter and then put
/// back to it: Micro Dynamics MARS (.pbt), Skantek (.skn), Xionics SMP (.smp), Ricoh IS30 (.pig),
/// Ricoh Fax (.001) and SmartFax (.001). Every fixture below is built the way it was built for the
/// converter, and the sizes and pixels asserted are the ones the converter reported and wrote back.
/// <para/>
/// Two of these — Ricoh Fax and SmartFax — replace readers that required a magic number
/// (<c>RICF</c>, <c>SMFX</c>) that exists in no file and in no other implementation.
/// </summary>
[TestFixture]
public sealed class GapF1ScannerTests {

  // -------- shared fixtures --------

  /// <summary>A packed bilevel pattern, a set bit being ink.</summary>
  private static byte[] _Pattern(int width, int height) {
    var stride = (width + 7) / 8;
    var rows = new byte[stride * height];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
      if ((x / 3 + y) % 2 == 0)
        rows[y * stride + (x >> 3)] |= (byte)(0x80 >> (x & 7));

    return rows;
  }

  /// <summary>Turns every byte over, which is the fill order three of these formats store.</summary>
  private static byte[] _Reversed(byte[] data) {
    var result = new byte[data.Length];
    for (var i = 0; i < data.Length; ++i) {
      var value = 0;
      for (var bit = 0; bit < 8; ++bit)
        if ((data[i] & (1 << bit)) != 0)
          value |= 0x80 >> bit;

      result[i] = (byte)value;
    }

    return result;
  }

  /// <summary>Checks a decoded picture against the packed rows it was built from, padding aside.</summary>
  private static void _AssertPage(RawImage image, byte[] expected, int width, int height) {
    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(width));
      Assert.That(image.Height, Is.EqualTo(height));

      var stride = (width + 7) / 8;
      for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var ink = (expected[y * stride + (x >> 3)] >> (7 - (x & 7))) & 1;
        Assert.That(image.PixelData[y * width + x], Is.EqualTo((byte)ink), $"pixel {x},{y}");
      }
    });
  }

  // -------- Micro Dynamics MARS --------

  private static byte[] _Mars(int width, int height, int resolution, byte[] coded) {
    var header = new byte[MicroDynamicsMarsFile.HeaderSize];
    MicroDynamicsMarsFile.Signature.CopyTo(header);
    BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(MicroDynamicsMarsFile.ResolutionOffset), resolution);
    BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(MicroDynamicsMarsFile.HeightOffset), height);
    BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(MicroDynamicsMarsFile.WidthOffset), width);

    var file = new byte[header.Length + coded.Length];
    header.CopyTo(file, 0);
    coded.CopyTo(file, header.Length);
    return file;
  }

  [Test]
  [Category("Unit")]
  public void MicroDynamicsMars_ReadsTheGroupFourPageBehindItsHeader() {
    const int width = 64, height = 6;
    var rows = _Pattern(width, height);
    var file = MicroDynamicsMarsReader.FromBytes(
      _Mars(width, height, 200, CcittG4Encoder.Encode(rows, width, height)));

    Assert.That(file.Resolution, Is.EqualTo(200));
    _AssertPage(MicroDynamicsMarsFile.ToRawImage(file), rows, width, height);
  }

  [Test]
  [Category("Unit")]
  public void MicroDynamicsMars_RefusesAFileThatDoesNotOpenWithPbit() {
    var file = _Mars(64, 6, 200, CcittG4Encoder.Encode(_Pattern(64, 6), 64, 6));
    file[3] = (byte)'X';

    Assert.Throws<InvalidDataException>(() => MicroDynamicsMarsReader.FromBytes(file));
  }

  // -------- Skantek --------

  private static byte[] _Skantek(int width, int height, byte[] coded) {
    var header = new byte[SkantekFile.HeaderSize];
    SkantekFile.Signature.CopyTo(header);
    SkantekFile.Stamp.CopyTo(header.AsSpan(SkantekFile.StampOffset));
    BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(SkantekFile.HeightOffset), height);
    BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(SkantekFile.WidthOffset), width);

    var file = new byte[header.Length + coded.Length];
    header.CopyTo(file, 0);
    coded.CopyTo(file, header.Length);
    return file;
  }

  [Test]
  [Category("Unit")]
  public void Skantek_ReadsAGroupFourPageWhoseBitsRunTheOtherWayUp() {
    const int width = 64, height = 6;
    var rows = _Pattern(width, height);
    var coded = _Reversed(CcittG4Encoder.Encode(rows, width, height));

    _AssertPage(SkantekFile.ToRawImage(SkantekReader.FromBytes(_Skantek(width, height, coded))), rows, width, height);
  }

  [Test]
  [Category("Unit")]
  public void Skantek_RefusesAPageWithoutTheStampAtThreeHundredAndTwo() {
    var coded = _Reversed(CcittG4Encoder.Encode(_Pattern(64, 6), 64, 6));
    var file = _Skantek(64, 6, coded);
    file[SkantekFile.StampOffset] = (byte)'8';

    Assert.Throws<InvalidDataException>(() => SkantekReader.FromBytes(file));
  }

  [Test]
  [Category("Unit")]
  public void Skantek_RefusesAPageWhoseOpeningLongsAreNotTheFormats() {
    var coded = _Reversed(CcittG4Encoder.Encode(_Pattern(64, 6), 64, 6));
    var file = _Skantek(64, 6, coded);
    file[9] = 0x00;

    Assert.Throws<InvalidDataException>(() => SkantekReader.FromBytes(file));
  }

  // -------- Xionics SMP --------

  private static byte[] _Smp(int width, int height, int compression, byte[] body) {
    var header = new byte[XionicsSmpFile.HeaderSize];
    XionicsSmpFile.Signature.CopyTo(header);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(XionicsSmpFile.OneOffset), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(XionicsSmpFile.CompressionOffset), (ushort)compression);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(XionicsSmpFile.BytesPerRowOffset), (ushort)(width / 8));
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(XionicsSmpFile.HeightOffset), (ushort)height);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(XionicsSmpFile.EscapeOffset), 0x1B);
    header[XionicsSmpFile.HorizontalTagOffset] = 0x19;
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(XionicsSmpFile.HorizontalResolutionOffset), 200);
    header[XionicsSmpFile.VerticalTagOffset] = 0x1A;
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(XionicsSmpFile.VerticalResolutionOffset), 200);

    var file = new byte[header.Length + body.Length];
    header.CopyTo(file, 0);
    body.CopyTo(file, header.Length);
    return file;
  }

  [Test]
  [Category("Unit")]
  public void XionicsSmp_ReadsUncompressedRows() {
    const int width = 64, height = 6;
    var rows = _Pattern(width, height);
    var file = XionicsSmpReader.FromBytes(_Smp(width, height, XionicsSmpFile.CompressionNone, rows));

    Assert.That(file.HorizontalResolution, Is.EqualTo(200));
    _AssertPage(XionicsSmpFile.ToRawImage(file), rows, width, height);
  }

  [Test]
  [Category("Unit")]
  public void XionicsSmp_ReadsGroupThreeAndGroupFourWithTheBitsTheOtherWayUp() {
    const int width = 64, height = 6;
    var rows = _Pattern(width, height);

    var group3 = _Reversed(CcittG3Encoder.Encode(rows, width, height, leadingEndOfLine: true));
    _AssertPage(
      XionicsSmpFile.ToRawImage(XionicsSmpReader.FromBytes(_Smp(width, height, XionicsSmpFile.CompressionGroup3, group3))),
      rows, width, height);

    var group4 = _Reversed(CcittG4Encoder.Encode(rows, width, height));
    _AssertPage(
      XionicsSmpFile.ToRawImage(XionicsSmpReader.FromBytes(_Smp(width, height, XionicsSmpFile.CompressionGroup4, group4))),
      rows, width, height);
  }

  [Test]
  [Category("Unit")]
  public void XionicsSmp_RefusesTheCodingsItCannotDecodeRatherThanDrawingThemWrong() {
    var rows = _Pattern(64, 6);

    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(
        () => XionicsSmpReader.FromBytes(_Smp(64, 6, XionicsSmpFile.CompressionGroup3TwoDimensional, rows)));
      Assert.Throws<InvalidDataException>(() => XionicsSmpReader.FromBytes(_Smp(64, 6, 4, rows)));
    });
  }

  [Test]
  [Category("Unit")]
  public void XionicsSmp_RefusesAFileThatDoesNotCarryTheVendorsName() {
    var file = _Smp(64, 6, XionicsSmpFile.CompressionNone, _Pattern(64, 6));
    file[2] = (byte)'Y';

    Assert.Throws<InvalidDataException>(() => XionicsSmpReader.FromBytes(file));
  }

  // -------- Ricoh IS30 --------

  private static byte[] _Is30(int depthSelector, int resolution, int bytesPerRow, int height, byte[] body) {
    var header = new byte[RicohIs30File.HeaderSize];
    RicohIs30File.Signature.CopyTo(header);
    header[RicohIs30File.DepthSelectorOffset] = (byte)depthSelector;
    _Ascii(header, RicohIs30File.ResolutionOffset, RicohIs30File.ResolutionLength, resolution);
    _Ascii(header, RicohIs30File.BytesPerRowOffset, RicohIs30File.BytesPerRowLength, bytesPerRow);
    _Ascii(header, RicohIs30File.HeightOffset, RicohIs30File.HeightLength, height);
    header[RicohIs30File.MarkerOffset] = RicohIs30File.MarkerValue;

    var file = new byte[header.Length + body.Length];
    header.CopyTo(file, 0);
    body.CopyTo(file, header.Length);
    return file;
  }

  private static void _Ascii(byte[] into, int offset, int length, int value) {
    var text = value.ToString().PadLeft(length, '0');
    for (var i = 0; i < length; ++i)
      into[offset + i] = (byte)text[i];
  }

  [Test]
  [Category("Unit")]
  public void RicohIs30_ReadsOneBitRowsAtTheWidthTheRowLengthImplies() {
    const int width = 48, height = 5;
    var rows = _Pattern(width, height);
    var file = RicohIs30Reader.FromBytes(_Is30(1, 200, width / 8, height, rows));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(width));
      Assert.That(file.BitsPerPixel, Is.EqualTo(1));
      Assert.That(file.Resolution, Is.EqualTo(200));
    });

    _AssertPage(RicohIs30File.ToRawImage(file), rows, width, height);
  }

  [Test]
  [Category("Unit")]
  public void RicohIs30_ReadsTwoBitRowsAsAGreyRampWithZeroWhite() {
    const int bytesPerRow = 3, height = 2;
    var body = new byte[] { 0x1B, 0x00, 0xFF, 0xE4, 0x00, 0x00 };
    var file = RicohIs30Reader.FromBytes(_Is30(2, 300, bytesPerRow, height, body));
    var image = RicohIs30File.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(bytesPerRow * 8 / 2));
      Assert.That(file.BitsPerPixel, Is.EqualTo(2));
      Assert.That(image.PaletteCount, Is.EqualTo(4));

      // 0x1B is 00 01 10 11, and the ramp runs white, light, dark, black.
      Assert.That(image.PixelData[0], Is.EqualTo((byte)0));
      Assert.That(image.PixelData[1], Is.EqualTo((byte)1));
      Assert.That(image.PixelData[2], Is.EqualTo((byte)2));
      Assert.That(image.PixelData[3], Is.EqualTo((byte)3));
      Assert.That(image.Palette![0], Is.EqualTo((byte)255));
      Assert.That(image.Palette[9], Is.EqualTo((byte)0));
    });
  }

  [Test]
  [Category("Unit")]
  public void RicohIs30_RefusesAFileWhoseHeaderNumbersAreNotWrittenAsDigits() {
    var file = _Is30(1, 200, 6, 5, _Pattern(48, 5));
    file[RicohIs30File.BytesPerRowOffset] = (byte)'x';

    Assert.Throws<InvalidDataException>(() => RicohIs30Reader.FromBytes(file));
  }

  // -------- Ricoh Fax --------

  private static byte[] _RicohFax(byte[] coded) {
    var file = new byte[RicohFaxFile.HeaderSize + coded.Length];
    RicohFaxFile.Signature.CopyTo(file.AsSpan(RicohFaxFile.SignatureOffset));
    coded.CopyTo(file, RicohFaxFile.HeaderSize);
    return file;
  }

  [Test]
  [Category("Unit")]
  public void RicohFax_ReadsThePageBehindFaxnetRicohAtTheOneWidthItHas() {
    const int height = 4;
    var rows = _Pattern(RicohFaxFile.PageWidth, height);
    var coded = _Reversed(CcittG3Encoder.Encode(rows, RicohFaxFile.PageWidth, height, leadingEndOfLine: true));
    var file = RicohFaxReader.FromBytes(_RicohFax(coded));

    Assert.That(file.Height, Is.EqualTo(height));
    _AssertPage(RicohFaxFile.ToRawImage(file), rows, RicohFaxFile.PageWidth, height);
  }

  [Test]
  [Category("Unit")]
  public void RicohFax_RoundTripsThroughItsOwnWriter() {
    const int height = 3;
    var rows = _Pattern(RicohFaxFile.PageWidth, height);
    var bytes = RicohFaxWriter.ToBytes(new() { Height = height, PixelData = rows });

    Assert.That(bytes.AsSpan(RicohFaxFile.SignatureOffset, RicohFaxFile.Signature.Length).SequenceEqual(RicohFaxFile.Signature));
    _AssertPage(RicohFaxFile.ToRawImage(RicohFaxReader.FromBytes(bytes)), rows, RicohFaxFile.PageWidth, height);
  }

  [Test]
  [Category("Unit")]
  public void RicohFax_RefusesTheInventedMagicTheOldReaderRequired() {
    var file = new byte[RicohFaxFile.HeaderSize + 32];
    "RICF"u8.CopyTo(file);

    Assert.Throws<InvalidDataException>(() => RicohFaxReader.FromBytes(file));
  }

  // -------- SmartFax --------

  private static byte[] _SmartFax(int width, byte[] coded) {
    var file = new byte[SmartFaxFile.HeaderSize + coded.Length];
    SmartFaxFile.Signature.CopyTo(file.AsSpan(0));
    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(SmartFaxFile.BytesPerRowOffset), (ushort)(width / 8));
    file[SmartFaxFile.ResolutionOffset] = 1;
    coded.CopyTo(file, SmartFaxFile.HeaderSize);
    return file;
  }

  [Test]
  [Category("Unit")]
  public void SmartFax_ReadsThePageBehindFax1dAtTheWidthTheRowLengthImplies() {
    const int width = 128, height = 8;
    var rows = _Pattern(width, height);
    var coded = _Reversed(CcittG3Encoder.Encode(rows, width, height, leadingEndOfLine: true));
    var file = SmartFaxReader.FromBytes(_SmartFax(width, coded));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(width));
      Assert.That(file.Height, Is.EqualTo(height));
      Assert.That(file.VerticalResolution, Is.EqualTo(SmartFaxFile.FineResolution));
    });

    _AssertPage(SmartFaxFile.ToRawImage(file), rows, width, height);
  }

  [Test]
  [Category("Unit")]
  public void SmartFax_RoundTripsThroughItsOwnWriter() {
    const int width = 64, height = 5;
    var rows = _Pattern(width, height);
    var bytes = SmartFaxWriter.ToBytes(new() {
      Width = width, Height = height, VerticalResolution = SmartFaxFile.CoarseResolution, PixelData = rows,
    });

    var file = SmartFaxReader.FromBytes(bytes);
    Assert.That(file.VerticalResolution, Is.EqualTo(SmartFaxFile.CoarseResolution));
    _AssertPage(SmartFaxFile.ToRawImage(file), rows, width, height);
  }

  [Test]
  [Category("Unit")]
  public void SmartFax_RefusesTheInventedMagicTheOldReaderRequired() {
    var file = new byte[SmartFaxFile.HeaderSize + 32];
    "SMFX"u8.CopyTo(file);

    Assert.Throws<InvalidDataException>(() => SmartFaxReader.FromBytes(file));
  }
}
