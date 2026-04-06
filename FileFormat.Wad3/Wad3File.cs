using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Wad3;

/// <summary>In-memory representation of a Half-Life WAD3 texture package.</summary>
[FormatMagicBytes([0x57, 0x41, 0x44, 0x33])]
public readonly record struct Wad3File : IImageFormatReader<Wad3File>, IImageToRawImage<Wad3File>, IImageFromRawImage<Wad3File>, IImageFormatWriter<Wad3File> {

  static string IImageFormatMetadata<Wad3File>.PrimaryExtension => ".wad";
  static string[] IImageFormatMetadata<Wad3File>.FileExtensions => [".wad"];
  static Wad3File IImageFormatReader<Wad3File>.FromSpan(ReadOnlySpan<byte> data) => Wad3Reader.FromSpan(data);
  static byte[] IImageFormatWriter<Wad3File>.ToBytes(Wad3File file) => Wad3Writer.ToBytes(file);
  /// <summary>Textures contained in this WAD3 file.</summary>
  public IReadOnlyList<Wad3Texture> Textures { get; init; }

  /// <summary>Converts the first texture of a WAD3 file to a <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(Wad3File file) {
    if (file.Textures.Count == 0)
      throw new ArgumentException("WAD3 file contains no textures.", nameof(file));

    var texture = file.Textures[0];
    return new RawImage {
      Width = texture.Width,
      Height = texture.Height,
      Format = PixelFormat.Indexed8,
      PixelData = texture.PixelData[..],
      Palette = texture.Palette[..],
      PaletteCount = 256
    };
  }

  /// <summary>Creates a single-texture WAD3 file from a <see cref="RawImage"/>. Must be Indexed8 with palette.</summary>
  public static Wad3File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"WAD3 requires Indexed8 pixel format, got {image.Format}.", nameof(image));
    if (image.Palette == null)
      throw new ArgumentException("WAD3 requires a palette.", nameof(image));

    return new Wad3File {
      Textures = [
        new Wad3Texture {
          Name = "texture",
          Width = image.Width,
          Height = image.Height,
          PixelData = image.PixelData[..],
          Palette = image.Palette[..]
        }
      ]
    };
  }
}
