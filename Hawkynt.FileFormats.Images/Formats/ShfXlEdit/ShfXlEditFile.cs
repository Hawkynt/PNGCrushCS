using System;
using FileFormat.Core;

namespace FileFormat.ShfXlEdit;

/// <summary>In-memory representation of an SHF-XL Edit picture (.shx).</summary>
/// <remarks>
/// A C64 FLI screen 144 pixels across with sprites over it, in two forms that share nothing but
/// their dimensions. The unpacked one is a copy of video memory, so the sprite that covers a cell
/// has to be worked out from where the machine put it. The packed one is a rearrangement the editor
/// made for its own convenience — three plain planes of bitmap, mask and colour — which is much
/// simpler to read and could not have been displayed without being taken apart again.
/// </remarks>
public readonly record struct ShfXlEditFile
  : IImageFormatReader<ShfXlEditFile>, IImageToRawImage<ShfXlEditFile> {

  /// <summary>Pixels across.</summary>
  public const int Width = 144;

  /// <summary>Rows.</summary>
  public const int Height = 168;

  /// <summary>Bytes one row occupies in the packed form.</summary>
  public const int PackedStride = Width / 8;

  /// <summary>Size of the form that is a copy of video memory.</summary>
  public const int RawFileSize = 15362;

  /// <summary>Size the packed form unpacks to.</summary>
  public const int UnpackedSize = 9168;

  /// <summary>Offset of the sprite colour in the packed form.</summary>
  public const int PackedSpriteColorOffset = 3025;

  /// <summary>Offset of the sprite mask in the packed form.</summary>
  public const int PackedMaskOffset = 3072;

  /// <summary>Offset of the colour map in the packed form.</summary>
  public const int PackedColorOffset = 6144;

  static string IImageFormatMetadata<ShfXlEditFile>.PrimaryExtension => ".shx";
  static string[] IImageFormatMetadata<ShfXlEditFile>.FileExtensions => [".shx"];
  static ShfXlEditFile IImageFormatReader<ShfXlEditFile>.FromSpan(ReadOnlySpan<byte> data)
    => ShfXlEditReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<ShfXlEditFile>.VideoModes => [
    new("SHF-XL", [(Width, Height)], [Commodore64Graphics.ColorCount])
  ];

  /// <summary>The picture, unpacked if it was packed.</summary>
  public byte[] Data { get; init; }

  /// <summary>Whether the file is a copy of video memory rather than the editor's own layout.</summary>
  public bool IsRaw { get; init; }

  public static RawImage ToRawImage(ShfXlEditFile file) {
    var data = file.Data ?? [];
    var pixels = new byte[Width * Height];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x)
      pixels[y * Width + x] = (byte)((file.IsRaw ? _RawPixel(data, x, y) : _PackedPixel(data, x, y)) & 15);

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = Commodore64Graphics.CreatePalette(),
      PaletteCount = Commodore64Graphics.ColorCount,
    };
  }

  private static int _RawPixel(ReadOnlySpan<byte> data, int x, int y) {
    var bit = ~x & 7;
    var column = x >> 3;
    var inLine = column / 3;

    // Fifteen sprites cover a line, reused down the screen; the correction accounts for the ones
    // the machine could not place contiguously.
    var sprite = ((y - 1) & 7) * 15 + inLine;
    if (sprite < 105)
      sprite += ((inLine + 1) >> 1) * 3;

    if (((_At(data, 8194 + (sprite << 6) + y % 21 * 3 + column % 3) >> bit) & 1) != 0)
      return data[1003];

    var offset = (y & ~7) * 5 + column;

    return _At(data, 13 + ((y & 7) << 10) + offset)
           >> (((_At(data, 8282 + (offset << 3) + (y & 7)) >> bit) & 1) << 2);
  }

  private static int _PackedPixel(ReadOnlySpan<byte> data, int x, int y) {
    var bit = ~x & 7;
    var offset = y * PackedStride + (x >> 3);

    if (((_At(data, offset) >> bit) & 1) != 0)
      return _At(data, PackedSpriteColorOffset);

    return _At(data, PackedColorOffset + offset)
           >> (((_At(data, PackedMaskOffset + offset) >> bit) & 1) << 2);
  }

  private static byte _At(ReadOnlySpan<byte> data, int offset)
    => offset >= 0 && offset < data.Length ? data[offset] : (byte)0;
}
