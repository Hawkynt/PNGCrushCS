using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.DaliST;

/// <summary>In-memory representation of an Atari ST Dali image (SD0/SD1/SD2).</summary>
public readonly record struct DaliSTFile : IImageFormatReader<DaliSTFile>, IImageToRawImage<DaliSTFile>, IImageFromRawImage<DaliSTFile>, IImageFormatWriter<DaliSTFile> {

  /// <summary>Bytes occupied by the required zero file identifier.</summary>
  public const int FileIdSize = 4;

  /// <summary>Offset of the 32-byte palette inside the header.</summary>
  public const int PaletteOffset = FileIdSize;

  /// <summary>Palette size in bytes (16 big-endian words).</summary>
  public const int PaletteSize = 16 * 2;

  /// <summary>Offset of the reserved header bytes.</summary>
  public const int ReservedOffset = PaletteOffset + PaletteSize;

  /// <summary>Reserved bytes between the palette and the raster.</summary>
  public const int ReservedSize = 92;

  /// <summary>Dali reserves a fixed 128-byte header; the bitmap starts immediately after it.</summary>
  public const int HeaderSize = 128;

  /// <summary>Bytes occupied by one Atari ST screen in every resolution.</summary>
  public const int PlanarDataSize = 32_000;

  /// <summary>Exact physical file size.</summary>
  public const int ExpectedFileSize = HeaderSize + PlanarDataSize;

  static string IImageFormatMetadata<DaliSTFile>.PrimaryExtension => ".sd0";
  static string[] IImageFormatMetadata<DaliSTFile>.FileExtensions => [".sd0", ".sd1", ".sd2"];
  static DaliSTFile IImageFormatReader<DaliSTFile>.FromSpan(ReadOnlySpan<byte> data) => DaliSTReader.FromSpan(data);
  static DaliSTFile IImageFormatReader<DaliSTFile>.FromFile(FileInfo file) => DaliSTReader.FromFile(file);
  static DaliSTFile IImageFromRawImage<DaliSTFile>.FromRawImage(RawImage image, string extension) => FromRawImage(image, extension);
  static VideoMode[] IImageFormatMetadata<DaliSTFile>.VideoModes => [
    new("Low resolution (320x200, 16 colours)", [(320, 200)], [new IntegerRange(2, 16)]),
    new("Medium resolution (640x200, 4 colours)", [(640, 200)], [new IntegerRange(2, 4)]),
    new("High resolution (640x400, monochrome)", [(640, 400)], [2])
  ];
  static byte[] IImageFormatWriter<DaliSTFile>.ToBytes(DaliSTFile file) => DaliSTWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Resolution mode selected by the file extension.</summary>
  public DaliSTResolution Resolution { get; init; }

  /// <summary>All 16 stored Atari ST palette words.</summary>
  public short[] Palette { get; init; }

  /// <summary>The 92 reserved bytes from the on-disk header. New files may leave this null to write zeroes.</summary>
  public byte[]? ReservedData { get; init; }

  /// <summary>Exactly 32,000 bytes of Atari ST interleaved planar screen memory.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(DaliSTFile file) {
    Validate(file, nameof(file));
    var (_, _, planes) = GetMode(file.Resolution);
    var chunky = PlanarConverter.AtariStToChunky(file.PixelData, file.Width, file.Height, planes);
    var paletteCount = 1 << planes;

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = chunky,
      Palette = AtariStGraphics.ScreenPalette(file.Palette.AsSpan(0, paletteCount), planes),
      PaletteCount = paletteCount,
    };
  }

  /// <summary>Creates the Dali variant implied by the image's exact ST screen dimensions.</summary>
  public static DaliSTFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    var resolution = (image.Width, image.Height) switch {
      (320, 200) => DaliSTResolution.Low,
      (640, 200) => DaliSTResolution.Medium,
      (640, 400) => DaliSTResolution.High,
      _ => throw new ArgumentException("Dali images must be exactly 320x200, 640x200, or 640x400 pixels.", nameof(image)),
    };

    return FromRawImage(image, resolution);
  }

  /// <summary>Creates the Dali variant selected by .SD0, .SD1, or .SD2.</summary>
  public static DaliSTFile FromRawImage(RawImage image, string extension)
    => FromRawImage(image, ResolutionFromExtension(extension));

  /// <summary>Creates a specific Dali screen without resizing or clipping the source.</summary>
  public static DaliSTFile FromRawImage(RawImage image, DaliSTResolution resolution) {
    ArgumentNullException.ThrowIfNull(image);
    var (width, height, planes) = GetMode(resolution);
    if (image.Width != width || image.Height != height)
      throw new ArgumentException($"{resolution} Dali images must be exactly {width}x{height} pixels.", nameof(image));
    if (!image.HasEnoughPixelData)
      throw new ArgumentException("The source image does not contain enough pixel data for its dimensions.", nameof(image));

    var palette = new short[16];
    byte[] indices;
    if (resolution == DaliSTResolution.High) {
      var rgb = image.EnsureAnyFormat(PixelFormat.Rgb24);
      indices = new byte[width * height];
      for (var i = 0; i < indices.Length; ++i) {
        var at = i * 3;
        var luma = (299 * rgb.PixelData[at] + 587 * rgb.PixelData[at + 1] + 114 * rgb.PixelData[at + 2] + 500) / 1000;
        indices[i] = luma < 128 ? (byte)1 : (byte)0;
      }
    } else {
      var indexed = image.EnsureIndexedAtMost(1 << planes);
      if (indexed.Palette is null || indexed.PaletteCount < 1 || indexed.Palette.Length < indexed.PaletteCount * 3)
        throw new ArgumentException("Colour Dali images require a valid palette.", nameof(image));
      if (indexed.PixelData.Length != width * height)
        throw new ArgumentException("Indexed Dali input must contain exactly one palette index per pixel.", nameof(image));

      foreach (var index in indexed.PixelData)
        if (index >= indexed.PaletteCount || index >= (1 << planes))
          throw new ArgumentException("A Dali pixel index exceeds the selected mode's palette.", nameof(image));

      indices = indexed.PixelData;
      var storedPalette = PlanarConverter.RgbToStPalette(indexed.Palette, indexed.PaletteCount);
      storedPalette.AsSpan(0, Math.Min(storedPalette.Length, palette.Length)).CopyTo(palette);
    }

    return new() {
      Width = width,
      Height = height,
      Resolution = resolution,
      Palette = palette,
      ReservedData = new byte[ReservedSize],
      PixelData = PlanarConverter.ChunkyToAtariSt(indices, width, height, planes),
    };
  }

  internal static DaliSTResolution ResolutionFromExtension(string extension) {
    ArgumentException.ThrowIfNullOrWhiteSpace(extension);
    return extension.ToLowerInvariant() switch {
      ".sd0" => DaliSTResolution.Low,
      ".sd1" => DaliSTResolution.Medium,
      ".sd2" => DaliSTResolution.High,
      _ => throw new ArgumentException("Dali file extension must be .sd0, .sd1, or .sd2.", nameof(extension)),
    };
  }

  internal static (int Width, int Height, int Planes) GetMode(DaliSTResolution resolution) => resolution switch {
    DaliSTResolution.Low => (320, 200, 4),
    DaliSTResolution.Medium => (640, 200, 2),
    DaliSTResolution.High => (640, 400, 1),
    _ => throw new ArgumentOutOfRangeException(nameof(resolution), resolution, "Unsupported Dali resolution."),
  };

  internal static void Validate(DaliSTFile file, string parameterName) {
    var (width, height, _) = GetMode(file.Resolution);
    if (file.Width != width || file.Height != height)
      throw new ArgumentException($"{file.Resolution} Dali geometry must be exactly {width}x{height}.", parameterName);
    if (file.Palette is null || file.Palette.Length != 16)
      throw new ArgumentException("Dali files must contain exactly 16 palette words.", parameterName);
    if (file.ReservedData is not null && file.ReservedData.Length != ReservedSize)
      throw new ArgumentException($"Dali reserved header data must contain exactly {ReservedSize} bytes when supplied.", parameterName);
    if (file.PixelData is null || file.PixelData.Length != PlanarDataSize)
      throw new ArgumentException($"Dali screen memory must contain exactly {PlanarDataSize} bytes.", parameterName);
  }
}
