using System;
using FileFormat.Core;

namespace FileFormat.GraphSaurus7;

/// <summary>In-memory representation of a Graph Saurus Screen 7 picture (.sr7).</summary>
/// <remarks>
/// The one member of the Graph Saurus family that was missing. Screen 5 (<c>.sr5</c>), Screen 6
/// (<c>.sr6</c>), Screen 8 (<c>.sr8</c>) and the interlaced Screen 7 (<c>.sri</c>) were all here;
/// plain Screen 7 was not, and it is the mode the interlaced one is two frames of.
/// <para/>
/// A BSAVE header and then 512 pixels a line at four bits each, sixteen colours, 212 stored rows.
/// Unlike Screen 6 the length is fixed: the reference decoder turns down a shorter file rather than
/// reading a part-height picture from the header's end address.
/// <para/>
/// Screen 7's pixels are half as tall as they are wide, so a stored row is drawn on two scanlines
/// and the picture comes out 512 by 424 — the same correction Screen 6 gets, and what the reference
/// decoder draws.
/// <para/>
/// The palette lives in a companion <c>.PL7</c>, sixteen two-byte entries and no header of its own.
/// Without one the picture means the sixteen colours an MSX2 powers up with.
/// </remarks>
public readonly record struct GraphSaurus7File
  : IImageFormatReader<GraphSaurus7File>, IImageToRawImage<GraphSaurus7File>,
    IImageFromRawImage<GraphSaurus7File>, IImageFormatWriter<GraphSaurus7File> {

  /// <summary>Stored pixels per row.</summary>
  public const int Width = 512;

  /// <summary>Stored rows.</summary>
  public const int StoredHeight = 212;

  /// <summary>Rows drawn: each stored one covers two scanlines.</summary>
  public const int DrawnHeight = StoredHeight * 2;

  /// <summary>Bytes one stored row occupies, at two pixels per byte.</summary>
  public const int BytesPerRow = Width / 2;

  /// <summary>Offset of the bitmap, after the BSAVE header.</summary>
  public const int BitmapOffset = MsxGraphics.BsaveHeaderSize;

  /// <summary>Colours the picture can show at once.</summary>
  public const int ColorCount = 16;

  /// <summary>Bytes the bitmap takes.</summary>
  public const int BitmapSize = BytesPerRow * StoredHeight;

  /// <summary>The least a whole picture takes: the header and the bitmap.</summary>
  public const int MinimumFileSize = BitmapOffset + BitmapSize;

  /// <summary>The palette sits beside the picture rather than in it.</summary>
  internal const string CompanionExtension = ".pl7";

  static string IImageFormatMetadata<GraphSaurus7File>.PrimaryExtension => ".sr7";
  static string[] IImageFormatMetadata<GraphSaurus7File>.FileExtensions => [".sr7"];
  static GraphSaurus7File IImageFormatReader<GraphSaurus7File>.FromSpan(ReadOnlySpan<byte> data)
    => GraphSaurus7Reader.FromSpan(data);

  /// <summary>Reads a named file, which is the only way the palette beside it is seen.</summary>
  static GraphSaurus7File IImageFormatReader<GraphSaurus7File>.FromFile(System.IO.FileInfo file)
    => GraphSaurus7Reader.FromFile(file);

  static byte[] IImageFormatWriter<GraphSaurus7File>.ToBytes(GraphSaurus7File file)
    => GraphSaurus7Writer.ToBytes(file);

  static VideoMode[] IImageFormatMetadata<GraphSaurus7File>.VideoModes => [
    new("Screen 7", [(Width, DrawnHeight)], [ColorCount])
  ];

  /// <summary>The bitmap, two pixels per byte, high nibble leftmost.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>The companion palette's bytes, or null when there was none.</summary>
  public byte[]? Palette { get; init; }

  public static RawImage ToRawImage(GraphSaurus7File file) {
    var data = file.PixelData ?? [];
    var pixels = new byte[Width * DrawnHeight];

    for (var y = 0; y < DrawnHeight; ++y)
    for (var x = 0; x < Width; ++x)
      pixels[y * Width + x] = (byte)MsxGraphics.GetNibble(data, (y >> 1) * BytesPerRow, x);

    var palette = file.Palette is { Length: >= ColorCount * MsxGraphics.PaletteEntrySize }
      ? file.Palette
      : MsxGraphics.DefaultPalette.ToArray();

    return new() {
      Width = Width,
      Height = DrawnHeight,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = MsxGraphics.PaletteToRgb(palette, ColorCount),
      PaletteCount = ColorCount,
    };
  }

  /// <summary>Builds a picture and the palette that goes with it.</summary>
  /// <remarks>
  /// Screen 7 can name its own sixteen colours, so unlike Screen 6 this does not have to settle for
  /// the ones the machine starts with — the palette is chosen for the picture and kept, ready for
  /// the companion file to be written beside it.
  /// <para/>
  /// A stored row is drawn twice, so it is taken from the upper scanline of the pair rather than
  /// averaged: averaging invents colours that are not in the sixteen and then has to quantise them
  /// back, which loses more than it gains.
  /// </remarks>
  public static GraphSaurus7File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var indexed = image.SampleTo(Width, DrawnHeight).EnsureIndexedAtMost(ColorCount);
    var indices = indexed.PixelData;

    var data = new byte[BitmapSize];
    for (var row = 0; row < StoredHeight; ++row)
    for (var x = 0; x < Width; ++x)
      MsxGraphics.SetNibble(data, row * BytesPerRow, x, indices[row * 2 * Width + x]);

    return new() {
      PixelData = data,
      Palette = MsxGraphics.PaletteFromRgb(indexed.Palette ?? [], indexed.PaletteCount, ColorCount),
    };
  }
}
