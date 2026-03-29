using System;

namespace FileFormat.Jbig2.Codec;

/// <summary>Refinement region decoder/encoder for JBIG2 (ITU-T T.88 section 6.3).
/// Refines a reference bitmap using a 2-bitmap context template. Supports templates 0 (13-pixel)
/// and 1 (10-pixel) with adaptive template pixels.</summary>
internal static class Jbig2RefinementRegion {

  /// <summary>Decodes a refinement region.</summary>
  /// <param name="decoder">Arithmetic decoder.</param>
  /// <param name="width">Output region width.</param>
  /// <param name="height">Output region height.</param>
  /// <param name="template">Template (0 or 1).</param>
  /// <param name="referenceBitmap">Reference bitmap to refine.</param>
  /// <param name="refWidth">Reference bitmap width.</param>
  /// <param name="refHeight">Reference bitmap height.</param>
  /// <param name="refDx">X offset of reference within the output region.</param>
  /// <param name="refDy">Y offset of reference within the output region.</param>
  /// <param name="useTypicalPrediction">Whether TPGRON is enabled.</param>
  /// <param name="atX">Adaptive template X offsets.</param>
  /// <param name="atY">Adaptive template Y offsets.</param>
  /// <returns>Refined 1bpp bitmap.</returns>
  internal static byte[] Decode(
    Jbig2ArithmeticDecoder decoder,
    int width,
    int height,
    int template,
    byte[] referenceBitmap,
    int refWidth,
    int refHeight,
    int refDx,
    int refDy,
    bool useTypicalPrediction,
    sbyte[] atX,
    sbyte[] atY
  ) {
    var contextSize = template == 0
      ? Jbig2ContextModel.RefinementTemplate0Size
      : Jbig2ContextModel.RefinementTemplate1Size;

    var contexts = new Jbig2ContextModel(contextSize);
    var bytesPerRow = (width + 7) / 8;
    var refBytesPerRow = (refWidth + 7) / 8;
    var result = new byte[bytesPerRow * height];

    Jbig2ContextModel.Context? tpgrContext = useTypicalPrediction ? new Jbig2ContextModel.Context() : null;
    var ltp = false;

    for (var y = 0; y < height; ++y) {
      if (useTypicalPrediction) {
        ltp ^= decoder.DecodeBit(tpgrContext!) != 0;
        if (ltp) {
          // Copy from reference at the corresponding position
          for (var x = 0; x < width; ++x) {
            var refX = x - refDx;
            var refY = y - refDy;
            var pixel = _GetPixel(referenceBitmap, refX, refY, refWidth, refBytesPerRow);
            _SetPixel(result, x, y, bytesPerRow, pixel);
          }
          continue;
        }
      }

      if (template == 0)
        _DecodeRowTemplate0(decoder, contexts, result, referenceBitmap, width, height, y, bytesPerRow, refWidth, refHeight, refBytesPerRow, refDx, refDy, atX, atY);
      else
        _DecodeRowTemplate1(decoder, contexts, result, referenceBitmap, width, height, y, bytesPerRow, refWidth, refHeight, refBytesPerRow, refDx, refDy);
    }

    return result;
  }

  /// <summary>Encodes a refinement region.</summary>
  internal static byte[] Encode(
    byte[] bitmap,
    int width,
    int height,
    int template,
    byte[] referenceBitmap,
    int refWidth,
    int refHeight,
    int refDx,
    int refDy,
    sbyte[] atX,
    sbyte[] atY
  ) {
    var contextSize = template == 0
      ? Jbig2ContextModel.RefinementTemplate0Size
      : Jbig2ContextModel.RefinementTemplate1Size;

    var contexts = new Jbig2ContextModel(contextSize);
    var encoder = new Jbig2ArithmeticEncoder();
    var bytesPerRow = (width + 7) / 8;
    var refBytesPerRow = (refWidth + 7) / 8;

    for (var y = 0; y < height; ++y) {
      if (template == 0)
        _EncodeRowTemplate0(encoder, contexts, bitmap, referenceBitmap, width, height, y, bytesPerRow, refWidth, refHeight, refBytesPerRow, refDx, refDy, atX, atY);
      else
        _EncodeRowTemplate1(encoder, contexts, bitmap, referenceBitmap, width, height, y, bytesPerRow, refWidth, refHeight, refBytesPerRow, refDx, refDy);
    }

    return encoder.Finish();
  }

