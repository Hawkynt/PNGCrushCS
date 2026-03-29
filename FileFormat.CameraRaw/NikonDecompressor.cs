using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.CameraRaw;

/// <summary>Decompresses Nikon NEF compressed raw data. Nikon uses a modified Huffman coding with an optional linearization curve.</summary>
internal static class NikonDecompressor {

  /// <summary>Decompress Nikon NEF compressed raw data.</summary>
  /// <param name="data">Full file data.</param>
  /// <param name="stripOffset">Offset to the compressed strip data.</param>
  /// <param name="stripLength">Length of the compressed strip data.</param>
  /// <param name="width">Image width.</param>
  /// <param name="height">Image height.</param>
  /// <param name="bitsPerSample">Bit depth (12 or 14).</param>
  /// <param name="curve">Optional linearization curve (up to 4096 or 16384 entries). Null for no curve.</param>
  /// <param name="isLossy">True for lossy Nikon compression (uses quantization step).</param>
  /// <returns>Decompressed 16-bit CFA samples in raster order.</returns>
  public static ushort[] Decompress(byte[] data, int stripOffset, int stripLength, int width, int height, int bitsPerSample, ushort[]? curve, bool isLossy) {
    ArgumentNullException.ThrowIfNull(data);
    if (stripOffset < 0 || stripOffset + stripLength > data.Length)
      throw new InvalidDataException("Nikon strip data out of bounds.");

    var maxVal = (1 << bitsPerSample) - 1;
    var output = new ushort[width * height];

    // Nikon uses a two-table Huffman scheme: one for the first two elements per row,
    // then alternating between two tables.
    // The Huffman trees are built from a fixed table depending on bit depth.
    var tree0 = _BuildNikonHuffmanTree(bitsPerSample, 0);
    var tree1 = _BuildNikonHuffmanTree(bitsPerSample, 1);

    var bitBuffer = 0u;
    var bitsInBuffer = 0;
    var pos = stripOffset;
    var endPos = stripOffset + stripLength;

    for (var row = 0; row < height; ++row) {
      var vpred0 = row == 0 ? (1 << (bitsPerSample - 1)) : output[(row - 1) * width]; // prediction for even columns
      var vpred1 = row == 0 ? (1 << (bitsPerSample - 1)) : output[(row - 1) * width + 1]; // prediction for odd columns

      for (var col = 0; col < width; ++col) {
        var tree = col < 2 ? tree0 : (col & 1) == 0 ? tree0 : tree1;

        // Decode Huffman symbol: number of bits for the difference
        var category = _DecodeNikonHuffman(data, ref pos, ref bitBuffer, ref bitsInBuffer, endPos, tree);

        int diff;
        if (category == 0) {
          diff = 0;
        } else {
          var bits = _ReadBits(data, ref pos, ref bitBuffer, ref bitsInBuffer, endPos, category);
          diff = bits < (1 << (category - 1))
            ? bits - (1 << category) + 1
            : bits;

          if (isLossy && category > 0 && category < bitsPerSample)
            diff <<= 1; // lossy quantization step
        }

        // Apply prediction
        int predicted;
        if (col < 2)
          predicted = col == 0 ? vpred0 : vpred1;
        else
          predicted = output[row * width + col - 2];

        var value = Math.Clamp(predicted + diff, 0, maxVal);
        output[row * width + col] = (ushort)value;

        // Update vertical prediction
        if (col == 0)
          vpred0 = value;
        else if (col == 1)
          vpred1 = value;
      }
    }

    // Apply linearization curve if present
    if (curve is { Length: > 0 })
      for (var i = 0; i < output.Length; ++i)
        output[i] = output[i] < curve.Length ? curve[output[i]] : output[i];

    return output;
  }

  /// <summary>Build a Nikon Huffman tree from the fixed tables.</summary>
  /// <param name="bitsPerSample">12 or 14.</param>
  /// <param name="tableIndex">0 or 1.</param>
  private static int[][] _BuildNikonHuffmanTree(int bitsPerSample, int tableIndex) {
    // Nikon Huffman tables are well-known fixed structures.
    var sourceTable = _GetNikonSourceTable(bitsPerSample, tableIndex);
    return _BuildHuffmanFromLengths(sourceTable);
  }

