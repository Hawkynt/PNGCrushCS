using System;

namespace FileFormat.Jbig2.Codec;

/// <summary>Generic region decoder/encoder for JBIG2 (ITU-T T.88 section 6.2).
/// Supports templates 0-3 with adaptive template (AT) pixel offsets,
/// typical prediction for generic direct coding (TPGDON), and both
/// arithmetic coding and MMR modes.</summary>
internal static class Jbig2GenericRegion {

  /// <summary>Decodes a generic region using arithmetic coding.</summary>
  /// <param name="decoder">Arithmetic decoder positioned at the start of the region data.</param>
  /// <param name="width">Region width in pixels.</param>
  /// <param name="height">Region height in pixels.</param>
  /// <param name="template">Template number (0-3).</param>
  /// <param name="useTypicalPrediction">Whether TPGDON is enabled.</param>
  /// <param name="atX">Adaptive template X offsets (up to 4 entries depending on template).</param>
  /// <param name="atY">Adaptive template Y offsets (up to 4 entries depending on template).</param>
  /// <returns>1bpp packed pixel data (MSB first, ceil(width/8) bytes per row).</returns>
  internal static byte[] Decode(
    Jbig2ArithmeticDecoder decoder,
    int width,
    int height,
    int template,
    bool useTypicalPrediction,
    sbyte[] atX,
    sbyte[] atY
  ) {
    var contextSize = template switch {
      0 => Jbig2ContextModel.GenericTemplate0Size,
      1 => Jbig2ContextModel.GenericTemplate1Size,
      2 => Jbig2ContextModel.GenericTemplate2Size,
      3 => Jbig2ContextModel.GenericTemplate3Size,
      _ => Jbig2ContextModel.GenericTemplate0Size,
    };

    var contexts = new Jbig2ContextModel(contextSize);
    var gbReg = new byte[((width + 7) / 8) * height];
    var bytesPerRow = (width + 7) / 8;

    // Typical prediction context
    Jbig2ContextModel.Context? tpgdContext = useTypicalPrediction ? new Jbig2ContextModel.Context() : null;
    var ltp = false;

    for (var y = 0; y < height; ++y) {
      if (useTypicalPrediction) {
        ltp ^= decoder.DecodeBit(tpgdContext!) != 0;
        if (ltp) {
          // Copy previous row
          if (y > 0)
            Array.Copy(gbReg, (y - 1) * bytesPerRow, gbReg, y * bytesPerRow, bytesPerRow);
          continue;
        }
      }

      switch (template) {
        case 0:
          _DecodeRowTemplate0(decoder, contexts, gbReg, width, y, bytesPerRow, atX, atY);
          break;
        case 1:
          _DecodeRowTemplate1(decoder, contexts, gbReg, width, y, bytesPerRow, atX, atY);
          break;
        case 2:
          _DecodeRowTemplate2(decoder, contexts, gbReg, width, y, bytesPerRow, atX, atY);
          break;
        case 3:
          _DecodeRowTemplate3(decoder, contexts, gbReg, width, y, bytesPerRow, atX, atY);
          break;
      }
    }

    return gbReg;
  }

  /// <summary>Encodes a generic region using arithmetic coding.</summary>
  internal static byte[] Encode(
    byte[] pixelData,
    int width,
    int height,
    int template,
    bool useTypicalPrediction,
    sbyte[] atX,
    sbyte[] atY
  ) {
    var contextSize = template switch {
      0 => Jbig2ContextModel.GenericTemplate0Size,
      1 => Jbig2ContextModel.GenericTemplate1Size,
      2 => Jbig2ContextModel.GenericTemplate2Size,
      3 => Jbig2ContextModel.GenericTemplate3Size,
      _ => Jbig2ContextModel.GenericTemplate0Size,
    };

    var contexts = new Jbig2ContextModel(contextSize);
    var encoder = new Jbig2ArithmeticEncoder();
    var bytesPerRow = (width + 7) / 8;

    Jbig2ContextModel.Context? tpgdContext = useTypicalPrediction ? new Jbig2ContextModel.Context() : null;
    var ltp = false;

    for (var y = 0; y < height; ++y) {
      if (useTypicalPrediction) {
        var thisRowSameAsPrev = y > 0 && _RowsEqual(pixelData, (y - 1) * bytesPerRow, y * bytesPerRow, bytesPerRow);
        var tpVal = thisRowSameAsPrev != ltp;
        encoder.EncodeBit(tpgdContext!, tpVal ? 1 : 0);
        ltp = thisRowSameAsPrev;
        if (thisRowSameAsPrev)
          continue;
      }

      switch (template) {
        case 0:
          _EncodeRowTemplate0(encoder, contexts, pixelData, width, y, bytesPerRow, atX, atY);
          break;
        case 1:
          _EncodeRowTemplate1(encoder, contexts, pixelData, width, y, bytesPerRow, atX, atY);
          break;
        case 2:
          _EncodeRowTemplate2(encoder, contexts, pixelData, width, y, bytesPerRow, atX, atY);
          break;
        case 3:
          _EncodeRowTemplate3(encoder, contexts, pixelData, width, y, bytesPerRow, atX, atY);
          break;
      }
    }

    return encoder.Finish();
  }

