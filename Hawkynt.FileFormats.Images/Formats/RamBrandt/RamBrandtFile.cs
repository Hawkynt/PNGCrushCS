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
  public static RamBrandtFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != DisplayWidth || image.Height != DisplayHeight)
      throw new ArgumentException($"Expected {DisplayWidth}x{DisplayHeight} but got {image.Width}x{image.Height}.", nameof(image));

    // Reduce to the four colours mode D can show, then express those as GTIA colour registers.
    var indexed = image.EnsureFormat(PixelFormat.Indexed4);
    var palette = indexed.Palette ?? [];
    var gtia = Atari8BitGraphics.CreatePalette();

    var colors = new byte[ColorCount];
    for (var value = 0; value < Graphics7ColorCount && value < indexed.PaletteCount; ++value)
      colors[_ColorIndex(value)] = Atari8BitGraphics.FindNearestColorByte(gtia, palette[value * 3], palette[value * 3 + 1], palette[value * 3 + 2]);

    // Collapse the displayed image back to the stored 160x96 grid, sampling each 2x2 block once.
    var rows = StoredRows(RamBrandtMode.Graphics7);
    var pixels = new byte[Atari8BitGraphics.Gr7Width * rows];
    for (var y = 0; y < rows; ++y)
    for (var x = 0; x < Atari8BitGraphics.Gr7Width; ++x) {
      var source = y * 2 * DisplayWidth + x * 2;
      var packed = indexed.PixelData[source >> 1];
      var index = (source & 1) == 0 ? (packed >> 4) & 0x0F : packed & 0x0F;
      pixels[y * Atari8BitGraphics.Gr7Width + x] = (byte)(index < Graphics7ColorCount ? index : 0);
    }

    var bitmap = new byte[BitmapDataSize];
    Atari8BitGraphics.PackGr7(pixels, rows).CopyTo(bitmap, 0);

    return new() {
      Mode = RamBrandtMode.Graphics7,
      BitmapData = bitmap,
      Colors = colors,
      DisplayList = new byte[DisplayListSize],
    };
  }
}
