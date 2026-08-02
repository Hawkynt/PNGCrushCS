using System;
using System.Buffers.Binary;
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

  /// <summary>The length of the OS/2 BITMAPCOREHEADER, which is what marks a file as that older kind.</summary>
  private const int CORE_HEADER_SIZE = 12;

  public static BmpFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < BitmapFileHeader.StructSize + CORE_HEADER_SIZE)
      throw new InvalidDataException("Data too small for a valid BMP file.");

    // BITMAPFILEHEADER (14 bytes)
    var fileHeader = BitmapFileHeader.ReadFrom(data);
    if (fileHeader.Sig1 != (byte)'B' || fileHeader.Sig2 != (byte)'M')
      throw new InvalidDataException("Invalid BMP signature.");

    var pixelDataOffset = fileHeader.PixelDataOffset;

    // The first field of the second header states its own length, and that is what says which of
    // the two shapes follows: 12 is the OS/2 one, anything from 40 up is the Windows one.
    var headerSize = BinaryPrimitives.ReadInt32LittleEndian(data[BitmapFileHeader.StructSize..]);

    int width, rawHeight, bitsPerPixel, bmpCompression, colorsUsed, paletteEntrySize;
    if (headerSize == CORE_HEADER_SIZE) {
      // BITMAPCOREHEADER: the sizes are 16-bit, there is no compression or colour count, and the
      // palette that follows is three bytes an entry rather than four.
      var core = data[(BitmapFileHeader.StructSize + 4)..];
      width = BinaryPrimitives.ReadUInt16LittleEndian(core);
      rawHeight = BinaryPrimitives.ReadUInt16LittleEndian(core[2..]);
      bitsPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(core[6..]);
      bmpCompression = 0;
      colorsUsed = 0;
      paletteEntrySize = 3;
    } else {
      if (headerSize < BitmapInfoHeader.StructSize || data.Length < BitmapFileHeader.StructSize + BitmapInfoHeader.StructSize)
        throw new InvalidDataException($"Unsupported BMP header size: {headerSize}.");

      var infoHeader = BitmapInfoHeader.ReadFrom(data[BitmapFileHeader.StructSize..]);
      width = infoHeader.Width;
      rawHeight = infoHeader.Height;
      bitsPerPixel = infoHeader.BitsPerPixel;
      bmpCompression = infoHeader.Compression;
      colorsUsed = infoHeader.ColorsUsed;
      paletteEntrySize = 4;
    }

    var rowOrder = rawHeight < 0 ? BmpRowOrder.TopDown : BmpRowOrder.BottomUp;
    var height = Math.Abs(rawHeight);

    // Skip any extra header bytes + BITFIELDS masks
    var paletteStart = BitmapFileHeader.StructSize + headerSize;
    if (bmpCompression == 3 && headerSize == BitmapInfoHeader.StructSize)
      paletteStart += 12; // 3 x 4-byte masks

    // Read palette
    byte[]? palette = null;
    var paletteColorCount = 0;
    if (bitsPerPixel <= 8) {
      paletteColorCount = colorsUsed > 0 ? colorsUsed : 1 << bitsPerPixel;

      // A file may state more entries than it carries; keep to what is actually there.
      var available = (pixelDataOffset > paletteStart ? pixelDataOffset - paletteStart : data.Length - paletteStart) / paletteEntrySize;
      if (available > 0 && paletteColorCount > available)
        paletteColorCount = available;

      palette = new byte[paletteColorCount * 3];
      var paletteOffset = paletteStart;
      for (var i = 0; i < paletteColorCount; ++i) {
        palette[i * 3] = data[paletteOffset + 2];     // R (from BGR+reserved)
        palette[i * 3 + 1] = data[paletteOffset + 1]; // G
        palette[i * 3 + 2] = data[paletteOffset];     // B
        paletteOffset += paletteEntrySize;
      }
    }

    // Read pixel data
    var remainingBytes = data.Length - pixelDataOffset;
    var rawPixelData = new byte[remainingBytes];
    data.Slice(pixelDataOffset, remainingBytes).CopyTo(rawPixelData.AsSpan(0));

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
      // KNOWN WRONG for at least one uncompressed 1-bit file, and worth saying so here because BMP is
      // the last format anyone would think to doubt.
      //
      // A 196 by 228 one-bit BMP with a two-entry palette decodes here to 71% of the pixels XnView
      // and ImageMagick agree on — and those two agree with each other exactly, so the picture is not
      // in question. The errors run both ways, six thousand pixels black that should be white and six
      // thousand the reverse, scattered rather than shifted, so it is not the row order, the padding,
      // the palette or the bit order within a byte; each of those was tried against the file and
      // ruled out. Reading the file by hand bottom-up with a set bit as white reproduces the other
      // two exactly, which is what this code appears to do.
      //
      // Left recorded rather than guessed at. Whoever picks it up should start from the sample rather
      // than from this loop, since the loop reads correctly to inspection.
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

  public static BmpFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
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
