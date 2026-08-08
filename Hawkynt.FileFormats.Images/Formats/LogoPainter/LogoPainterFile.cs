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
public readonly record struct LogoPainterFile
  : IImageFormatReader<LogoPainterFile>, IImageToRawImage<LogoPainterFile>,
    IImageFromRawImage<LogoPainterFile>, IImageFormatWriter<LogoPainterFile> {

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

  /// <summary>Default load address, putting the screen at $1800 where the display routine reads it.</summary>
  internal const ushort DefaultLoadAddress = 0x1800;

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

  /// <summary>Encodes a picture as a Logo Painter 3 logo, scaling it to 320x400 first.</summary>
  /// <remarks>
  /// This is not a bitmap format, which is what keeps a logo small: the file holds a character set
  /// and a screen of codes into it. Two thousand cells therefore have to be said in 256 characters,
  /// so identical cells are shared and, once the set is full, a cell is given whichever character it
  /// differs from least. A logo — a few shapes on a flat ground — needs far fewer than 256 and comes
  /// back exactly; a photograph does not and is approximated, which is the format's own limit rather
  /// than the encoder's.
  /// <para/>
  /// The four colours are the picture's four commonest, with one exception: pattern 11 is read out
  /// of colour memory, whose fourth bit is the multicolour flag rather than part of the colour, so
  /// only the low eight of the machine's sixteen can go there. Choosing freely and letting the
  /// writer mask it would change the colour after the fact.
  /// </remarks>
  public static LogoPainterFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    // A stored pixel is drawn twice, so the picture is sampled at the width it is stored at.
    var stored = FixedWidth / 2;
    var rgb = image.SampleTo(stored, FixedHeight).PixelData;

    var colors = _ChooseColors(rgb);
    var screen = new byte[Columns * Rows];
    var characters = new byte[CharacterSetSize];
    var used = 0;

    Span<byte> glyph = stackalloc byte[8];
    for (var row = 0; row < Rows; ++row)
    for (var column = 0; column < Columns; ++column) {
      for (var line = 0; line < 8; ++line) {
        var packed = 0;
        for (var x = 0; x < 4; ++x) {
          var at = ((row * 8 + line) * stored + column * 4 + x) * 3;
          packed |= _Pattern(rgb[at], rgb[at + 1], rgb[at + 2], colors) << ((3 - x) * 2);
        }

        glyph[line] = (byte)packed;
      }

      screen[row * Columns + column] = _Intern(characters, glyph, ref used);
    }

    return new() { LoadAddress = DefaultLoadAddress, Screen = screen, CharacterSet = characters, Colors = colors };
  }

  /// <summary>Gives a cell a character code, reusing one already stored or adding it where there is room.</summary>
  /// <remarks>
  /// Past 256 the set is full and nothing can be added, so the cell takes whichever stored character
  /// it differs from in the fewest bit pairs. That is a fallback for pictures the format was never
  /// meant to hold; a logo never reaches it.
  /// </remarks>
  private static byte _Intern(byte[] characters, ReadOnlySpan<byte> glyph, ref int used) {
    for (var candidate = 0; candidate < used; ++candidate)
      if (glyph.SequenceEqual(characters.AsSpan(candidate * 8, 8)))
        return (byte)candidate;

    if (used < CharacterSetSize / 8) {
      glyph.CopyTo(characters.AsSpan(used * 8, 8));
      return (byte)used++;
    }

    var best = 0;
    var bestError = int.MaxValue;
    for (var candidate = 0; candidate < used; ++candidate) {
      var error = 0;
      for (var line = 0; line < 8; ++line)
      for (var x = 0; x < 4; ++x) {
        var shift = (3 - x) * 2;
        if (((glyph[line] >> shift) & 3) != ((characters[candidate * 8 + line] >> shift) & 3))
          ++error;
      }

      if (error >= bestError)
        continue;

      bestError = error;
      best = candidate;
    }

    return (byte)best;
  }

  /// <summary>The four colours the picture is best said in, pattern 11 being confined to the low eight.</summary>
  private static byte[] _ChooseColors(ReadOnlySpan<byte> rgb) {
    Span<int> totals = stackalloc int[Commodore64Graphics.ColorCount];
    for (var at = 0; at + 2 < rgb.Length; at += 3)
      ++totals[Commodore64Graphics.FindNearestColorIndex(rgb[at], rgb[at + 1], rgb[at + 2])];

    var colors = new byte[4];
    Span<bool> taken = stackalloc bool[Commodore64Graphics.ColorCount];

    for (var slot = 0; slot < 4; ++slot) {
      // Colour memory keeps only three bits of what it is given; the fourth is the multicolour flag.
      var limit = slot == 3 ? 8 : Commodore64Graphics.ColorCount;
      var best = -1;
      for (var i = 0; i < limit; ++i)
        if (!taken[i] && (best < 0 || totals[i] > totals[best]))
          best = i;

      if (best < 0)
        best = 0;

      colors[slot] = (byte)best;
      taken[best] = true;
    }

    return colors;
  }

  /// <summary>Which of the cell's four colours a pixel is nearest.</summary>
  private static int _Pattern(byte red, byte green, byte blue, ReadOnlySpan<byte> colors) {
    var index = Commodore64Graphics.FindNearestColorIndex(red, green, blue);
    var best = 0;
    var bestDistance = int.MaxValue;

    for (var i = 0; i < 4; ++i) {
      int a = Commodore64Graphics.HexColors[index], b = Commodore64Graphics.HexColors[colors[i]];
      int dr = ((a >> 16) & 0xFF) - ((b >> 16) & 0xFF), dg = ((a >> 8) & 0xFF) - ((b >> 8) & 0xFF), db = (a & 0xFF) - (b & 0xFF);
      var distance = dr * dr + dg * dg + db * db;
      if (distance >= bestDistance)
        continue;

      bestDistance = distance;
      best = i;
    }

    return best;
  }
}
