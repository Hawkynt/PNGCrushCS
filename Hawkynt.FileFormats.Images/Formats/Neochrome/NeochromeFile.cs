using System;
using FileFormat.Core;

namespace FileFormat.Neochrome;

/// <summary>In-memory representation of an Atari ST NEOchrome image.</summary>
public readonly record struct NeochromeFile : IImageFormatReader<NeochromeFile>, IImageToRawImage<NeochromeFile>, IImageFromRawImage<NeochromeFile>, IImageFormatWriter<NeochromeFile> {

  internal const ushort VirtualCanvasFlag = 0xBABE;

  static string IImageFormatMetadata<NeochromeFile>.PrimaryExtension => ".neo";
  static string[] IImageFormatMetadata<NeochromeFile>.FileExtensions => [".neo"];
  static NeochromeFile IImageFormatReader<NeochromeFile>.FromSpan(ReadOnlySpan<byte> data) => NeochromeReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<NeochromeFile>.VideoModes => [
    new("Low resolution", [(320, 200)], [new IntegerRange(2, 16)]),
    new("Medium resolution", [(640, 200)], [new IntegerRange(2, 4)]),
    new("High resolution", [(640, 400)], [2]),
    new("Virtual canvas", [(640, 400)], [new IntegerRange(2, 16)]),
  ];
  static byte[] IImageFormatWriter<NeochromeFile>.ToBytes(NeochromeFile file) => NeochromeWriter.ToBytes(file);

  /// <summary>Decoded image width.</summary>
  public int Width { get; init; }

  /// <summary>Decoded image height.</summary>
  public int Height { get; init; }

  /// <summary>Header flag word: normally zero, or <c>0xBABE</c> for the 640x400 virtual canvas.</summary>
  public short Flag { get; init; }

  /// <summary>Stored Atari ST resolution number: 0 low, 1 medium, 2 high.</summary>
  public short Resolution { get; init; }

  /// <summary>16-entry palette of raw Atari ST hardware colour words.</summary>
  public short[] Palette { get; init; }

  /// <summary>Twelve-byte legacy filename field.</summary>
  public byte[] FileName { get; init; }

  /// <summary>Raw colour-animation limits word.</summary>
  public short AnimationLimits { get; init; }

  /// <summary>High byte of the raw colour-animation speed/direction word.</summary>
  public byte AnimSpeed { get; init; }

  /// <summary>Low byte of the raw colour-animation speed/direction word.</summary>
  public byte AnimDirection { get; init; }

  /// <summary>Number of slideshow/animation steps.</summary>
  public short AnimSteps { get; init; }

  /// <summary>Stored image X offset; specified files use zero.</summary>
  public short AnimXOffset { get; init; }

  /// <summary>Stored image Y offset; specified files use zero.</summary>
  public short AnimYOffset { get; init; }

  /// <summary>Stored header width: 320 for ordinary files, 640 for the virtual canvas.</summary>
  public short AnimWidth { get; init; }

  /// <summary>Stored header height: 200 for ordinary files, 400 for the virtual canvas.</summary>
  public short AnimHeight { get; init; }

  /// <summary>Thirty-three reserved expansion words from the fixed header.</summary>
  public short[] Reserved { get; init; }

  /// <summary>Atari ST word-interleaved planar screen memory.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(NeochromeFile file) {
    var mode = Validate(file, nameof(file));
    var chunky = PlanarConverter.AtariStToChunky(file.PixelData, mode.Width, mode.Height, mode.Planes);
    var paletteCount = mode.Planes == 1 ? 2 : 1 << mode.Planes;
    var rgb = AtariStGraphics.ScreenPalette(file.Palette.AsSpan(0, paletteCount), mode.Planes);

    return new() {
      Width = mode.Width,
      Height = mode.Height,
      Format = PixelFormat.Indexed8,
      PixelData = chunky,
      Palette = rgb,
      PaletteCount = paletteCount,
    };
  }

  public static NeochromeFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Indexed8);

    var (flag, resolution, planes) = (image.Width, image.Height, image.PaletteCount) switch {
      (320, 200, <= 16) => ((short)0, (short)0, 4),
      (640, 200, <= 4) => ((short)0, (short)1, 2),
      (640, 400, <= 2) => ((short)0, (short)2, 1),
      (640, 400, <= 16) => (unchecked((short)VirtualCanvasFlag), (short)0, 4),
      _ => throw new ArgumentException("NEOchrome supports 320x200/16-colour, 640x200/4-colour, 640x400/2-colour, or 640x400/16-colour virtual-canvas images.", nameof(image)),
    };

    var expectedPixels = checked(image.Width * image.Height);
    if (image.PixelData is null || image.PixelData.Length != expectedPixels)
      throw new ArgumentException($"Indexed source must contain exactly {expectedPixels} pixels.", nameof(image));

    var maxColors = 1 << planes;
    if (image.PaletteCount <= 0 || image.PaletteCount > maxColors || image.Palette is null || image.Palette.Length < image.PaletteCount * 3)
      throw new ArgumentException($"Source palette must contain between 1 and {maxColors} RGB colours.", nameof(image));

    var palette = new short[16];
    if (planes > 1) {
      var converted = PlanarConverter.RgbToStPalette(image.Palette, image.PaletteCount);
      converted.CopyTo(palette, 0);
    }

    var virtualCanvas = unchecked((ushort)flag) == VirtualCanvasFlag;
    return new() {
      Width = image.Width,
      Height = image.Height,
      Flag = flag,
      Resolution = resolution,
      Palette = palette,
      FileName = "        .   "u8.ToArray(),
      AnimWidth = virtualCanvas ? (short)640 : (short)320,
      AnimHeight = virtualCanvas ? (short)400 : (short)200,
      Reserved = new short[33],
      PixelData = PlanarConverter.ChunkyToAtariSt(image.PixelData, image.Width, image.Height, planes),
    };
  }

  internal static (int Width, int Height, int Planes) Validate(NeochromeFile file, string parameterName) {
    var mode = GetMode(file.Flag, file.Resolution);

    if (file.Width != 0 && file.Width != mode.Width)
      throw new ArgumentException($"NEOchrome decoded width must be {mode.Width} for this variant.", parameterName);
    if (file.Height != 0 && file.Height != mode.Height)
      throw new ArgumentException($"NEOchrome decoded height must be {mode.Height} for this variant.", parameterName);
    if (file.Palette is null || file.Palette.Length != 16)
      throw new ArgumentException("NEOchrome palette must contain exactly 16 words.", parameterName);
    if (file.FileName is not null && file.FileName.Length != 12)
      throw new ArgumentException("NEOchrome filename field must contain exactly 12 bytes.", parameterName);
    if (file.Reserved is not null && file.Reserved.Length != 33)
      throw new ArgumentException("NEOchrome reserved field must contain exactly 33 words.", parameterName);
    if (file.AnimXOffset != 0 || file.AnimYOffset != 0)
      throw new ArgumentException("NEOchrome image offsets must be zero.", parameterName);

    var virtualCanvas = unchecked((ushort)file.Flag) == VirtualCanvasFlag;
    var storedWidth = virtualCanvas ? 640 : 320;
    var storedHeight = virtualCanvas ? 400 : 200;
    if (file.AnimWidth != 0 && file.AnimWidth != storedWidth)
      throw new ArgumentException($"NEOchrome stored width must be {storedWidth} for this variant.", parameterName);
    if (file.AnimHeight != 0 && file.AnimHeight != storedHeight)
      throw new ArgumentException($"NEOchrome stored height must be {storedHeight} for this variant.", parameterName);

    var expectedBytes = checked(AtariStGraphics.BytesPerRow(mode.Width, mode.Planes) * mode.Height);
    if (file.PixelData is null || file.PixelData.Length != expectedBytes)
      throw new ArgumentException($"NEOchrome planar raster must contain exactly {expectedBytes} bytes.", parameterName);

    return mode;
  }

  internal static (int Width, int Height, int Planes) GetMode(short flag, short resolution) {
    if (unchecked((ushort)flag) == VirtualCanvasFlag) {
      if (resolution != 0)
        throw new ArgumentException("NEOchrome virtual-canvas files use low-resolution/four-plane memory (resolution 0).");
      return (640, 400, 4);
    }

    if (flag != 0)
      throw new ArgumentException($"Unsupported NEOchrome flag word 0x{unchecked((ushort)flag):X4}.");

    return resolution switch {
      0 => (320, 200, 4),
      1 => (640, 200, 2),
      2 => (640, 400, 1),
      _ => throw new ArgumentException($"Unsupported NEOchrome resolution value {resolution}."),
    };
  }
}
