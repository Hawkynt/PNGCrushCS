using System;
using FileFormat.Core;

namespace FileFormat.InterlacedLogoEditor;

/// <summary>In-memory representation of an Interlaced Logo Editor picture (.ile) for the Commodore 64.</summary>
/// <remarks>
/// A logo rather than a screen: 320 by 48, two four-colour bitmaps shown on alternate television
/// fields with the second displaced a pixel. Four colours interlaced against four gives ten
/// distinct ones, which for a logo is generous.
/// <para/>
/// The four colours sit in the last bytes of the file. The last of them is masked to three bits
/// rather than four — it is the border register, which on the VIC-II has only eight values.
/// </remarks>
public readonly record struct InterlacedLogoEditorFile
  : IImageFormatReader<InterlacedLogoEditorFile>, IImageToRawImage<InterlacedLogoEditorFile> {

  /// <summary>Picture width.</summary>
  public const int Width = 320;

  /// <summary>Picture height.</summary>
  public const int Height = 48;

  /// <summary>Colours one field can show.</summary>
  public const int ColorCount = 4;

  /// <summary>Offset of the second field's bitmap.</summary>
  public const int SecondFieldOffset = 2;

  /// <summary>Offset of the first field's bitmap.</summary>
  public const int FirstFieldOffset = 2050;

  /// <summary>Offset of the four colour registers.</summary>
  public const int ColorsOffset = 4094;

  /// <summary>How far the second field sits from the first.</summary>
  public const int SecondFieldShift = 1;

  /// <summary>Total file size.</summary>
  public const int FileSize = 4098;

  static string IImageFormatMetadata<InterlacedLogoEditorFile>.PrimaryExtension => ".ile";
  static string[] IImageFormatMetadata<InterlacedLogoEditorFile>.FileExtensions => [".ile"];
  static InterlacedLogoEditorFile IImageFormatReader<InterlacedLogoEditorFile>.FromSpan(ReadOnlySpan<byte> data)
    => InterlacedLogoEditorReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<InterlacedLogoEditorFile>.VideoModes => [
    new("Interlaced logo", [(Width, Height)], [10])
  ];

  /// <summary>The file's bytes, kept whole because both fields share one palette.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(InterlacedLogoEditorFile file) {
    var data = file.Data ?? [];
    var c64 = Commodore64Graphics.CreatePalette();

    // The fourth register is the border, which has only eight values rather than sixteen.
    var palette = new byte[ColorCount * 3];
    for (var i = 0; i < ColorCount; ++i) {
      var color = _At(data, ColorsOffset + i) & (i < 3 ? 15 : 7);
      c64.AsSpan(color * 3, 3).CopyTo(palette.AsSpan(i * 3));
    }

    var first = _RenderField(data, FirstFieldOffset, 0, palette);
    var second = _RenderField(data, SecondFieldOffset, SecondFieldShift, palette);

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = FrameBlend.Average(first, second),
    };
  }

  private static byte[] _RenderField(ReadOnlySpan<byte> data, int offset, int shift, ReadOnlySpan<byte> palette) {
    var rgb = new byte[Width * Height * 3];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      var source = x - shift;
      // Two bits a pixel in the C64's cell layout; a displaced field starts on colour zero.
      var index = source < 0
        ? 0
        : (_At(data, offset + (y & ~7) * 40 + (source & ~7) + (y & 7)) >> (~source & 6)) & 3;

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
