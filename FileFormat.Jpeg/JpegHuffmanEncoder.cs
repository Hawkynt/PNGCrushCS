using System;

namespace FileFormat.Jpeg;

/// <summary>Huffman encoding helpers: encode DC/AC coefficients and build optimal tables.</summary>
internal static class JpegHuffmanEncoder {

  /// <summary>Encodes a single DC coefficient difference.</summary>
  public static void EncodeDc(JpegBitWriter writer, JpegHuffmanTable table, int dcDiff) {
    var category = _CategoryOf(dcDiff);
    writer.WriteHuffmanCode(table.EhufCo[category], table.EhufSi[category]);
    if (category > 0)
      writer.WriteBits(_EncodeValue(dcDiff, category), category);
  }

  /// <summary>Encodes AC coefficients for indices start..end from a coefficient block.</summary>
  public static void EncodeAcBlock(JpegBitWriter writer, JpegHuffmanTable table, short[] coefficients, int start, int end) {
    var zeroRun = 0;

    for (var k = start; k <= end; ++k) {
      var value = coefficients[k];
      if (value == 0) {
        ++zeroRun;
        continue;
      }

      // Emit ZRL symbols for runs > 15
      while (zeroRun > 15) {
        writer.WriteHuffmanCode(table.EhufCo[0xF0], table.EhufSi[0xF0]); // ZRL
        zeroRun -= 16;
      }

      var category = _CategoryOf(value);
      var rs = (zeroRun << 4) | category;
      writer.WriteHuffmanCode(table.EhufCo[rs], table.EhufSi[rs]);
      writer.WriteBits(_EncodeValue(value, category), category);
      zeroRun = 0;
    }

    // EOB if not at end
    if (zeroRun > 0)
      writer.WriteHuffmanCode(table.EhufCo[0x00], table.EhufSi[0x00]); // EOB
  }

  /// <summary>Counts DC symbol frequencies for optimal Huffman table construction.</summary>
  public static void CountDcFrequencies(long[] freq, int dcDiff) {
    var category = _CategoryOf(dcDiff);
    ++freq[category];
  }

  /// <summary>Counts AC symbol frequencies for optimal Huffman table construction.</summary>
  public static void CountAcFrequencies(long[] freq, short[] coefficients, int start, int end) {
    var zeroRun = 0;

    for (var k = start; k <= end; ++k) {
      if (coefficients[k] == 0) {
        ++zeroRun;
        continue;
      }

      while (zeroRun > 15) {
        ++freq[0xF0]; // ZRL
        zeroRun -= 16;
      }

      var category = _CategoryOf(coefficients[k]);
      ++freq[(zeroRun << 4) | category];
      zeroRun = 0;
    }

    if (zeroRun > 0)
      ++freq[0x00]; // EOB
  }

  /// <summary>Returns the JPEG bit category (number of bits needed) for a value.</summary>
  private static int _CategoryOf(int value) {
    if (value < 0)
      value = -value;
    var category = 0;
    while (value > 0) {
      ++category;
      value >>= 1;
    }

    return category;
  }

  /// <summary>Encodes a value for JPEG's bit representation (complement for negatives).</summary>
  private static int _EncodeValue(int value, int category) {
    if (value >= 0)
      return value;
    return value + (1 << category) - 1;
  }
}
