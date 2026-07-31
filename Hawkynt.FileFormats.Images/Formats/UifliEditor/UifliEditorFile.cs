using System;
using FileFormat.Core;

namespace FileFormat.UifliEditor;

/// <summary>In-memory representation of a UIFLI-editor picture (.uif).</summary>
/// <remarks>
/// Two FLI screens shown alternately and averaged, each with sprites over it, at 288 pixels across
/// — wider than the C64's own 320-pixel display allows for a bitmap, because the sprites extend
/// past where FLI's timing leaves the bitmap usable.
/// <para/>
/// FLI switches the video matrix every scanline, and here the switch happens every other one: the
/// colour a cell takes comes from a bank chosen by two bits of the row, not three. That halves the
/// colour data for a picture already being averaged against a second one.
/// </remarks>
public readonly record struct UifliEditorFile
  : IImageFormatReader<UifliEditorFile>, IImageToRawImage<UifliEditorFile> {

  /// <summary>Pixels across.</summary>
  public const int Width = 288;

  /// <summary>Rows.</summary>
  public const int Height = 200;

  /// <summary>Size a file unpacks to.</summary>
  public const int UnpackedSize = 32576;

  /// <summary>Offset of the first frame's video matrix.</summary>
  public const int FirstMatrixOffset = 0;

  /// <summary>Offset of the first frame's sprites.</summary>
  public const int FirstSpriteOffset = 4096;

  /// <summary>Offset of the first frame's bitmap.</summary>
  public const int FirstBitmapOffset = 8192;

  /// <summary>Offset of the second frame's video matrix.</summary>
  public const int SecondMatrixOffset = 16384;

  /// <summary>Offset of the second frame's sprites.</summary>
  public const int SecondSpriteOffset = 20480;

  /// <summary>Offset of the second frame's bitmap.</summary>
  public const int SecondBitmapOffset = 24576;

  /// <summary>Offset of the colour the sprites draw in.</summary>
  public const int SpriteColorOffset = 4080;

  static string IImageFormatMetadata<UifliEditorFile>.PrimaryExtension => ".uif";
  static string[] IImageFormatMetadata<UifliEditorFile>.FileExtensions => [".uif"];
  static UifliEditorFile IImageFormatReader<UifliEditorFile>.FromSpan(ReadOnlySpan<byte> data)
    => UifliEditorReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<UifliEditorFile>.VideoModes => [
    new("UIFLI", [(Width, Height)], [Commodore64Graphics.ColorCount])
  ];

  /// <summary>The unpacked picture.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(UifliEditorFile file) {
    var data = file.Data ?? [];
    var palette = Commodore64Graphics.CreatePalette();

    var first = _Render(data, FirstBitmapOffset, FirstMatrixOffset, FirstSpriteOffset, palette);
    var second = _Render(data, SecondBitmapOffset, SecondMatrixOffset, SecondSpriteOffset, palette);

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = FrameBlend.Average(first, second),
    };
  }

  private static byte[] _Render(
    ReadOnlySpan<byte> data, int bitmap, int matrix, int sprites, ReadOnlySpan<byte> palette) {
    var rgb = new byte[Width * Height * 3];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      var column = x >> 3;
      var offset = 3 + (y & ~7) * 5 + column;

      // The video matrix bank changes every other scanline rather than every one.
      var color = _At(data, matrix + ((y & 6) << 9) + offset);

      if (((_At(data, bitmap + (offset << 3) + (y & 7)) >> (~x & 7)) & 1) != 0)
        color >>= 4;
      else {
        // Sprites are half the bitmap's resolution both ways, so each covers four of its pixels.
        var sprite = sprites + (((y / 40 * 12 + (y & 2) * 3 + column / 6) << 6)
                                + ((y + 1) >> 1) % 21 * 3 + (column >> 1) % 3);

        if (((_At(data, sprite) >> ((~x >> 1) & 7)) & 1) != 0)
          color = _At(data, SpriteColorOffset);
      }

      var entry = (color & 15) * 3;
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