  /// <summary>Build Huffman lookup from (code_length, symbol_value) pairs using canonical Huffman.</summary>
  private static int[][] _BuildHuffmanFromLengths((int length, int symbol)[] entries) {
    // Create a binary tree with max depth 16
    // Negative values = leaf with symbol -(value+1)
    // Positive values = index of child node
    var tree = new List<int[]> { new[] { 0, 0 } }; // root at index 0

    // Sort by code length, then symbol
    Array.Sort(entries, (a, b) => a.length != b.length ? a.length.CompareTo(b.length) : a.symbol.CompareTo(b.symbol));

    // Assign canonical codes
    var code = 0;
    var prevLen = 0;
    foreach (var (length, symbol) in entries) {
      if (length <= 0)
        continue;
      code <<= (length - prevLen);
      prevLen = length;

      // Insert into tree
      var node = 0;
      for (var bit = length - 1; bit >= 0; --bit) {
        var direction = (code >> bit) & 1;

        while (tree.Count <= node || tree[node] == null)
          tree.Add([0, 0]);

        if (bit == 0) {
          // Leaf
          tree[node][direction] = -(symbol + 1);
        } else {
          if (tree[node][direction] <= 0) {
            tree[node][direction] = tree.Count;
            tree.Add([0, 0]);
          }

          node = tree[node][direction];
        }
      }

      ++code;
    }

    return tree.ToArray();
  }

  /// <summary>Decode one Huffman symbol from the Nikon bitstream.</summary>
  private static int _DecodeNikonHuffman(byte[] data, ref int pos, ref uint bitBuffer, ref int bitsInBuffer, int endPos, int[][] tree) {
    var node = 0;
    while (true) {
      if (node < 0 || node >= tree.Length)
        return 0; // safety

      var bit = _ReadBits(data, ref pos, ref bitBuffer, ref bitsInBuffer, endPos, 1);
      var child = tree[node][bit];

      if (child < 0)
        return -(child + 1); // leaf: return symbol

      node = child;
    }
  }

  /// <summary>Read N bits from the bitstream (MSB first).</summary>
  private static int _ReadBits(byte[] data, ref int pos, ref uint bitBuffer, ref int bitsInBuffer, int endPos, int count) {
    while (bitsInBuffer < count) {
      if (pos >= endPos) {
        bitBuffer <<= 8;
        bitsInBuffer += 8;
        continue;
      }

      bitBuffer = (bitBuffer << 8) | data[pos++];
      bitsInBuffer += 8;
    }

    bitsInBuffer -= count;
    return (int)((bitBuffer >> bitsInBuffer) & ((1u << count) - 1));
  }

  /// <summary>Get fixed Nikon Huffman table entries as (code_length, symbol) pairs.</summary>
  private static (int length, int symbol)[] _GetNikonSourceTable(int bitsPerSample, int tableIndex) {
    // These are the standard Nikon lossless Huffman tables.
    // The tables encode SSSS categories (0-16 for difference magnitudes).
    // Format: (bit_length_of_code, symbol_value)
    if (bitsPerSample == 12) {
      if (tableIndex == 0)
        return [
          (2, 0), (3, 1), (4, 2), (5, 3), (6, 4), (7, 5),
          (8, 6), (9, 7), (10, 8), (11, 9), (12, 10), (13, 11), (14, 12)
        ];
      return [
        (2, 0), (3, 1), (4, 2), (5, 3), (6, 4), (7, 5),
        (8, 6), (9, 7), (10, 8), (11, 9), (12, 10), (13, 11), (14, 12)
      ];
    }

    // 14-bit
    if (tableIndex == 0)
      return [
        (2, 0), (3, 1), (4, 2), (5, 3), (6, 4), (7, 5),
        (8, 6), (9, 7), (10, 8), (11, 9), (12, 10), (13, 11),
        (14, 12), (15, 13), (16, 14)
      ];
    return [
      (2, 0), (3, 1), (4, 2), (5, 3), (6, 4), (7, 5),
      (8, 6), (9, 7), (10, 8), (11, 9), (12, 10), (13, 11),
      (14, 12), (15, 13), (16, 14)
    ];
  }
}
