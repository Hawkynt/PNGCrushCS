using System;
using FileFormat.Core;

namespace FileFormat.IffMultiPalette;

/// <summary>An IFF ILBM image whose sixteen colour registers may change while the beam moves down the picture.</summary>
/// <remarks>
/// Amiga multi-palette pictures are ordinary <c>FORM ILBM</c> files carrying the documented
/// <c>PCHG</c> property chunk.  They are not a separate <c>FORM MPAL</c> type.  The small PCHG form
/// used here stores twelve-bit RGB register values and, following the format's Copper-safe writing
/// policy, changes at most seven registers between adjacent scanlines.
/// </remarks>
public readonly record struct IffMultiPaletteFile :
  IImageFormatReader<IffMultiPaletteFile>, IImageToRawImage<IffMultiPaletteFile>,
  IImageFromRawImage<IffMultiPaletteFile>, IImageFormatWriter<IffMultiPaletteFile> {

  /// <summary>Colour registers carried by the OCS/ECS palette form this writer emits.</summary>
  internal const int PaletteEntries = 16;

  /// <summary>RGB bytes in one complete scanline palette.</summary>
  internal const int PaletteByteSize = PaletteEntries * 3;

  /// <summary>Maximum Copper-safe palette changes per non-interlaced scanline stated by PCHG 0.6.</summary>
  internal const int MaxChangesPerLine = 7;

  static string IImageFormatMetadata<IffMultiPaletteFile>.PrimaryExtension => ".mpl";
  static string[] IImageFormatMetadata<IffMultiPaletteFile>.FileExtensions => [".mpl", ".mpal"];
  static IffMultiPaletteFile IImageFormatReader<IffMultiPaletteFile>.FromSpan(ReadOnlySpan<byte> data) => IffMultiPaletteReader.FromSpan(data);
  static byte[] IImageFormatWriter<IffMultiPaletteFile>.ToBytes(IffMultiPaletteFile file) => IffMultiPaletteWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>One four-bit colour-register index per pixel.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>
  /// One sixteen-entry RGB palette per scanline, <see cref="Height"/> × <see cref="PaletteByteSize"/>
  /// bytes.  Every channel is one of the sixteen values representable by the Amiga's four-bit DAC.
  /// </summary>
  public byte[] ScanlinePalettes { get; init; }

  /// <summary>Paints every scanline through the palette that is active on that line.</summary>
  public static RawImage ToRawImage(IffMultiPaletteFile file) {
    Validate(file, nameof(file));

    var rgb = new byte[checked(file.Width * file.Height * 3)];
    for (var y = 0; y < file.Height; ++y) {
      var paletteAt = y * PaletteByteSize;
      for (var x = 0; x < file.Width; ++x) {
        var pixel = y * file.Width + x;
        var from = paletteAt + file.PixelData[pixel] * 3;
        var to = pixel * 3;
        rgb[to] = file.ScanlinePalettes[from];
        rgb[to + 1] = file.ScanlinePalettes[from + 1];
        rgb[to + 2] = file.ScanlinePalettes[from + 2];
      }
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>
  /// Fits a raster to a Copper-safe sixteen-register PCHG picture.
  /// </summary>
  /// <remarks>
  /// Colours are first snapped to the twelve-bit palette the small PCHG records can state.  The
  /// first line may choose all sixteen registers; each following line may replace at most seven,
  /// exactly the limit the PCHG specification gives for a non-interlaced Copper list.  Pictures that
  /// already fit those constraints round-trip exactly after the unavoidable twelve-bit colour snap;
  /// denser pictures are mapped to the nearest palette the permitted register changes can provide.
  /// </remarks>
  public static IffMultiPaletteFile FromRawImage(RawImage image)
    => IffMultiPaletteEncoder.Encode(image);

  internal static void Validate(IffMultiPaletteFile file, string parameterName) {
    if (file.Width is < 1 or > ushort.MaxValue)
      throw new ArgumentException($"IFF multi-palette width must be between 1 and {ushort.MaxValue} pixels.", parameterName);
    if (file.Height is < 1 or > ushort.MaxValue)
      throw new ArgumentException($"IFF multi-palette height must be between 1 and {ushort.MaxValue} pixels.", parameterName);

    int pixelCount;
    int paletteBytes;
    try {
      pixelCount = checked(file.Width * file.Height);
      paletteBytes = checked(file.Height * PaletteByteSize);
    } catch (OverflowException exception) {
      throw new ArgumentException("IFF multi-palette dimensions are too large for an in-memory picture.", parameterName, exception);
    }

    if (file.PixelData is null || file.PixelData.Length != pixelCount)
      throw new ArgumentException($"Pixel data must contain exactly {pixelCount} register indices.", parameterName);
    if (file.ScanlinePalettes is null || file.ScanlinePalettes.Length != paletteBytes)
      throw new ArgumentException($"Scanline palettes must contain exactly {paletteBytes} RGB bytes.", parameterName);

    foreach (var index in file.PixelData)
      if (index >= PaletteEntries)
        throw new ArgumentException($"Pixel register index {index} is outside the sixteen-register PCHG palette.", parameterName);

    foreach (var channel in file.ScanlinePalettes)
      if (channel % 17 != 0)
        throw new ArgumentException(
          $"Palette channel value {channel} is not representable by the four-bit RGB form this PCHG writer emits.", parameterName);
  }
}
