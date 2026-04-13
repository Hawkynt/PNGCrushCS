using System;
using System.IO;

namespace FileFormat.SunRaster;

/// <summary>Reads Sun Raster files from bytes, streams, or file paths.</summary>
public static class SunRasterReader {

  public static SunRasterFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Sun Raster file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SunRasterFile FromStream(Stream stream) {
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

  public static SunRasterFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < SunRasterHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid Sun Raster file.");

    var span = data;
    var header = SunRasterHeader.ReadFrom(span);

    if (header.Magic != SunRasterHeader.MagicValue)
      throw new InvalidDataException("Invalid Sun Raster magic number.");

    var width = header.Width;
    var height = header.Height;
    var depth = header.Depth;
    var type = header.Type;
    var mapType = header.MapType;
    var mapLength = header.MapLength;

    var offset = SunRasterHeader.StructSize;

    // Read colormap
    byte[]? palette = null;
    var paletteColorCount = 0;
    if (mapLength > 0 && mapType == 1) {
      // RGB colormap: R[n], G[n], B[n] stored as three consecutive planes
      paletteColorCount = mapLength / 3;
      palette = new byte[paletteColorCount * 3];
      for (var i = 0; i < paletteColorCount; ++i) {
        palette[i * 3] = data[offset + i];                          // R
        palette[i * 3 + 1] = data[offset + paletteColorCount + i];  // G
        palette[i * 3 + 2] = data[offset + paletteColorCount * 2 + i]; // B
      }

      offset += mapLength;
    } else if (mapLength > 0) {
      // Raw or no colormap, skip
      offset += mapLength;
    }

    // Read pixel data
    var remainingBytes = data.Length - offset;
    var rawPixelData = new byte[remainingBytes];
    data.Slice(offset, remainingBytes).CopyTo(rawPixelData.AsSpan(0));

    // Rows are padded to 16-bit (2-byte) boundary
    var bytesPerRow = (width * depth + 7) / 8;
    var paddedBytesPerRow = (bytesPerRow + 1) & ~1; // pad to 2-byte boundary
    var expectedSize = paddedBytesPerRow * height;

    byte[] pixelData;
    var compression = (SunRasterCompression)type;
    if (compression == SunRasterCompression.Rle)
      pixelData = SunRasterRleCompressor.Decompress(rawPixelData, expectedSize);
    else {
      pixelData = new byte[expectedSize];
      rawPixelData.AsSpan(0, Math.Min(rawPixelData.Length, expectedSize)).CopyTo(pixelData.AsSpan(0));
    }

    var colorMode = _DetectColorMode(depth, palette, paletteColorCount);

    return new SunRasterFile {
      Width = width,
      Height = height,
      Depth = depth,
      Compression = compression,
      ColorMode = colorMode,
      PixelData = pixelData,
      Palette = palette,
      PaletteColorCount = paletteColorCount
    };
    }

  public static SunRasterFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  private static SunRasterColorMode _DetectColorMode(int depth, byte[]? palette, int paletteColorCount) {
    if (depth == 1)
      return SunRasterColorMode.Monochrome;

    if (depth == 8 && palette != null && paletteColorCount > 0)
      return SunRasterColorMode.Palette8;

    if (depth == 24)
      return SunRasterColorMode.Rgb24;

    if (depth == 32)
      return SunRasterColorMode.Rgb32;

    return SunRasterColorMode.Original;
  }
}
