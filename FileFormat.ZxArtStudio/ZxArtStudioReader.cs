using System;
using System.IO;

namespace FileFormat.ZxArtStudio;

/// <summary>Reads ZX Spectrum Art Studio (.zas) files from bytes, streams, or file paths.</summary>
public static class ZxArtStudioReader {

  /// <summary>Total file size: 6144 bitmap + 768 attributes.</summary>
  internal const int FileSize = 6912;

  /// <summary>Bitmap data size in bytes.</summary>
  internal const int BitmapSize = 6144;

  /// <summary>Attribute data size in bytes.</summary>
  internal const int AttributeSize = 768;

  /// <summary>Bytes per pixel row.</summary>
  internal const int BytesPerRow = 32;

  /// <summary>Number of pixel rows.</summary>
  internal const int RowCount = 192;

  public static ZxArtStudioFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ZX Spectrum Art Studio file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ZxArtStudioFile FromStream(Stream stream) {
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

  public static ZxArtStudioFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != FileSize)
      throw new InvalidDataException($"ZX Spectrum Art Studio file must be exactly {FileSize} bytes, got {data.Length}.");

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

    return new ZxArtStudioFile {
      BitmapData = linearBitmap,
      AttributeData = attributes,
    };
  }
}
