using System;
using System.IO;

namespace FileFormat.ZxFlash;

/// <summary>Reads ZX Spectrum Flash animation (.zfl) files from bytes, streams, or file paths.</summary>
public static class ZxFlashReader {

  /// <summary>Minimum file size: one complete 6912-byte screen.</summary>
  internal const int MinFileSize = 6912;

  /// <summary>Size of one complete ZX Spectrum screen.</summary>
  internal const int ScreenSize = 6912;

  /// <summary>Bitmap data size in bytes.</summary>
  internal const int BitmapSize = 6144;

  /// <summary>Attribute data size in bytes.</summary>
  internal const int AttributeSize = 768;

  /// <summary>Bytes per pixel row.</summary>
  internal const int BytesPerRow = 32;

  /// <summary>Number of pixel rows.</summary>
  internal const int RowCount = 192;

  public static ZxFlashFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ZX Spectrum Flash file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ZxFlashFile FromStream(Stream stream) {
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

  public static ZxFlashFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < MinFileSize)
      throw new InvalidDataException($"ZX Spectrum Flash file must be at least {MinFileSize} bytes, got {data.Length}.");

    var frameCount = data.Length / ScreenSize;

    var linearBitmap = new byte[BitmapSize];

    // Deinterleave first frame from ZX Spectrum memory layout to linear row order
    for (var y = 0; y < RowCount; ++y) {
      var third = y / 64;
      var characterRow = (y % 64) / 8;
      var pixelLine = y % 8;
      var srcOffset = third * 2048 + pixelLine * 256 + characterRow * BytesPerRow;
      var dstOffset = y * BytesPerRow;
      data.Slice(srcOffset, BytesPerRow).CopyTo(linearBitmap.AsSpan(dstOffset));
    }

    var attributes = new byte[AttributeSize];
    data.Slice(BitmapSize, AttributeSize).CopyTo(attributes.AsSpan(0));

    return new ZxFlashFile {
      BitmapData = linearBitmap,
      AttributeData = attributes,
      FrameCount = frameCount,
    };
    }

  public static ZxFlashFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < MinFileSize)
      throw new InvalidDataException($"ZX Spectrum Flash file must be at least {MinFileSize} bytes, got {data.Length}.");

    var frameCount = data.Length / ScreenSize;

    var linearBitmap = new byte[BitmapSize];

    // Deinterleave first frame from ZX Spectrum memory layout to linear row order
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

    return new ZxFlashFile {
      BitmapData = linearBitmap,
      AttributeData = attributes,
      FrameCount = frameCount,
    };
  }
}
