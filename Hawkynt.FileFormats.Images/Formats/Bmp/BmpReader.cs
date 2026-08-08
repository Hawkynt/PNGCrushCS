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
      // A 4-bit run-length picture used to fall straight through here, which read its opcodes as
      // pixels and drew noise; the writer has been able to produce these all along and nothing could
      // read one back. Unpacking it into the rows an uncompressed one would have had keeps the
      // ordering and the un-padding below as one path rather than two.
      if (compression == BmpCompression.Rle4)
        rawPixelData = RleCompressor.DecompressRle4(rawPixelData, width, height);

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

      // A RawImage in a sub-byte format runs its indices straight on across the picture, where BMP
      // starts every row on a byte boundary. The two agree for any width that is a multiple of eight
      // pixels, which is nearly every picture — and diverge by the padding bits for the rest, putting
      // every row after the first further out of step than the one above it. A 196 by 228 one-bit
      // file came out 71% right against XnView and ImageMagick, which agree with each other exactly.
      pixelData = _RemoveRowPadding(pixelData, width, height, bitsPerPixel);
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

  /// <summary>Restacks sub-byte rows from BMP's byte-aligned layout to the continuous one.</summary>
  private static byte[] _RemoveRowPadding(byte[] padded, int width, int height, int bitsPerPixel) {
    if (bitsPerPixel >= 8)
      return padded;

    var paddedStride = (width * bitsPerPixel + 7) / 8;
    if (paddedStride * 8 == width * bitsPerPixel)
      return padded;

    var result = new byte[(width * height * bitsPerPixel + 7) / 8];
    var mask = (1 << bitsPerPixel) - 1;

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var sourceBit = y * paddedStride * 8 + x * bitsPerPixel;
      var sourceByte = sourceBit >> 3;
      if (sourceByte >= padded.Length)
        return result;

      var value = (padded[sourceByte] >> (8 - bitsPerPixel - (sourceBit & 7))) & mask;
      var targetBit = (y * width + x) * bitsPerPixel;
      result[targetBit >> 3] |= (byte)(value << (8 - bitsPerPixel - (targetBit & 7)));
    }

    return result;
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
