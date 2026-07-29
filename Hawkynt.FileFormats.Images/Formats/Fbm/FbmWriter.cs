using System;

namespace FileFormat.Fbm;

/// <summary>Assembles CMU Fuzzy Bitmap (FBM) file bytes from pixel data.</summary>
public static class FbmWriter {

  public static byte[] ToBytes(FbmFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height, file.Bands, file.Title);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height, int bands, string title) {
    var bytesPerPixelRow = width * bands;

    // rowlen is padded to 16-byte boundary
    var rowLen = (bytesPerPixelRow + 15) & ~15;
    var plnLen = rowLen * height;
    const int clrLen = 0;
    const int bits = 8;
    const int physBits = 8;
    const double aspect = 1.0;

    var fileSize = FbmHeader.StructSize + clrLen + plnLen;
    var result = new byte[fileSize];
    var span = result.AsSpan();

    var header = new FbmHeader(
      Magic: FbmHeader.MagicBytes,
      Cols: width,
      Rows: height,
      Bands: bands,
      Bits: bits,
      PhysBits: physBits,
      RowLen: rowLen,
      PlnLen: plnLen,
      ClrLen: clrLen,
      Aspect: aspect,
      Title: title ?? string.Empty
    );
    header.WriteTo(span);

    // Write pixel data with row padding
    for (var y = 0; y < height; ++y) {
      var srcOffset = y * bytesPerPixelRow;
      var dstOffset = FbmHeader.StructSize + y * rowLen;
      var copyLen = Math.Min(bytesPerPixelRow, pixelData.Length - srcOffset);
      if (copyLen > 0)
        pixelData.AsSpan(srcOffset, copyLen).CopyTo(result.AsSpan(dstOffset));
    }

    return result;
  }
}
