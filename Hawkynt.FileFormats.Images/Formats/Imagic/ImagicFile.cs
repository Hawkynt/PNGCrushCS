using System;
using FileFormat.Core;

namespace FileFormat.Imagic;

/// <summary>In-memory representation of an Atari ST Imagic (.ic1, .ic2, .ic3) screen.</summary>
/// <remarks>
/// A 67-byte header — the "IMDC" tag, the screen resolution, the ST palette and the escape byte
/// the compressor settled on — followed by the run-length stream. Unlike DEGAS, the extension does
/// not decide the resolution: it is a header field, so a mislabelled file still decodes correctly.
/// </remarks>
public readonly record struct ImagicFile
  : IImageFormatReader<ImagicFile>, IImageToRawImage<ImagicFile>,
    IImageFromRawImage<ImagicFile>, IImageFormatWriter<ImagicFile> {

  /// <summary>The tag every file starts with.</summary>
  public static ReadOnlySpan<byte> Signature => "IMDC"u8;

  /// <summary>Offset of the resolution byte.</summary>
  public const int ModeOffset = 5;

  /// <summary>Offset of the ST palette.</summary>
  public const int PaletteOffset = 6;

  /// <summary>Palette entries stored, one 16-bit ST colour each.</summary>
  public const int PaletteCount = 16;

  /// <summary>Offset of the block between the palette and the trailer.</summary>
  public const int ReservedOffset = PaletteOffset + PaletteCount * 2;

  /// <summary>Size of that block.</summary>
  public const int ReservedSize = 26;

  /// <summary>Offset of the two stamp bytes the reader checks.</summary>
  public const int StampOffset = ReservedOffset + ReservedSize;

  /// <summary>The values those two bytes always hold.</summary>
  public static ReadOnlySpan<byte> Stamp => [200, 2];

  /// <summary>Offset of the escape byte.</summary>
  public const int EscapeOffset = StampOffset + 2;

  /// <summary>Offset of the compressed stream.</summary>
  public const int DataOffset = EscapeOffset + 1;

  static string IImageFormatMetadata<ImagicFile>.PrimaryExtension => ".ic1";
  static string[] IImageFormatMetadata<ImagicFile>.FileExtensions => [".ic1", ".ic2", ".ic3"];
  static ImagicFile IImageFormatReader<ImagicFile>.FromSpan(ReadOnlySpan<byte> data) => ImagicReader.FromSpan(data);
  static byte[] IImageFormatWriter<ImagicFile>.ToBytes(ImagicFile file) => ImagicWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<ImagicFile>.VideoModes => [
    new("Low resolution (320x200, 16 colours)", [(320, 200)], [new IntegerRange(2, 16)]),
    new("Medium resolution (640x200, 4 colours)", [(640, 200)], [new IntegerRange(2, 4)]),
    new("High resolution (640x400, monochrome)", [(640, 400)], [2]),
  ];

  /// <summary>Which ST resolution the screen is in.</summary>
  public ImagicResolution Resolution { get; init; }

  /// <summary>The ST palette, one packed 16-bit colour per entry.</summary>
  public short[] Palette { get; init; }

  /// <summary>The header bytes between the palette and the stamp, preserved as found.</summary>
  public byte[] Reserved { get; init; }

  /// <summary>The uncompressed 32000-byte ST screen.</summary>
  public byte[] ScreenData { get; init; }

  /// <summary>Bitplanes a resolution uses.</summary>
  public static int BitplanesFor(ImagicResolution resolution) => resolution switch {
    ImagicResolution.Low => 4,
    ImagicResolution.Medium => 2,
    ImagicResolution.High => 1,
    _ => throw new ArgumentOutOfRangeException(nameof(resolution), resolution, "Unknown Imagic resolution."),
  };

  /// <summary>Displayed size of a resolution.</summary>
  public static (int Width, int Height) SizeFor(ImagicResolution resolution) => resolution switch {
    ImagicResolution.Low => (320, 200),
    ImagicResolution.Medium => (640, 200),
    ImagicResolution.High => (640, 400),
    _ => throw new ArgumentOutOfRangeException(nameof(resolution), resolution, "Unknown Imagic resolution."),
  };

  public static RawImage ToRawImage(ImagicFile file) {
    var (width, height) = SizeFor(file.Resolution);
    var bitplanes = BitplanesFor(file.Resolution);

    var chunky = PlanarConverter.AtariStToChunky(file.ScreenData, width, height, bitplanes);

    // High resolution has no colours to choose: the ST's monochrome mode is ink on white paper and
    // ignores the palette the file still carries, so honouring it here would tint the whole page.
    var count = file.Resolution == ImagicResolution.High ? 2 : Math.Min(1 << bitplanes, file.Palette.Length);
    var palette = file.Resolution == ImagicResolution.High
      ? [255, 255, 255, 0, 0, 0]
      : PlanarConverter.StPaletteToRgb(file.Palette.AsSpan(0, count));

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = chunky,
      Palette = palette,
      PaletteCount = count,
    };
  }

  public static ImagicFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    var indexed = image.EnsureFormat(PixelFormat.Indexed8);

    var resolution = (indexed.Width, indexed.Height) switch {
      (640, 400) => ImagicResolution.High,
      (640, 200) => ImagicResolution.Medium,
      (320, 200) => ImagicResolution.Low,
      _ => throw new ArgumentException($"Imagic stores 320x200, 640x200 or 640x400, got {indexed.Width}x{indexed.Height}.", nameof(image)),
    };

    var (width, height) = SizeFor(resolution);
    var bitplanes = BitplanesFor(resolution);

    var count = Math.Min(indexed.PaletteCount, PaletteCount);
    var packed = PlanarConverter.RgbToStPalette(indexed.Palette ?? [], count);
    var palette = new short[PaletteCount];
    packed.AsSpan(0, Math.Min(packed.Length, PaletteCount)).CopyTo(palette);

    return new() {
      Resolution = resolution,
      Palette = palette,
      Reserved = new byte[ReservedSize],
      ScreenData = PlanarConverter.ChunkyToAtariSt(indexed.PixelData, width, height, bitplanes),
    };
  }
}
