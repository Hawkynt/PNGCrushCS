using System;
using System.IO;

namespace FileFormat.ZxMulticolor;

/// <summary>Reads ZX Spectrum Multicolor (.mlt) files from bytes, streams, or file paths.</summary>
public static class ZxMulticolorReader {

  /// <summary>Total file size: 6144 bitmap + 6144 per-scanline attributes.</summary>
  internal const int FileSize = 12288;

  /// <summary>Bitmap data size in bytes.</summary>
  internal const int BitmapSize = 6144;

  /// <summary>Per-scanline attribute data size in bytes (32 * 192).</summary>
  internal const int AttributeSize = 6144;

  /// <summary>Bytes per pixel row (256 pixels / 8 bits per pixel).</summary>
  internal const int BytesPerRow = 32;

  /// <summary>Number of pixel rows.</summary>
  internal const int RowCount = 192;

  public static ZxMulticolorFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ZX Spectrum Multicolor file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ZxMulticolorFile FromStream(Stream stream) {
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

  public static ZxMulticolorFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != FileSize)
      throw new InvalidDataException($"ZX Spectrum Multicolor file must be exactly {FileSize} bytes, got {data.Length}.");

    var linearBitmap = new byte[BitmapSize];

    // Deinterleave from ZX Spectrum memory layout to linear row order
    for (var y = 0; y < RowCount; ++y) {
      var third = y / 64;
      var characterRow = (y % 64) / 8;
      var pixelLine = y % 8;
      var srcOffset = third * 2048 + pixelLine * 256 + characterRow * BytesPerRow;
      var dstOffset = y * BytesPerRow;
      data.AsSpan(srcOffset, BytesPerRow).CopyTo(linearBitmap.AsSpan(dstOffset));
    }

    var attributes = new byte[AttributeSize];
    data.AsSpan(BitmapSize, AttributeSize).CopyTo(attributes.AsSpan(0));

    return new ZxMulticolorFile {
      BitmapData = linearBitmap,
      AttributeData = attributes,
    };
  }
}
