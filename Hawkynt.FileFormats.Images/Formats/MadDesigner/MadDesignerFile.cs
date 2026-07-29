using System;
using FileFormat.Core;

namespace FileFormat.MadDesigner;

/// <summary>In-memory representation of a Mad Designer picture (.mbg) for the Atari 8-bit.</summary>
/// <remarks>
/// A bare 512x256 bitmap at one bit per pixel and nothing else — no header, no palette, no
/// terminator. The two colours are not stored because they cannot vary: the program draws in
/// GTIA colour 14 on colour 0, so the file is exactly the 16384 bytes of the bitmap and its length
/// is the only thing that identifies it.
/// </remarks>
public readonly record struct MadDesignerFile
  : IImageFormatReader<MadDesignerFile>, IImageToRawImage<MadDesignerFile>,
    IImageFromRawImage<MadDesignerFile>, IImageFormatWriter<MadDesignerFile> {

  /// <summary>Picture width.</summary>
  public const int Width = 512;

  /// <summary>Picture height.</summary>
  public const int Height = 256;

  /// <summary>Bytes per row: eight pixels each.</summary>
  public const int BytesPerRow = Width / 8;

  /// <summary>Total file size, which is the bitmap and nothing else.</summary>
  public const int FileSize = BytesPerRow * Height;

  /// <summary>GTIA colour the background uses.</summary>
  public const byte BackgroundColor = 0;

  /// <summary>GTIA colour the ink uses.</summary>
  public const byte InkColor = 14;

  static string IImageFormatMetadata<MadDesignerFile>.PrimaryExtension => ".mbg";
  static string[] IImageFormatMetadata<MadDesignerFile>.FileExtensions => [".mbg"];
  static MadDesignerFile IImageFormatReader<MadDesignerFile>.FromSpan(ReadOnlySpan<byte> data) => MadDesignerReader.FromSpan(data);
  static byte[] IImageFormatWriter<MadDesignerFile>.ToBytes(MadDesignerFile file) => MadDesignerWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<MadDesignerFile>.VideoModes => [
    new("Mad Designer", [(Width, Height)], [2])
  ];

  /// <summary>The bitmap, one bit per pixel, most significant bit leftmost.</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>The two fixed colours as RGB triplets.</summary>
  internal static byte[] PaletteRgb() {
    var gtia = Atari8BitGraphics.Palette;
    var palette = new byte[6];
    gtia.Slice(BackgroundColor * 3, 3).CopyTo(palette);
    gtia.Slice(InkColor * 3, 3).CopyTo(palette.AsSpan(3));

    return palette;
  }

  public static RawImage ToRawImage(MadDesignerFile file) {
    var data = file.BitmapData ?? [];
    var pixels = new byte[Width * Height];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      var index = y * BytesPerRow + (x >> 3);
      var b = index < data.Length ? data[index] : 0;
      pixels[y * Width + x] = (byte)((b >> (~x & 7)) & 1);
    }

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = PaletteRgb(),
      PaletteCount = 2,
    };
  }

  public static MadDesignerFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != Width || image.Height != Height)
      throw new ArgumentException($"Expected {Width}x{Height} but got {image.Width}x{image.Height}.", nameof(image));

    var bgra = PixelConverter.Convert(image, PixelFormat.Bgra32);
    var data = new byte[FileSize];

    // Only two colours exist and neither is stored, so a pixel is ink when it is nearer the ink
    // colour than the background — which for black on near-white is simply "bright enough".
    var palette = PaletteRgb();
    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      var pixel = (y * Width + x) * 4;
      if (_IsInk(bgra.PixelData, pixel, palette))
        data[y * BytesPerRow + (x >> 3)] |= (byte)(0x80 >> (x & 7));
    }

    return new() { BitmapData = data };
  }

  private static bool _IsInk(ReadOnlySpan<byte> bgra, int pixel, ReadOnlySpan<byte> palette) {
    int red = bgra[pixel + 2], green = bgra[pixel + 1], blue = bgra[pixel];
    var toBackground = _Distance(palette, 0, red, green, blue);
    var toInk = _Distance(palette, 1, red, green, blue);

    return toInk < toBackground;
  }

  private static int _Distance(ReadOnlySpan<byte> palette, int entry, int red, int green, int blue) {
    int dr = palette[entry * 3] - red, dg = palette[entry * 3 + 1] - green, db = palette[entry * 3 + 2] - blue;

    return dr * dr + dg * dg + db * db;
  }
}
