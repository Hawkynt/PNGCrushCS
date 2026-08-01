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
  : IImageFormatReader<TrzmielCompressedFile>, IImageToRawImage<TrzmielCompressedFile>,
    IImageFromRawImage<TrzmielCompressedFile>, IImageFormatWriter<TrzmielCompressedFile> {

  static byte[] IImageFormatWriter<TrzmielCompressedFile>.ToBytes(TrzmielCompressedFile file)
    => TrzmielCompressedWriter.ToBytes(file);

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

  /// <summary>Builds a picture in the two colours the mode has, which are fixed rather than chosen.</summary>
  public static TrzmielCompressedFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(Width, Height).PixelData;
    var gtia = Atari8BitGraphics.Palette;
    var screen = new byte[ScreenSize];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      var at = (y * Width + x) * 3;
      if (_Distance(rgb, at, gtia, Foreground) >= _Distance(rgb, at, gtia, Background))
        continue;

      screen[y * Stride + (x >> 3)] |= (byte)(1 << (~x & 7));
    }

    return new() { ScreenData = screen };
  }

  private static long _Distance(ReadOnlySpan<byte> rgb, int at, ReadOnlySpan<byte> gtia, int color) {
    var entry = color * 3;
    long dr = rgb[at] - gtia[entry], dg = rgb[at + 1] - gtia[entry + 1], db = rgb[at + 2] - gtia[entry + 2];

    return dr * dr + dg * dg + db * db;
  }
}
