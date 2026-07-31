using System;
using FileFormat.Core;

namespace FileFormat.ZxPaintyOne;

/// <summary>In-memory representation of a ZXpaintyONE picture (.zp1).</summary>
/// <remarks>
/// A ZX81 screen written as hexadecimal text, two characters to a character code and nothing else —
/// no header, no separators, no line breaks. It is a screen dump made printable, the same idea as
/// the bulletin-board formats but without their alphabet: plain hexadecimal was enough because
/// nothing was expected to carry it but a file.
/// </remarks>
public readonly record struct ZxPaintyOneFile
  : IImageFormatReader<ZxPaintyOneFile>, IImageToRawImage<ZxPaintyOneFile>,
    IImageFromRawImage<ZxPaintyOneFile>, IImageFormatWriter<ZxPaintyOneFile> {

  static string IImageFormatMetadata<ZxPaintyOneFile>.PrimaryExtension => ".zp1";
  static string[] IImageFormatMetadata<ZxPaintyOneFile>.FileExtensions => [".zp1"];
  static ZxPaintyOneFile IImageFormatReader<ZxPaintyOneFile>.FromSpan(ReadOnlySpan<byte> data)
    => ZxPaintyOneReader.FromSpan(data);
  static byte[] IImageFormatWriter<ZxPaintyOneFile>.ToBytes(ZxPaintyOneFile file)
    => ZxPaintyOneWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<ZxPaintyOneFile>.VideoModes => [
    new("ZX81", [(Zx81Graphics.Width, Zx81Graphics.Height)], [2])
  ];

  /// <summary>The screen's character codes.</summary>
  public byte[] Screen { get; init; }

  public static RawImage ToRawImage(ZxPaintyOneFile file) => new() {
    Width = Zx81Graphics.Width,
    Height = Zx81Graphics.Height,
    Format = PixelFormat.Indexed8,
    PixelData = Zx81Graphics.Decode(file.Screen ?? []),
    Palette = Zx81Graphics.CreatePalette(),
    PaletteCount = 2,
  };

  /// <summary>Builds a screen from a picture, one character shape at a time.</summary>
  /// <remarks>
  /// The machine has no bitmap: a cell can only be one of sixty-four shapes, or one of them
  /// inverted. A picture drawn with those shapes comes back exactly; anything else comes back as
  /// the nearest arrangement of them, which for a photograph is not close and for a diagram often
  /// is.
  /// </remarks>
  public static ZxPaintyOneFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width < 1 || image.Height < 1)
      throw new ArgumentException("A picture needs at least one pixel.", nameof(image));

    var rgb = PixelConverter.Convert(image, PixelFormat.Rgb24);
    var wanted = new byte[Zx81Graphics.Width * Zx81Graphics.Height];

    for (var y = 0; y < Zx81Graphics.Height; ++y)
    for (var x = 0; x < Zx81Graphics.Width; ++x) {
      var sourceX = image.Width == Zx81Graphics.Width ? x : x * image.Width / Zx81Graphics.Width;
      var sourceY = image.Height == Zx81Graphics.Height ? y : y * image.Height / Zx81Graphics.Height;
      var source = (sourceY * image.Width + sourceX) * 3;

      var luminance = rgb.PixelData[source] * 77 + rgb.PixelData[source + 1] * 150 + rgb.PixelData[source + 2] * 29;

      // A set bit in an uninverted glyph shows ink, so what the matcher is asked for is the dark
      // half of the picture rather than the light one.
      wanted[y * Zx81Graphics.Width + x] = (byte)(luminance < 128 * 256 ? 1 : 0);
    }

    return new() {
      Screen = CharacterRoms.MatchGlyphs(wanted, Zx81Graphics.Columns, Zx81Graphics.Rows, CharacterRoms.Zx81, 64),
    };
  }
}
