using System;
using FileFormat.Core;

namespace FileFormat.InterlaceCharacterEditor;

/// <summary>
/// In-memory representation of an Interlace Character Editor picture (.irg, .ir2, .icn, .imn,
/// .ipc).
/// </summary>
/// <remarks>
/// These are character-mode pictures, not bitmaps: a 40x24 grid of character codes indexes into a
/// font, and the font is what holds the pixels. Two such screens are shown in alternation and the
/// eye averages them, which is where the extra colours come from. The two screens read the font at
/// bases 1024 bytes apart, so one font area serves both.
/// <para>
/// The character code's high byte comes from the scanline rather than the map, changing every 24
/// scanlines. That splits the screen into eight bands of three character rows, each band with its
/// own 256-entry slice of the font — and since a band holds only 120 cells, every cell can be
/// given a glyph of its own. That is what lets an arbitrary picture be encoded exactly instead of
/// being fitted to a shared character set.
/// </para>
/// </remarks>
public readonly record struct IceFile
  : IImageFormatReader<IceFile>, IImageToRawImage<IceFile>,
    IImageFromRawImage<IceFile>, IImageFormatWriter<IceFile> {

  static string IImageFormatMetadata<IceFile>.PrimaryExtension => ".irg";
  static string[] IImageFormatMetadata<IceFile>.FileExtensions => [".irg", ".ir2", ".icn", ".imn", ".ipc"];
  static IceFile IImageFormatReader<IceFile>.FromSpan(ReadOnlySpan<byte> data) => IceReader.FromSpan(data);
  static byte[] IImageFormatWriter<IceFile>.ToBytes(IceFile file) => IceWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<IceFile>.VideoModes => [
    new("Super IRG", [(IceLayout.DisplayWidth, IceLayout.DisplayHeight)], [16]),
    new("ICE MIN", [(IceLayout.DisplayWidth, IceLayout.DisplayHeight)], [64]),
  ];

  /// <summary>Which of the five picture formats this is.</summary>
  public IceMode Mode { get; init; }

  /// <summary>The colour bytes preceding the font area.</summary>
  public byte[] Header { get; init; }

  /// <summary>The font area, shared by both frames at bases 1024 bytes apart.</summary>
  public byte[] FontData { get; init; }

  /// <summary>The first frame's character map.</summary>
  public byte[] Characters1 { get; init; }

  /// <summary>The second frame's character map; the same content when the mode shares one.</summary>
  public byte[] Characters2 { get; init; }

  /// <summary>Offset of a frame's font base within <see cref="FontData"/>.</summary>
  private static int _FontBase(int frame) => frame * IceLayout.FontBaseStride;

  /// <summary>Reads the glyph byte a cell shows on a given scanline.</summary>
  private static byte _GlyphByte(byte[] font, int frame, int character, int y) {
    // Bit 7 of the code selects the inverse-video register set rather than a different glyph, so
    // it is masked out of the font index.
    var code = ((y / (IceLayout.RowsPerBank * 8)) << 8) + character;
    var offset = _FontBase(frame) + ((code & ~128) * IceLayout.GlyphSize) + (y & 7);

    return offset < font.Length ? font[offset] : (byte)0;
  }

  /// <summary>Renders one frame as an Atari colour byte per displayed pixel.</summary>
  private static byte[] _RenderFrame(IceFile file, byte[] characters, int frame, byte[] colors, IceFrameKind kind, byte? inverse = null) {
    var font = file.FontData ?? [];
    var result = new byte[IceLayout.DisplayWidth * IceLayout.DisplayHeight];

    for (var y = 0; y < IceLayout.DisplayHeight; ++y)
    for (var col = 0; col < IceLayout.Columns; ++col) {
      var index = (y >> 3) * IceLayout.Columns + col;
      var character = index < characters.Length ? characters[index] : 0;
      var glyph = _GlyphByte(font, frame, character, y);
      var target = y * IceLayout.DisplayWidth + (col << 3);

      if (kind == IceFrameKind.Graphics12) {
        // Two bits per pixel, each drawn two screen pixels wide. A character with bit 7 set draws
        // its highest value from the inverse register rather than the third playfield one.
        var highest = (character & 128) != 0 && inverse is { } other ? other : colors[3];
        for (var x = 0; x < 8; ++x) {
          var value = (glyph >> (~x & 6)) & 3;
          result[target + x] = value == 3 ? highest : colors[value];
        }

        continue;
      }

      // A nibble per pixel, each drawn four screen pixels wide.
      for (var x = 0; x < 8; ++x) {
        var value = (glyph >> (~x & 4)) & 15;
        result[target + x] = value < colors.Length ? colors[value] : (byte)0;
      }
    }

    return result;
  }

  public static RawImage ToRawImage(IceFile file) {
    var header = file.Header ?? [];
    var kind = IceLayout.SecondFrameKind(file.Mode);
    var first = _RenderFrame(file, file.Characters1 ?? [], 0, IceLayout.FirstFrameColors(file.Mode, header), IceFrameKind.Graphics12, IceLayout.InverseColor(file.Mode, header, 0));
    var second = _RenderFrame(file, file.Characters2 ?? [], 1, IceLayout.SecondFrameColors(file.Mode, header), kind, IceLayout.InverseColor(file.Mode, header, 1));

    var gtia = Atari8BitGraphics.CreatePalette();

    // Each distinct pair of frame colours averages to one displayed colour.
    var slot = new int[256 * 256];
    Array.Fill(slot, -1);

    var palette = new byte[256 * 3];
    var pixels = new byte[first.Length];
    var used = 0;
    for (var i = 0; i < first.Length; ++i) {
      var key = (first[i] << 8) | second[i];
      if (slot[key] < 0 && used < 256) {
        for (var channel = 0; channel < 3; ++channel)
          palette[used * 3 + channel] = (byte)((gtia[first[i] * 3 + channel] + gtia[second[i] * 3 + channel]) >> 1);

        slot[key] = used;
        ++used;
      }

      pixels[i] = (byte)Math.Max(slot[key], 0);
    }

    return new() {
      Width = IceLayout.DisplayWidth,
      Height = IceLayout.DisplayHeight,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = palette,
      PaletteCount = Math.Max(used, 1),
    };
  }

  public static IceFile FromRawImage(RawImage image) => FromRawImage(image, IceMode.SuperIrg);

  /// <summary>Encodes for the mode the extension names rather than always for Super IRG.</summary>
  public static IceFile FromRawImage(RawImage image, string extension)
    => FromRawImage(image, IceReader.ModeFromExtension(extension ?? string.Empty));

  /// <summary>Encodes a picture in a chosen one of the five formats.</summary>
  public static IceFile FromRawImage(RawImage image, IceMode mode) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != IceLayout.DisplayWidth || image.Height != IceLayout.DisplayHeight)
      throw new ArgumentException(
        $"Expected {IceLayout.DisplayWidth}x{IceLayout.DisplayHeight} but got {image.Width}x{image.Height}.", nameof(image));

    var bgra = PixelConverter.Convert(image, PixelFormat.Bgra32);
    var gtia = Atari8BitGraphics.CreatePalette();
    var header = IceEncoder.ChooseColors(mode, bgra.PixelData, gtia);

    var firstColors = IceLayout.FirstFrameColors(mode, header);
    var secondColors = IceLayout.SecondFrameColors(mode, header);
    var kind = IceLayout.SecondFrameKind(mode);

    var (firstValues, secondValues) = IceEncoder.ChooseValues(bgra.PixelData, gtia, firstColors, secondColors, kind);

    var font = new byte[IceLayout.FontSize];
    var characters1 = new byte[IceLayout.CharacterMapSize];
    var characters2 = new byte[IceLayout.CharacterMapSize];

    IceEncoder.WriteFrame(font, characters1, 0, firstValues, IceFrameKind.Graphics12);
    IceEncoder.WriteFrame(font, characters2, 1, secondValues, kind);

    return new() {
      Mode = mode,
      Header = header,
      FontData = font,
      Characters1 = characters1,
      Characters2 = IceLayout.SharesCharacterMap(mode) ? characters1 : characters2,
    };
  }
}
