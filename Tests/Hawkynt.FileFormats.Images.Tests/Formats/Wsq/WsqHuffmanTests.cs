using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Wsq;

namespace FileFormat.Wsq.Tests;

[TestFixture]
public sealed class WsqHuffmanTests {

  [Test]
  [Category("Unit")]
  public void BuildEncodeTable_ProducesValidCodes() {
    var table = new WsqHuffman.HuffmanTable {
      CodeLengths = [0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
      Values = [0, 1]
    };

    var encodeMap = WsqHuffman.BuildEncodeTable(table);

    Assert.That(encodeMap.ContainsKey(0), Is.True);
    Assert.That(encodeMap.ContainsKey(1), Is.True);
    Assert.That(encodeMap[0].Length, Is.EqualTo(2));
    Assert.That(encodeMap[1].Length, Is.EqualTo(2));
    Assert.That(encodeMap[0].Code, Is.Not.EqualTo(encodeMap[1].Code));
  }

  [Test]
  [Category("Unit")]
  public void BitWriter_WritesCorrectBits() {
    var writer = new WsqHuffman.BitWriter();
    writer.WriteBits(0b10110, 5);
    writer.WriteBits(0b101, 3);
    var bytes = writer.Flush();

    Assert.That(bytes.Length, Is.EqualTo(1));
    Assert.That(bytes[0], Is.EqualTo(0b10110101));
  }

  [Test]
  [Category("Unit")]
  public void BitWriter_FlushPadsWithZeros() {
    var writer = new WsqHuffman.BitWriter();
    writer.WriteBits(0b110, 3);
    var bytes = writer.Flush();

    Assert.That(bytes.Length, Is.EqualTo(1));
    Assert.That(bytes[0], Is.EqualTo(0b11000000));
  }

  [Test]
  [Category("Unit")]
  public void BitReader_ReadsCorrectBits() {
    var data = new byte[] { 0b10110101 };
    var reader = new WsqHuffman.BitReader(data, 0);

    Assert.That(reader.ReadBits(5), Is.EqualTo(0b10110));
    Assert.That(reader.ReadBits(3), Is.EqualTo(0b101));
  }

  [Test]
  [Category("Unit")]
  public void EncodeDecodeRoundTrip_SingleSymbol() {
    var table = new WsqHuffman.HuffmanTable {
      CodeLengths = [0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
      Values = [0, 1]
    };
    table.BuildDerived();

    var indices = new int[] { 1 };
    var encoded = WsqHuffman.Encode(indices, table);
    var decoded = WsqHuffman.Decode(encoded, 0, 1, table);

    Assert.That(decoded[0], Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void BuildFromIndices_ProducesValidTable() {
    var indices = new int[] { 0, 0, 0, 1, -1, 2, 0, 0, 3, -2 };
    var table = WsqHuffman.BuildFromIndices(indices);

    Assert.That(table.CodeLengths, Is.Not.Null);
    Assert.That(table.Values.Length, Is.GreaterThan(0));
  }

  [Test]
  [Category("Unit")]
  public void BuildFromIndices_AllZeros_ProducesValidTable() {
    var indices = new int[100];
    var table = WsqHuffman.BuildFromIndices(indices);

    Assert.That(table.Values.Length, Is.GreaterThanOrEqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void EncodeDecodeRoundTrip_MixedValues() {
    var indices = new int[] { 0, 0, 0, 1, -1, 2, 0, 0, 3 };
    var table = WsqHuffman.BuildFromIndices(indices);
    table.BuildDerived();

    var encoded = WsqHuffman.Encode(indices, table);
    var decoded = WsqHuffman.Decode(encoded, 0, indices.Length, table);

    Assert.That(decoded.Length, Is.EqualTo(indices.Length));
    for (var i = 0; i < indices.Length; ++i)
      Assert.That(decoded[i], Is.EqualTo(indices[i]));
  }

  [Test]
  [Category("Unit")]
  public void DecodeSymbol_InvalidCode_ThrowsInvalidDataException() {
    var table = new WsqHuffman.HuffmanTable {
      CodeLengths = [1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
      Values = [42]
    };
    table.BuildDerived();

    // Fill reader with all 1s — only code 0 (single bit) is valid
    var data = new byte[] { 0xFF, 0xFF, 0xFF };
    var reader = new WsqHuffman.BitReader(data, 0);

    // The code "0" is the first entry, so bit "1" is not a valid 1-bit code
    // With 16 bits of "1", it should exhaust all possibilities
    Assert.Throws<InvalidDataException>(() => WsqHuffman.DecodeSymbol(reader, table));
  }
}
