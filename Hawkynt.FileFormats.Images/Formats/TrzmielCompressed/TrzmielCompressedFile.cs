using System;
using FileFormat.Core;

namespace FileFormat.TrzmielCompressed;

/// <summary>In-memory representation of a compressed Trzmiel picture (.cpr).</summary>
/// <remarks>
/// A 320x192 monochrome Atari screen packed with Koala's encoding, and the first byte says which of
/// three ways: stored outright, packed straight through, or packed column by column. The last is
/// what a picture with vertical structure wants — a run down a column is one run rather than 192.
/// <para/>
/// The two colours are the opposite way round from the usual: a clear bit draws light grey and a
/// set one black, so the picture is ink on paper rather than light on a dark screen.
/// </remarks>
public readonly record struct TrzmielCompressedFile
  : IImageFormatReader<TrzmielCompressedFile>, IImageToRawImage<TrzmielCompressedFile> {

  /// <summary>Pixels across.</summary>
  public const int Width = 320;

  /// <summary>Rows.</summary>
  public const int Height = 192;

  /// <summary>Bytes one row occupies.</summary>
  public const int Stride = Width / 8;

  /// <summary>Size of the screen a file unpacks to.</summary>
  public const int ScreenSize = Stride * Height;

  /// <summary>Bytes the column-by-column form steps between writes.</summary>
  public const int ColumnStride = Stride * 2;

  /// <summary>The colour a clear bit draws.</summary>
  public const byte Background = 12;

  /// <summary>The colour a set bit draws.</summary>
  public const byte Foreground = 0;

  static string IImageFormatMetadata<TrzmielCompressedFile>.PrimaryExtension => ".cpr";
  static string[] IImageFormatMetadata<TrzmielCompressedFile>.FileExtensions => [".cpr"];
  static TrzmielCompressedFile IImageFormatReader<TrzmielCompressedFile>.FromSpan(ReadOnlySpan<byte> data)
    => TrzmielCompressedReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<TrzmielCompressedFile>.VideoModes => [
    new("Trzmiel", [(Width, Height)], [2])
  ];

  /// <summary>The unpacked screen.</summary>
  public byte[] ScreenData { get; init; }

  public static RawImage ToRawImage(TrzmielCompressedFile file) {
    var screen = file.ScreenData ?? [];
    var gtia = Atari8BitGraphics.Palette;
    var pixels = new byte[Width * Height];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      var at = y * Stride + (x >> 3);
      if (at < screen.Length && ((screen[at] >> (~x & 7)) & 1) != 0)
        pixels[y * Width + x] = 1;
    }

    var palette = new byte[6];
    gtia.Slice(Background * 3, 3).CopyTo(palette);
    gtia.Slice(Foreground * 3, 3).CopyTo(palette.AsSpan(3));

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = palette,
      PaletteCount = 2,
    };
  }
}
