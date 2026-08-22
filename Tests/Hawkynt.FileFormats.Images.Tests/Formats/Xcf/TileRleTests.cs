using NUnit.Framework;

namespace FileFormat.Xcf.Tests;

/// <summary>
/// The tile RLE is checked against hand-written opcodes, not against our own encoder.
/// </summary>
/// <remarks>
/// Decoder and encoder had the two opcode classes swapped in the same direction, so every
/// round-trip test passed and no other reader could open what we wrote. Only bytes whose meaning
/// the format fixes can tell the two apart.
/// </remarks>
[TestFixture]
public sealed class TileRleTests {

  [Test]
  [Category("Unit")]
  public void AnOpcodeBelow128OpensARepeat() {
    // 0x02 = repeat the next byte three times.
    var decoded = XcfTileDecoder.DecodeRle([0x02, 0xAB], bytesPerPixel: 1, tileWidth: 3, tileHeight: 1);

    Assert.That(decoded, Is.EqualTo(new byte[] { 0xAB, 0xAB, 0xAB }));
  }

  [Test]
  [Category("Unit")]
  public void AnOpcodeAbove128OpensALiteral() {
    // 0xFD = 256 - 3 = three literal bytes.
    var decoded = XcfTileDecoder.DecodeRle([0xFD, 0x01, 0x02, 0x03], bytesPerPixel: 1, tileWidth: 3, tileHeight: 1);

    Assert.That(decoded, Is.EqualTo(new byte[] { 0x01, 0x02, 0x03 }));
  }

  [Test]
  [Category("Unit")]
  public void OpcodeSeven127EscapesToATwoByteRepeatCount() {
    // 0x7F would mean a repeat of 128; instead it introduces a two-byte count.
    var decoded = XcfTileDecoder.DecodeRle([0x7F, 0x00, 0x04, 0x55], bytesPerPixel: 1, tileWidth: 4, tileHeight: 1);

    Assert.That(decoded, Is.EqualTo(new byte[] { 0x55, 0x55, 0x55, 0x55 }));
  }

  [Test]
  [Category("Unit")]
  public void Opcode128EscapesToATwoByteLiteralCount() {
    // 0x80 would mean a literal of 128; instead it introduces a two-byte count.
    var decoded = XcfTileDecoder.DecodeRle([0x80, 0x00, 0x03, 0x07, 0x08, 0x09], bytesPerPixel: 1, tileWidth: 3, tileHeight: 1);

    Assert.That(decoded, Is.EqualTo(new byte[] { 0x07, 0x08, 0x09 }));
  }

  [Test]
  [Category("Unit")]
  public void WhatTheEncoderWritesMeansWhatTheFormatSays() {
    // A run of five, then five distinct values: a repeat opcode then a literal one.
    byte[] pixels = [9, 9, 9, 9, 9, 1, 2, 3, 4, 5];

    var encoded = XcfTileDecoder.EncodeRle(pixels, bytesPerPixel: 1, tileWidth: 10, tileHeight: 1);

    Assert.Multiple(() => {
      Assert.That(encoded[0], Is.LessThan((byte)128), "a repeat has to open with an opcode below 128");
      Assert.That(encoded[0], Is.EqualTo((byte)4), "a repeat of five is opcode four");
      Assert.That(encoded[1], Is.EqualTo((byte)9));
      Assert.That(encoded[2], Is.GreaterThan((byte)128), "a literal has to open with an opcode above 128");
      Assert.That(encoded[2], Is.EqualTo((byte)(256 - 5)), "a literal of five is opcode 251");
    });
  }
}
