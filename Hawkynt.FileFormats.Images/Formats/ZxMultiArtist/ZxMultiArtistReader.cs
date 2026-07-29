using System;
using System.IO;

namespace FileFormat.ZxMultiArtist;

/// <summary>Reads ZX Spectrum MultiArtist (.mg1/.mg2/.mg4/.mg8) files from bytes, streams, or file paths.</summary>
public static class ZxMultiArtistReader {

  /// <summary>Bitmap data size in bytes.</summary>
  internal const int BitmapSize = 6144;

  /// <summary>Bytes per pixel row (256 pixels / 8 bits per pixel).</summary>
  internal const int BytesPerRow = 32;

  /// <summary>Number of pixel rows.</summary>
  internal const int RowCount = 192;

  public static ZxMultiArtistFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ZX Spectrum MultiArtist file not found.", file.FullName);

    var data = File.ReadAllBytes(file.FullName);
    var mode = _DetectModeFromExtension(file.Extension) ?? ZxMultiArtistFile.DetectMode(data.Length);
    return FromBytes(data, mode);
  }

  public static ZxMultiArtistFile FromStream(Stream stream) {
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

  public static ZxMultiArtistFile FromSpan(ReadOnlySpan<byte> data, ZxMultiArtistMode? hintMode = null) {
    var mode = hintMode ?? ZxMultiArtistFile.DetectMode(data.Length)
      ?? throw new InvalidDataException($"Cannot determine MultiArtist mode from file size {data.Length}. Expected 6912 (MG8), 7680 (MG4), 9216 (MG2), or 12288 (MG1).");

    var expectedSize = ZxMultiArtistFile.GetFileSize(mode);
    if (data.Length != expectedSize)
      throw new InvalidDataException($"ZX Spectrum MultiArtist {mode} file must be exactly {expectedSize} bytes, got {data.Length}.");

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

    var attributeSize = ZxMultiArtistFile.GetAttributeSize(mode);
    var attributes = new byte[attributeSize];
    data.Slice(BitmapSize, attributeSize).CopyTo(attributes);

    return new ZxMultiArtistFile {
      Mode = mode,
      BitmapData = linearBitmap,
      AttributeData = attributes,
    };
  }

  public static ZxMultiArtistFile FromBytes(byte[] data) => FromBytes(data, null);

  public static ZxMultiArtistFile FromBytes(byte[] data, ZxMultiArtistMode? hintMode) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data, hintMode);
  }

  private static ZxMultiArtistMode? _DetectModeFromExtension(string extension) => extension.ToLowerInvariant() switch {
    ".mg1" => ZxMultiArtistMode.Mg1,
    ".mg2" => ZxMultiArtistMode.Mg2,
    ".mg4" => ZxMultiArtistMode.Mg4,
    ".mg8" => ZxMultiArtistMode.Mg8,
    _ => null
  };
}
