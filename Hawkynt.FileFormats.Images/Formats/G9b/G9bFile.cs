using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.G9b;

/// <summary>In-memory representation of a V9990 GFX9000 (.g9b) image.</summary>
/// <remarks>
/// Sixteen bytes of header — magic, a version byte, the depth, a mode byte, how many palette
/// entries follow, the size, and whether the bitmap is packed — then the palette at three bytes an
/// entry, then the bitmap. The depth decides everything else: two, four and eight bits a pixel
/// index the palette, and sixteen carry the colour outright.
/// <para/>
/// What was here before was an eleven-byte header of this project's own design, with the depth byte
/// holding a "screen mode" number that meant nothing, and a sixteen-bit pixel laid out red first.
/// The V9990 puts green first, so even the one depth that lined up came out with two channels
/// swapped.
/// </remarks>
[FormatMagicBytes([0x47, 0x39, 0x42])]
public readonly record struct G9bFile : IImageFormatReader<G9bFile>, IImageToRawImage<G9bFile>, IImageFromRawImage<G9bFile>, IImageFormatWriter<G9bFile> {

  /// <summary>The fixed part of the header, before the palette.</summary>
  internal const int FixedHeaderSize = 16;

  /// <summary>The byte at offset three, which every file has.</summary>
  internal const byte Version = 11;

  static string IImageFormatMetadata<G9bFile>.PrimaryExtension => ".g9b";
  static string[] IImageFormatMetadata<G9bFile>.FileExtensions => [".g9b"];
  static G9bFile IImageFormatReader<G9bFile>.FromSpan(ReadOnlySpan<byte> data) => G9bReader.FromSpan(data);
  static byte[] IImageFormatWriter<G9bFile>.ToBytes(G9bFile file) => G9bWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Bits a pixel: two, four, eight or sixteen.</summary>
  public int Depth { get; init; }

  /// <summary>
  /// What an eight-bit pixel means when no palette follows: 64 the fixed Screen 8 colours, 128 and
  /// 192 the V9958's luma-chroma encodings.
  /// </summary>
  public byte ColorMode { get; init; }

  /// <summary>The palette as stored, three bytes an entry at five bits each.</summary>
  public byte[] Palette { get; init; }

  /// <summary>The bitmap, at the file's depth.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Where the bitmap starts, the palette lying between it and the fixed header.</summary>
  internal int BitmapOffset => FixedHeaderSize + (this.Palette?.Length ?? 0);

  /// <summary>Bytes one row of the bitmap takes.</summary>
  internal int Stride => (this.Width * this.Depth + 7) >> 3;

  /// <summary>Widens the stored five-bit palette to eight bits a channel.</summary>
  private byte[] _PaletteRgb() {
    var entries = this.Palette.Length / 3;
    var rgb = new byte[entries * 3];
    for (var i = 0; i < rgb.Length; ++i) {
      var value = this.Palette[i] & 31;
      rgb[i] = (byte)((value << 3) | (value >> 2));
    }

    return rgb;
  }

  public static RawImage ToRawImage(G9bFile file) => file.Depth switch {
    16 => _Direct(file),
    8 when file.Palette.Length == 0 && file.ColorMode == 64 => _Indexed(file, MsxGraphics.Screen8Palette(), 256),
    8 when file.Palette.Length == 0 => throw new NotSupportedException(
      $"A G9B holding luma-chroma pixels (colour mode {file.ColorMode}) is not read here yet."),
    2 or 4 or 8 => _Indexed(file, file._PaletteRgb(), file.Palette.Length / 3),
    _ => throw new InvalidDataException($"A G9B pixel is 2, 4, 8 or 16 bits, not {file.Depth}."),
  };

  /// <summary>Sixteen bits a pixel, little-endian, five each with green in the top bits.</summary>
  private static RawImage _Direct(G9bFile file) {
    var count = file.Width * file.Height;
    var rgb = new byte[count * 3];

    for (var i = 0; i < count; ++i) {
      var word = file.PixelData[i * 2] | (file.PixelData[i * 2 + 1] << 8);
      var g = (word >> 10) & 31;
      var r = (word >> 5) & 31;
      var b = word & 31;

      rgb[i * 3] = (byte)((r << 3) | (r >> 2));
      rgb[i * 3 + 1] = (byte)((g << 3) | (g >> 2));
      rgb[i * 3 + 2] = (byte)((b << 3) | (b >> 2));
    }

    return new() { Width = file.Width, Height = file.Height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  private static RawImage _Indexed(G9bFile file, byte[] palette, int count) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed8,
    PixelData = file.Depth == 8
      ? PackedRows.Compact(file.PixelData, file.Width, file.Height, 1, file.Stride)
      : PackedRows.Unpack(file.PixelData, file.Width, file.Height, file.Depth, file.Stride),
    Palette = palette,
    PaletteCount = count,
  };

  /// <summary>Writes sixteen bits a pixel, which needs no palette and holds any picture.</summary>
  public static G9bFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Rgb24);

    var count = image.Width * image.Height;
    var pixels = new byte[count * 2];

    for (var i = 0; i < count; ++i) {
      var r = image.PixelData[i * 3] >> 3;
      var g = image.PixelData[i * 3 + 1] >> 3;
      var b = image.PixelData[i * 3 + 2] >> 3;
      var word = (g << 10) | (r << 5) | b;

      pixels[i * 2] = (byte)word;
      pixels[i * 2 + 1] = (byte)(word >> 8);
    }

    return new() {
      Width = image.Width,
      Height = image.Height,
      Depth = 16,
      Palette = [],
      PixelData = pixels,
    };
  }
}