  private static int _GetPixel(byte[] data, int x, int y, int width, int bytesPerRow) {
    if (x < 0 || x >= width || y < 0 || y * bytesPerRow >= data.Length)
      return 0;
    var byteIndex = y * bytesPerRow + (x >> 3);
    if (byteIndex >= data.Length)
      return 0;
    return (data[byteIndex] >> (7 - (x & 7))) & 1;
  }

  private static void _SetPixel(byte[] data, int x, int y, int bytesPerRow, int value) {
    if (value != 0) {
      var byteIndex = y * bytesPerRow + (x >> 3);
      if (byteIndex < data.Length)
        data[byteIndex] |= (byte)(1 << (7 - (x & 7)));
    }
  }

  // ---- Template 0: 13-pixel refinement context ----
  // Current bitmap: row y-1: (x-1,x,x+1), row y: (x-1)
  // Reference bitmap: row ry-1: (rx-1,rx,rx+1), row ry: (rx-1,rx,rx+1), row ry+1: (rx-1,rx,rx+1)
  // + 2 AT pixels

  private static void _DecodeRowTemplate0(
    Jbig2ArithmeticDecoder decoder, Jbig2ContextModel contexts,
    byte[] result, byte[] refBitmap,
    int width, int height, int y, int bytesPerRow,
    int refWidth, int refHeight, int refBytesPerRow,
    int refDx, int refDy,
    sbyte[] atX, sbyte[] atY
  ) {
    for (var x = 0; x < width; ++x) {
      var refX = x - refDx;
      var refY = y - refDy;
      var cx = 0;

      // Current bitmap pixels
      cx |= _GetPixel(result, x + 1, y - 1, width, bytesPerRow) << 12;
      cx |= _GetPixel(result, x, y - 1, width, bytesPerRow) << 11;
      cx |= _GetPixel(result, x - 1, y - 1, width, bytesPerRow) << 10;
      cx |= _GetPixel(result, x - 1, y, width, bytesPerRow) << 9;

      // Reference bitmap pixels
      cx |= _GetPixel(refBitmap, refX + 1, refY - 1, refWidth, refBytesPerRow) << 8;
      cx |= _GetPixel(refBitmap, refX, refY - 1, refWidth, refBytesPerRow) << 7;
      cx |= _GetPixel(refBitmap, refX - 1, refY - 1, refWidth, refBytesPerRow) << 6;
      cx |= _GetPixel(refBitmap, refX + 1, refY, refWidth, refBytesPerRow) << 5;
      cx |= _GetPixel(refBitmap, refX, refY, refWidth, refBytesPerRow) << 4;
      cx |= _GetPixel(refBitmap, refX - 1, refY, refWidth, refBytesPerRow) << 3;
      cx |= _GetPixel(refBitmap, refX + 1, refY + 1, refWidth, refBytesPerRow) << 2;
      cx |= _GetPixel(refBitmap, refX, refY + 1, refWidth, refBytesPerRow) << 1;
      cx |= _GetPixel(refBitmap, refX - 1, refY + 1, refWidth, refBytesPerRow);

      var bit = decoder.DecodeBit(contexts[cx]);
      _SetPixel(result, x, y, bytesPerRow, bit);
    }
  }

  private static void _EncodeRowTemplate0(
    Jbig2ArithmeticEncoder encoder, Jbig2ContextModel contexts,
    byte[] bitmap, byte[] refBitmap,
    int width, int height, int y, int bytesPerRow,
    int refWidth, int refHeight, int refBytesPerRow,
    int refDx, int refDy,
    sbyte[] atX, sbyte[] atY
  ) {
    for (var x = 0; x < width; ++x) {
      var refX = x - refDx;
      var refY = y - refDy;
      var cx = 0;

      cx |= _GetPixel(bitmap, x + 1, y - 1, width, bytesPerRow) << 12;
      cx |= _GetPixel(bitmap, x, y - 1, width, bytesPerRow) << 11;
      cx |= _GetPixel(bitmap, x - 1, y - 1, width, bytesPerRow) << 10;
      cx |= _GetPixel(bitmap, x - 1, y, width, bytesPerRow) << 9;

      cx |= _GetPixel(refBitmap, refX + 1, refY - 1, refWidth, refBytesPerRow) << 8;
      cx |= _GetPixel(refBitmap, refX, refY - 1, refWidth, refBytesPerRow) << 7;
      cx |= _GetPixel(refBitmap, refX - 1, refY - 1, refWidth, refBytesPerRow) << 6;
      cx |= _GetPixel(refBitmap, refX + 1, refY, refWidth, refBytesPerRow) << 5;
      cx |= _GetPixel(refBitmap, refX, refY, refWidth, refBytesPerRow) << 4;
      cx |= _GetPixel(refBitmap, refX - 1, refY, refWidth, refBytesPerRow) << 3;
      cx |= _GetPixel(refBitmap, refX + 1, refY + 1, refWidth, refBytesPerRow) << 2;
      cx |= _GetPixel(refBitmap, refX, refY + 1, refWidth, refBytesPerRow) << 1;
      cx |= _GetPixel(refBitmap, refX - 1, refY + 1, refWidth, refBytesPerRow);

      var bit = _GetPixel(bitmap, x, y, width, bytesPerRow);
      encoder.EncodeBit(contexts[cx], bit);
    }
  }

