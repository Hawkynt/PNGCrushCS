using System;

namespace FileFormat.Ingr;

/// <summary>Assembles Intergraph Raster (INGR) file bytes from pixel data.</summary>
public static class IngrWriter {

  /// <summary>INGR header type value (type 9).</summary>
  internal const ushort HeaderType = 0x0809;

  /// <summary>Size of the INGR header block in bytes.</summary>
  internal const int HeaderSize = 512;

  public static byte[] ToBytes(IngrFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height, file.DataType);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height, IngrDataType dataType) {
    var bytesPerPixel = dataType == IngrDataType.ByteData ? 1 : 3;
    var expectedPixelBytes = width * height * bytesPerPixel;

    var result = new byte[HeaderSize + expectedPixelBytes];

    // Header type at offset 0-1
    BitConverter.TryWriteBytes(result.AsSpan(0), HeaderType);

    // Data type code at offset 2-3
    BitConverter.TryWriteBytes(result.AsSpan(2), (ushort)dataType);

    // X extent at offset 8-9 (int16 LE)
    BitConverter.TryWriteBytes(result.AsSpan(8), (short)width);

    // Y extent at offset 10-11 (int16 LE)
    BitConverter.TryWriteBytes(result.AsSpan(10), (short)height);

    // Pixels per line at offset 184-187 (int32 LE)
    BitConverter.TryWriteBytes(result.AsSpan(184), width);

    // Number of lines at offset 188-191 (int32 LE)
    BitConverter.TryWriteBytes(result.AsSpan(188), height);

    // Copy pixel data
    var copyLen = Math.Min(expectedPixelBytes, pixelData.Length);
    pixelData.AsSpan(0, copyLen).CopyTo(result.AsSpan(HeaderSize));

    return result;
  }
}
