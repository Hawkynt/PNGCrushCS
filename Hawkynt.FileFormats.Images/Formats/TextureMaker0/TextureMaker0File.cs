using System;
using FileFormat.Core;

namespace FileFormat.TextureMaker0;

/// <summary>In-memory representation of an Atari 8-bit Texture Maker0 (.tx0) texture.</summary>
/// <remarks>
/// A fixed 257-byte file: a 16x16 grid of luminance values 0..15, one per byte, then a single
/// GTIA colour byte naming the hue. Viewers show it at 64x64 with every texel drawn as a 4x4
/// block. As in the Graphics 9 modes the stored values carry luminance only, so the trailing
/// colour byte is what decides the hue the whole texture appears in.
/// </remarks>
public readonly record struct TextureMaker0File
  : IImageFormatReader<TextureMaker0File>, IImageToRawImage<TextureMaker0File>,
    IImageFromRawImage<TextureMaker0File>, IImageFormatWriter<TextureMaker0File> {

  /// <summary>Texels across and down.</summary>
  public const int TextureSize = 16;

  /// <summary>How many screen pixels each texel occupies per axis.</summary>
  public const int TexelScale = 4;

  /// <summary>Displayed width and height.</summary>
  public const int DisplaySize = TextureSize * TexelScale;

  /// <summary>Size of the texel block.</summary>
  public const int TexelDataSize = TextureSize * TextureSize;

  /// <summary>Offset of the trailing colour byte.</summary>
  public const int ColorOffset = TexelDataSize;

  /// <summary>Total file size.</summary>
  public const int FileSize = ColorOffset + 1;

  /// <summary>Luminance levels a texel can take.</summary>
  public const int ColorCount = 16;

  static string IImageFormatMetadata<TextureMaker0File>.PrimaryExtension => ".tx0";
  static string[] IImageFormatMetadata<TextureMaker0File>.FileExtensions => [".tx0"];
  static TextureMaker0File IImageFormatReader<TextureMaker0File>.FromSpan(ReadOnlySpan<byte> data)
    => TextureMaker0Reader.FromSpan(data);
  static byte[] IImageFormatWriter<TextureMaker0File>.ToBytes(TextureMaker0File file)
    => TextureMaker0Writer.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<TextureMaker0File>.VideoModes => [
    new("Texture", [(DisplaySize, DisplaySize)], [ColorCount])
  ];

  /// <summary>One luminance value per texel.</summary>
  public byte[] TexelData { get; init; }

  /// <summary>GTIA colour byte naming the hue; only its high nibble is meaningful.</summary>
  public byte Color { get; init; }

  public static RawImage ToRawImage(TextureMaker0File file) {
    var gtia = Atari8BitGraphics.CreatePalette();
    var hue = file.Color & 0xF0;

    var palette = new byte[ColorCount * 3];
    for (var level = 0; level < ColorCount; ++level)
      Array.Copy(gtia, (hue | level) * 3, palette, level * 3, 3);

    var pixels = new byte[DisplaySize * DisplaySize];
    for (var y = 0; y < DisplaySize; ++y)
    for (var x = 0; x < DisplaySize; ++x)
      pixels[y * DisplaySize + x] = (byte)(file.TexelData[(y / TexelScale) * TextureSize + x / TexelScale] & 15);

    return new() {
      Width = DisplaySize,
      Height = DisplaySize,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = palette,
      PaletteCount = ColorCount,
    };
  }

  public static TextureMaker0File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != DisplaySize || image.Height != DisplaySize)
      throw new ArgumentException($"Expected {DisplaySize}x{DisplaySize} but got {image.Width}x{image.Height}.", nameof(image));

    // Luminance only; the hue lives in the trailing colour byte.
    var grey = PixelConverter.Convert(image, PixelFormat.Gray8);
    var texels = new byte[TexelDataSize];
    for (var y = 0; y < TextureSize; ++y)
    for (var x = 0; x < TextureSize; ++x) {
      var source = (y * TexelScale) * DisplaySize + x * TexelScale;
      texels[y * TextureSize + x] = (byte)(grey.PixelData[source] * (ColorCount - 1) / 255);
    }

    return new() { TexelData = texels, Color = 0 };
  }
}
