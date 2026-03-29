using System;
using System.IO;

namespace FileFormat.AppleII;

/// <summary>Reads Apple II Hi-Res Graphics files from bytes, streams, or file paths.</summary>
public static class AppleIIReader {

  /// <summary>HGR file size in bytes.</summary>
  internal const int HgrFileSize = 8192;

  /// <summary>DHGR file size in bytes.</summary>
  internal const int DhgrFileSize = 16384;

  /// <summary>Number of scanlines.</summary>
  internal const int RowCount = 192;

  /// <summary>HGR width in pixels.</summary>
  internal const int HgrWidth = 280;

  /// <summary>DHGR width in pixels.</summary>
  internal const int DhgrWidth = 560;

  /// <summary>Bytes per scanline for HGR.</summary>
  internal const int HgrBytesPerLine = 40;

  public static AppleIIFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Apple II HGR file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AppleIIFile FromStream(Stream stream) {
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

  public static AppleIIFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < HgrFileSize)
      throw new InvalidDataException($"Data too small for a valid Apple II HGR file. Expected at least {HgrFileSize} bytes, got {data.Length}.");

    var mode = data.Length switch {
      HgrFileSize => AppleIIMode.Hgr,
      DhgrFileSize => AppleIIMode.Dhgr,
      _ => throw new InvalidDataException($"Invalid Apple II HGR file size. Expected {HgrFileSize} (HGR) or {DhgrFileSize} (DHGR) bytes, got {data.Length}.")
    };

    var width = mode == AppleIIMode.Dhgr ? DhgrWidth : HgrWidth;
    var pixelData = AppleIILayoutConverter.Deinterleave(data, mode);

    return new AppleIIFile {
      Width = width,
      Height = RowCount,
      Mode = mode,
      PixelData = pixelData
    };
  }
}
