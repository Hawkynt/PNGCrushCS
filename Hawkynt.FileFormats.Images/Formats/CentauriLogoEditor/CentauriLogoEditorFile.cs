using System;
using FileFormat.Core;

namespace FileFormat.CentauriLogoEditor;

/// <summary>In-memory representation of a Centauri Logo-Editor picture (.cle) for the Commodore 64.</summary>
/// <remarks>
/// A full screen in four freely chosen colours, with no per-cell attributes: two bits a pixel
/// against one palette for the whole picture. That is fewer colours than a multicolour screen but
/// no restriction on where they go, which for a logo is the better trade.
/// <para/>
/// The four registers are not stored in order. Two of them share one byte's nibbles and the other
/// two have a byte each, which is how the C64's registers happen to sit in memory rather than
/// anything the format chose.
/// </remarks>
public readonly record struct CentauriLogoEditorFile
  : IImageFormatReader<CentauriLogoEditorFile>, IImageToRawImage<CentauriLogoEditorFile> {

  /// <summary>Picture width.</summary>
  public const int Width = 320;

  /// <summary>Picture height.</summary>
  public const int Height = 200;

  /// <summary>Offset of the bitmap.</summary>
  public const int BitmapOffset = 2;

  /// <summary>Offset of the byte holding the second and third colours.</summary>
  public const int PairOffset = 8002;

  /// <summary>Offset of the byte holding the fourth colour.</summary>
  public const int FourthOffset = 8003;

  /// <summary>Offset of the byte holding the first colour.</summary>
  public const int FirstOffset = 8004;

  /// <summary>Total file size.</summary>
  public const int FileSize = 8194;

  static string IImageFormatMetadata<CentauriLogoEditorFile>.PrimaryExtension => ".cle";
  static string[] IImageFormatMetadata<CentauriLogoEditorFile>.FileExtensions => [".cle"];
  static CentauriLogoEditorFile IImageFormatReader<CentauriLogoEditorFile>.FromSpan(ReadOnlySpan<byte> data)
    => CentauriLogoEditorReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<CentauriLogoEditorFile>.VideoModes => [
    new("Centauri logo", [(Width, Height)], [4])
  ];

  /// <summary>The file's bytes, kept whole because the registers sit past the bitmap.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(CentauriLogoEditorFile file) {
    var data = file.Data ?? [];
    var c64 = Commodore64Graphics.CreatePalette();

    // Scattered rather than consecutive: the middle pair share a byte and the outer two do not.
    ReadOnlySpan<int> colors = [
      _At(data, FirstOffset) & 15,
      _At(data, PairOffset) >> 4,
      _At(data, PairOffset) & 15,
      _At(data, FourthOffset) & 15,
    ];

    var palette = new byte[colors.Length * 3];
    for (var i = 0; i < colors.Length; ++i)
      c64.AsSpan(colors[i] * 3, 3).CopyTo(palette.AsSpan(i * 3));

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = Commodore64Graphics.DecodeFourColor(data, BitmapOffset, 0, Width, Height, palette),
    };
  }

  private static byte _At(ReadOnlySpan<byte> data, int offset)
    => offset >= 0 && offset < data.Length ? data[offset] : (byte)0;
}
