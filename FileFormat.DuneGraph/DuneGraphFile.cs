using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.DuneGraph;

/// <summary>In-memory representation of an Atari Falcon DuneGraph (.dg1/.dc1) indexed image.</summary>
public sealed class DuneGraphFile : IImageFileFormat<DuneGraphFile> {

  /// <summary>Fixed image width.</summary>
  public const int FixedWidth = 320;

  /// <summary>Fixed image height.</summary>
  public const int FixedHeight = 200;

  /// <summary>Number of palette entries.</summary>
  public const int PaletteEntryCount = 256;

  /// <summary>Bytes per palette entry in the Falcon format (RRRRRRrr GGGGGGgg 00000000 BBBBBBbb).</summary>
  public const int BytesPerPaletteEntry = 4;

  /// <summary>Size of the raw palette section in bytes.</summary>
  public const int PaletteDataSize = PaletteEntryCount * BytesPerPaletteEntry;

  /// <summary>Size of uncompressed pixel data.</summary>
  public const int PixelDataSize = FixedWidth * FixedHeight;

  /// <summary>Expected size of an uncompressed .dg1 file.</summary>
  public const int UncompressedFileSize = PaletteDataSize + PixelDataSize;

  /// <summary>The RLE escape byte used in .dc1 compressed files.</summary>
  internal const byte RleEscape = 0x00;

  static string IImageFileFormat<DuneGraphFile>.PrimaryExtension => ".dg1";
  static string[] IImageFileFormat<DuneGraphFile>.FileExtensions => [".dg1", ".dc1"];
  static FormatCapability IImageFileFormat<DuneGraphFile>.Capabilities => FormatCapability.IndexedOnly;
  static DuneGraphFile IImageFileFormat<DuneGraphFile>.FromFile(FileInfo file) => DuneGraphReader.FromFile(file);
  static DuneGraphFile IImageFileFormat<DuneGraphFile>.FromBytes(byte[] data) => DuneGraphReader.FromBytes(data);
  static DuneGraphFile IImageFileFormat<DuneGraphFile>.FromStream(Stream stream) => DuneGraphReader.FromStream(stream);
  static byte[] IImageFileFormat<DuneGraphFile>.ToBytes(DuneGraphFile file) => DuneGraphWriter.ToBytes(file);

  /// <summary>Always 320.</summary>
  public int Width => FixedWidth;

  /// <summary>Always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>Whether this file was loaded from or should be saved as compressed (.dc1) format.</summary>
  public bool IsCompressed { get; init; }

  /// <summary>RGB palette (3 bytes per entry, 768 bytes total).</summary>
  public byte[] Palette { get; init; } = [];

  /// <summary>Pixel data (1 byte per pixel, 64000 bytes total).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Converts the Falcon 4-byte palette entry to an RGB triplet.</summary>
  internal static void ConvertFalconPaletteToRgb(ReadOnlySpan<byte> falcon, Span<byte> rgb) {
    for (var i = 0; i < PaletteEntryCount; ++i) {
      var srcOff = i * BytesPerPaletteEntry;
      var dstOff = i * 3;
      rgb[dstOff] = falcon[srcOff];       // R
      rgb[dstOff + 1] = falcon[srcOff + 1]; // G
      rgb[dstOff + 2] = falcon[srcOff + 3]; // B
    }
  }

  /// <summary>Converts an RGB palette to the Falcon 4-byte palette format.</summary>
  internal static void ConvertRgbPaletteToFalcon(ReadOnlySpan<byte> rgb, Span<byte> falcon) {
    for (var i = 0; i < PaletteEntryCount; ++i) {
      var srcOff = i * 3;
      var dstOff = i * BytesPerPaletteEntry;
      falcon[dstOff] = rgb[srcOff];       // R
      falcon[dstOff + 1] = rgb[srcOff + 1]; // G
      falcon[dstOff + 2] = 0x00;            // padding
      falcon[dstOff + 3] = rgb[srcOff + 2]; // B
    }
  }

  public static RawImage ToRawImage(DuneGraphFile file) {
    ArgumentNullException.ThrowIfNull(file);

    return new() {
      Width = FixedWidth,
      Height = FixedHeight,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = file.Palette[..],
      PaletteCount = PaletteEntryCount,
    };
  }

  public static DuneGraphFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"Expected {PixelFormat.Indexed8} but got {image.Format}.", nameof(image));
    if (image.Width != FixedWidth || image.Height != FixedHeight)
      throw new ArgumentException($"Expected {FixedWidth}x{FixedHeight} but got {image.Width}x{image.Height}.", nameof(image));
    if (image.Palette == null || image.Palette.Length < 3)
      throw new ArgumentException("DuneGraph requires an RGB palette.", nameof(image));

    var palette = new byte[PaletteEntryCount * 3];
    image.Palette.AsSpan(0, Math.Min(image.Palette.Length, palette.Length)).CopyTo(palette);

    return new() {
      Palette = palette,
      PixelData = image.PixelData[..],
    };
  }
}
