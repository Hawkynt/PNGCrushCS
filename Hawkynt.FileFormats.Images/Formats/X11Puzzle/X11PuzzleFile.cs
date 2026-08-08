using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.X11Puzzle;

/// <summary>In-memory representation of a jigsaw puzzle picture (.pzl).</summary>
/// <remarks>
/// The width and the height as big-endian 32-bit counts, one byte this does not use, a 256-entry
/// palette of RGB triplets, and then one byte a pixel. Nine plus 768 plus the picture is the file to
/// the byte, which is what identifies one — there is no signature.
/// </remarks>
public readonly record struct X11PuzzleFile
  : IImageFormatReader<X11PuzzleFile>, IImageToRawImage<X11PuzzleFile>,
    IImageFromRawImage<X11PuzzleFile>, IImageFormatWriter<X11PuzzleFile> {

  /// <summary>Two counts and a byte.</summary>
  public const int HeaderSize = 9;

  public const int PaletteCount = 256;

  public const int PaletteSize = PaletteCount * 3;

  /// <summary>Where the picture starts.</summary>
  public const int PixelOffset = HeaderSize + PaletteSize;

  static string IImageFormatMetadata<X11PuzzleFile>.PrimaryExtension => ".pzl";
  static string[] IImageFormatMetadata<X11PuzzleFile>.FileExtensions => [".pzl"];
  static X11PuzzleFile IImageFormatReader<X11PuzzleFile>.FromSpan(ReadOnlySpan<byte> data) => X11PuzzleReader.FromSpan(data);
  static byte[] IImageFormatWriter<X11PuzzleFile>.ToBytes(X11PuzzleFile file) => X11PuzzleWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<X11PuzzleFile>.VideoModes => [
    new("Default", [(new IntegerRange(1, 8192), new IntegerRange(1, 8192))], [PaletteCount])
  ];

  public int Width { get; init; }

  public int Height { get; init; }

  /// <summary>The byte between the size and the palette, kept so writing one back preserves it.</summary>
  public byte Reserved { get; init; }

  public byte[] Palette { get; init; }

  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(X11PuzzleFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed8,
    PixelData = (file.PixelData ?? [])[..],
    Palette = (file.Palette ?? new byte[PaletteSize])[..],
    PaletteCount = PaletteCount,
  };

  public static X11PuzzleFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    var indexed = image.EnsureIndexedAtMost(PaletteCount);

    var palette = new byte[PaletteSize];
    (indexed.Palette ?? []).AsSpan(0, Math.Min(indexed.PaletteCount * 3, PaletteSize)).CopyTo(palette);

    return new() {
      Width = indexed.Width,
      Height = indexed.Height,
      Palette = palette,
      PixelData = indexed.PixelData[..],
    };
  }
}
