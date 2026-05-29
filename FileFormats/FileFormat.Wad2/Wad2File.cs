using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Wad2;

/// <summary>In-memory representation of a Quake 1 WAD2 texture package.</summary>
[FormatMagicBytes([0x57, 0x41, 0x44, 0x32])]
public readonly record struct Wad2File : IImageFormatReader<Wad2File>, IImageToRawImage<Wad2File>, IImageFromRawImage<Wad2File>, IImageFormatWriter<Wad2File> {

  static string IImageFormatMetadata<Wad2File>.PrimaryExtension => ".wad";
  static string[] IImageFormatMetadata<Wad2File>.FileExtensions => [".wad"];
  static Wad2File IImageFormatReader<Wad2File>.FromSpan(ReadOnlySpan<byte> data) => Wad2Reader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<Wad2File>.Capabilities => FormatCapability.IndexedOnly;
  static IntegerRange[] IImageFormatMetadata<Wad2File>.AllowedPaletteRanges => [new IntegerRange(2, 256)];
  static FixedPalette[] IImageFormatMetadata<Wad2File>.FixedPalettes => _FixedPalettes;
  static byte[] IImageFormatWriter<Wad2File>.ToBytes(Wad2File file) => Wad2Writer.ToBytes(file);

  private static readonly FixedPalette[] _FixedPalettes = [
    new FixedPalette("Quake",
      0x000000, 0x0F0F0F, 0x1F1F1F, 0x2F2F2F, 0x3F3F3F, 0x4B4B4B, 0x5B5B5B, 0x6B6B6B,
      0x7B7B7B, 0x8B8B8B, 0x9B9B9B, 0xABABAB, 0xBBBBBB, 0xCBCBCB, 0xDBDBDB, 0xEBEBEB,
      0x0F0B07, 0x170F0B, 0x1F170B, 0x271B0F, 0x2F2313, 0x372B17, 0x3F2F17, 0x4B371B,
      0x53371B, 0x5B431F, 0x634B1F, 0x6B531F, 0x735723, 0x7B5F23, 0x836723, 0x8F6F23,
      0x0B0B0F, 0x13131B, 0x1B1B27, 0x272733, 0x3B2F4B, 0x4B354F, 0x5F3F5F, 0x73476F,
      0x873F73, 0x974B7B, 0xA75F87, 0xB77393, 0xCB8B9F, 0xDBA3AB, 0xEFC3BF, 0xFFE3CF,
      0x4F3B17, 0x5B431F, 0x6B4F27, 0x77572F, 0x876337, 0x936B3F, 0xA37747, 0xAF8353,
      0xBF8F5B, 0xCB9B67, 0xDBA773, 0xE7B37F, 0xF7BF8B, 0xFFCB97, 0xFFD7A3, 0xFFE3B3,
      0xA77B3B, 0x9F7337, 0x976B33, 0x8F632F, 0x875B2B, 0x7F5727, 0x774F23, 0x6F471F,
      0x67431B, 0x5F3B17, 0x573713, 0x4F2F0F, 0x47270B, 0x3F2307, 0x371B07, 0x2F1707,
      0xBBBBB3, 0xABA39B, 0x97877F, 0x83736B, 0x6F635B, 0x5F5347, 0x4F4337, 0x3F3327,
      0x33271B, 0x270F0B, 0x1F0707, 0x6F8B7B, 0x5F7B6B, 0x536B5F, 0x475F4F, 0x3F4B3F,
      0x33433B, 0x2B3733, 0x232B27, 0x171F1B, 0x0F1313, 0x070B0B, 0xFFF31B, 0xEFDF17,
      0xDBCB13, 0xCBB70F, 0xBBA70F, 0xAB970B, 0x9B8307, 0x8B7307, 0x7B6307, 0x6B5300,
      0x5B4700, 0x4B3700, 0x3B2B00, 0x2B1F00, 0x1B0F00, 0x0B0700, 0x00000F, 0x00000B,
      0x000007, 0x000003, 0x000300, 0x000B00, 0x001300, 0x001B00, 0x002300, 0x002B00,
      0x002F00, 0x003700, 0x003F00, 0x004B00, 0x005300, 0x005F00, 0x006700, 0x007300,
      0x00009F, 0x0000AF, 0x0000BB, 0x0000CB, 0x0F1FCB, 0x1F2FCB, 0x2F3FCB, 0x3F4FCB,
      0x4F5FCB, 0x5F6FCB, 0x6F7FCB, 0x7F8FCB, 0x8F9FCB, 0x9FAFCB, 0xAFBFCB, 0xBFCFCB,
      0x270000, 0x3B0000, 0x4F0700, 0x5F0700, 0x730F00, 0x870F00, 0x9B1700, 0xB31B00,
      0xC32307, 0xD7330B, 0xD75F2B, 0xDB8B53, 0xDFAB7B, 0xE3CBA3, 0xFFFFCF, 0xFFFFEF,
      0x002B00, 0x003F00, 0x005700, 0x006F00, 0x008300, 0x009B00, 0x00B300, 0x00CB00,
      0x07CB07, 0x1FCB1F, 0x37CB37, 0x4FCB4F, 0x67CB67, 0x7FCB7F, 0x97CB97, 0xAFCBAF,
      0xFF0000, 0xEF0000, 0xDF0000, 0xCF0000, 0xBF0000, 0xAF0000, 0x9F0000, 0x8F0000,
      0x7F0000, 0x6F0000, 0x5F0000, 0x4F0000, 0x3F0000, 0x2F0000, 0x1F0000, 0x0F0000,
      0x6F6F00, 0x7F7F00, 0x8F8F00, 0x9F9F00, 0xAFAF00, 0xBFBF00, 0xCFCF00, 0xDFDF00,
      0xEFEF00, 0xFFFF00, 0xFFEB47, 0xFFD787, 0xFFC3C7, 0xFFFFFF, 0x3F1B07, 0x53270B,
      0x67330F, 0x7B3F13, 0x8F4B17, 0xA3571B, 0xB7631F, 0xCB6F23, 0xCB7F2F, 0xCB8F3B,
      0xCB9F47, 0xCBAF53, 0xCBBF63, 0xCBCB73, 0xB7B767, 0xA3A35B, 0x8F8F4F, 0x7B7B43,
      0x676737, 0x53532B, 0x3F3F1F, 0x2B2B13, 0xEFA7A7, 0xDF8B8B, 0xCB7373, 0xBB5B5B,
      0xAB4747, 0x9B3737, 0x8B2727, 0x7B1B1B, 0x6B0F0F, 0x5B0707, 0x4B0000, 0x3B0000,
      0x2B0000, 0x1B0000, 0xFFC700, 0xFF8F00, 0xFF5B00, 0xFF2300, 0xC73F00, 0x9B3300)
  ];

  /// <summary>Textures contained in this WAD2 file.</summary>
  public IReadOnlyList<Wad2Texture> Textures { get; init; }

  /// <summary>The default 256-color Quake palette (grayscale ramp for format implementation).</summary>
  public static byte[] DefaultPalette { get; } = _BuildDefaultPalette();

  private static byte[] _BuildDefaultPalette() {
    var palette = new byte[768];
    for (var i = 0; i < 256; ++i) {
      palette[i * 3] = (byte)i;
      palette[i * 3 + 1] = (byte)i;
      palette[i * 3 + 2] = (byte)i;
    }
    return palette;
  }

  /// <summary>Converts the first texture of a WAD2 file to a <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(Wad2File file) {
    if (file.Textures.Count == 0)
      throw new ArgumentException("WAD2 file contains no textures.", nameof(file));

    var texture = file.Textures[0];
    return new RawImage {
      Width = texture.Width,
      Height = texture.Height,
      Format = PixelFormat.Indexed8,
      PixelData = texture.PixelData[..],
      Palette = DefaultPalette[..],
      PaletteCount = 256
    };
  }

  /// <summary>Creates a single-texture WAD2 file from a <see cref="RawImage"/>. Must be Indexed8.</summary>
  public static Wad2File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"WAD2 requires Indexed8 pixel format, got {image.Format}.", nameof(image));

    var w = image.Width;
    var h = image.Height;

    return new Wad2File {
      Textures = [
        new Wad2Texture {
          Name = "texture",
          Width = w,
          Height = h,
          PixelData = image.PixelData[..],
          MipMaps = [
            new byte[(w / 2) * (h / 2)],
            new byte[(w / 4) * (h / 4)],
            new byte[(w / 8) * (h / 8)]
          ]
        }
      ]
    };
  }
}
