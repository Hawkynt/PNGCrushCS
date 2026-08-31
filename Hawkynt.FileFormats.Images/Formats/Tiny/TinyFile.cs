using System;
using FileFormat.Core;

namespace FileFormat.Tiny;

/// <summary>In-memory representation of a Tiny Stuff compressed Atari ST screen image.</summary>
public readonly record struct TinyFile : IImageFormatReader<TinyFile>, IImageToRawImage<TinyFile>, IImageFromRawImage<TinyFile>, IImageFormatWriter<TinyFile> {

  /// <summary>Bytes of Atari ST screen memory represented by every Tiny Stuff picture.</summary>
  public const int ScreenDataSize = 32_000;

  /// <summary>Number of 16-bit words in the expanded Tiny Stuff data stream.</summary>
  public const int ScreenWordCount = ScreenDataSize / 2;

  /// <summary>Smallest control block emitted by the original format.</summary>
  public const int MinimumControlBytes = 3;

  /// <summary>Largest control block described by the original TNYSTUFF format.</summary>
  public const int MaximumControlBytes = 10_667;

  static string IImageFormatMetadata<TinyFile>.PrimaryExtension => ".tny";
  static string[] IImageFormatMetadata<TinyFile>.FileExtensions => [".tny", ".tn1", ".tn2", ".tn3", ".tn4", ".tn5", ".tn6"];
  static TinyFile IImageFormatReader<TinyFile>.FromSpan(ReadOnlySpan<byte> data) => TinyReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<TinyFile>.VideoModes => [
    new("Low resolution", [(320, 200)], [new IntegerRange(2, 16)]),
    new("Medium resolution", [(640, 200)], [new IntegerRange(2, 4)]),
    new("High resolution", [(640, 400)], [2]),
  ];
  static byte[] IImageFormatWriter<TinyFile>.ToBytes(TinyFile file) => TinyWriter.ToBytes(file);

  /// <summary>Decoded pixel width implied by <see cref="Resolution"/>.</summary>
  public int Width { get; init; }

  /// <summary>Decoded pixel height implied by <see cref="Resolution"/>.</summary>
  public int Height { get; init; }

  /// <summary>Atari ST screen resolution used to interpret the expanded screen memory.</summary>
  public TinyResolution Resolution { get; init; }

  /// <summary>Whether the header carries the four-byte colour-rotation extension.</summary>
  public bool HasColorAnimation { get; init; }

  /// <summary>Colour-rotation limits; high nibble is the left/start register and low nibble the right/end register.</summary>
  public byte AnimationLimits { get; init; }

  /// <summary>Signed rotation direction and delay in 1/60-second units.</summary>
  public sbyte AnimationSpeedDirection { get; init; }

  /// <summary>Number of colour-rotation iterations.</summary>
  public ushort AnimationDuration { get; init; }

  /// <summary>Sixteen raw Atari ST hardware palette words stored by the file.</summary>
  public short[] Palette { get; init; }

  /// <summary>Exactly 32,000 bytes of Atari ST word-interleaved screen memory.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts Tiny Stuff screen memory to a platform-independent indexed image.</summary>
  public static RawImage ToRawImage(TinyFile file) {
    Validate(file, nameof(file));
    var mode = GetMode(file.Resolution);
    var chunky = PlanarConverter.AtariStToChunky(file.PixelData, mode.Width, mode.Height, mode.Planes);
    var paletteCount = 1 << mode.Planes;
    var rgb = AtariStGraphics.ScreenPalette(file.Palette.AsSpan(0, paletteCount), mode.Planes);

    return new RawImage {
      Width = mode.Width,
      Height = mode.Height,
      Format = PixelFormat.Indexed8,
      PixelData = chunky,
      Palette = rgb,
      PaletteCount = paletteCount,
    };
  }

  /// <summary>Creates a non-animated Tiny Stuff picture from an indexed Atari ST screen geometry.</summary>
  public static TinyFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Indexed8);

    var resolution = (image.Width, image.Height) switch {
      (320, 200) => TinyResolution.Low,
      (640, 200) => TinyResolution.Medium,
      (640, 400) => TinyResolution.High,
      _ => throw new ArgumentException("Tiny Stuff supports only 320x200, 640x200, and 640x400 Atari ST screen geometries.", nameof(image)),
    };

    var mode = GetMode(resolution);
    var maximumColors = 1 << mode.Planes;
    var expectedPixels = checked(mode.Width * mode.Height);
    if (image.PixelData.Length != expectedPixels)
      throw new ArgumentException($"Indexed source image must contain exactly {expectedPixels} pixels.", nameof(image));
    if (image.PaletteCount is < 1 || image.PaletteCount > maximumColors || image.Palette is null || image.Palette.Length < image.PaletteCount * 3)
      throw new ArgumentException($"Source palette must contain between 1 and {maximumColors} RGB colours.", nameof(image));

    foreach (var index in image.PixelData)
      if (index >= image.PaletteCount)
        throw new ArgumentException($"Source pixel index {index} refers beyond the {image.PaletteCount}-entry palette.", nameof(image));

    var palette = new short[16];
    var converted = PlanarConverter.RgbToStPalette(image.Palette, image.PaletteCount);
    converted.AsSpan(0, Math.Min(converted.Length, palette.Length)).CopyTo(palette);

    return new TinyFile {
      Width = mode.Width,
      Height = mode.Height,
      Resolution = resolution,
      Palette = palette,
      PixelData = PlanarConverter.ChunkyToAtariSt(image.PixelData, mode.Width, mode.Height, mode.Planes),
    };
  }

  internal static (int Width, int Height, int Planes) GetMode(TinyResolution resolution) => resolution switch {
    TinyResolution.Low => (320, 200, 4),
    TinyResolution.Medium => (640, 200, 2),
    TinyResolution.High => (640, 400, 1),
    _ => throw new ArgumentOutOfRangeException(nameof(resolution), resolution, "Unsupported Tiny Stuff resolution."),
  };

  internal static void Validate(TinyFile file, string parameterName) {
    (int Width, int Height, int Planes) mode;
    try {
      mode = GetMode(file.Resolution);
    } catch (ArgumentOutOfRangeException exception) {
      throw new ArgumentException(exception.Message, parameterName, exception);
    }

    if (file.Width != mode.Width || file.Height != mode.Height)
      throw new ArgumentException($"Tiny Stuff {file.Resolution} images must be exactly {mode.Width}x{mode.Height} pixels.", parameterName);
    if (file.Palette is null || file.Palette.Length != 16)
      throw new ArgumentException("Tiny Stuff palette must contain exactly 16 hardware words.", parameterName);
    if (file.PixelData is null || file.PixelData.Length != ScreenDataSize)
      throw new ArgumentException($"Tiny Stuff screen memory must contain exactly {ScreenDataSize} bytes.", parameterName);
    if (!file.HasColorAnimation && (file.AnimationLimits != 0 || file.AnimationSpeedDirection != 0 || file.AnimationDuration != 0))
      throw new ArgumentException("Tiny Stuff colour-rotation metadata requires HasColorAnimation to be enabled.", parameterName);
  }
}