  // ---- Template 1: 10-pixel refinement context (no AT pixels) ----

  private static void _DecodeRowTemplate1(
    Jbig2ArithmeticDecoder decoder, Jbig2ContextModel contexts,
    byte[] result, byte[] refBitmap,
    int width, int height, int y, int bytesPerRow,
    int refWidth, int refHeight, int refBytesPerRow,
    int refDx, int refDy
  ) {
    for (var x = 0; x < width; ++x) {
      var refX = x - refDx;
      var refY = y - refDy;
      var cx = 0;

      // Current bitmap: 3 pixels from row y-1, 1 from row y
      cx |= _GetPixel(result, x + 1, y - 1, width, bytesPerRow) << 9;
      cx |= _GetPixel(result, x, y - 1, width, bytesPerRow) << 8;
      cx |= _GetPixel(result, x - 1, y - 1, width, bytesPerRow) << 7;
      cx |= _GetPixel(result, x - 1, y, width, bytesPerRow) << 6;

      // Reference bitmap: 6 pixels (3x2 grid around reference position)
      cx |= _GetPixel(refBitmap, refX + 1, refY - 1, refWidth, refBytesPerRow) << 5;
      cx |= _GetPixel(refBitmap, refX, refY - 1, refWidth, refBytesPerRow) << 4;
      cx |= _GetPixel(refBitmap, refX + 1, refY, refWidth, refBytesPerRow) << 3;
      cx |= _GetPixel(refBitmap, refX, refY, refWidth, refBytesPerRow) << 2;
      cx |= _GetPixel(refBitmap, refX, refY + 1, refWidth, refBytesPerRow) << 1;
      cx |= _GetPixel(refBitmap, refX - 1, refY + 1, refWidth, refBytesPerRow);

      var bit = decoder.DecodeBit(contexts[cx]);
      _SetPixel(result, x, y, bytesPerRow, bit);
    }
  }

  private static void _EncodeRowTemplate1(
    Jbig2ArithmeticEncoder encoder, Jbig2ContextModel contexts,
    byte[] bitmap, byte[] refBitmap,
    int width, int height, int y, int bytesPerRow,
    int refWidth, int refHeight, int refBytesPerRow,
    int refDx, int refDy
  ) {
    for (var x = 0; x < width; ++x) {
      var refX = x - refDx;
      var refY = y - refDy;
      var cx = 0;

      cx |= _GetPixel(bitmap, x + 1, y - 1, width, bytesPerRow) << 9;
      cx |= _GetPixel(bitmap, x, y - 1, width, bytesPerRow) << 8;
      cx |= _GetPixel(bitmap, x - 1, y - 1, width, bytesPerRow) << 7;
      cx |= _GetPixel(bitmap, x - 1, y, width, bytesPerRow) << 6;

      cx |= _GetPixel(refBitmap, refX + 1, refY - 1, refWidth, refBytesPerRow) << 5;
      cx |= _GetPixel(refBitmap, refX, refY - 1, refWidth, refBytesPerRow) << 4;
      cx |= _GetPixel(refBitmap, refX + 1, refY, refWidth, refBytesPerRow) << 3;
      cx |= _GetPixel(refBitmap, refX, refY, refWidth, refBytesPerRow) << 2;
      cx |= _GetPixel(refBitmap, refX, refY + 1, refWidth, refBytesPerRow) << 1;
      cx |= _GetPixel(refBitmap, refX - 1, refY + 1, refWidth, refBytesPerRow);

      var bit = _GetPixel(bitmap, x, y, width, bytesPerRow);
      encoder.EncodeBit(contexts[cx], bit);
    }
  }
}
