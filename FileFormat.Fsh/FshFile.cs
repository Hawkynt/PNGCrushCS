using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Fsh;

/// <summary>In-memory representation of an FSH (EA Sports Shape/Texture) archive.</summary>
[FormatMagicBytes([0x53, 0x48, 0x50, 0x49])]
public readonly record struct FshFile : IImageFormatReader<FshFile>, IImageToRawImage<FshFile>, IImageFromRawImage<FshFile>, IImageFormatWriter<FshFile> {

  static string IImageFormatMetadata<FshFile>.PrimaryExtension => ".fsh";
  static string[] IImageFormatMetadata<FshFile>.FileExtensions => [".fsh"];
  static FshFile IImageFormatReader<FshFile>.FromSpan(ReadOnlySpan<byte> data) => FshReader.FromSpan(data);
  static byte[] IImageFormatWriter<FshFile>.ToBytes(FshFile file) => FshWriter.ToBytes(file);

  /// <summary>4-character directory ID (e.g. "GIMX").</summary>
  public string DirectoryId { get; init; }

  /// <summary>Image entries contained in this FSH archive.</summary>
  public IReadOnlyList<FshEntry> Entries { get; init; }

  /// <summary>Converts the first supported entry of an FSH file to a <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(FshFile file) {
    if (file.Entries.Count == 0)
      throw new ArgumentException("FSH file contains no entries.", nameof(file));

    var entry = _FindFirstSupported(file.Entries) ?? throw new ArgumentException("FSH file contains no supported pixel format entries.", nameof(file));

    return entry.RecordCode switch {
      FshRecordCode.Argb8888 or FshRecordCode.Argb8888_78 => _Argb8888ToRaw(entry),
      FshRecordCode.Rgb888 => _Rgb888ToRaw(entry),
      FshRecordCode.Rgb565 => _Rgb565ToRaw(entry),
      FshRecordCode.Indexed8 => _Indexed8ToRaw(entry),
      FshRecordCode.Argb4444 => _Argb4444ToRaw(entry),
      FshRecordCode.Argb1555 => _Argb1555ToRaw(entry),
      FshRecordCode.Dxt1 => _Dxt1ToRaw(entry),
      FshRecordCode.Dxt3 => _Dxt3ToRaw(entry),
      _ => throw new NotSupportedException($"Unsupported FSH record code: 0x{(byte)entry.RecordCode:X2}.")
    };
  }

  /// <summary>Creates a single-entry FSH file from a <see cref="RawImage"/> using ARGB8888 format.</summary>
  public static FshFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var bgra = image.ToBgra32();
    var totalPixels = image.Width * image.Height;

    // Convert BGRA32 to ARGB8888
    var argb = new byte[totalPixels * 4];
    for (var i = 0; i < totalPixels; ++i) {
      var src = i * 4;
      argb[src] = bgra[src + 3]; // A
      argb[src + 1] = bgra[src + 2]; // R
      argb[src + 2] = bgra[src + 1]; // G
      argb[src + 3] = bgra[src]; // B
    }

    return new() {
      Entries = [
        new FshEntry {
          Tag = "img0",
          RecordCode = FshRecordCode.Argb8888,
          Width = image.Width,
          Height = image.Height,
          PixelData = argb
        }
      ]
    };
  }

  private static FshEntry? _FindFirstSupported(IReadOnlyList<FshEntry> entries) {
    foreach (var entry in entries)
      if (entry.RecordCode is FshRecordCode.Argb8888 or FshRecordCode.Argb8888_78 or FshRecordCode.Rgb888 or FshRecordCode.Rgb565 or FshRecordCode.Indexed8 or FshRecordCode.Argb4444 or FshRecordCode.Argb1555 or FshRecordCode.Dxt1 or FshRecordCode.Dxt3)
        return entry;

    return null;
  }

  private static RawImage _Argb8888ToRaw(FshEntry entry) {
    var totalPixels = entry.Width * entry.Height;
    var bgra = new byte[totalPixels * 4];

    for (var i = 0; i < totalPixels; ++i) {
      var src = i * 4;
      bgra[src] = entry.PixelData[src + 3]; // B
      bgra[src + 1] = entry.PixelData[src + 2]; // G
      bgra[src + 2] = entry.PixelData[src + 1]; // R
      bgra[src + 3] = entry.PixelData[src]; // A
    }

    return new() {
      Width = entry.Width,
      Height = entry.Height,
      Format = PixelFormat.Bgra32,
      PixelData = bgra,
    };
  }

  private static RawImage _Rgb888ToRaw(FshEntry entry) => new() {
    Width = entry.Width,
    Height = entry.Height,
    Format = PixelFormat.Rgb24,
    PixelData = entry.PixelData[..],
  };

  private static RawImage _Rgb565ToRaw(FshEntry entry) => new() {
    Width = entry.Width,
    Height = entry.Height,
    Format = PixelFormat.Rgb565,
    PixelData = entry.PixelData[..],
  };

  private static RawImage _Indexed8ToRaw(FshEntry entry) {
    var palette = entry.Palette ?? throw new InvalidDataException("Indexed8 entry missing palette.");

    // Convert BGRA palette (4 bytes/entry) to RGB palette (3 bytes/entry) + alpha table
    var paletteCount = palette.Length / 4;
    var rgbPalette = new byte[paletteCount * 3];
    var alphaTable = new byte[paletteCount];

    for (var i = 0; i < paletteCount; ++i) {
      var src = i * 4;
      rgbPalette[i * 3] = palette[src + 2]; // R (from BGRA)
      rgbPalette[i * 3 + 1] = palette[src + 1]; // G
      rgbPalette[i * 3 + 2] = palette[src]; // B
      alphaTable[i] = palette[src + 3]; // A
    }

    return new() {
      Width = entry.Width,
      Height = entry.Height,
      Format = PixelFormat.Indexed8,
      PixelData = entry.PixelData[..],
      Palette = rgbPalette,
      PaletteCount = paletteCount,
      AlphaTable = alphaTable,
    };
  }

  private static RawImage _Argb4444ToRaw(FshEntry entry) {
    var totalPixels = entry.Width * entry.Height;
    var bgra = new byte[totalPixels * 4];

    for (var i = 0; i < totalPixels; ++i) {
      var src = i * 2;
      var lo = entry.PixelData[src];
      var hi = entry.PixelData[src + 1];
      var b = lo & 0x0F;
      var g = (lo >> 4) & 0x0F;
      var r = hi & 0x0F;
      var a = (hi >> 4) & 0x0F;
      var dst = i * 4;
      bgra[dst] = (byte)(b | (b << 4));
      bgra[dst + 1] = (byte)(g | (g << 4));
      bgra[dst + 2] = (byte)(r | (r << 4));
      bgra[dst + 3] = (byte)(a | (a << 4));
    }

    return new() {
      Width = entry.Width,
      Height = entry.Height,
      Format = PixelFormat.Bgra32,
      PixelData = bgra,
    };
  }

  private static RawImage _Argb1555ToRaw(FshEntry entry) {
    var totalPixels = entry.Width * entry.Height;
    var bgra = new byte[totalPixels * 4];

    for (var i = 0; i < totalPixels; ++i) {
      var src = i * 2;
      var value = entry.PixelData[src] | (entry.PixelData[src + 1] << 8);
      var b5 = value & 0x1F;
      var g5 = (value >> 5) & 0x1F;
      var r5 = (value >> 10) & 0x1F;
      var a = (value >> 15) & 1;
      var dst = i * 4;
      bgra[dst] = (byte)((b5 << 3) | (b5 >> 2));
      bgra[dst + 1] = (byte)((g5 << 3) | (g5 >> 2));
      bgra[dst + 2] = (byte)((r5 << 3) | (r5 >> 2));
      bgra[dst + 3] = (byte)(a * 255);
    }

    return new() {
      Width = entry.Width,
      Height = entry.Height,
      Format = PixelFormat.Bgra32,
      PixelData = bgra,
    };
  }

  private static RawImage _Dxt1ToRaw(FshEntry entry) {
    var totalPixels = entry.Width * entry.Height;
    var rgba = new byte[totalPixels * 4];
    FileFormat.Core.BlockDecoders.Bc1Decoder.DecodeImage(entry.PixelData, entry.Width, entry.Height, rgba);

    // BC1 outputs RGBA, convert to BGRA
    var bgra = PixelConverter.RgbaToBgra(rgba, totalPixels);
    return new() {
      Width = entry.Width,
      Height = entry.Height,
      Format = PixelFormat.Bgra32,
      PixelData = bgra,
    };
  }

  private static RawImage _Dxt3ToRaw(FshEntry entry) {
    var totalPixels = entry.Width * entry.Height;
    var rgba = new byte[totalPixels * 4];
    FileFormat.Core.BlockDecoders.Bc2Decoder.DecodeImage(entry.PixelData, entry.Width, entry.Height, rgba);

    // BC2 outputs RGBA, convert to BGRA
    var bgra = PixelConverter.RgbaToBgra(rgba, totalPixels);
    return new() {
      Width = entry.Width,
      Height = entry.Height,
      Format = PixelFormat.Bgra32,
      PixelData = bgra,
    };
  }
}
