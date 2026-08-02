using System;
using System.IO;
using FileFormat.Core;
using FileFormat.TextMode;

namespace FileFormat.MadStudio;

/// <summary>In-memory representation of a Mad Studio character screen (.an2, .an4, .an5, .gr1, .gr2).</summary>
/// <remarks>
/// These are character screens, not bitmaps: the file holds a grid of character codes and, in
/// every mode but the two-colour one, five colour registers. The glyphs live in the machine's
/// character ROM and are not part of the file, so a picture can only be expressed as whichever
/// characters happen to look most like it. The encoder does exactly that, trying every code in
/// every cell.
/// </remarks>
public readonly record struct MadStudioFile
  : IImageFormatReader<MadStudioFile>, IImageToRawImage<MadStudioFile>,
    IImageFromRawImage<MadStudioFile>, IImageFormatWriter<MadStudioFile> {

  /// <summary>The two colours ANTIC 2 draws with, as Atari colour bytes.</summary>
  private static readonly byte[] _Antic2Colors = [0, 14];

  static string IImageFormatMetadata<MadStudioFile>.PrimaryExtension => ".an4";
  static string[] IImageFormatMetadata<MadStudioFile>.FileExtensions => [".an4", ".an2", ".an5", ".gr1", ".gr2"];
  static MadStudioFile IImageFormatReader<MadStudioFile>.FromSpan(ReadOnlySpan<byte> data) => MadStudioReader.FromSpan(data);

  /// <summary>
  /// Reads a named file, the extension being what its reader needs.
  /// </summary>
  /// <remarks>
  /// The reader takes the extension into account and only the by-bytes entry was wired up here,
  /// so the registry could never reach it: whatever the extension would have settled was decided
  /// by a default instead. Ten formats carried this, each one otherwise found only when a sample
  /// happened to expose it.
  /// </remarks>
  static MadStudioFile IImageFormatReader<MadStudioFile>.FromFile(FileInfo file) => MadStudioReader.FromFile(file);
  static byte[] IImageFormatWriter<MadStudioFile>.ToBytes(MadStudioFile file) => MadStudioWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<MadStudioFile>.VideoModes => [
    new("ANTIC 4", [(MadStudioLayout.DisplayWidth, MadStudioLayout.DisplayHeight)], [MadStudioLayout.ColorCount]),
    new("ANTIC 2", [(MadStudioLayout.DisplayWidth, MadStudioLayout.DisplayHeight)], [2]),
  ];

  /// <summary>Which character mode the screen is in.</summary>
  public MadStudioMode Mode { get; init; }

  /// <summary>The grid of character codes.</summary>
  public byte[] Characters { get; init; }

  /// <summary>The colour registers, background first; empty in the two-colour mode.</summary>
  public byte[] Colors { get; init; }

  /// <summary>The character ROM the modes draw from, one byte per glyph row.</summary>
  internal static byte[] Font => BitmapFontEmbedded.AtariAtascii8x8.GlyphData;

  /// <summary>The Atari colour byte each register index draws.</summary>
  private static byte[] _ColorBytes(MadStudioFile file) {
    if (file.Mode == MadStudioMode.Antic2)
      return _Antic2Colors;

    var colors = new byte[MadStudioLayout.ColorCount];
    var stored = file.Colors ?? [];
    for (var i = 0; i < colors.Length; ++i)
      colors[i] = (byte)(i < stored.Length ? stored[i] & 254 : 0);

    return colors;
  }

  public static RawImage ToRawImage(MadStudioFile file) {
    var mode = file.Mode;
    var colors = _ColorBytes(file);
    var font = Font;
    var characters = file.Characters ?? [];
    var columns = MadStudioLayout.ColumnsFor(mode);
    var cellWidth = MadStudioLayout.CellWidthFor(mode);
    var cellHeight = MadStudioLayout.CellHeightFor(mode);

    var gtia = Atari8BitGraphics.CreatePalette();
    var palette = new byte[colors.Length * 3];
    for (var i = 0; i < colors.Length; ++i)
      Array.Copy(gtia, colors[i] * 3, palette, i * 3, 3);

    var pixels = new byte[MadStudioLayout.DisplayWidth * MadStudioLayout.DisplayHeight];
    for (var y = 0; y < MadStudioLayout.DisplayHeight; ++y)
    for (var x = 0; x < MadStudioLayout.DisplayWidth; ++x) {
      var cell = (y / cellHeight) * columns + (x / cellWidth);
      var character = cell < characters.Length ? characters[cell] : 0;
      var value = MadStudioLayout.PixelAt(mode, font, character, x % cellWidth, y % cellHeight);
      pixels[y * MadStudioLayout.DisplayWidth + x] = (byte)MadStudioLayout.RegisterFor(mode, character, value);
    }

    return new() {
      Width = MadStudioLayout.DisplayWidth,
      Height = MadStudioLayout.DisplayHeight,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = palette,
      PaletteCount = colors.Length,
    };
  }

  public static MadStudioFile FromRawImage(RawImage image) => FromRawImage(image, MadStudioMode.Antic4);

  /// <summary>Encodes for the mode the extension names rather than always for ANTIC 4.</summary>
  public static MadStudioFile FromRawImage(RawImage image, string extension)
    => FromRawImage(image, MadStudioLayout.ModeFromExtension(extension ?? string.Empty));

  /// <summary>Encodes a picture in a chosen one of the five character modes.</summary>
  public static MadStudioFile FromRawImage(RawImage image, MadStudioMode mode) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != MadStudioLayout.DisplayWidth || image.Height != MadStudioLayout.DisplayHeight)
      throw new ArgumentException(
        $"Expected {MadStudioLayout.DisplayWidth}x{MadStudioLayout.DisplayHeight} but got {image.Width}x{image.Height}.", nameof(image));

    var bgra = PixelConverter.Convert(image, PixelFormat.Bgra32);
    var gtia = Atari8BitGraphics.CreatePalette();
    var colors = MadStudioEncoder.ChooseColors(mode, bgra.PixelData, gtia, _Antic2Colors);

    return new() {
      Mode = mode,
      Characters = MadStudioEncoder.ChooseCharacters(mode, bgra.PixelData, gtia, colors, Font),
      Colors = mode == MadStudioMode.Antic2 ? [] : colors,
    };
  }
}