  private static int _GetPixel(byte[] data, int x, int y, int width, int bytesPerRow) {
    if (x < 0 || x >= width || y < 0)
      return 0;
    var byteIndex = y * bytesPerRow + (x >> 3);
    if (byteIndex >= data.Length)
      return 0;
    return (data[byteIndex] >> (7 - (x & 7))) & 1;
  }

  private static void _SetPixel(byte[] data, int x, int y, int bytesPerRow, int value) {
    if (value != 0) {
      var byteIndex = y * bytesPerRow + (x >> 3);
      data[byteIndex] |= (byte)(1 << (7 - (x & 7)));
    }
  }

  private static bool _RowsEqual(byte[] data, int offset1, int offset2, int length) {
    for (var i = 0; i < length; ++i)
      if (data[offset1 + i] != data[offset2 + i])
        return false;
    return true;
  }

  // ---- Template 0: 16-pixel context ----
  // Reference pixels (T.88 Fig. 3):
  //   Row y-2: (x-1), (x), (x+1), (x+2)
  //   Row y-1: (x-2), (x-1), (x), (x+1), (x+2)
  //   Row y:   (x-4), (x-3), (x-2), (x-1)
  //   + AT pixels at (atX[0],atY[0]), (atX[1],atY[1]), (atX[2],atY[2]), (atX[3],atY[3])

  private static void _DecodeRowTemplate0(
    Jbig2ArithmeticDecoder decoder, Jbig2ContextModel contexts,
    byte[] gbReg, int width, int y, int bytesPerRow,
    sbyte[] atX, sbyte[] atY
  ) {
    for (var x = 0; x < width; ++x) {
      var cx = 0;

      // Build 16-bit context from surrounding pixels
      cx |= _GetPixel(gbReg, x + 2, y - 2, width, bytesPerRow) << 15;
      cx |= _GetPixel(gbReg, x + 1, y - 2, width, bytesPerRow) << 14;
      cx |= _GetPixel(gbReg, x, y - 2, width, bytesPerRow) << 13;
      cx |= _GetPixel(gbReg, x - 1, y - 2, width, bytesPerRow) << 12;

      cx |= _GetPixel(gbReg, x + 2, y - 1, width, bytesPerRow) << 11;
      cx |= _GetPixel(gbReg, x + 1, y - 1, width, bytesPerRow) << 10;
      cx |= _GetPixel(gbReg, x, y - 1, width, bytesPerRow) << 9;
      cx |= _GetPixel(gbReg, x - 1, y - 1, width, bytesPerRow) << 8;
      cx |= _GetPixel(gbReg, x - 2, y - 1, width, bytesPerRow) << 7;

      cx |= _GetPixel(gbReg, x - 1, y, width, bytesPerRow) << 6;
      cx |= _GetPixel(gbReg, x - 2, y, width, bytesPerRow) << 5;
      cx |= _GetPixel(gbReg, x - 3, y, width, bytesPerRow) << 4;
      cx |= _GetPixel(gbReg, x - 4, y, width, bytesPerRow) << 3;

      // AT pixels
      cx |= _GetPixel(gbReg, x + atX[0], y + atY[0], width, bytesPerRow) << 2;
      cx |= _GetPixel(gbReg, x + atX[1], y + atY[1], width, bytesPerRow) << 1;
      cx |= _GetPixel(gbReg, x + atX[2], y + atY[2], width, bytesPerRow);

      var bit = decoder.DecodeBit(contexts[cx]);
      _SetPixel(gbReg, x, y, bytesPerRow, bit);
    }
  }

