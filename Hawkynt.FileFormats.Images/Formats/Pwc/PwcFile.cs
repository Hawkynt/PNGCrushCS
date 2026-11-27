using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.Pwc;

/// <summary>In-memory representation of a PWC (piecewise-constant image model) picture.</summary>
/// <remarks>
/// Paul Ausbeck's lossless coder for palette pictures, distributed in 1999 as <c>pwcencode</c>/<c>pwcdecode</c>.
/// The container is a 12-byte header, an optional palette and a four-byte option word; the picture itself is
/// one arithmetic-coded stream. See <c>SPEC.md</c> beside this file for the bitstream.
/// </remarks>
[FormatMagicBytes([0x34, 0x79, 0x56, 0x61])]
[FormatMimeType("image/x-pwc")]
public readonly record struct PwcFile :
  IImageFormatReader<PwcFile>, IImageToRawImage<PwcFile>,
  IImageFromRawImage<PwcFile>, IImageFormatWriter<PwcFile>,
  IImageInfoReader<PwcFile> {

  /// <summary>The <c>4yVa</c> signature, read as a little-endian 32-bit word.</summary>
  internal const uint Magic = 0x61567934;

  /// <summary>The fixed header in front of the palette.</summary>
  internal const int HeaderSize = 12;

  static string IImageFormatMetadata<PwcFile>.PrimaryExtension => ".pwc";
  static string[] IImageFormatMetadata<PwcFile>.FileExtensions => [".pwc"];
  static PwcFile IImageFormatReader<PwcFile>.FromSpan(ReadOnlySpan<byte> data) => PwcReader.FromSpan(data);
  static byte[] IImageFormatWriter<PwcFile>.ToBytes(PwcFile file) => PwcWriter.ToBytes(file);

  public int Width { get; init; }

  public int Height { get; init; }

  /// <summary>The coded depth: 1, 4 or 8 bits per pixel, which selects the model.</summary>
  public int BitsPerPixel { get; init; }

  /// <summary>One palette index per pixel, top row first.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>The palette as RGB triplets; for a file without one, the linear palette its depth implies.</summary>
  public byte[] Palette { get; init; }

  /// <summary>Whether the file stores its palette (otherwise the decoder assumes a linear grey ramp).</summary>
  public bool StoresPalette { get; init; }

  /// <summary>The model options of the coding block.</summary>
  public PwcCodingFlags CodingFlags { get; init; }

  /// <summary>The adaptation limit: a context halves its counts once both sides exceed this many decisions.</summary>
  public int MaxLps { get; init; }

  /// <summary>Whether the rows are coded bottom row first (the encoder's <c>-flip</c>).</summary>
  public bool BottomUp { get; init; }

  /// <summary>The encoder's default adaptation limit.</summary>
  public const int DefaultMaxLps = 7;

  public static ImageInfo? ReadImageInfo(ReadOnlySpan<byte> header) {
    if (header.Length < HeaderSize || BinaryPrimitives.ReadUInt32LittleEndian(header) != Magic)
      return null;

    var height = header[4] | header[5] << 8;
    var width = header[6] | header[7] << 8;
    var depth = header[8];
    if (width == 0 || height == 0 || depth is not (1 or 4 or 8))
      return null;

    return new(width, height, depth, depth == 1 ? "Monochrome" : $"Indexed{depth}", "PWC");
  }

  public static RawImage ToRawImage(PwcFile file) {
    var colors = file.Palette.Length / 3;
    var highest = 0;
    foreach (var index in file.PixelData)
      highest = Math.Max(highest, index);

    // Indices past the stored palette are legal in the bitstream; they are shown black rather than dropped.
    var entries = Math.Max(colors, highest + 1);
    var palette = new byte[entries * 3];
    file.Palette.AsSpan(0, colors * 3).CopyTo(palette);

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = palette,
      PaletteCount = entries,
    };
  }

  /// <summary>Quantises to a palette if needed and picks the shallowest model that holds it.</summary>
  public static PwcFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    var indexed = image.Format == PixelFormat.Indexed8 ? image : PixelConverter.Convert(image, PixelFormat.Indexed8);
    var palette = indexed.Palette ?? throw new ArgumentException("PWC needs a palette.", nameof(image));
    var colors = Math.Clamp(indexed.PaletteCount > 0 ? indexed.PaletteCount : palette.Length / 3, 1, 256);
    var pixels = indexed.PixelData[..(image.Width * image.Height)];
    var highest = 0;
    foreach (var index in pixels)
      highest = Math.Max(highest, index);

    colors = Math.Max(colors, highest + 1);
    var stored = new byte[colors * 3];
    palette.AsSpan(0, Math.Min(palette.Length, stored.Length)).CopyTo(stored);

    return new() {
      Width = image.Width,
      Height = image.Height,
      BitsPerPixel = colors <= 2 ? 1 : colors <= 16 ? 4 : 8,
      PixelData = pixels,
      Palette = stored,
      StoresPalette = true,
      // The encoder's own default: the "is it listed at all?" decision only pays off on full 256-colour palettes.
      CodingFlags = colors == 256 ? PwcCodingFlags.None : PwcCodingFlags.NoListDecision,
      MaxLps = DefaultMaxLps,
    };
  }
}
