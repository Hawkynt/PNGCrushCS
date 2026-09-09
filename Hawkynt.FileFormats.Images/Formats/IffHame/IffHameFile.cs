using System;
using FileFormat.Core;

namespace FileFormat.IffHame;

/// <summary>In-memory representation of an IFF HAM-E (HAM Enhanced, 18-bit color) image.</summary>
public readonly record struct IffHameFile : IImageFormatReader<IffHameFile>, IImageToRawImage<IffHameFile>, IImageFromRawImage<IffHameFile>, IImageFormatWriter<IffHameFile> {

  /// <summary>Normal HAM-E mode is 320 logical pixels wide; wider screens are horizontal overscan.</summary>
  internal const int MinimumWidth = 320;

  /// <summary>Original HAM-E hardware supports 384 logical pixels across in overscan.</summary>
  internal const int MaximumWidth = 384;

  /// <summary>PAL overscan is the tallest mode documented for original HAM-E hardware.</summary>
  internal const int MaximumHeight = 560;

  static string IImageFormatMetadata<IffHameFile>.PrimaryExtension => ".hame";
  static string[] IImageFormatMetadata<IffHameFile>.FileExtensions => [".hame"];
  static IffHameFile IImageFormatReader<IffHameFile>.FromSpan(ReadOnlySpan<byte> data) => IffHameReader.FromSpan(data);
  static byte[] IImageFormatWriter<IffHameFile>.ToBytes(IffHameFile file) => IffHameWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<IffHameFile>.VideoModes => [
    new("HAM-E 18-bit", [(new IntegerRange(MinimumWidth, MaximumWidth), new IntegerRange(1, MaximumHeight))])
  ];

  /// <summary>Displayed image width in HAM-E pixels.</summary>
  public int Width { get; init; }

  /// <summary>Displayed image height, excluding the palette/cookie scanlines.</summary>
  public int Height { get; init; }

  /// <summary>Whether the ILBM carrier uses the Amiga interlace display mode.</summary>
  public bool Interlaced { get; init; }

  /// <summary>HAM-E colour registers as packed RGB triplets, one to four complete banks of 64 entries.</summary>
  public byte[] Palette { get; init; }

  /// <summary>Number of valid RGB triplets in <see cref="Palette"/>; always a multiple of 64.</summary>
  public int PaletteCount { get; init; }

  /// <summary>One HAM-E command byte per displayed pixel.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>
  /// Original carrier bytes when this instance came from a file. Writers deliberately rebuild the
  /// carrier from the decoded HAM-E data instead of copying this buffer.
  /// </summary>
  public byte[]? RawData { get; init; }

  /// <summary>Decodes the HAM-E command stream to RGB24.</summary>
  public static RawImage ToRawImage(IffHameFile file) {
    Validate(file, nameof(file));
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = IffHameCodec.Decode(file.PixelData, file.Width, file.Height, file.Palette, file.PaletteCount),
    };
  }

  /// <summary>Encodes an image for original HAM-E hardware.</summary>
  /// <remarks>
  /// The source is quantized to at most sixty direct colours. HAM-E reserves direct codes 60..63
  /// for palette-bank switching, so using all 64 as colours would create a stream the hardware
  /// interprets differently. Component modifications then refine the held colour at six bits per
  /// channel. The resulting file is therefore lossy for arbitrary RGB input by design.
  /// </remarks>
  public static IffHameFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    if (image.Width is < MinimumWidth or > MaximumWidth)
      throw new ArgumentException($"IFF HAM-E width must be between {MinimumWidth} and {MaximumWidth} pixels.", nameof(image));
    if (image.Height is < 1 or > MaximumHeight)
      throw new ArgumentException($"IFF HAM-E height must be between 1 and {MaximumHeight} pixels.", nameof(image));

    var rgb = image.EnsureFormat(PixelFormat.Rgb24);
    var indexed = image.EnsureIndexedAtMost(IffHameCodec.SelectableEntriesPerBank);
    if (indexed.Palette is not { Length: > 0 } sourcePalette || indexed.PaletteCount < 1)
      throw new ArgumentException("IFF HAM-E encoding requires at least one palette colour.", nameof(image));

    var sourcePaletteCount = Math.Min(indexed.PaletteCount, IffHameCodec.SelectableEntriesPerBank);
    var palette = new byte[IffHameCodec.PaletteEntriesPerLine * 3];
    sourcePalette.AsSpan(0, sourcePaletteCount * 3).CopyTo(palette);

    return new() {
      Width = image.Width,
      Height = image.Height,
      Interlaced = image.Height > 283,
      Palette = palette,
      PaletteCount = IffHameCodec.PaletteEntriesPerLine,
      PixelData = IffHameCodec.Encode(rgb.PixelData, image.Width, image.Height, palette, sourcePaletteCount),
      RawData = null,
    };
  }

  internal static void Validate(IffHameFile file, string parameterName) {
    if (file.Width is < MinimumWidth or > MaximumWidth)
      throw new ArgumentException($"IFF HAM-E width must be between {MinimumWidth} and {MaximumWidth} pixels.", parameterName);
    if (file.Height is < 1 or > MaximumHeight)
      throw new ArgumentException($"IFF HAM-E height must be between 1 and {MaximumHeight} pixels.", parameterName);
    if (file.PaletteCount is < IffHameCodec.PaletteEntriesPerLine or > IffHameCodec.MaximumPaletteEntries
        || file.PaletteCount % IffHameCodec.PaletteEntriesPerLine != 0)
      throw new ArgumentException("IFF HAM-E palette data must contain one to four complete 64-register banks.", parameterName);
    if (file.Palette is null || file.Palette.Length < file.PaletteCount * 3)
      throw new ArgumentException("IFF HAM-E palette storage is shorter than PaletteCount.", parameterName);
    if (file.PixelData is null || file.PixelData.Length != file.Width * file.Height)
      throw new ArgumentException($"IFF HAM-E command data must contain exactly {file.Width * file.Height} bytes.", parameterName);

    var paletteLines = file.PaletteCount / IffHameCodec.PaletteEntriesPerLine;
    if (file.Height + paletteLines > ushort.MaxValue)
      throw new ArgumentException("IFF HAM-E carrier height exceeds the ILBM header range.", parameterName);
  }
}
