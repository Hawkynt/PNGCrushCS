using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.Tests;

[TestFixture]
public sealed class CanopusLosslessMalformedInputTests {

  [Test]
  [Category("Unit")]
  public void InfoPrefixCannotRunPastPacketEnd() {
    var packet = new byte[10];
    "INFO"u8.CopyTo(packet);
    BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), 32);
    var decoder = CanopusLosslessVideoDecoder.Create(_Stream());

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, packet), out _));
  }

  [Test]
  [Category("Unit")]
  public void HuffmanCodeLengthCannotExceedFourteenBits() {
    var bits = new MsbBitWriter();
    bits.WriteBits(1, 8); // RGB coding type
    bits.WriteBits(0, 8);
    bits.WriteBits(15, 5); // first table exceeds CLLC's 14-bit format limit immediately
    var decoder = CanopusLosslessVideoDecoder.Create(_Stream());

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, bits.ToWordSwappedPacket()), out _));
  }

  [Test]
  [Category("Unit")]
  public void HuffmanTableCannotDeclareMoreThanTwoHundredFiftySixSymbols() {
    var bits = new MsbBitWriter();
    bits.WriteBits(1, 8); // RGB coding type
    bits.WriteBits(0, 8);
    bits.WriteBits(2, 5); // maximum length
    bits.WriteBits(256, 9); // all byte symbols at length one
    for (var symbol = 0; symbol < 256; ++symbol)
      bits.WriteBits((uint)symbol, 8);
    bits.WriteBits(1, 9); // plus one more at length two => invalid before its symbol is read
    var decoder = CanopusLosslessVideoDecoder.Create(_Stream());

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, bits.ToWordSwappedPacket()), out _));
  }

  [Test]
  [Category("Unit")]
  public void EntropyPayloadCannotRunPastPacketEnd() {
    var bits = new MsbBitWriter();
    bits.WriteBits(1, 8); // RGB coding type
    bits.WriteBits(0, 8);
    for (var table = 0; table < 3; ++table) {
      bits.WriteBits(1, 5);
      bits.WriteBits(1, 9);
      bits.WriteBits(0, 8);
    }
    // Deliberately provide no three pixel codes. Padding to a complete swapped word is shorter than
    // the three bits the 1x1 RGB decoder still needs.
    var packet = bits.ToWordSwappedPacket(trimFinalWord: true);
    var decoder = CanopusLosslessVideoDecoder.Create(_Stream());

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, packet), out _));
  }

  private static MediaStreamInfo _Stream() => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("CLLC"),
    Width = 1,
    Height = 1,
    BitsPerPixel = 24,
  };

  private sealed class MsbBitWriter {
    private readonly List<byte> _bytes = [];
    private int _position;

    internal void WriteBits(uint value, int count) {
      for (var bit = count - 1; bit >= 0; --bit) {
        var byteIndex = this._position >> 3;
        if (byteIndex == this._bytes.Count)
          this._bytes.Add(0);
        if (((value >> bit) & 1) != 0)
          this._bytes[byteIndex] |= (byte)(1 << (7 - (this._position & 7)));
        ++this._position;
      }
    }

    internal byte[] ToWordSwappedPacket(bool trimFinalWord = false) {
      while ((this._bytes.Count & 1) != 0)
        this._bytes.Add(0);

      var result = this._bytes.ToArray();
      for (var i = 0; i < result.Length; i += 2)
        (result[i], result[i + 1]) = (result[i + 1], result[i]);

      if (trimFinalWord && result.Length >= 2)
        Array.Resize(ref result, result.Length - 2);
      return result;
    }
  }
}
