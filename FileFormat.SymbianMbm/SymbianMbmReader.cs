using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.SymbianMbm;

/// <summary>Reads Symbian OS MBM files from bytes, streams, or file paths.</summary>
public static class SymbianMbmReader {

  public static SymbianMbmFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MBM file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SymbianMbmFile FromStream(Stream stream) {
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

  public static SymbianMbmFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < SymbianMbmFile.MinimumFileSize)
      throw new InvalidDataException($"Data too small for a valid MBM file: expected at least {SymbianMbmFile.MinimumFileSize} bytes, got {data.Length}.");

    var span = data;

    // Parse file header
    var fileHeader = SymbianMbmFileHeader.ReadFrom(span[..SymbianMbmFileHeader.StructSize]);

    // Validate UID1 magic
    if (fileHeader.Uid1 != SymbianMbmFile.Uid1)
      throw new InvalidDataException($"Invalid MBM UID1: expected 0x{SymbianMbmFile.Uid1:X8}, got 0x{fileHeader.Uid1:X8}.");

    // Validate UID2
    if (fileHeader.Uid2 != SymbianMbmFile.Uid2)
      throw new InvalidDataException($"Invalid MBM UID2: expected 0x{SymbianMbmFile.Uid2:X8}, got 0x{fileHeader.Uid2:X8}.");

    // Read trailer offset
    var trailerOffset = fileHeader.TrailerOffset;
    if (trailerOffset < 0 || trailerOffset + 4 > data.Length)
      throw new InvalidDataException($"Invalid trailer offset: {trailerOffset}.");

    // Read bitmap count from trailer
    var bitmapCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[trailerOffset..]);
    if (bitmapCount < 0)
      throw new InvalidDataException($"Invalid bitmap count: {bitmapCount}.");

    var offsetsStart = trailerOffset + 4;
    if (offsetsStart + bitmapCount * 4 > data.Length)
      throw new InvalidDataException("Data too small for bitmap offset table.");

    // Read bitmap offsets
    var offsets = new int[bitmapCount];
    for (var i = 0; i < bitmapCount; ++i)
      offsets[i] = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[(offsetsStart + i * 4)..]);

    // Parse each bitmap
    var bitmaps = new SymbianMbmBitmap[bitmapCount];
    for (var i = 0; i < bitmapCount; ++i) {
      var offset = offsets[i];
      if (offset + SymbianMbmFile.BitmapHeaderSize > data.Length)
        throw new InvalidDataException($"Bitmap {i} header extends beyond file.");

      var bmpSpan = span[offset..];
      var bmpHeader = SymbianMbmBitmapHeader.ReadFrom(bmpSpan[..SymbianMbmBitmapHeader.StructSize]);

      var pixelDataOffset = offset + bmpHeader.HeaderLength;
      var pixelDataLength = (int)bmpHeader.DataSize;
      if (pixelDataOffset + pixelDataLength > data.Length)
        pixelDataLength = Math.Max(0, data.Length - pixelDataOffset);

      var pixelData = new byte[pixelDataLength];
      if (pixelDataLength > 0)
        data.Slice(pixelDataOffset, pixelDataLength).CopyTo(pixelData.AsSpan(0));

      bitmaps[i] = new SymbianMbmBitmap {
        Width = bmpHeader.Width,
        Height = bmpHeader.Height,
        BitsPerPixel = bmpHeader.BitsPerPixel,
        ColorMode = bmpHeader.ColorMode,
        Compression = bmpHeader.Compression,
        PaletteSize = bmpHeader.PaletteSize,
        DataSize = bmpHeader.DataSize,
        PixelData = pixelData,
      };
    }

    return new SymbianMbmFile {
      Bitmaps = bitmaps
    };
    }

  public static SymbianMbmFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
