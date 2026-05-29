using System;
using System.IO;

namespace FileFormat.MacPaint;

/// <summary>Assembles MacPaint file bytes from pixel data.</summary>
public static class MacPaintWriter {

  public static byte[] ToBytes(MacPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

    using var ms = new MemoryStream();

    // Write 512-byte header
    var patterns = file.BrushPatterns ?? new byte[MacPaintHeader.PatternsSize];
    var header = new MacPaintHeader(file.Version, patterns);
    var headerBytes = new byte[MacPaintHeader.StructSize];
    header.WriteTo(headerBytes);
    ms.Write(headerBytes);

    // Compress pixel data with PackBits
    var compressed = PackBitsCompressor.Compress(file.PixelData);
    ms.Write(compressed);

    return ms.ToArray();
  }
}
