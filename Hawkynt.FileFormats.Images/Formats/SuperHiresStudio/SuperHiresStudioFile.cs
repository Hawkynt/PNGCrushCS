using System;
using FileFormat.Core;

namespace FileFormat.SuperHiresStudio;

/// <summary>In-memory representation of a Super Hires Studio picture (.shs) for the Commodore 64.</summary>
/// <remarks>
/// A high-resolution screen with two layers of hardware sprites over its middle. The sprites cover
/// only the twelve character columns and 168 scanlines the hardware can reach with eight sprites
/// multiplexed, so the picture is a full 320 by 200 with a window in it where the extra colours
/// live — everything outside that window is an ordinary two-colours-per-cell screen.
/// <para/>
/// Each sprite band gets one colour per layer, so the window shows two extra colours across each of
/// its four sprite-widths rather than per cell.
/// </remarks>
public readonly record struct SuperHiresStudioFile
  : IImageFormatReader<SuperHiresStudioFile>, IImageToRawImage<SuperHiresStudioFile>,
    IImageFromRawImage<SuperHiresStudioFile>, IImageFormatWriter<SuperHiresStudioFile> {

  /// <summary>Picture width.</summary>
  public const int Width = 320;

  /// <summary>Picture height.</summary>
  public const int Height = 200;

  /// <summary>Offset of the bitmap.</summary>
  public const int BitmapOffset = 2;

  /// <summary>Offset of the background sprite colours, one per band.</summary>
  public const int BackgroundColorsOffset = 8002;

  /// <summary>Offset of the foreground sprite colours, one per band.</summary>
  public const int ForegroundColorsOffset = 8006;

  /// <summary>Offset of the background sprite shapes.</summary>
  public const int BackgroundSpritesOffset = 8194;

  /// <summary>Offset of the foreground sprite shapes.</summary>
  public const int ForegroundSpritesOffset = 10242;

  /// <summary>Offset of the video matrix.</summary>
  public const int VideoMatrixOffset = 13314;

  /// <summary>First scanline the sprites reach.</summary>
  public const int SpritesTop = 17;

  /// <summary>First scanline past the sprites.</summary>
  public const int SpritesBottom = 185;

  /// <summary>First character column the sprites reach.</summary>
  public const int SpritesLeft = 2;

  /// <summary>First character column past the sprites.</summary>
  public const int SpritesRight = 14;

  /// <summary>Total file size.</summary>
  public const int FileSize = 14338;

  static string IImageFormatMetadata<SuperHiresStudioFile>.PrimaryExtension => ".shs";
  static string[] IImageFormatMetadata<SuperHiresStudioFile>.FileExtensions => [".shs"];
  static SuperHiresStudioFile IImageFormatReader<SuperHiresStudioFile>.FromSpan(ReadOnlySpan<byte> data)
    => SuperHiresStudioReader.FromSpan(data);
  static byte[] IImageFormatWriter<SuperHiresStudioFile>.ToBytes(SuperHiresStudioFile file)
    => SuperHiresStudioWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<SuperHiresStudioFile>.VideoModes => [
    new("Super Hires Studio", [(Width, Height)], [Commodore64Graphics.ColorCount])
  ];

  /// <summary>The file's bytes, kept whole because every area is at an absolute offset.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(SuperHiresStudioFile file) {
    var data = file.Data ?? [];
    var pixels = new byte[Width * Height];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x)
      pixels[y * Width + x] = _ColorAt(data, x, y);

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = Commodore64Graphics.CreatePalette(),
      PaletteCount = Commodore64Graphics.ColorCount,
    };
  }

  /// <summary>Builds a picture from any image, sampling it to the 320x200 screen.</summary>
  /// <remarks>
  /// The sprite window is left empty. Its two layers add one extra colour each across a whole
  /// character-column band and all 168 of the scanlines they reach — a choice that can only be made
  /// well by knowing which colour the picture wants held constant down a strip that tall, and made
  /// badly it replaces good cell colours with a worse one over a sixth of the screen. Left clear,
  /// every pixel comes from the bitmap, which is where the cell-by-cell search already puts the two
  /// best colours it can find.
  /// </remarks>
  public static SuperHiresStudioFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(Width, Height).EnsureFormat(PixelFormat.Rgb24);
    var data = new byte[FileSize];
    Commodore64Graphics.EncodeHires(
      rgb.PixelData, Width, Height,
      data.AsSpan(BitmapOffset, Width * Height / 8),
      data.AsSpan(VideoMatrixOffset, Width / 8 * (Height / Commodore64Graphics.CellHeight)));

    return new() { Data = data };
  }

  private static byte _ColorAt(ReadOnlySpan<byte> data, int x, int y) {
    var bit = ~x & 7;
    var column = x >> 3;

    if (y >= SpritesTop && y < SpritesBottom && column >= SpritesLeft && column < SpritesRight) {
      var spriteColumn = column - SpritesLeft;
      var band = spriteColumn / 3;
      var spriteY = y - SpritesTop;
      // Eight sprites are multiplexed down each band, each twenty-one lines tall and padded to 64.
      var offset = (((band << 3) + spriteY / 21) << 6) + spriteY % 21 * 3 + spriteColumn % 3;

      if (((_At(data, ForegroundSpritesOffset + offset) >> bit) & 1) != 0)
        return (byte)(_At(data, ForegroundColorsOffset + band) & 15);

      if (((_At(data, BackgroundSpritesOffset + offset) >> bit) & 1) != 0)
        return (byte)(_At(data, BackgroundColorsOffset + band) & 15);
    }

    // Outside the sprite window it is an ordinary hires screen.
    var cell = (y >> 3) * Commodore64Graphics.Columns + column;
    var set = (_At(data, BitmapOffset + (cell << 3) + (y & 7)) >> bit) & 1;

    return (byte)((_At(data, VideoMatrixOffset + cell) >> (set << 2)) & 15);
  }

  private static byte _At(ReadOnlySpan<byte> data, int offset)
    => offset >= 0 && offset < data.Length ? data[offset] : (byte)0;
}
