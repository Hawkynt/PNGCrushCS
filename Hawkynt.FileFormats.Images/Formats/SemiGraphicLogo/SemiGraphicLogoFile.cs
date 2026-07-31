using System;
using FileFormat.Core;

namespace FileFormat.SemiGraphicLogo;

/// <summary>In-memory representation of a Semi-Graphic logos Editor screen (.sge).</summary>
/// <remarks>
/// A plain Atari 8-bit text screen — 960 character codes and nothing else, not even a header — so
/// the picture exists only once the machine's own character set is applied to it.
/// <para/>
/// The editor patches four characters of that set before drawing: two become solid halves so that a
/// logo can be built out of blocks the stock font has no shape for. Those four bytes are the whole
/// of what makes it a drawing program rather than a text editor.
/// </remarks>
public readonly record struct SemiGraphicLogoFile
  : IImageFormatReader<SemiGraphicLogoFile>, IImageToRawImage<SemiGraphicLogoFile>,
    IImageFromRawImage<SemiGraphicLogoFile>, IImageFormatWriter<SemiGraphicLogoFile> {

  /// <summary>Pixels across.</summary>
  public const int Width = 320;

  /// <summary>Rows.</summary>
  public const int Height = 192;

  /// <summary>Character cells across.</summary>
  public const int Columns = Width / 8;

  /// <summary>Total file size: one code per cell.</summary>
  public const int FileSize = Columns * (Height / 8);

  static string IImageFormatMetadata<SemiGraphicLogoFile>.PrimaryExtension => ".sge";
  static string[] IImageFormatMetadata<SemiGraphicLogoFile>.FileExtensions => [".sge"];
  static SemiGraphicLogoFile IImageFormatReader<SemiGraphicLogoFile>.FromSpan(ReadOnlySpan<byte> data)
    => SemiGraphicLogoReader.FromSpan(data);
  static byte[] IImageFormatWriter<SemiGraphicLogoFile>.ToBytes(SemiGraphicLogoFile file)
    => SemiGraphicLogoWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<SemiGraphicLogoFile>.VideoModes => [
    new("Atari 8-bit", [(Width, Height)], [2])
  ];

  /// <summary>The character codes.</summary>
  public byte[] Characters { get; init; }

  /// <summary>
  /// The character set the editor draws with: the machine's own, with four shapes replaced by
  /// solid halves so that a logo can be built out of blocks the stock set has not got.
  /// </summary>
  public static byte[] CreateFont() {
    var font = CharacterRoms.Atari8.ToArray();

    for (var i = 0; i < 4; ++i) {
      font[1004 + i] = font[728 + i] = 15;
      font[1000 + i] = font[732 + i] = 240;
    }

    return font;
  }

  public static RawImage ToRawImage(SemiGraphicLogoFile file) {
    var font = CreateFont();

    var frame = new byte[Width * Height];
    CharacterRoms.DecodeGraphics0(file.Characters ?? [], 0, Columns, font, frame, Width, Height);

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = Atari8BitGraphics.ApplyPalette(frame),
    };
  }

  /// <summary>Builds a screen from a picture, one character shape at a time.</summary>
  /// <remarks>
  /// The picture can only be made of the 128 shapes in the set, each usable inverted as well, and
  /// four of those were replaced with solid halves — which is what makes a logo possible at all.
  /// A picture drawn with them comes back exactly.
  /// </remarks>
  public static SemiGraphicLogoFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width < 1 || image.Height < 1)
      throw new ArgumentException("A picture needs at least one pixel.", nameof(image));

    var rgb = PixelConverter.Convert(image, PixelFormat.Rgb24);
    var wanted = new byte[Width * Height];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      var sourceX = image.Width == Width ? x : x * image.Width / Width;
      var sourceY = image.Height == Height ? y : y * image.Height / Height;
      var source = (sourceY * image.Width + sourceX) * 3;

      var luminance = rgb.PixelData[source] * 77 + rgb.PixelData[source + 1] * 150 + rgb.PixelData[source + 2] * 29;

      // A set bit shows the foreground, which in this mode is the lighter of the two.
      wanted[y * Width + x] = (byte)(luminance >= 128 * 256 ? 1 : 0);
    }

    return new() { Characters = CharacterRoms.MatchGlyphs(wanted, Columns, Height / 8, CreateFont(), 128) };
  }
}
