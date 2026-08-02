using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.RamBrandt;

/// <summary>In-memory representation of a Ram Brandt (Atari 8-bit) screen.</summary>
/// <remarks>
/// Ram Brandt saves five different ANTIC modes under five extensions (.rm0 to .rm4) that share one
/// layout: 7680 bytes of bitmap, nine GTIA colour registers at 7680, and three 128-byte tables at
/// 7808 driving display-list interrupts. The first table lists the scanlines where the palette
/// changes; the other two say which register is reloaded there and with what. That is how a
/// four-colour mode ends up showing up to 99 colours down the screen.
/// </remarks>
public readonly record struct RamBrandtFile : IImageFormatReader<RamBrandtFile>, IImageToRawImage<RamBrandtFile>, IImageFromRawImage<RamBrandtFile>, IImageFormatWriter<RamBrandtFile> {

  /// <summary>Size of the bitmap section.</summary>
  public const int BitmapDataSize = 7680;

  /// <summary>Offset of the GTIA colour registers.</summary>
  public const int ColorsOffset = BitmapDataSize;

  /// <summary>Colour registers stored: a border colour plus PM1-PM3, PF0-PF3 and BAK.</summary>
  public const int ColorCount = 9;

  /// <summary>Offset of the three display-list interrupt tables.</summary>
  public const int DisplayListOffset = 7808;

  /// <summary>Entries in each of the three tables.</summary>
  public const int DisplayListEntries = 128;

  /// <summary>Combined size of the scanline, register and value tables.</summary>
  public const int DisplayListSize = DisplayListEntries * 3;

  /// <summary>The exact file size of an unpacked screen.</summary>
  public const int ExpectedFileSize = DisplayListOffset + DisplayListSize;

  /// <summary>Displayed width; every mode is shown across the same 320 screen pixels.</summary>
  public const int DisplayWidth = 320;

  /// <summary>Displayed height.</summary>
  public const int DisplayHeight = 192;

  /// <summary>Bytes per bitmap row.</summary>
  internal const int BytesPerRow = 40;

  /// <summary>Colours a Graphics 7 screen shows without display-list interrupts.</summary>
  public const int Graphics7ColorCount = 4;

  static string IImageFormatMetadata<RamBrandtFile>.PrimaryExtension => ".rm0";
  static string[] IImageFormatMetadata<RamBrandtFile>.FileExtensions => [".rm0", ".rm1", ".rm2", ".rm3", ".rm4"];
  static RamBrandtFile IImageFormatReader<RamBrandtFile>.FromSpan(ReadOnlySpan<byte> data) => RamBrandtReader.FromSpan(data);

  /// <summary>
  /// Reads a named file, the extension being what its reader needs.
  /// </summary>
  /// <remarks>
  /// The reader takes the extension into account and only the by-bytes entry was wired up here,
  /// so the registry could never reach it: whatever the extension would have settled was decided
  /// by a default instead. Ten formats carried this, each one otherwise found only when a sample
  /// happened to expose it.
  /// </remarks>
  static RamBrandtFile IImageFormatReader<RamBrandtFile>.FromFile(FileInfo file) => RamBrandtReader.FromFile(file);
  static byte[] IImageFormatWriter<RamBrandtFile>.ToBytes(RamBrandtFile file) => RamBrandtWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<RamBrandtFile>.VideoModes => [
    new("Graphics 7", [(DisplayWidth, DisplayHeight)], [Graphics7ColorCount]),
    new("Graphics 9", [(DisplayWidth, DisplayHeight)], [16]),
    new("Graphics 10", [(DisplayWidth, DisplayHeight)], [ColorCount]),
    new("Graphics 11", [(DisplayWidth, DisplayHeight)], [16]),
    new("Graphics 15", [(DisplayWidth, DisplayHeight)], [Graphics7ColorCount]),
  ];

  /// <summary>Which ANTIC mode the bitmap is in.</summary>
  public RamBrandtMode Mode { get; init; }

  /// <summary>Raw bitmap bytes (<see cref="BitmapDataSize"/>).</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>The nine stored GTIA colour registers.</summary>
  public byte[] Colors { get; init; }

  /// <summary>The three display-list interrupt tables, concatenated (<see cref="DisplayListSize"/>).</summary>
  public byte[] DisplayList { get; init; }

  /// <summary>Always 320.</summary>
  public int Width => DisplayWidth;

  /// <summary>Always 192.</summary>
  public int Height => DisplayHeight;

  /// <summary>Bitmap rows actually stored; Graphics 7 shows each of its 96 rows twice.</summary>
  public static int StoredRows(RamBrandtMode mode) => mode == RamBrandtMode.Graphics7 ? 96 : DisplayHeight;

  /// <summary>
  /// Renders the screen as one Atari colour byte per displayed pixel, replaying the display-list
  /// interrupts as it walks down the picture.
  /// </summary>
  private static byte[] _RenderColorBytes(RamBrandtFile file) {
    var colors = _ReadRegisters(file);
    var scanlines = _ScanlinesWithInterrupts(file);
    var bitmap = file.BitmapData ?? [];
    var displayList = file.DisplayList ?? [];

    var frame = new byte[DisplayWidth * DisplayHeight];
    var rows = StoredRows(file.Mode);
    var rowHeight = DisplayHeight / rows;

    for (var y = 0; y < rows; ++y) {
      var target = y * rowHeight * DisplayWidth;
      _RenderRow(file.Mode, bitmap, y * BytesPerRow, colors, frame, target);

      // Graphics 7 draws each stored row on two scanlines, so duplicate before the palette moves on.
      for (var repeat = 1; repeat < rowHeight; ++repeat)
        Array.Copy(frame, target, frame, target + repeat * DisplayWidth, DisplayWidth);

      if (!scanlines[y])
        continue;

      // The tables are indexed by the vertical counter the interrupt fires on, not by the row.
      var vcount = file.Mode == RamBrandtMode.Graphics7 ? 16 + y : 16 + ((y - 1) >> 1);
      var register = _At(displayList, DisplayListEntries + vcount);
      if (register < ColorCount)
        colors[register] = (byte)(_At(displayList, DisplayListEntries * 2 + vcount) & 254);
    }

    return frame;
  }

  /// <summary>Reads the stored registers, applying the masking each mode expects.</summary>
  private static byte[] _ReadRegisters(RamBrandtFile file) {
    var colors = new byte[ColorCount];
    var stored = file.Colors ?? [];

    // Graphics 9 takes only the hue from the background register; the bitmap supplies every luminance.
    if (file.Mode == RamBrandtMode.Graphics9) {
      colors[8] = (byte)(_At(stored, 8) & 240);
      return colors;
    }

    for (var i = 0; i < ColorCount; ++i)
      colors[i] = (byte)(_At(stored, i) & 254);

    return colors;
  }

  /// <summary>
  /// Expands the scanline table into a flag per stored row. Entries are biased so that value 0 can
  /// mean "no interrupt", and row 3 is spelled specially because the bias would push it negative.
  /// </summary>
  private static bool[] _ScanlinesWithInterrupts(RamBrandtFile file) {
    var result = new bool[DisplayHeight];
    var displayList = file.DisplayList ?? [];
    var graphics7 = file.Mode == RamBrandtMode.Graphics7;

    for (var i = 0; i < DisplayListEntries; ++i) {
      int y = _At(displayList, i);
      switch (y) {
        case 0:
        case 1:
        case 2:
        case 4:
        case 5:
          continue;
        case 3:
          y = graphics7 ? 0 : 1;
          break;
        default:
          y = graphics7 ? y - 5 : y < 100 ? y - 4 : y - 6;
          break;
      }

      if (y >= 0 && y < result.Length)
        result[y] = true;
    }

    return result;
  }

  /// <summary>Draws one stored bitmap row as Atari colour bytes.</summary>
  private static void _RenderRow(RamBrandtMode mode, byte[] bitmap, int offset, byte[] colors, byte[] frame, int target) {
    switch (mode) {
      case RamBrandtMode.Graphics7:
      case RamBrandtMode.Graphics15:
        // Two bits per pixel, four pixels per byte, each drawn two screen pixels wide.
        for (var x = 0; x < DisplayWidth; ++x) {
          var c = (_At(bitmap, offset + (x >> 3)) >> (~x & 6)) & 3;
          frame[target + x] = colors[c == 0 ? 8 : c + 3];
        }

        break;
      case RamBrandtMode.Graphics9:
        // A nibble per pixel selecting the luminance, the hue coming from the background register.
        for (var x = 0; x < DisplayWidth; ++x) {
          var c = (_At(bitmap, offset + (x >> 3)) >> (~x & 4)) & 15;
          frame[target + x] = (byte)(colors[8] | c);
        }

        break;
      case RamBrandtMode.Graphics10:
        // A nibble per pixel indexing the nine registers directly.
        for (var x = 0; x < DisplayWidth; ++x) {
          var c = (_At(bitmap, offset + (x >> 3)) >> (~x & 4)) & 15;
          frame[target + x] = colors[c < ColorCount ? c : 0];
        }

        break;
      case RamBrandtMode.Graphics11:
        // A nibble per pixel selecting the hue; luminance stays that of the background register.
        for (var x = 0; x < DisplayWidth; ++x) {
          var c = (_At(bitmap, offset + (x >> 3)) << (x & 4)) & 240;
          frame[target + x] = (byte)(c == 0 ? colors[8] & 240 : colors[8] | c);
        }

        break;
      default:
        throw new InvalidDataException($"Unknown Ram Brandt mode {mode}.");
    }
  }

  private static byte _At(byte[] data, int index) => index >= 0 && index < data.Length ? data[index] : (byte)0;

  /// <summary>Playfield register drawing a given Graphics 7 pixel value.</summary>
  /// <remarks>Value 0 comes from the background register; 1, 2 and 3 from PF0, PF1 and PF2.</remarks>
  private static int _ColorIndex(int pixel) => pixel == 0 ? 8 : pixel + 3;

  /// <summary>Converts this screen to a platform-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(RamBrandtFile file) {
    var frame = _RenderColorBytes(file);
    var gtia = Atari8BitGraphics.CreatePalette();

    // Only the colour bytes the picture actually uses become palette entries.
    var slot = new int[256];
    Array.Fill(slot, -1);

    var palette = new byte[256 * 3];
    var indices = new byte[frame.Length];
    var used = 0;
    for (var i = 0; i < frame.Length; ++i) {
      var colorByte = frame[i];
      if (slot[colorByte] < 0) {
        slot[colorByte] = used;
        Array.Copy(gtia, colorByte * 3, palette, used * 3, 3);
        ++used;
      }

      indices[i] = (byte)slot[colorByte];
    }

    return new() {
      Width = DisplayWidth,
      Height = DisplayHeight,
      Format = PixelFormat.Indexed8,
      PixelData = indices,
      Palette = palette,
      PaletteCount = used,
    };
  }

  /// <summary>Creates a Ram Brandt screen from a platform-independent <see cref="RawImage"/>.</summary>
  /// <remarks>
  /// Written as Graphics 7 with one palette for the whole picture: the display-list tables are left
  /// zeroed, which every reader takes as "no palette changes".
  /// </remarks>
  public static RamBrandtFile FromRawImage(RawImage image) => FromRawImage(image, ".rm0");

  /// <summary>Creates a Ram Brandt screen in the mode the extension names.</summary>
  /// <remarks>
  /// All five share one size and one layout, so the extension is the whole of the difference. Always
  /// writing Graphics 7 meant a file named <c>.rm2</c> held mode 7 bytes that its own reader, and
  /// every other, then took as mode 10.
  /// </remarks>
  public static RamBrandtFile FromRawImage(RawImage image, string extension) {
    ArgumentNullException.ThrowIfNull(image);

    var mode = RamBrandtReader.ModeFromExtension(extension ?? string.Empty);
    return mode == RamBrandtMode.Graphics7 || mode == RamBrandtMode.Graphics15
      ? _FromRawImagePlayfield(image, mode)
      : _FromRawImageGtiaNibble(image, mode);
  }

  /// <summary>Encodes the two-bit playfield modes, 7 and 15.</summary>
  private static RamBrandtFile _FromRawImagePlayfield(RawImage image, RamBrandtMode mode) {
    if (image.Width != DisplayWidth || image.Height != DisplayHeight)
      throw new ArgumentException($"Expected {DisplayWidth}x{DisplayHeight} but got {image.Width}x{image.Height}.", nameof(image));

    // Reduce to the four colours mode D can show, then express those as GTIA colour registers.
    // Reducing to Indexed4 instead asks for sixteen, and the twelve that do not fit were then read
    // as colour 0 — a picture of any variety came back as one register with three unused.
    var indexed = image.EnsureIndexedAtMost(Graphics7ColorCount);
    var palette = indexed.Palette ?? [];
    var gtia = Atari8BitGraphics.CreatePalette();

    var colors = new byte[ColorCount];
    for (var value = 0; value < Graphics7ColorCount && value < indexed.PaletteCount; ++value)
      colors[_ColorIndex(value)] = Atari8BitGraphics.FindNearestColorByte(gtia, palette[value * 3], palette[value * 3 + 1], palette[value * 3 + 2]);

    // Collapse the displayed image back to the stored grid, sampling one pixel a block. Graphics 7
    // stores 96 rows drawn twice each; Graphics 15 stores all 192.
    var rows = StoredRows(mode);
    var rowHeight = DisplayHeight / rows;
    var pixels = new byte[Atari8BitGraphics.Gr7Width * rows];
    for (var y = 0; y < rows; ++y)
    for (var x = 0; x < Atari8BitGraphics.Gr7Width; ++x) {
      var index = indexed.PixelData[y * rowHeight * DisplayWidth + x * 2];
      pixels[y * Atari8BitGraphics.Gr7Width + x] = (byte)(index < Graphics7ColorCount ? index : 0);
    }

    var bitmap = new byte[BitmapDataSize];
    Atari8BitGraphics.PackGr7(pixels, rows).CopyTo(bitmap, 0);

    return new() {
      Mode = mode,
      BitmapData = bitmap,
      Colors = colors,
      DisplayList = new byte[DisplayListSize],
    };
  }

  /// <summary>Stored pixels a row holds in the nibble modes, each drawn four screen pixels wide.</summary>
  private const int NibbleWidth = 80;

  /// <summary>Encodes the four-bit GTIA modes, 9, 10 and 11.</summary>
  /// <remarks>
  /// Mode 10 spends its nibble on a register outright. Modes 9 and 11 split a colour in two: the
  /// nibble carries one half for every pixel and the background register carries the other half for
  /// the whole screen, so the register is chosen first by trying all sixteen and keeping whichever
  /// leaves the least error.
  /// </remarks>
  private static RamBrandtFile _FromRawImageGtiaNibble(RawImage image, RamBrandtMode mode) {
    if (image.Width != DisplayWidth || image.Height != DisplayHeight)
      throw new ArgumentException($"Expected {DisplayWidth}x{DisplayHeight} but got {image.Width}x{image.Height}.", nameof(image));

    var gtia = Atari8BitGraphics.CreatePalette();
    var colors = new byte[ColorCount];
    var nibbles = new byte[NibbleWidth * DisplayHeight];

    if (mode == RamBrandtMode.Graphics10) {
      var indexed = image.EnsureIndexedAtMost(ColorCount);
      var palette = indexed.Palette ?? [];
      for (var i = 0; i < ColorCount && i < indexed.PaletteCount; ++i)
        colors[i] = Atari8BitGraphics.FindNearestColorByte(gtia, palette[i * 3], palette[i * 3 + 1], palette[i * 3 + 2]);

      for (var y = 0; y < DisplayHeight; ++y)
      for (var x = 0; x < NibbleWidth; ++x) {
        var index = indexed.PixelData[y * DisplayWidth + x * 4];
        nibbles[y * NibbleWidth + x] = (byte)(index < ColorCount ? index : 0);
      }
    } else {
      var bgra = PixelConverter.Convert(image, PixelFormat.Bgra32).PixelData;
      var luminanceNibble = mode == RamBrandtMode.Graphics9;

      var best = (byte)0;
      var bestError = long.MaxValue;
      for (var candidate = 0; candidate < 16; ++candidate) {
        // Mode 9 keeps the hue in the register and spends the nibble on luminance; 11 is the reverse.
        var register = (byte)(luminanceNibble ? candidate << 4 : candidate << 1);
        var error = _ChooseNibbles(bgra, gtia, register, luminanceNibble, null);
        if (error >= bestError)
          continue;

        bestError = error;
        best = register;
      }

      colors[8] = best;
      _ChooseNibbles(bgra, gtia, best, luminanceNibble, nibbles);
    }

    var bitmap = new byte[BitmapDataSize];
    for (var y = 0; y < DisplayHeight; ++y)
    for (var x = 0; x < NibbleWidth; ++x) {
      var value = nibbles[y * NibbleWidth + x] & 15;
      bitmap[y * BytesPerRow + (x >> 1)] |= (byte)((x & 1) == 0 ? value << 4 : value);
    }

    return new() {
      Mode = mode,
      BitmapData = bitmap,
      Colors = colors,
      DisplayList = new byte[DisplayListSize],
    };
  }

  /// <summary>
  /// Picks the closest nibble for every stored pixel under a given register, returning the total
  /// error and, when asked, the nibbles themselves.
  /// </summary>
  private static long _ChooseNibbles(byte[] bgra, byte[] gtia, byte register, bool luminanceNibble, byte[]? chosen) {
    Span<int> candidates = stackalloc int[16];
    for (var n = 0; n < 16; ++n)
      candidates[n] = luminanceNibble ? register | n : n == 0 ? register & 240 : register | (n << 4);

    var total = 0L;
    for (var y = 0; y < DisplayHeight; ++y)
    for (var x = 0; x < NibbleWidth; ++x) {
      var at = (y * DisplayWidth + x * 4) * 4;
      int blue = bgra[at], green = bgra[at + 1], red = bgra[at + 2];

      var pick = 0;
      var closest = int.MaxValue;
      for (var n = 0; n < 16; ++n) {
        var entry = candidates[n] * 3;
        int dr = gtia[entry] - red, dg = gtia[entry + 1] - green, db = gtia[entry + 2] - blue;
        var distance = dr * dr + dg * dg + db * db;
        if (distance >= closest)
          continue;

        closest = distance;
        pick = n;
      }

      total += closest;
      if (chosen != null)
        chosen[y * NibbleWidth + x] = (byte)pick;
    }

    return total;
  }
}