  private static void _EncodeRowTemplate0(
    Jbig2ArithmeticEncoder encoder, Jbig2ContextModel contexts,
    byte[] pixelData, int width, int y, int bytesPerRow,
    sbyte[] atX, sbyte[] atY
  ) {
    for (var x = 0; x < width; ++x) {
      var cx = 0;

      cx |= _GetPixel(pixelData, x + 2, y - 2, width, bytesPerRow) << 15;
      cx |= _GetPixel(pixelData, x + 1, y - 2, width, bytesPerRow) << 14;
      cx |= _GetPixel(pixelData, x, y - 2, width, bytesPerRow) << 13;
      cx |= _GetPixel(pixelData, x - 1, y - 2, width, bytesPerRow) << 12;

      cx |= _GetPixel(pixelData, x + 2, y - 1, width, bytesPerRow) << 11;
      cx |= _GetPixel(pixelData, x + 1, y - 1, width, bytesPerRow) << 10;
      cx |= _GetPixel(pixelData, x, y - 1, width, bytesPerRow) << 9;
      cx |= _GetPixel(pixelData, x - 1, y - 1, width, bytesPerRow) << 8;
      cx |= _GetPixel(pixelData, x - 2, y - 1, width, bytesPerRow) << 7;

      cx |= _GetPixel(pixelData, x - 1, y, width, bytesPerRow) << 6;
      cx |= _GetPixel(pixelData, x - 2, y, width, bytesPerRow) << 5;
      cx |= _GetPixel(pixelData, x - 3, y, width, bytesPerRow) << 4;
      cx |= _GetPixel(pixelData, x - 4, y, width, bytesPerRow) << 3;

      cx |= _GetPixel(pixelData, x + atX[0], y + atY[0], width, bytesPerRow) << 2;
      cx |= _GetPixel(pixelData, x + atX[1], y + atY[1], width, bytesPerRow) << 1;
      cx |= _GetPixel(pixelData, x + atX[2], y + atY[2], width, bytesPerRow);

      var bit = _GetPixel(pixelData, x, y, width, bytesPerRow);
      encoder.EncodeBit(contexts[cx], bit);
    }
  }

  // ---- Template 1: 13-pixel context ----

  private static void _DecodeRowTemplate1(
    Jbig2ArithmeticDecoder decoder, Jbig2ContextModel contexts,
    byte[] gbReg, int width, int y, int bytesPerRow,
    sbyte[] atX, sbyte[] atY
  ) {
    for (var x = 0; x < width; ++x) {
      var cx = 0;

      cx |= _GetPixel(gbReg, x + 2, y - 2, width, bytesPerRow) << 12;
      cx |= _GetPixel(gbReg, x + 1, y - 2, width, bytesPerRow) << 11;
      cx |= _GetPixel(gbReg, x, y - 2, width, bytesPerRow) << 10;
      cx |= _GetPixel(gbReg, x - 1, y - 2, width, bytesPerRow) << 9;

      cx |= _GetPixel(gbReg, x + 2, y - 1, width, bytesPerRow) << 8;
      cx |= _GetPixel(gbReg, x + 1, y - 1, width, bytesPerRow) << 7;
      cx |= _GetPixel(gbReg, x, y - 1, width, bytesPerRow) << 6;
      cx |= _GetPixel(gbReg, x - 1, y - 1, width, bytesPerRow) << 5;
      cx |= _GetPixel(gbReg, x - 2, y - 1, width, bytesPerRow) << 4;

      cx |= _GetPixel(gbReg, x - 1, y, width, bytesPerRow) << 3;
      cx |= _GetPixel(gbReg, x - 2, y, width, bytesPerRow) << 2;
      cx |= _GetPixel(gbReg, x - 3, y, width, bytesPerRow) << 1;

      // AT pixel
      cx |= _GetPixel(gbReg, x + atX[0], y + atY[0], width, bytesPerRow);

      var bit = decoder.DecodeBit(contexts[cx]);
      _SetPixel(gbReg, x, y, bytesPerRow, bit);
    }
  }

