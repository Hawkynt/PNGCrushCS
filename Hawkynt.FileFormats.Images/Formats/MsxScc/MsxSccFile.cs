using System;
using FileFormat.Core;

namespace FileFormat.MsxScc;

/// <summary>In-memory representation of an MSX2+ Screen 12 picture (.scc).</summary>
/// <remarks>
/// A YJK screen: every byte carries five bits of luma and three of one chroma component, and a
/// group of four pixels pools its twelve chroma bits into two values they all share. The machine
/// therefore holds far more brightness detail than colour detail, which is exactly the trade a
/// television makes, and is why photographs on an MSX2+ look better than its sixteen-colour modes
/// would suggest.
/// <para/>
/// The file is a BSAVE image of the video memory, so a picture that filled it exactly carries the
/// sprite tables after the screen, and those are drawn on top. A shorter file may instead be
/// packed, which a different leading byte announces.
/// </remarks>
public readonly record struct MsxSccFile
  : IImageFormatReader<MsxSccFile>, IImageToRawImage<MsxSccFile>,
    IImageFromRawImage<MsxSccFile>, IImageFormatWriter<MsxSccFile> {

  /// <summary>Pixels across.</summary>
  public const int Width = 256;

  /// <summary>Bytes before the screen: the BSAVE header.</summary>
  public const int ScreenOffset = 7;

  /// <summary>The length a file has when it carries the sprite tables as well.</summary>
  public const int WithSpritesSize = 64167;

  /// <summary>Where the sprite attributes sit in such a file.</summary>
  public const int SpriteAttributesOffset = 64007;

  /// <summary>Where the sprite patterns sit.</summary>
  public const int SpritePatternsOffset = 61447;

  /// <summary>Where the sprite palette sits.</summary>
  public const int SpritePaletteOffset = 64135;

  static string IImageFormatMetadata<MsxSccFile>.PrimaryExtension => ".scc";
  static string[] IImageFormatMetadata<MsxSccFile>.FileExtensions => [".scc", ".yjk"];
  static MsxSccFile IImageFormatReader<MsxSccFile>.FromSpan(ReadOnlySpan<byte> data)
    => MsxSccReader.FromSpan(data);
  static byte[] IImageFormatWriter<MsxSccFile>.ToBytes(MsxSccFile file) => MsxSccWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<MsxSccFile>.VideoModes => [
    new("MSX2+ Screen 12", [(Width, new(192, 212))], [19268])
  ];

  /// <summary>The video memory, unpacked if it was not stored as it stands.</summary>
  public byte[] Screen { get; init; }

  /// <summary>Rows: 192 for a file that stores only that much, 212 otherwise.</summary>
  public int Height { get; init; }

  /// <summary>The sprite tables and their palette, or empty where the file carries none.</summary>
  public byte[] Sprites { get; init; }

  public static RawImage ToRawImage(MsxSccFile file) {
    var screen = file.Screen ?? [];
    var rgb = new byte[Width * file.Height * 3];

    for (var y = 0; y < file.Height; ++y) {
      var offset = ScreenOffset + (y << 8);
      if (offset + Width > screen.Length)
        break;

      MsxGraphics.DecodeYjkRow(screen.AsSpan(offset, Width), Width, false, [], rgb.AsSpan(y * Width * 3, Width * 3));
    }

    var sprites = file.Sprites ?? [];
    if (sprites.Length == 0)
      return new() { Width = Width, Height = file.Height, Format = PixelFormat.Rgb24, PixelData = rgb };

    // Sprites are drawn over the picture and carry a palette of their own — the only place a
    // Screen 12 picture has one at all, its own pixels naming colours directly.
    var drawn = new byte[Width * file.Height];
    Array.Fill(drawn, (byte)255);
    MsxGraphics.OverlaySprites(
      sprites, SpriteAttributesOffset, SpritePatternsOffset, 12, drawn, Width, file.Height);

    var palette = MsxGraphics.PaletteToRgb(sprites.AsSpan(SpritePaletteOffset), 16);
    for (var i = 0; i < drawn.Length; ++i) {
      if (drawn[i] == 255)
        continue;

      var entry = drawn[i] * 3;
      rgb[i * 3] = palette[entry];
      rgb[i * 3 + 1] = palette[entry + 1];
      rgb[i * 3 + 2] = palette[entry + 2];
    }

    return new() { Width = Width, Height = file.Height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  /// <summary>Rows a whole video memory holds.</summary>
  public const int StoredRows = 212;

  /// <summary>Builds a picture from an image, scaled to the machine's screen and coded as YJK.</summary>
  /// <remarks>
  /// YJK keeps a luma for every pixel but only one chroma pair for every four, so a picture put
  /// through it comes back with its edges intact and its colour smeared four pixels wide. That is
  /// not a defect of the encoding but what the machine's memory holds, and it is why photographs
  /// suit this mode and flat-coloured drawings do not.
  /// </remarks>
  public static MsxSccFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width < 1 || image.Height < 1)
      throw new ArgumentException("A picture needs at least one pixel.", nameof(image));

    var rgb = PixelConverter.Convert(image, PixelFormat.Rgb24);
    var screen = new byte[54279];
    var row = new byte[Width * 3];

    for (var y = 0; y < StoredRows; ++y) {
      var sourceY = image.Height == StoredRows ? y : y * image.Height / StoredRows;

      for (var x = 0; x < Width; ++x) {
        var sourceX = image.Width == Width ? x : x * image.Width / Width;
        var source = (sourceY * image.Width + sourceX) * 3;
        row[x * 3] = rgb.PixelData[source];
        row[x * 3 + 1] = rgb.PixelData[source + 1];
        row[x * 3 + 2] = rgb.PixelData[source + 2];
      }

      MsxGraphics.EncodeYjkRow(row, Width, false, screen.AsSpan(ScreenOffset + (y << 8), Width));
    }

    return new() { Screen = screen, Height = StoredRows, Sprites = [] };
  }
}
