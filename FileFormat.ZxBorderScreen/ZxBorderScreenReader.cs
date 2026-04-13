using System;
using System.IO;

namespace FileFormat.ZxBorderScreen;

/// <summary>Reads ZX Spectrum Border Screen (.bsc) files from bytes, streams, or file paths.</summary>
public static class ZxBorderScreenReader {

  /// <summary>Total file size: 6144 bitmap + 768 attributes + 4224 border data.</summary>
  internal const int FileSize = 11136;

  /// <summary>Bitmap data size in bytes.</summary>
  internal const int BitmapSize = 6144;

  /// <summary>Attribute data size in bytes.</summary>
  internal const int AttributeSize = 768;

  /// <summary>Border data size in bytes.</summary>
  internal const int BorderSize = 4224;

  /// <summary>Bytes per pixel row (256 pixels / 8 bits per pixel).</summary>
  internal const int BytesPerRow = 32;

  /// <summary>Number of pixel rows.</summary>
  internal const int RowCount = 192;

  public static ZxBorderScreenFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ZX Spectrum Border Screen file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ZxBorderScreenFile FromStream(Stream stream) {
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

  public static ZxBorderScreenFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length != FileSize)
      throw new InvalidDataException($"ZX Spectrum Border Screen file must be exactly {FileSize} bytes, got {data.Length}.");

    var linearBitmap = new byte[BitmapSize];

    // Deinterleave from ZX Spectrum memory layout to linear row order
    for (var y = 0; y < RowCount; ++y) {
      var third = y / 64;
      var characterRow = (y % 64) / 8;
      var pixelLine = y % 8;
      var srcOffset = third * 2048 + pixelLine * 256 + characterRow * BytesPerRow;
      var dstOffset = y * BytesPerRow;
      data.Slice(srcOffset, BytesPerRow).CopyTo(linearBitmap.AsSpan(dstOffset));
    }

    var attributes = new byte[AttributeSize];
    data.Slice(BitmapSize, AttributeSize).CopyTo(attributes);

    var border = new byte[BorderSize];
    data.Slice(BitmapSize + AttributeSize, BorderSize).CopyTo(border);

    return new ZxBorderScreenFile {
      BitmapData = linearBitmap,
      AttributeData = attributes,
      BorderData = border,
    };
    }

  public static ZxBorderScreenFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != FileSize)
      throw new InvalidDataException($"ZX Spectrum Border Screen file must be exactly {FileSize} bytes, got {data.Length}.");

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
    data.AsSpan(BitmapSize, AttributeSize).CopyTo(attributes);

    var border = new byte[BorderSize];
    data.AsSpan(BitmapSize + AttributeSize, BorderSize).CopyTo(border);

    return new ZxBorderScreenFile {
      BitmapData = linearBitmap,
      AttributeData = attributes,
      BorderData = border,
    };
  }
}
