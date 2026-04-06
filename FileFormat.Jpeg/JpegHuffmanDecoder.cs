namespace FileFormat.Jpeg;

/// <summary>Huffman decoding helpers using JpegBitReader + JpegHuffmanTable.</summary>
internal static class JpegHuffmanDecoder {

  /// <summary>Decodes a DC coefficient: Huffman category → receive additional bits → extend.</summary>
  public static int DecodeDc(JpegBitReader reader, JpegHuffmanTable table) {
    var category = reader.DecodeHuffman(table);
    if (category == 0)
      return 0;
    return reader.Receive(category);
  }

  /// <summary>Decodes a block of AC coefficients (indices 1..63) into zigzag-ordered coefficients.</summary>
  public static void DecodeAcBlock(JpegBitReader reader, JpegHuffmanTable table, short[] coefficients, int start, int end) {
    for (var k = start; k <= end;) {
      var rs = reader.DecodeHuffman(table);
      var r = rs >> 4;    // Run length of zeros
      var s = rs & 0x0F;  // Category (bit length)

      if (s == 0) {
        if (r == 15) {
          // ZRL: skip 16 zeros
          k += 16;
          continue;
        }

        // EOB: rest of block is zero
        break;
      }

      k += r; // Skip zero run
      if (k > end)
        break;

      coefficients[k] = (short)reader.Receive(s);
      ++k;
    }
  }
}