  private static void _EncodeRowTemplate1(
    Jbig2ArithmeticEncoder encoder, Jbig2ContextModel contexts,
    byte[] pixelData, int width, int y, int bytesPerRow,
    sbyte[] atX, sbyte[] atY
  ) {
    for (var x = 0; x < width; ++x) {
      var cx = 0;

      cx |= _GetPixel(pixelData, x + 2, y - 2, width, bytesPerRow) << 12;
      cx |= _GetPixel(pixelData, x + 1, y - 2, width, bytesPerRow) << 11;
      cx |= _GetPixel(pixelData, x, y - 2, width, bytesPerRow) << 10;
      cx |= _GetPixel(pixelData, x - 1, y - 2, width, bytesPerRow) << 9;

      cx |= _GetPixel(pixelData, x + 2, y - 1, width, bytesPerRow) << 8;
      cx |= _GetPixel(pixelData, x + 1, y - 1, width, bytesPerRow) << 7;
      cx |= _GetPixel(pixelData, x, y - 1, width, bytesPerRow) << 6;
      cx |= _GetPixel(pixelData, x - 1, y - 1, width, bytesPerRow) << 5;
      cx |= _GetPixel(pixelData, x - 2, y - 1, width, bytesPerRow) << 4;

      cx |= _GetPixel(pixelData, x - 1, y, width, bytesPerRow) << 3;
      cx |= _GetPixel(pixelData, x - 2, y, width, bytesPerRow) << 2;
      cx |= _GetPixel(pixelData, x - 3, y, width, bytesPerRow) << 1;

      cx |= _GetPixel(pixelData, x + atX[0], y + atY[0], width, bytesPerRow);

      var bit = _GetPixel(pixelData, x, y, width, bytesPerRow);
      encoder.EncodeBit(contexts[cx], bit);
    }
  }

  // ---- Template 2: 10-pixel context ----

  private static void _DecodeRowTemplate2(
    Jbig2ArithmeticDecoder decoder, Jbig2ContextModel contexts,
    byte[] gbReg, int width, int y, int bytesPerRow,
    sbyte[] atX, sbyte[] atY
  ) {
    for (var x = 0; x < width; ++x) {
      var cx = 0;

      cx |= _GetPixel(gbReg, x + 1, y - 2, width, bytesPerRow) << 9;
      cx |= _GetPixel(gbReg, x, y - 2, width, bytesPerRow) << 8;
      cx |= _GetPixel(gbReg, x - 1, y - 2, width, bytesPerRow) << 7;

      cx |= _GetPixel(gbReg, x + 1, y - 1, width, bytesPerRow) << 6;
      cx |= _GetPixel(gbReg, x, y - 1, width, bytesPerRow) << 5;
      cx |= _GetPixel(gbReg, x - 1, y - 1, width, bytesPerRow) << 4;

      cx |= _GetPixel(gbReg, x - 1, y, width, bytesPerRow) << 3;
      cx |= _GetPixel(gbReg, x - 2, y, width, bytesPerRow) << 2;

      // AT pixel
      cx |= _GetPixel(gbReg, x + atX[0], y + atY[0], width, bytesPerRow) << 1;

      // Bit 0 is always from a fixed position
      cx |= _GetPixel(gbReg, x - 2, y - 1, width, bytesPerRow);

      var bit = decoder.DecodeBit(contexts[cx]);
      _SetPixel(gbReg, x, y, bytesPerRow, bit);
    }
  }

