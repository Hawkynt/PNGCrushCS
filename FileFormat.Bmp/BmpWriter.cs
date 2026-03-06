using System;
using System.IO;

namespace FileFormat.Bmp;

/// <summary>Assembles BMP file bytes from pixel data.</summary>
public static class BmpWriter {

  public static byte[] ToBytes(BmpFile file) => Assemble(
    file.PixelData,
    file.Width,
    file.Height,
    file.ColorMode,
    file.Compression,
    file.RowOrder,
    file.Palette,
    file.PaletteColorCount
  );

  internal static byte[] Assemble(
    byte[] pixelData,
    int width,
    int height,
    BmpColorMode colorMode,
    BmpCompression compression,
    BmpRowOrder rowOrder,
    byte[]? palette = null,
    int paletteColorCount = 0
  ) {
    int bitsPerPixel;
    int bmpCompression;

    switch (colorMode) {
      case BmpColorMode.Rgb24:
      case BmpColorMode.Original when palette == null:
        bitsPerPixel = 24;
        bmpCompression = 0; // BI_RGB
        break;
      case BmpColorMode.Rgb16_565:
        bitsPerPixel = 16;
        bmpCompression = 3; // BI_BITFIELDS
        break;
      case BmpColorMode.Palette8:
      case BmpColorMode.Grayscale8:
        bitsPerPixel = 8;
        bmpCompression = compression == BmpCompression.Rle8 ? 1 : 0;
        break;
      case BmpColorMode.Palette4:
        bitsPerPixel = 4;
        bmpCompression = compression == BmpCompression.Rle4 ? 2 : 0;
        break;
      case BmpColorMode.Palette1:
        bitsPerPixel = 1;
        bmpCompression = 0;
        break;
      default:
        bitsPerPixel = 24;
        bmpCompression = 0;
        break;
    }

    var paletteEntryCount = bitsPerPixel <= 8 ? (paletteColorCount > 0 ? paletteColorCount : 1 << bitsPerPixel) : 0;
    var paletteSize = paletteEntryCount * 4;
    var bitfieldsSize = bmpCompression == 3 ? 12 : 0;
    var pixelDataOffset = BitmapFileHeader.StructSize + BitmapInfoHeader.StructSize + bitfieldsSize + paletteSize;

    var imageData = _WritePixelData(pixelData, width, height, bitsPerPixel, bmpCompression, compression, rowOrder);
    var fileSize = pixelDataOffset + imageData.Length;

    var result = new byte[fileSize];
    var span = result.AsSpan();

    // BITMAPFILEHEADER (14 bytes)
    var fileHeader = new BitmapFileHeader((byte)'B', (byte)'M', fileSize, 0, 0, pixelDataOffset);
    fileHeader.WriteTo(span);

    // BITMAPINFOHEADER (40 bytes)
    var infoHeader = new BitmapInfoHeader(
      BitmapInfoHeader.StructSize,
      width,
      rowOrder == BmpRowOrder.TopDown ? -height : height,
      1,
      (short)bitsPerPixel,
      bmpCompression,
      imageData.Length,
      2835,
      2835,
      paletteColorCount > 0 ? paletteColorCount : 0,
      0
    );
    infoHeader.WriteTo(span[BitmapFileHeader.StructSize..]);

    var offset = BitmapFileHeader.StructSize + BitmapInfoHeader.StructSize;

    if (bmpCompression == 3) {
      span[offset] = 0x00; span[offset + 1] = 0xF8; span[offset + 2] = 0x00; span[offset + 3] = 0x00;
      span[offset + 4] = 0xE0; span[offset + 5] = 0x07; span[offset + 6] = 0x00; span[offset + 7] = 0x00;
      span[offset + 8] = 0x1F; span[offset + 9] = 0x00; span[offset + 10] = 0x00; span[offset + 11] = 0x00;
      offset += 12;
    }

    if (bitsPerPixel <= 8 && palette != null) {
      for (var i = 0; i < paletteEntryCount; ++i) {
        if (i * 3 + 2 < palette.Length) {
          span[offset] = palette[i * 3 + 2];     // Blue
          span[offset + 1] = palette[i * 3 + 1]; // Green
          span[offset + 2] = palette[i * 3];     // Red
          span[offset + 3] = 0;
        }
        offset += 4;
      }
    } else if (bitsPerPixel <= 8) {
      // zero-init palette area (already zero from new byte[])
      offset += paletteEntryCount * 4;
    }

    Array.Copy(imageData, 0, result, offset, imageData.Length);

    return result;
  }

  private static byte[] _WritePixelData(
    byte[] pixelData,
    int width,
    int height,
    int bitsPerPixel,
    int bmpCompression,
    BmpCompression compression,
    BmpRowOrder rowOrder
  ) {
    using var ms = new MemoryStream();

    var bytesPerRow = (width * bitsPerPixel + 7) / 8;
    var paddedBytesPerRow = (bytesPerRow + 3) & ~3;

    if (bmpCompression is 1 or 2) {
      for (var row = 0; row < height; ++row) {
        var srcRow = rowOrder == BmpRowOrder.BottomUp ? height - 1 - row : row;
        var rowOffset = srcRow * bytesPerRow;
        var rowData = new byte[bytesPerRow];
        Array.Copy(pixelData, rowOffset, rowData, 0, Math.Min(bytesPerRow, pixelData.Length - rowOffset));

        byte[] compressed;
        if (compression == BmpCompression.Rle8)
          compressed = RleCompressor.CompressRle8(rowData);
        else
          compressed = RleCompressor.CompressRle4(rowData, width);

        ms.Write(compressed);
      }

      ms.WriteByte(0x00);
      ms.WriteByte(0x01);
    } else {
      for (var row = 0; row < height; ++row) {
        var srcRow = rowOrder == BmpRowOrder.BottomUp ? height - 1 - row : row;
        var rowOffset = srcRow * bytesPerRow;

        var rowData = new byte[paddedBytesPerRow];
        var copyLen = Math.Min(bytesPerRow, pixelData.Length - rowOffset);
        if (copyLen > 0)
          Array.Copy(pixelData, rowOffset, rowData, 0, copyLen);

        ms.Write(rowData);
      }
    }

    return ms.ToArray();
  }
}
