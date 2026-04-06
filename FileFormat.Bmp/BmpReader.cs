using System;
using System.IO;

namespace FileFormat.Bmp;

/// <summary>Reads BMP files from bytes, streams, or file paths.</summary>
public static class BmpReader {

  public static BmpFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("BMP file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static BmpFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static BmpFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static BmpFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < BitmapFileHeader.StructSize + BitmapInfoHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid BMP file.");

    var span = data.AsSpan();

    // BITMAPFILEHEADER (14 bytes)
    var fileHeader = BitmapFileHeader.ReadFrom(span);
    if (fileHeader.Sig1 != (byte)'B' || fileHeader.Sig2 != (byte)'M')
      throw new InvalidDataException("Invalid BMP signature.");

    var pixelDataOffset = fileHeader.PixelDataOffset;

    // BITMAPINFOHEADER (40 bytes minimum)
    var infoHeader = BitmapInfoHeader.ReadFrom(span[BitmapFileHeader.StructSize..]);
    if (infoHeader.HeaderSize < BitmapInfoHeader.StructSize)
      throw new InvalidDataException($"Unsupported BMP header size: {infoHeader.HeaderSize}.");

    var width = infoHeader.Width;
    var rawHeight = infoHeader.Height;
    var rowOrder = rawHeight < 0 ? BmpRowOrder.TopDown : BmpRowOrder.BottomUp;
    var height = Math.Abs(rawHeight);

    var bitsPerPixel = infoHeader.BitsPerPixel;
    var bmpCompression = infoHeader.Compression;
    var colorsUsed = infoHeader.ColorsUsed;

    // Skip any extra header bytes + BITFIELDS masks
    var paletteStart = BitmapFileHeader.StructSize + infoHeader.HeaderSize;
    if (bmpCompression == 3 && infoHeader.HeaderSize == BitmapInfoHeader.StructSize)
      paletteStart += 12; // 3 x 4-byte masks

    // Read palette
    byte[]? palette = null;
    var paletteColorCount = 0;
    if (bitsPerPixel <= 8) {
      paletteColorCount = colorsUsed > 0 ? colorsUsed : 1 << bitsPerPixel;
      palette = new byte[paletteColorCount * 3];
      var paletteOffset = paletteStart;
      for (var i = 0; i < paletteColorCount; ++i) {
        palette[i * 3] = data[paletteOffset + 2];     // R (from BGR+reserved)
        palette[i * 3 + 1] = data[paletteOffset + 1]; // G
        palette[i * 3 + 2] = data[paletteOffset];     // B
        paletteOffset += 4;
      }
    }

    // Read pixel data
    var remainingBytes = data.Length - pixelDataOffset;
    var rawPixelData = new byte[remainingBytes];
    data.AsSpan(pixelDataOffset, remainingBytes).CopyTo(rawPixelData.AsSpan(0));

    var compression = bmpCompression switch {
      1 => BmpCompression.Rle8,
      2 => BmpCompression.Rle4,
      _ => BmpCompression.None
    };

    var colorMode = _DetectColorMode(bitsPerPixel, bmpCompression, palette, paletteColorCount);

    byte[] pixelData;
    if (compression == BmpCompression.Rle8) {
      pixelData = RleCompressor.DecompressRle8(rawPixelData, width, height);
    } else {
      var bytesPerRow = (width * bitsPerPixel + 7) / 8;
      var paddedBytesPerRow = (bytesPerRow + 3) & ~3;
      pixelData = new byte[bytesPerRow * height];
      for (var row = 0; row < height; ++row) {
        var srcOffset = row * paddedBytesPerRow;
        var dstRow = rowOrder == BmpRowOrder.BottomUp ? height - 1 - row : row;
        var dstOffset = dstRow * bytesPerRow;
        if (srcOffset + bytesPerRow <= rawPixelData.Length)
          rawPixelData.AsSpan(srcOffset, bytesPerRow).CopyTo(pixelData.AsSpan(dstOffset));
      }
      // After de-ordering, data is in top-down order
      rowOrder = BmpRowOrder.TopDown;
    }

    return new BmpFile {
      Width = width,
      Height = height,
      BitsPerPixel = bitsPerPixel,
      PixelData = pixelData,
      Palette = palette,
      PaletteColorCount = paletteColorCount,
      RowOrder = rowOrder,
      Compression = compression,
      ColorMode = colorMode
    };
  }

  private static BmpColorMode _DetectColorMode(int bitsPerPixel, int bmpCompression, byte[]? palette, int paletteColorCount) {
    if (bmpCompression == 3 && bitsPerPixel == 16)
      return BmpColorMode.Rgb16_565;

    if (bitsPerPixel == 24)
      return BmpColorMode.Rgb24;

    if (bitsPerPixel == 8 && palette != null) {
      var isGray = true;
      for (var i = 0; i < paletteColorCount; ++i) {
        if (palette[i * 3] != palette[i * 3 + 1] || palette[i * 3 + 1] != palette[i * 3 + 2]) {
          isGray = false;
          break;
        }
      }

      return isGray ? BmpColorMode.Grayscale8 : BmpColorMode.Palette8;
    }

    if (bitsPerPixel == 4)
      return BmpColorMode.Palette4;

    if (bitsPerPixel == 1)
      return BmpColorMode.Palette1;

    return BmpColorMode.Original;
  }
}