  private static void _EncodeRowTemplate2(
    Jbig2ArithmeticEncoder encoder, Jbig2ContextModel contexts,
    byte[] pixelData, int width, int y, int bytesPerRow,
    sbyte[] atX, sbyte[] atY
  ) {
    for (var x = 0; x < width; ++x) {
      var cx = 0;

      cx |= _GetPixel(pixelData, x + 1, y - 2, width, bytesPerRow) << 9;
      cx |= _GetPixel(pixelData, x, y - 2, width, bytesPerRow) << 8;
      cx |= _GetPixel(pixelData, x - 1, y - 2, width, bytesPerRow) << 7;

      cx |= _GetPixel(pixelData, x + 1, y - 1, width, bytesPerRow) << 6;
      cx |= _GetPixel(pixelData, x, y - 1, width, bytesPerRow) << 5;
      cx |= _GetPixel(pixelData, x - 1, y - 1, width, bytesPerRow) << 4;

      cx |= _GetPixel(pixelData, x - 1, y, width, bytesPerRow) << 3;
      cx |= _GetPixel(pixelData, x - 2, y, width, bytesPerRow) << 2;

      cx |= _GetPixel(pixelData, x + atX[0], y + atY[0], width, bytesPerRow) << 1;
      cx |= _GetPixel(pixelData, x - 2, y - 1, width, bytesPerRow);

      var bit = _GetPixel(pixelData, x, y, width, bytesPerRow);
      encoder.EncodeBit(contexts[cx], bit);
    }
  }

  // ---- Template 3: 10-pixel context (single AT pixel) ----

  private static void _DecodeRowTemplate3(
    Jbig2ArithmeticDecoder decoder, Jbig2ContextModel contexts,
    byte[] gbReg, int width, int y, int bytesPerRow,
    sbyte[] atX, sbyte[] atY
  ) {
    for (var x = 0; x < width; ++x) {
      var cx = 0;

      cx |= _GetPixel(gbReg, x + 2, y - 1, width, bytesPerRow) << 9;
      cx |= _GetPixel(gbReg, x + 1, y - 1, width, bytesPerRow) << 8;
      cx |= _GetPixel(gbReg, x, y - 1, width, bytesPerRow) << 7;
      cx |= _GetPixel(gbReg, x - 1, y - 1, width, bytesPerRow) << 6;
      cx |= _GetPixel(gbReg, x - 2, y - 1, width, bytesPerRow) << 5;
      cx |= _GetPixel(gbReg, x - 3, y - 1, width, bytesPerRow) << 4;

      cx |= _GetPixel(gbReg, x - 1, y, width, bytesPerRow) << 3;
      cx |= _GetPixel(gbReg, x - 2, y, width, bytesPerRow) << 2;
      cx |= _GetPixel(gbReg, x - 3, y, width, bytesPerRow) << 1;

      // Single AT pixel
      cx |= _GetPixel(gbReg, x + atX[0], y + atY[0], width, bytesPerRow);

      var bit = decoder.DecodeBit(contexts[cx]);
      _SetPixel(gbReg, x, y, bytesPerRow, bit);
    }
  }

  private static void _EncodeRowTemplate3(
    Jbig2ArithmeticEncoder encoder, Jbig2ContextModel contexts,
    byte[] pixelData, int width, int y, int bytesPerRow,
    sbyte[] atX, sbyte[] atY
  ) {
    for (var x = 0; x < width; ++x) {
      var cx = 0;

      cx |= _GetPixel(pixelData, x + 2, y - 1, width, bytesPerRow) << 9;
      cx |= _GetPixel(pixelData, x + 1, y - 1, width, bytesPerRow) << 8;
      cx |= _GetPixel(pixelData, x, y - 1, width, bytesPerRow) << 7;
      cx |= _GetPixel(pixelData, x - 1, y - 1, width, bytesPerRow) << 6;
      cx |= _GetPixel(pixelData, x - 2, y - 1, width, bytesPerRow) << 5;
      cx |= _GetPixel(pixelData, x - 3, y - 1, width, bytesPerRow) << 4;

      cx |= _GetPixel(pixelData, x - 1, y, width, bytesPerRow) << 3;
      cx |= _GetPixel(pixelData, x - 2, y, width, bytesPerRow) << 2;
      cx |= _GetPixel(pixelData, x - 3, y, width, bytesPerRow) << 1;

      cx |= _GetPixel(pixelData, x + atX[0], y + atY[0], width, bytesPerRow);

      var bit = _GetPixel(pixelData, x, y, width, bytesPerRow);
      encoder.EncodeBit(contexts[cx], bit);
    }
  }
}
