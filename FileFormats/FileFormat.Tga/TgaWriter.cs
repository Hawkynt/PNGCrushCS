using System;
using System.IO;

namespace FileFormat.Tga;

/// <summary>Assembles TGA file bytes from pixel data.</summary>
public static class TgaWriter {

  public static byte[] ToBytes(TgaFile file) => Assemble(
    file.PixelData,
    file.Width,
    file.Height,
    file.ColorMode,
    file.Compression,
    file.Origin,
    file.Palette,
    file.PaletteColorCount
  );

  internal static byte[] Assemble(
    byte[] pixelData,
    int width,
    int height,
    TgaColorMode colorMode,
    TgaCompression compression,
    TgaOrigin origin,
    byte[]? palette = null,
    int paletteColorCount = 0
  ) {
    using var ms = new MemoryStream();

    int bitsPerPixel;
    byte imageType;
    var hasColorMap = palette != null && paletteColorCount > 0;

    switch (colorMode) {
      case TgaColorMode.Rgba32:
        bitsPerPixel = 32;
        imageType = (byte)(compression == TgaCompression.Rle ? 10 : 2); // TrueColor
        break;
      case TgaColorMode.Rgb24:
      case TgaColorMode.Original when !hasColorMap:
        bitsPerPixel = 24;
        imageType = (byte)(compression == TgaCompression.Rle ? 10 : 2); // TrueColor
        break;
      case TgaColorMode.Grayscale8:
        bitsPerPixel = 8;
        imageType = (byte)(compression == TgaCompression.Rle ? 11 : 3); // Grayscale
        break;
      case TgaColorMode.Indexed8:
        bitsPerPixel = 8;
        imageType = (byte)(compression == TgaCompression.Rle ? 9 : 1); // Color-mapped
        break;
      default:
        bitsPerPixel = 24;
        imageType = (byte)(compression == TgaCompression.Rle ? 10 : 2);
        break;
    }

    var bytesPerPixel = (bitsPerPixel + 7) / 8;
    var imageDescriptor = (byte)(origin == TgaOrigin.TopLeft ? 0x20 : 0x00);
    if (bitsPerPixel == 32)
      imageDescriptor |= 0x08; // 8 alpha bits

    // TGA Header (18 bytes)
    var header = new TgaHeader(
      IdLength: 0,
      ColorMapType: (byte)(hasColorMap ? 1 : 0),
      ImageType: imageType,
      ColorMapFirstEntry: 0,
      ColorMapLength: (short)(hasColorMap ? paletteColorCount : 0),
      ColorMapEntrySize: (byte)(hasColorMap ? 24 : 0),
      XOrigin: 0,
      YOrigin: 0,
      Width: (short)width,
      Height: (short)height,
      BitsPerPixel: (byte)bitsPerPixel,
      ImageDescriptor: imageDescriptor
    );

    var headerBytes = new byte[TgaHeader.StructSize];
    header.WriteTo(headerBytes);
    ms.Write(headerBytes);

    // Color map data (BGR)
    if (hasColorMap)
      for (var i = 0; i < paletteColorCount; ++i) {
        if (i * 3 + 2 < palette!.Length) {
          ms.WriteByte(palette[i * 3 + 2]); // B
          ms.WriteByte(palette[i * 3 + 1]); // G
          ms.WriteByte(palette[i * 3]);     // R
        } else {
          ms.WriteByte(0);
          ms.WriteByte(0);
          ms.WriteByte(0);
        }
      }

    // Pixel data
    if (compression == TgaCompression.Rle) {
      for (var row = 0; row < height; ++row) {
        var srcRow = origin == TgaOrigin.TopLeft ? row : height - 1 - row;
        var rowOffset = srcRow * width * bytesPerPixel;
        var rowLen = width * bytesPerPixel;
        var rowData = new byte[rowLen];
        pixelData.AsSpan(rowOffset, Math.Min(rowLen, pixelData.Length - rowOffset)).CopyTo(rowData.AsSpan(0));
        var compressed = TgaRleCompressor.Compress(rowData, bytesPerPixel);
        ms.Write(compressed);
      }
    } else {
      for (var row = 0; row < height; ++row) {
        var srcRow = origin == TgaOrigin.TopLeft ? row : height - 1 - row;
        var rowOffset = srcRow * width * bytesPerPixel;
        var rowLen = width * bytesPerPixel;
        if (rowOffset + rowLen <= pixelData.Length)
          ms.Write(pixelData, rowOffset, rowLen);
        else {
          var available = Math.Max(0, pixelData.Length - rowOffset);
          if (available > 0)
            ms.Write(pixelData, rowOffset, available);
          ms.Write(new byte[rowLen - available]);
        }
      }
    }

    // TGA 2.0 footer
    var footer = new TgaFooter(0, 0, TgaFooter.SignatureString);
    var footerBytes = new byte[TgaFooter.StructSize];
    footer.WriteTo(footerBytes);
    ms.Write(footerBytes);

    return ms.ToArray();
  }
}
