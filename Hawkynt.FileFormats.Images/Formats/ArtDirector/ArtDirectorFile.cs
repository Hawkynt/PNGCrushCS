using System;
using FileFormat.Core;

namespace FileFormat.ArtDirector;

/// <summary>In-memory representation of an Atari ST Art Director low-resolution picture.</summary>
public readonly record struct ArtDirectorFile() : IImageFormatReader<ArtDirectorFile>, IImageToRawImage<ArtDirectorFile>, IImageFromRawImage<ArtDirectorFile>, IImageFormatWriter<ArtDirectorFile> {

  /// <summary>Fixed image width in pixels.</summary>
  public const int FixedWidth = 320;

  /// <summary>Fixed image height in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>Bytes occupied by the four-plane Atari ST low-resolution screen.</summary>
  public const int PlanarDataSize = 32_000;

  /// <summary>Number of hardware colours in each stored palette.</summary>
  public const int ColorsPerPalette = 16;

  /// <summary>Number of palettes stored after the screen: one picture palette plus 15 animation palettes.</summary>
  public const int StoredPaletteCount = 16;

  /// <summary>Total number of 16-bit palette words stored by the format.</summary>
  public const int PaletteCycleWords = ColorsPerPalette * StoredPaletteCount;

  /// <summary>Bytes occupied by all sixteen stored palettes.</summary>
  public const int PaletteCycleSize = PaletteCycleWords * 2;

  /// <summary>Exact Art Director file size: 32,000 bytes of screen memory followed by 512 bytes of palettes.</summary>
  public const int ExpectedFileSize = PlanarDataSize + PaletteCycleSize;

  /// <summary>
  /// Palette slot used for ordinary display by the repository's real-file oracle set.
  /// </summary>
  /// <remarks>
  /// Historical format descriptions call the first palette the picture palette. The repository's
  /// existing corpus contains files whose first two slots differ, and RECOIL renders those samples
  /// with slot one. Keeping that established behavior avoids regressing known files while still
  /// preserving every stored palette losslessly.
  /// </remarks>
  public const int DisplayedPaletteIndex = 1;

  static string IImageFormatMetadata<ArtDirectorFile>.PrimaryExtension => ".art";
  static string[] IImageFormatMetadata<ArtDirectorFile>.FileExtensions => [".art"];
  static ArtDirectorFile IImageFormatReader<ArtDirectorFile>.FromSpan(ReadOnlySpan<byte> data) => ArtDirectorReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<ArtDirectorFile>.VideoModes => [new("Atari ST low resolution", [(FixedWidth, FixedHeight)], [new IntegerRange(2, 16)])];
  static byte[] IImageFormatWriter<ArtDirectorFile>.ToBytes(ArtDirectorFile file) => ArtDirectorWriter.ToBytes(file);

  /// <summary>Image width, always 320 for a valid Art Director file.</summary>
  public int Width { get; init; } = FixedWidth;

  /// <summary>Image height, always 200 for a valid Art Director file.</summary>
  public int Height { get; init; } = FixedHeight;

  /// <summary>Legacy resolution property retained for source compatibility; valid Art Director files are always zero/low resolution.</summary>
  public short Resolution { get; init; }

  /// <summary>The 16-entry palette used to render the picture.</summary>
  public short[] Palette { get; init; }

  /// <summary>
  /// Optional flat array containing all sixteen stored palettes, 16 words per palette.
  /// </summary>
  /// <remarks>
  /// Readers always populate all 256 words. For source compatibility, writers accept <c>null</c> and
  /// then repeat <see cref="Palette"/> into every stored slot.
  /// </remarks>
  public short[]? PaletteCycle { get; init; }

  /// <summary>Exactly 32,000 bytes of Atari ST four-plane low-resolution screen memory.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts the picture to indexed RGB using the displayed palette.</summary>
  public static RawImage ToRawImage(ArtDirectorFile file) {
    ValidatePicture(file, nameof(file));
    var chunky = PlanarConverter.AtariStToChunky(file.PixelData, FixedWidth, FixedHeight, 4);
    var rgb = PlanarConverter.StPaletteToRgb(file.Palette);

    return new RawImage {
      Width = FixedWidth,
      Height = FixedHeight,
      Format = PixelFormat.Indexed8,
      PixelData = chunky,
      Palette = rgb,
      PaletteCount = ColorsPerPalette,
    };
  }

  /// <summary>Encodes an image as a 320x200 Art Director picture and repeats its palette through the animation slots.</summary>
  public static ArtDirectorFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var indexed = image.SampleTo(FixedWidth, FixedHeight).EnsureFormat(PixelFormat.Indexed8);
    var quantised = ColorQuantizer.Quantize(
      PixelConverter.Convert(indexed, PixelFormat.Bgra32).PixelData,
      FixedWidth * FixedHeight,
      ColorsPerPalette
    );

    var chunky = new byte[FixedWidth * FixedHeight];
    for (var i = 0; i < chunky.Length; ++i)
      chunky[i] = (byte)quantised.Indices[i];

    var palette = new short[ColorsPerPalette];
    PlanarConverter.RgbToStPalette(quantised.Palette, quantised.Count)
      .AsSpan(0, Math.Min(quantised.Count, ColorsPerPalette))
      .CopyTo(palette);

    var cycle = new short[PaletteCycleWords];
    for (var slot = 0; slot < StoredPaletteCount; ++slot)
      palette.CopyTo(cycle, slot * ColorsPerPalette);

    return new ArtDirectorFile {
      Width = FixedWidth,
      Height = FixedHeight,
      Resolution = 0,
      Palette = palette,
      PaletteCycle = cycle,
      PixelData = PlanarConverter.ChunkyToAtariSt(chunky, FixedWidth, FixedHeight, 4),
    };
  }

  internal static void ValidatePicture(ArtDirectorFile file, string parameterName) {
    if (file.Width != FixedWidth || file.Height != FixedHeight || file.Resolution != 0)
      throw new ArgumentException($"Art Director images are always {FixedWidth}x{FixedHeight} Atari ST low resolution.", parameterName);
    if (file.Palette is null || file.Palette.Length != ColorsPerPalette)
      throw new ArgumentException($"Art Director displayed palette must contain exactly {ColorsPerPalette} words.", parameterName);
    if (file.PixelData is null || file.PixelData.Length != PlanarDataSize)
      throw new ArgumentException($"Art Director screen memory must contain exactly {PlanarDataSize} bytes.", parameterName);
  }

  internal static void ValidateForWrite(ArtDirectorFile file, string parameterName) {
    ValidatePicture(file, parameterName);
    if (file.PaletteCycle is not null && file.PaletteCycle.Length != PaletteCycleWords)
      throw new ArgumentException($"Art Director palette cycle must contain exactly {PaletteCycleWords} words when supplied.", parameterName);
  }
}
