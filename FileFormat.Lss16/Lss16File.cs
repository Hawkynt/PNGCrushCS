using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Lss16;

/// <summary>In-memory representation of a Syslinux LSS16 splash screen image.</summary>
[FormatMagicBytes([0x3D, 0xF3, 0x13, 0x14])]
public sealed class Lss16File : IImageFileFormat<Lss16File> {

  /// <summary>Magic bytes identifying an LSS16 file: 0x3D 0xF3 0x13 0x14.</summary>
  internal static readonly byte[] Magic = [0x3D, 0xF3, 0x13, 0x14];

  /// <summary>Header size: 4 bytes magic + 2 bytes width + 2 bytes height + 48 bytes palette = 56 bytes.</summary>
  internal const int HeaderSize = 56;

  /// <summary>Number of palette entries (always 16 for 4-bit indexed).</summary>
  internal const int PaletteEntryCount = 16;

  /// <summary>Bytes per palette entry (R, G, B).</summary>
  internal const int BytesPerPaletteEntry = 3;

  /// <summary>Total palette size in bytes.</summary>
  internal const int PaletteSize = PaletteEntryCount * BytesPerPaletteEntry;

  static string IImageFileFormat<Lss16File>.PrimaryExtension => ".lss";
  static string[] IImageFileFormat<Lss16File>.FileExtensions => [".lss", ".16"];
  static FormatCapability IImageFileFormat<Lss16File>.Capabilities => FormatCapability.IndexedOnly;
  static Lss16File IImageFileFormat<Lss16File>.FromFile(FileInfo file) => Lss16Reader.FromFile(file);
  static Lss16File IImageFileFormat<Lss16File>.FromBytes(byte[] data) => Lss16Reader.FromBytes(data);
  static Lss16File IImageFileFormat<Lss16File>.FromStream(Stream stream) => Lss16Reader.FromStream(stream);
  static byte[] IImageFileFormat<Lss16File>.ToBytes(Lss16File file) => Lss16Writer.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>16-entry color table, 3 bytes per entry (R, G, B), 6-bit VGA values (0-63).</summary>
  public byte[] Palette { get; init; } = new byte[PaletteSize];

  /// <summary>Pixel data, one byte per pixel, values 0-15 (4-bit color index stored in a full byte).</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(Lss16File file) {
    ArgumentNullException.ThrowIfNull(file);

    var expandedPalette = new byte[PaletteSize];
    for (var i = 0; i < PaletteSize; ++i) {
      var val = file.Palette[i] * 4;
      expandedPalette[i] = (byte)(val > 255 ? 255 : val);
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = expandedPalette,
      PaletteCount = PaletteEntryCount,
    };
  }

  public static Lss16File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"Expected {PixelFormat.Indexed8} but got {image.Format}.", nameof(image));
    if (image.PaletteCount > PaletteEntryCount)
      throw new ArgumentException($"LSS16 supports at most {PaletteEntryCount} palette entries, got {image.PaletteCount}.", nameof(image));
    if (image.Palette == null)
      throw new ArgumentException("Palette is required for indexed image.", nameof(image));

    var palette = new byte[PaletteSize];
    var srcPaletteBytes = Math.Min(image.Palette.Length, image.PaletteCount * BytesPerPaletteEntry);
    for (var i = 0; i < srcPaletteBytes; ++i)
      palette[i] = (byte)(image.Palette[i] / 4);

    var pixelData = image.PixelData[..];
    for (var i = 0; i < pixelData.Length; ++i)
      if (pixelData[i] >= PaletteEntryCount)
        pixelData[i] = 0;

    return new() {
      Width = image.Width,
      Height = image.Height,
      Palette = palette,
      PixelData = pixelData,
    };
  }
}
