using System;
using FileFormat.Core;

namespace FileFormat.VerticalHiresInterlace;

/// <summary>In-memory representation of a Vertical Hires Interlace picture (.vhi) for the Commodore 64.</summary>
/// <remarks>
/// Two high-resolution bitmaps shown on alternate television fields, sharing one video matrix. That
/// sharing is the whole idea: each cell still names only two colours, but a pixel can be lit in one
/// field and not the other, so the eye sees the two mixed as well as each alone — three shades from
/// two colours, at full resolution and with no extra colour memory.
/// </remarks>
public readonly record struct VerticalHiresInterlaceFile
  : IImageFormatReader<VerticalHiresInterlaceFile>, IImageToRawImage<VerticalHiresInterlaceFile> {

  /// <summary>Picture width.</summary>
  public const int Width = 320;

  /// <summary>Picture height.</summary>
  public const int Height = 200;

  /// <summary>Character cells across a row.</summary>
  public const int Columns = Width / 8;

  /// <summary>Size a packed file unpacks to.</summary>
  public const int UnpackedSize = 17384;

  /// <summary>Size of a file that is not packed.</summary>
  public const int PlainFileSize = 17389;

  static string IImageFormatMetadata<VerticalHiresInterlaceFile>.PrimaryExtension => ".vhi";
  static string[] IImageFormatMetadata<VerticalHiresInterlaceFile>.FileExtensions => [".vhi"];
  static VerticalHiresInterlaceFile IImageFormatReader<VerticalHiresInterlaceFile>.FromSpan(ReadOnlySpan<byte> data)
    => VerticalHiresInterlaceReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<VerticalHiresInterlaceFile>.VideoModes => [
    new("Vertical hires interlace", [(Width, Height)], [Commodore64Graphics.ColorCount])
  ];

  /// <summary>The picture's bytes, packed or not, as the reader settled them.</summary>
  public byte[] Data { get; init; }

  /// <summary>Offset of the first field's bitmap.</summary>
  public int FirstBitmapOffset { get; init; }

  /// <summary>Offset of the second field's bitmap.</summary>
  public int SecondBitmapOffset { get; init; }

  /// <summary>Offset of the video matrix, which both fields share.</summary>
  public int VideoMatrixOffset { get; init; }

  public static RawImage ToRawImage(VerticalHiresInterlaceFile file) {
    var data = file.Data ?? [];
    var palette = Commodore64Graphics.CreatePalette();

    var first = _RenderField(data, file.FirstBitmapOffset, file.VideoMatrixOffset, palette);
    var second = _RenderField(data, file.SecondBitmapOffset, file.VideoMatrixOffset, palette);

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = FrameBlend.Average(first, second),
    };
  }

  private static byte[] _RenderField(ReadOnlySpan<byte> data, int bitmap, int matrix, ReadOnlySpan<byte> palette) {
    var rgb = new byte[Width * Height * 3];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      // The C64 cell layout: a cell's eight rows are consecutive bytes.
      var offset = (y & ~7) * Columns + (x & ~7) + (y & 7);
      var set = (_At(data, bitmap + offset) >> (~x & 7)) & 1;
      var attribute = _At(data, matrix + (offset >> 3));
      var index = (attribute >> (set << 2)) & 15;

      var entry = index * 3;
      var target = (y * Width + x) * 3;
      rgb[target] = palette[entry];
      rgb[target + 1] = palette[entry + 1];
      rgb[target + 2] = palette[entry + 2];
    }

    return rgb;
  }

  private static byte _At(ReadOnlySpan<byte> data, int offset)
    => offset >= 0 && offset < data.Length ? data[offset] : (byte)0;
}
