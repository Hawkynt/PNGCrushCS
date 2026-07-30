using System;
using FileFormat.Core;

namespace FileFormat.HcbEditor;

/// <summary>In-memory representation of an HCB-editor picture (.hcb) for the Commodore 64.</summary>
/// <remarks>
/// A multicolour screen that changes two things the hardware normally fixes for the whole display.
/// The background colour is rewritten every four scanlines, and the video matrix alternates between
/// two copies on the same four-line cycle — so a character cell, which is eight lines tall, draws
/// its top half from one set of colours and its bottom half from the other.
/// <para/>
/// That doubles the colours available per cell at the cost of a raster interrupt every four lines,
/// which is where the name comes from. The picture is 296 pixels wide rather than 320 because the
/// interrupt costs the leftmost characters.
/// </remarks>
public readonly record struct HcbEditorFile
  : IImageFormatReader<HcbEditorFile>, IImageToRawImage<HcbEditorFile> {

  /// <summary>Displayed width; the raster interrupt costs the leftmost cells.</summary>
  public const int Width = 296;

  /// <summary>Displayed height.</summary>
  public const int Height = 200;

  /// <summary>Character cells a stored row spans, before the cropping.</summary>
  public const int StrideColumns = 40;

  /// <summary>Offset of the first video matrix.</summary>
  public const int VideoMatrixOffset = 2053;

  /// <summary>Distance to the second video matrix, which alternate four-line bands use.</summary>
  public const int VideoMatrixStride = 1024;

  /// <summary>Offset of the bitmap.</summary>
  public const int BitmapOffset = 4122;

  /// <summary>Offset of the background colours, one per four scanlines.</summary>
  public const int BackgroundOffset = 12098;

  /// <summary>Scanlines that share one background colour.</summary>
  public const int BackgroundBand = 4;

  /// <summary>Total file size.</summary>
  public const int FileSize = 12148;

  static string IImageFormatMetadata<HcbEditorFile>.PrimaryExtension => ".hcb";
  static string[] IImageFormatMetadata<HcbEditorFile>.FileExtensions => [".hcb"];
  static HcbEditorFile IImageFormatReader<HcbEditorFile>.FromSpan(ReadOnlySpan<byte> data) => HcbEditorReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<HcbEditorFile>.VideoModes => [
    new("HCB", [(Width, Height)], [Commodore64Graphics.ColorCount])
  ];

  /// <summary>The file's bytes, kept whole because every area is at an absolute offset.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(HcbEditorFile file) {
    var data = file.Data ?? [];
    var pixels = new byte[Width * Height];

    for (var y = 0; y < Height; ++y) {
      // Both the colour source and the background follow the same four-line cycle.
      var matrix = VideoMatrixOffset + ((y & BackgroundBand) << 8);
      var background = (byte)(_At(data, BackgroundOffset + y / BackgroundBand) & 15);

      for (var x = 0; x < Width; ++x) {
        var cell = (y >> 3) * StrideColumns + (x >> 3);
        var pattern = (_At(data, BitmapOffset + (cell << 3) + (y & 7)) >> (~x & 6)) & 3;

        pixels[y * Width + x] = (byte)(pattern switch {
          1 => _At(data, matrix + cell) >> 4,
          2 => _At(data, matrix + cell) & 15,
          3 => _At(data, matrix + cell) & 15,
          _ => background,
        });
      }
    }

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = Commodore64Graphics.CreatePalette(),
      PaletteCount = Commodore64Graphics.ColorCount,
    };
  }

  private static byte _At(ReadOnlySpan<byte> data, int offset) => offset >= 0 && offset < data.Length ? data[offset] : (byte)0;
}
