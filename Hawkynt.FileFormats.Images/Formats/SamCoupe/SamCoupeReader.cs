using System;
using System.IO;

namespace FileFormat.SamCoupe;

/// <summary>Reads SAM Coupe screen files from bytes, streams, or file paths.</summary>
public static class SamCoupeReader {

  /// <summary>Total file size: 24576 bytes (two 16 KiB pages, but only 12288 bytes used per page).</summary>
  internal const int FileSize = 24576;

  /// <summary>Bytes per scanline.</summary>
  internal const int BytesPerRow = 128;

  /// <summary>Number of pixel rows.</summary>
  internal const int RowCount = 192;

  /// <summary>Half the total size: bytes in each page (even lines / odd lines).</summary>
  internal const int PageSize = 12288;

  /// <summary>Number of lines per page (96 even + 96 odd).</summary>
  internal const int LinesPerPage = 96;

  public static SamCoupeFile FromFile(FileInfo file, SamCoupeMode mode = SamCoupeMode.Mode4) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("SAM Coupe screen file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName), mode);
  }

  public static SamCoupeFile FromStream(Stream stream, SamCoupeMode mode = SamCoupeMode.Mode4) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data, mode);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray(), mode);
  }

  public static SamCoupeFile FromSpan(ReadOnlySpan<byte> data, SamCoupeMode mode = SamCoupeMode.Mode4) {
    if (data.Length != FileSize)
      throw new InvalidDataException($"SAM Coupe screen must be exactly {FileSize} bytes, got {data.Length}.");

    var width = mode switch {
      SamCoupeMode.Mode3 => 512,
      SamCoupeMode.Mode4 => 256,
      _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown SAM Coupe mode.")
    };

    // Deinterleave pages: even lines (0,2,4,...) from first 12288 bytes, odd lines (1,3,5,...) from second 12288 bytes
    var linearData = new byte[RowCount * BytesPerRow];

    for (var i = 0; i < LinesPerPage; ++i) {
      var evenRow = i * 2;
      var oddRow = i * 2 + 1;

      data.Slice(i * BytesPerRow, BytesPerRow).CopyTo(linearData.AsSpan(evenRow * BytesPerRow));
      data.Slice(PageSize + i * BytesPerRow, BytesPerRow).CopyTo(linearData.AsSpan(oddRow * BytesPerRow));
    }

    return new() {
      Width = width,
      Height = RowCount,
      Mode = mode,
      PixelData = linearData
    };
  }

  public static SamCoupeFile FromBytes(byte[] data, SamCoupeMode mode = SamCoupeMode.Mode4) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data, mode);
  }
}
