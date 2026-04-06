using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using FileFormat.Png;

namespace FileFormat.Icns;

/// <summary>In-memory representation of an Apple ICNS icon container file.</summary>
[FormatMagicBytes([0x69, 0x63, 0x6E, 0x73])]
public sealed class IcnsFile : IImageFormatReader<IcnsFile>, IImageToRawImage<IcnsFile>, IImageFromRawImage<IcnsFile>, IImageFormatWriter<IcnsFile>, IMultiImageFileFormat<IcnsFile> {

  static string IImageFormatMetadata<IcnsFile>.PrimaryExtension => ".icns";
  static string[] IImageFormatMetadata<IcnsFile>.FileExtensions => [".icns"];
  static IcnsFile IImageFormatReader<IcnsFile>.FromSpan(ReadOnlySpan<byte> data) => IcnsReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<IcnsFile>.Capabilities => FormatCapability.MultiImage;
  static byte[] IImageFormatWriter<IcnsFile>.ToBytes(IcnsFile file) => IcnsWriter.ToBytes(file);

  /// <summary>The icon entries contained in this ICNS file.</summary>
  public IReadOnlyList<IcnsEntry> Entries { get; init; } = [];

  /// <summary>Converts the ICNS file to a <see cref="RawImage"/> by extracting the largest available icon.</summary>
  public static RawImage ToRawImage(IcnsFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Entries.Count == 0)
      throw new InvalidDataException("ICNS file contains no entries.");

    // Try PNG entries first (largest resolution preferred)
    var pngEntry = file.Entries
      .Where(e => e.IsPng)
      .OrderByDescending(e => e.Width * e.Height)
      .Cast<IcnsEntry?>()
      .FirstOrDefault();

    if (pngEntry is { } png)
      return _DecodePngEntry(png);

    // Try legacy RGB entries (largest resolution preferred)
    var rgbEntry = file.Entries
      .Where(e => e.IsLegacyRgb)
      .OrderByDescending(e => e.Width * e.Height)
      .Cast<IcnsEntry?>()
      .FirstOrDefault();

    if (rgbEntry is { } rgb) {
      var maskType = _GetMatchingMaskType(rgb.OsType);
      IcnsEntry? maskEntry = maskType != null
        ? file.Entries
            .Where(e => e.OsType == maskType)
            .Cast<IcnsEntry?>()
            .FirstOrDefault()
        : null;

      return _DecodeLegacyEntry(rgb, maskEntry);
    }

    throw new InvalidDataException("ICNS file contains no decodable icon entries.");
  }

  /// <summary>Returns the number of decodable icon entries (PNG or legacy RGB) in the ICNS file.</summary>
  public static int ImageCount(IcnsFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return _GetDecodableEntries(file).Count;
  }

  /// <summary>Converts a specific decodable icon entry at the given index to a <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(IcnsFile file, int index) {
    ArgumentNullException.ThrowIfNull(file);
    var decodable = _GetDecodableEntries(file);
    if ((uint)index >= (uint)decodable.Count)
      throw new ArgumentOutOfRangeException(nameof(index));

    var entry = decodable[index];
    if (entry.IsPng)
      return _DecodePngEntry(entry);

    var maskType = _GetMatchingMaskType(entry.OsType);
    IcnsEntry? maskEntry = maskType != null
      ? file.Entries
          .Where(e => e.OsType == maskType)
          .Cast<IcnsEntry?>()
          .FirstOrDefault()
      : null;

    return _DecodeLegacyEntry(entry, maskEntry);
  }

  /// <summary>Returns all decodable entries (PNG or legacy RGB), ordered by resolution descending.</summary>
  private static IReadOnlyList<IcnsEntry> _GetDecodableEntries(IcnsFile file)
    => file.Entries
      .Where(e => e.IsPng || e.IsLegacyRgb)
      .OrderByDescending(e => e.Width * e.Height)
      .ToList();

  /// <summary>Creates an ICNS file from a <see cref="RawImage"/> by encoding a single PNG entry at the appropriate size.</summary>
  public static IcnsFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    // Convert to a format PngFile understands
    var rgba = image.Format == PixelFormat.Rgba32 ? image : PixelConverter.Convert(image, PixelFormat.Rgba32);

    var pngFile = PngFile.FromRawImage(rgba);
    var pngBytes = PngWriter.ToBytes(pngFile);

    var osType = _SelectOsType(image.Width, image.Height);
    var entry = new IcnsEntry(osType, pngBytes, image.Width, image.Height);

    return new() { Entries = [entry] };
  }

  /// <summary>Decodes a PNG-based ICNS entry to a RawImage.</summary>
  private static RawImage _DecodePngEntry(IcnsEntry entry) {
    var pngFile = PngReader.FromBytes(entry.Data);
    return PngFile.ToRawImage(pngFile);
  }

  /// <summary>Decodes a legacy 24-bit RLE ICNS entry with optional alpha mask to a RawImage.</summary>
  private static RawImage _DecodeLegacyEntry(IcnsEntry rgbEntry, IcnsEntry? maskEntry) {
    var pixelCount = rgbEntry.Width * rgbEntry.Height;
    var data = rgbEntry.Data;

    // it32 has a 4-byte zero header before the RLE data
    if (rgbEntry.OsType == "it32" && data.Length >= 4 && data[0] == 0 && data[1] == 0 && data[2] == 0 && data[3] == 0) {
      var trimmed = new byte[data.Length - 4];
      data.AsSpan(4, trimmed.Length).CopyTo(trimmed.AsSpan(0));
      data = trimmed;
    }

    var rgb = IcnsRleCompressor.Decompress(data, pixelCount);

    var rgba = PixelConverter.Rgb24ToRgba32(rgb, pixelCount);

    // Apply alpha mask if present
    if (maskEntry is { } mask && mask.Data.Length >= pixelCount)
      for (var i = 0; i < pixelCount; ++i)
        rgba[i * 4 + 3] = mask.Data[i];

    return new() {
      Width = rgbEntry.Width,
      Height = rgbEntry.Height,
      Format = PixelFormat.Rgba32,
      PixelData = rgba,
    };
  }

  /// <summary>Returns the mask OSType that matches a given legacy RGB OSType, or null if none.</summary>
  private static string? _GetMatchingMaskType(string rgbType) => rgbType switch {
    "is32" => "s8mk",
    "il32" => "l8mk",
    "ih32" => "h8mk",
    "it32" => "t8mk",
    _ => null,
  };

  /// <summary>Selects the best PNG-based OSType for a given image dimension.</summary>
  private static string _SelectOsType(int width, int height) {
    // Use the larger dimension for selection
    var size = Math.Max(width, height);
    return size switch {
      <= 32 => "ic11",   // 32x32 (16x16@2x)
      <= 64 => "ic12",   // 64x64 (32x32@2x)
      <= 128 => "ic07",  // 128x128
      <= 256 => "ic08",  // 256x256
      <= 512 => "ic09",  // 512x512
      _ => "ic10",       // 1024x1024 (512x512@2x)
    };
  }
}
