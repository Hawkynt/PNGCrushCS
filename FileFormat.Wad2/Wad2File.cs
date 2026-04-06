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
  static byte[] IImageFormatWriter<Wad2File>.ToBytes(Wad2File file) => Wad2Writer.ToBytes(file);

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
