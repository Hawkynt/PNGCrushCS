using System;
using FileFormat.Core;

namespace FileFormat.TurboRascal;

/// <summary>In-memory representation of a Turbo Rascal Syntax Error (.flf) image.</summary>
/// <remarks>
/// FLF is the cross-development container the Turbo Rascal toolchain writes for every machine it
/// targets — the same extension and signature carry Amiga, Amstrad, Atari ST, BBC Micro, C64,
/// VIC-20, PC and ZX Spectrum pictures, distinguished by a mode byte. We read and write mode 12,
/// the 320x200 chunky form: one palette index per pixel followed by an RGB palette, which is the
/// mode that can carry an arbitrary image without imposing a host machine's colour constraints.
/// </remarks>
public readonly record struct TurboRascalFile
  : IImageFormatReader<TurboRascalFile>, IImageToRawImage<TurboRascalFile>,
    IImageFromRawImage<TurboRascalFile>, IImageFormatWriter<TurboRascalFile> {

  /// <summary>ASCII signature every FLF file starts with.</summary>
  public static ReadOnlySpan<byte> Signature => "FLUFF64"u8;

  /// <summary>Offset of the mode byte.</summary>
  public const int ModeOffset = 11;

  /// <summary>Mode 12: 320x200, one palette index per pixel, RGB palette after the pixels.</summary>
  public const byte ChunkyMode = 12;

  /// <summary>Offset of the pixel data.</summary>
  public const int PixelDataOffset = 13;

  /// <summary>Image width.</summary>
  public const int ImageWidth = 320;

  /// <summary>Image height.</summary>
  public const int ImageHeight = 200;

  /// <summary>Size of the pixel data.</summary>
  public const int PixelDataSize = ImageWidth * ImageHeight;

  /// <summary>Offset of the colour-count byte, immediately after the pixels.</summary>
  public const int ColorCountOffset = PixelDataOffset + PixelDataSize;

  /// <summary>Offset of the RGB palette.</summary>
  public const int PaletteOffset = ColorCountOffset + 1;

  /// <summary>Palette entries we write. Stored as 0 because the field counts modulo 256.</summary>
  public const int ColorCount = 256;

  /// <summary>Total file size.</summary>
  public const int FileSize = PaletteOffset + ColorCount * 3;

  static string IImageFormatMetadata<TurboRascalFile>.PrimaryExtension => ".flf";
  static string[] IImageFormatMetadata<TurboRascalFile>.FileExtensions => [".flf"];
  static TurboRascalFile IImageFormatReader<TurboRascalFile>.FromSpan(ReadOnlySpan<byte> data)
    => TurboRascalReader.FromSpan(data);
  static byte[] IImageFormatWriter<TurboRascalFile>.ToBytes(TurboRascalFile file)
    => TurboRascalWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<TurboRascalFile>.VideoModes => [
    new("Chunky 320x200", [(ImageWidth, ImageHeight)], [ColorCount])
  ];

  /// <summary>One palette index per pixel.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Palette as RGB triplets.</summary>
  public byte[] Palette { get; init; }

  public static RawImage ToRawImage(TurboRascalFile file) => new() {
    Width = ImageWidth,
    Height = ImageHeight,
    Format = PixelFormat.Indexed8,
    PixelData = file.PixelData[..],
    Palette = file.Palette[..],
    PaletteCount = (file.Palette?.Length ?? 0) / 3,
  };

  public static TurboRascalFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != ImageWidth || image.Height != ImageHeight)
      throw new ArgumentException($"Expected {ImageWidth}x{ImageHeight} but got {image.Width}x{image.Height}.", nameof(image));

    var indexed = PixelConverter.Convert(image, PixelFormat.Indexed8);

    // The palette block is a fixed 256 entries; anything the quantizer left unused stays black.
    var palette = new byte[ColorCount * 3];
    (indexed.Palette ?? []).AsSpan(0, Math.Min(indexed.Palette?.Length ?? 0, palette.Length)).CopyTo(palette);

    var pixels = new byte[PixelDataSize];
    indexed.PixelData.AsSpan(0, Math.Min(indexed.PixelData.Length, PixelDataSize)).CopyTo(pixels);

    return new() { PixelData = pixels, Palette = palette };
  }
}
