using System;
using System.IO;

namespace FileFormat.ZxTricolor;

/// <summary>Reads ZX Spectrum Tricolor (.3cl) files from bytes, streams, or file paths.</summary>
public static class ZxTricolorReader {

  /// <summary>Total file size: three complete 6912-byte screens.</summary>
  internal const int FileSize = 20736;

  /// <summary>Size of one complete ZX Spectrum screen.</summary>
  internal const int ScreenSize = 6912;

  /// <summary>Bitmap data size per screen.</summary>
  internal const int BitmapSize = 6144;

  /// <summary>Attribute data size per screen.</summary>
  internal const int AttributeSize = 768;

  /// <summary>Bytes per pixel row.</summary>
  internal const int BytesPerRow = 32;

  /// <summary>Number of pixel rows.</summary>
  internal const int RowCount = 192;

  public static ZxTricolorFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ZX Spectrum Tricolor file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ZxTricolorFile FromStream(Stream stream) {
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

  public static ZxTricolorFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != FileSize)
      throw new InvalidDataException($"ZX Spectrum Tricolor file must be exactly {FileSize} bytes, got {data.Length}.");

    var bitmap1 = _DeinterleaveBitmap(data, 0);
    var attr1 = new byte[AttributeSize];
    data.Slice(BitmapSize, AttributeSize).CopyTo(attr1);

    var bitmap2 = _DeinterleaveBitmap(data, ScreenSize);
    var attr2 = new byte[AttributeSize];
    data.Slice(ScreenSize + BitmapSize, AttributeSize).CopyTo(attr2);

    var bitmap3 = _DeinterleaveBitmap(data, ScreenSize * 2);
    var attr3 = new byte[AttributeSize];
    data.Slice(ScreenSize * 2 + BitmapSize, AttributeSize).CopyTo(attr3);

    return new ZxTricolorFile {
      BitmapData1 = bitmap1,
      AttributeData1 = attr1,
      BitmapData2 = bitmap2,
      AttributeData2 = attr2,
      BitmapData3 = bitmap3,
      AttributeData3 = attr3,
    };
  
  }

  public static ZxTricolorFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  private static byte[] _DeinterleaveBitmap(ReadOnlySpan<byte> data, int baseOffset) {
    var linear = new byte[BitmapSize];
    for (var y = 0; y < RowCount; ++y) {
      var third = y / 64;
      var characterRow = (y % 64) / 8;
      var pixelLine = y % 8;
      var srcOffset = baseOffset + third * 2048 + pixelLine * 256 + characterRow * BytesPerRow;
      var dstOffset = y * BytesPerRow;
      data.Slice(srcOffset, BytesPerRow).CopyTo(linear.AsSpan(dstOffset));
    }
    return linear;
  }
}
