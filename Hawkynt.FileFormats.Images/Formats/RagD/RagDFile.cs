using System;
using FileFormat.Core;

namespace FileFormat.RagD;

/// <summary>In-memory representation of a RAG-D picture (.rag) or a Music Compile 2 one (.ragc).</summary>
/// <remarks>
/// One header covering the whole range an Atari Falcon can display: one to eight bitplanes against
/// a stored palette, or sixteen bits a pixel with no palette at all. Which it is follows from the
/// plane count and the palette length together, and the two are not independent — sixteen colours
/// cannot describe an eight-plane picture, and a true-colour one has a palette it does not use.
/// <para/>
/// The chunky variant carries the same header and the same 256-colour palette but spends a whole
/// byte per pixel instead of spreading it across eight planes, which is the same picture in a
/// layout a program can draw into without shifting bits.
/// </remarks>
public readonly record struct RagDFile
  : IImageFormatReader<RagDFile>, IImageToRawImage<RagDFile> {

  /// <summary>The string every file starts with.</summary>
  public const string Signature = "RAG-D!";

  /// <summary>Offset of the palette.</summary>
  public const int PaletteOffset = 30;

  /// <summary>Size of a stored ST palette: sixteen colours of one word each.</summary>
  public const int StPaletteLength = 32;

  /// <summary>Size of a stored Falcon palette: 256 colours of four bytes each.</summary>
  public const int FalconPaletteLength = 1024;

  /// <summary>Offset of the bitmap in a file carrying a Falcon palette.</summary>
  public const int FalconBitmapOffset = PaletteOffset + FalconPaletteLength;

  static string IImageFormatMetadata<RagDFile>.PrimaryExtension => ".rag";
  static string[] IImageFormatMetadata<RagDFile>.FileExtensions => [".rag", ".ragc"];
  static RagDFile IImageFormatReader<RagDFile>.FromSpan(ReadOnlySpan<byte> data)
    => RagDReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<RagDFile>.VideoModes => [
    new("RAG-D", [(IntegerRange.Any, IntegerRange.Any)], [new IntegerRange(2, 65536)])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  /// <summary>Pixels across.</summary>
  public int Width { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  /// <summary>Bitplanes a pixel is spread over, or 16 for true colour.</summary>
  public int Planes { get; init; }

  /// <summary>Size of the stored palette.</summary>
  public int PaletteLength { get; init; }

  /// <summary>Whether the pixels are stored one byte each rather than as bitplanes.</summary>
  public bool IsChunky { get; init; }

  public static RawImage ToRawImage(RagDFile file) {
    var data = file.Data ?? [];
    int width = file.Width, height = file.Height;

    if (file.Planes == 16) {
      var rgb = new byte[width * height * 3];
      for (var i = 0; i < width * height; ++i)
        AtariStGraphics.FalconTrueColorToRgb(data, FalconBitmapOffset + i * 2, rgb.AsSpan(i * 3, 3));

      return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
    }

    var colors = file.PaletteLength == StPaletteLength ? 16 : 256;
    var palette = file.PaletteLength == StPaletteLength
      ? AtariStGraphics.ReadPalette(data, PaletteOffset, colors)
      : AtariStGraphics.ReadFalconPalette(data, PaletteOffset, colors);

    var bitmapOffset = PaletteOffset + file.PaletteLength;
    var pixels = new byte[width * height];

    if (file.IsChunky)
      data.AsSpan(bitmapOffset, Math.Min(pixels.Length, data.Length - bitmapOffset)).CopyTo(pixels);
    else
      pixels = AtariStGraphics.UnpackBitplanes(
        data, bitmapOffset, (width >> 3) * file.Planes, file.Planes, width, height);

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = palette,
      PaletteCount = colors,
    };
  }
}
