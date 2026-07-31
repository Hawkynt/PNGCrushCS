using System;
using FileFormat.Core;

namespace FileFormat.FunWithArt;

/// <summary>In-memory representation of a Fun with Art picture (.fwa).</summary>
/// <remarks>
/// A four-colour Atari 8-bit screen whose colours change down the picture, and which stores those
/// changes not as a table but as the 6502 routine that performs them. The program saved its whole
/// working state — the display list, the interrupt handlers, the screen — so reading the file means
/// reading the display list to find which lines interrupt, and then reading the machine code at
/// each interrupt to find which registers it writes.
/// <para/>
/// Only a handful of instructions can appear, and they are recognised by opcode rather than
/// executed: the routine saves two registers, waits for the beam, then loads and stores colours
/// until it returns. Anything else means the file is not one Fun with Art wrote.
/// </remarks>
public readonly record struct FunWithArtFile
  : IImageFormatReader<FunWithArtFile>, IImageToRawImage<FunWithArtFile> {

  /// <summary>Pixels across.</summary>
  public const int Width = 320;

  /// <summary>Rows.</summary>
  public const int Height = 192;

  /// <summary>Bytes one row of the bitmap occupies.</summary>
  public const int BytesPerRow = Width / 8;

  /// <summary>Where the bitmap starts.</summary>
  public const int BitmapOffset = 262;

  /// <summary>The row at which the bitmap skips the gap left for the display list's second half.</summary>
  public const int SplitRow = 102;

  /// <summary>How many bytes the bitmap skips at that row.</summary>
  public const int SplitGap = 16;

  /// <summary>Where the display list starts.</summary>
  public const int DisplayListOffset = 9;

  /// <summary>Where the interrupt routines start.</summary>
  public const int InterruptOffset = 7960;

  static string IImageFormatMetadata<FunWithArtFile>.PrimaryExtension => ".fwa";
  static string[] IImageFormatMetadata<FunWithArtFile>.FileExtensions => [".fwa"];
  static FunWithArtFile IImageFormatReader<FunWithArtFile>.FromSpan(ReadOnlySpan<byte> data)
    => FunWithArtReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<FunWithArtFile>.VideoModes => [
    new("Atari 8-bit", [(Width, Height)], [256])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  /// <summary>
  /// The four colour registers as each row sees them: background, PF0, PF1 and PF2, one set a row.
  /// </summary>
  public byte[] Registers { get; init; }

  public static RawImage ToRawImage(FunWithArtFile file) {
    var data = file.Data ?? [];
    var registers = file.Registers ?? [];
    var frame = new byte[Width * Height];

    for (var y = 0; y < Height; ++y)
      Atari8BitGraphics.DecodeGr15Into(
        data, BitmapOffset + BytesPerRow * y + (y >= SplitRow ? SplitGap : 0), BytesPerRow,
        frame, y * Width, Width, Width, 1,
        registers.AsSpan(y * Atari8BitGraphics.Gr15RegisterCount, Atari8BitGraphics.Gr15RegisterCount));

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = Atari8BitGraphics.ApplyPalette(frame),
    };
  }
}
