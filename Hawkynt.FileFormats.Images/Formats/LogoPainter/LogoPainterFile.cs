using System;
using FileFormat.Core;

namespace FileFormat.LogoPainter;

/// <summary>In-memory representation of a Logo Painter 3 picture for the Commodore 64.</summary>
/// <remarks>
/// This wanted 10002 bytes read as a bitmap with screen and colour memory — an ordinary multicolour
/// screen. Logo Painter does not save a bitmap at all. It saves a character set and a screen of
/// character codes, which is how a logo stays small: 2 + 2048 + 2048 = 4098, and every sample is
/// that or a little more. All of them were refused for being not quite half the expected length.
/// <para/>
/// The screen is 40 columns by 50 rows rather than the usual 25, so the picture is 320 by 400 with
/// each character four pixels wide shown doubled. The screen takes a whole page for the 2000 bytes
/// it uses and the character set follows it.
/// </remarks>
public readonly record struct LogoPainterFile : IImageFormatReader<LogoPainterFile>, IImageToRawImage<LogoPainterFile>, IImageFormatWriter<LogoPainterFile> {

  static string IImageFormatMetadata<LogoPainterFile>.PrimaryExtension => ".lp3";
  static string[] IImageFormatMetadata<LogoPainterFile>.FileExtensions => [".lp3"];
  static LogoPainterFile IImageFormatReader<LogoPainterFile>.FromSpan(ReadOnlySpan<byte> data) => LogoPainterReader.FromSpan(data);
  static byte[] IImageFormatWriter<LogoPainterFile>.ToBytes(LogoPainterFile file) => LogoPainterWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<LogoPainterFile>.VideoModes => [
    new("Logo Painter 3", [(FixedWidth, FixedHeight)], [4])
  ];

  /// <summary>Pixels across, each stored one being shown twice.</summary>
  public const int FixedWidth = 320;

  /// <summary>Rows: fifty character rows of eight.</summary>
  public const int FixedHeight = 400;

  /// <summary>Character columns.</summary>
  internal const int Columns = 40;

  /// <summary>Character rows, twice the usual twenty-five.</summary>
  internal const int Rows = FixedHeight / 8;

  internal const int LoadAddressSize = 2;

  /// <summary>The address space the screen occupies, a whole page for the 2000 bytes it uses.</summary>
  internal const int ScreenStride = 2048;

  /// <summary>The bytes a character set takes: 256 characters of eight rows.</summary>
  internal const int CharacterSetSize = 2048;

  internal const int ScreenOffset = LoadAddressSize;
  internal const int CharacterSetOffset = ScreenOffset + ScreenStride;

  /// <summary>The whole of a picture: 2 + 2048 + 2048.</summary>
  public const int ExpectedFileSize = CharacterSetOffset + CharacterSetSize;

  /// <summary>
  /// Where the colour registers sit, in the screen page's unused tail.
  /// </summary>
  /// <remarks>
  /// A file that carries its own display routine sets them, and the routine reads them from exactly
  /// here — $1FFB into the background register, $1FFD and $1FFE into the other two, and $1FFC into
  /// colour memory. A file saved without one leaves the whole tail at 0xFF, which is not four
  /// colours but no colours, and a viewer draws its own.
  /// </remarks>
  internal const int BackgroundRegisterOffset = 2045;
  internal const int ColorMemoryOffset = 2046;
  internal const int MulticolorRegister1Offset = 2047;
  internal const int MulticolorRegister2Offset = 2048;

  /// <summary>What the four patterns show where the file names no colours.</summary>
  internal static ReadOnlySpan<byte> DefaultColors => [0, 10, 2, 1];

  /// <summary>Always 320.</summary>
  public int Width => FixedWidth;

  /// <summary>Always 400.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>One character code per cell, forty across and fifty down.</summary>
  public byte[] Screen { get; init; }

  /// <summary>The character set the screen draws from.</summary>
  public byte[] CharacterSet { get; init; }

  /// <summary>What each of the four patterns shows, as indices into the machine's sixteen.</summary>
  public byte[] Colors { get; init; }

  /// <summary>Converts this picture to a platform-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(LogoPainterFile file) {
    var screen = file.Screen ?? [];
    var characters = file.CharacterSet ?? [];
    var colors = file.Colors ?? DefaultColors.ToArray();
    var indices = new byte[FixedWidth * FixedHeight];

    for (var y = 0; y < FixedHeight; ++y)
      for (var x = 0; x < FixedWidth / 2; ++x) {
        var cell = y / 8 * Columns + x / 4;
        var pattern = (characters[screen[cell] * 8 + y % 8] >> ((3 - x % 4) * 2)) & 3;
        var colour = colors[pattern];

        // Stored four pixels to a character, each shown twice.
        indices[y * FixedWidth + x * 2] = colour;
        indices[y * FixedWidth + x * 2 + 1] = colour;
      }

    return new() {
      Width = FixedWidth,
      Height = FixedHeight,
      Format = PixelFormat.Indexed8,
      PixelData = indices,
      Palette = Commodore64Graphics.CreatePalette(),
      PaletteCount = Commodore64Graphics.ColorCount,
    };
  }
}
