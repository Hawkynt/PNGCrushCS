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
  : IImageFormatReader<UifliEditorFile>, IImageToRawImage<UifliEditorFile>,
    IImageFromRawImage<UifliEditorFile>, IImageFormatWriter<UifliEditorFile> {

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
  static byte[] IImageFormatWriter<UifliEditorFile>.ToBytes(UifliEditorFile file)
    => UifliEditorWriter.ToBytes(file);
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

  /// <summary>Bytes from the start of the picture to the first cell of a video matrix.</summary>
  private const int _MATRIX_LEAD_IN = 3;

  /// <summary>Cells a video matrix row spans.</summary>
  private const int _MEMORY_COLUMNS = 40;

  /// <summary>Scanlines that share one video matrix byte.</summary>
  private const int _ROWS_PER_MATRIX = 2;

  /// <summary>Builds a picture from any image, sampling it to the 288x200 the editor showed.</summary>
  /// <remarks>
  /// The two frames are averaged, so a pixel lit in both shows one of its block's colours, lit in
  /// neither shows the other, and lit in exactly one shows their average — three shades where the
  /// stored pair is two. Both frames are given the same video matrix so that the pair is the same in
  /// each and the average of a mixed pixel lands between them; letting the frames disagree about the
  /// colours as well would put four in play and make the choice a search over pairs of pairs for a
  /// gain the eye does not get, since what it sees is still one blend per pixel.
  /// <para/>
  /// The sprites are left clear. They are half the bitmap's resolution both ways and draw in a
  /// single colour for the whole picture, so anywhere one covers, four bitmap pixels lose their own
  /// colours for one that was not chosen for them.
  /// </remarks>
  public static UifliEditorFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(Width, Height).EnsureFormat(PixelFormat.Rgb24).PixelData;
    var data = new byte[UnpackedSize];
    var palette = Commodore64Graphics.CreatePalette();
    var columns = Width / 8;
    Span<int> shades = stackalloc int[3];

    for (var top = 0; top < Height; top += Commodore64Graphics.CellHeight)
    for (var band = 0; band < Commodore64Graphics.CellHeight; band += _ROWS_PER_MATRIX)
    for (var column = 0; column < columns; ++column) {
      var (foreground, background) = _ChoosePair(rgb, palette, column << 3, top + band);
      _Shades(palette, foreground, background, shades);

      var offset = _MATRIX_LEAD_IN + top / Commodore64Graphics.CellHeight * _MEMORY_COLUMNS + column;
      var matrix = (band << 9) + offset;
      data[FirstMatrixOffset + matrix] = data[SecondMatrixOffset + matrix]
        = (byte)((foreground << 4) | background);

      for (var y = 0; y < _ROWS_PER_MATRIX; ++y)
      for (var x = 0; x < 8; ++x) {
        var shade = _ClosestShade(rgb, ((top + band + y) * Width + (column << 3) + x) * 3, shades);
        if (shade == 0)
          continue;

        var at = (offset << 3) + band + y;
        var bit = (byte)(1 << (~x & 7));
        data[FirstBitmapOffset + at] |= bit;
        if (shade == 2)
          data[SecondBitmapOffset + at] |= bit;
      }
    }

    return new() { Data = data };
  }

  /// <summary>The three colours a pair can show, darkest first: background, average, foreground.</summary>
  private static void _Shades(ReadOnlySpan<byte> palette, int foreground, int background, Span<int> shades) {
    var high = (palette[foreground * 3] << 16) | (palette[foreground * 3 + 1] << 8) | palette[foreground * 3 + 2];
    var low = (palette[background * 3] << 16) | (palette[background * 3 + 1] << 8) | palette[background * 3 + 2];

    shades[0] = low;
    shades[1] = ((low & high) + (((low ^ high) >> 1) & 0x7F7F7F)) & 0xFFFFFF;
    shades[2] = high;
  }

  /// <summary>Which of a pair's three shades a pixel comes closest to.</summary>
  private static int _ClosestShade(ReadOnlySpan<byte> rgb, int at, ReadOnlySpan<int> shades) {
    var best = 0;
    var bestDistance = int.MaxValue;

    for (var shade = 0; shade < shades.Length; ++shade) {
      var distance = _Distance(rgb, at, shades[shade]);
      if (distance >= bestDistance)
        continue;

      bestDistance = distance;
      best = shade;
    }

    return best;
  }

  /// <summary>The pair whose three shades describe one block of eight pixels by two with least error.</summary>
  private static (int Foreground, int Background) _ChoosePair(
    ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> palette, int left, int top) {
    int bestForeground = 0, bestBackground = 0;
    var bestError = long.MaxValue;
    Span<int> shades = stackalloc int[3];

    for (var first = 0; first < Commodore64Graphics.ColorCount; ++first)
    for (var second = 0; second <= first; ++second) {
      _Shades(palette, first, second, shades);

      long error = 0;
      for (var y = 0; y < _ROWS_PER_MATRIX; ++y)
      for (var x = 0; x < 8; ++x) {
        var at = ((top + y) * Width + left + x) * 3;
        var closest = int.MaxValue;
        foreach (var shade in shades)
          closest = Math.Min(closest, _Distance(rgb, at, shade));

        error += closest;
      }

      if (error >= bestError)
        continue;

      bestError = error;
      bestForeground = first;
      bestBackground = second;
    }

    return (bestForeground, bestBackground);
  }

  /// <summary>Squared distance between a pixel and a packed colour.</summary>
  private static int _Distance(ReadOnlySpan<byte> rgb, int at, int color) {
    int dr = rgb[at] - ((color >> 16) & 0xFF);
    int dg = rgb[at + 1] - ((color >> 8) & 0xFF);
    int db = rgb[at + 2] - (color & 0xFF);

    return dr * dr + dg * dg + db * db;
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
