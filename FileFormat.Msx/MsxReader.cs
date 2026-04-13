using System;
using System.IO;

namespace FileFormat.Msx;

/// <summary>Reads MSX2 screen dump files from bytes, streams, or file paths.</summary>
public static class MsxReader {

  /// <summary>BLOAD header magic byte.</summary>
  private const byte _BLOAD_MAGIC = 0xFE;

  /// <summary>BLOAD header size in bytes.</summary>
  private const int _BLOAD_HEADER_SIZE = 7;

  /// <summary>Raw data size for Screen 2 (256x192, pattern+color tables).</summary>
  private const int _SC2_SIZE = 16384;

  /// <summary>Raw data size for Screen 5 (256x212, 4bpp + 32-byte palette).</summary>
  private const int _SC5_SIZE = 26880;

  /// <summary>Raw data size for Screen 7/8 (512x212 4bpp or 256x212 8bpp + optional palette).</summary>
  private const int _SC7_SC8_SIZE = 54272;

  /// <summary>Palette size in bytes for SC5 and SC7.</summary>
  private const int _PALETTE_SIZE = 32;

  public static MsxFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MSX file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MsxFile FromStream(Stream stream) {
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

  public static MsxFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < _SC2_SIZE)
      throw new InvalidDataException($"Data too small for a valid MSX screen dump: got {data.Length} bytes.");

    var hasBload = data.Length >= _BLOAD_HEADER_SIZE && data[0] == _BLOAD_MAGIC;
    var rawData = hasBload ? data[_BLOAD_HEADER_SIZE..] : data;
    var rawLength = rawData.Length;

    return rawLength switch {
      _SC2_SIZE => _ParseSc2(rawData, hasBload),
      _SC5_SIZE => _ParseSc5(rawData, hasBload),
      _SC7_SC8_SIZE => _ParseSc8(rawData, hasBload),
      _ => throw new InvalidDataException($"Unrecognized MSX data size: {rawLength} bytes (after BLOAD header stripping).")
    };
    }

  public static MsxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  private static MsxFile _ParseSc2(ReadOnlySpan<byte> rawData, bool hasBload) {
    var pixelData = new byte[_SC2_SIZE];
    rawData[.._SC2_SIZE].CopyTo(pixelData);

    return new MsxFile {
      Width = 256,
      Height = 192,
      Mode = MsxMode.Screen2,
      BitsPerPixel = 1,
      PixelData = pixelData,
      Palette = null,
      HasBloadHeader = hasBload
    };
  }

  private static MsxFile _ParseSc5(ReadOnlySpan<byte> rawData, bool hasBload) {
    var pixelDataLength = _SC5_SIZE - _PALETTE_SIZE;
    var pixelData = new byte[pixelDataLength];
    rawData[..pixelDataLength].CopyTo(pixelData);

    var palette = new byte[_PALETTE_SIZE];
    rawData[pixelDataLength..].CopyTo(palette);

    return new MsxFile {
      Width = 256,
      Height = 212,
      Mode = MsxMode.Screen5,
      BitsPerPixel = 4,
      PixelData = pixelData,
      Palette = palette,
      HasBloadHeader = hasBload
    };
  }

  private static MsxFile _ParseSc8(ReadOnlySpan<byte> rawData, bool hasBload) {
    var pixelData = new byte[_SC7_SC8_SIZE];
    rawData[.._SC7_SC8_SIZE].CopyTo(pixelData);

    return new MsxFile {
      Width = 256,
      Height = 212,
      Mode = MsxMode.Screen8,
      BitsPerPixel = 8,
      PixelData = pixelData,
      Palette = null,
      HasBloadHeader = hasBload
    };
  }
}
