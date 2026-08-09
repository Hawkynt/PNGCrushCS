using System;
using FileFormat.Core;

namespace FileFormat.HalfLifeModel;

/// <summary>In-memory representation of the skins in a Half-Life model (.mdl).</summary>
/// <remarks>
/// A model is not a picture, but it carries its skins as ordinary paletted rasters and that is what
/// XnView reads it for. The file opens with <c>IDST</c> and a version of ten. The published
/// <c>studiohdr_t</c> puts the texture count at 0xB4, with the table's offset at 0xB8 and the pixel
/// data's at 0xBC; XnView's reader appeared to read them at 0xAC, and the difference was settled by
/// construction — the converter seeks 0xAC bytes on from where it already stands, having read the
/// eight bytes of identifier and version, which lands on 0xB4. A model with the fields at 0xAC is
/// refused, and one with them at 0xB4 is read. The published structure was right.
/// <para/>
/// Each entry in the texture table is eighty bytes: sixty-four of name, then flags, width, height and
/// the offset of that skin's pixels. The pixels are one byte a pixel, top row first, and a palette of
/// 256 red-green-blue triples follows them immediately.
/// <para/>
/// A model may hold several skins. The converter counts them and draws the last, and this reads the
/// last as well so that the two agree; the whole table is kept so a caller that wants another can ask.
/// </remarks>
public readonly record struct HalfLifeModelFile : IImageFormatReader<HalfLifeModelFile>, IImageToRawImage<HalfLifeModelFile> {

  /// <summary>The four characters a model opens with.</summary>
  public static ReadOnlySpan<byte> Signature => "IDST"u8;

  /// <summary>The only version that carries skins this way.</summary>
  public const int Version = 10;

  /// <summary>Where the texture count stands, with the table offset and the data offset behind it.</summary>
  public const int TextureCountOffset = 0xB4, TextureIndexOffset = 0xB8, TextureDataOffset = 0xBC;

  /// <summary>How long one entry in the texture table is.</summary>
  public const int TextureEntrySize = 80;

  /// <summary>How much of an entry is the skin's name.</summary>
  public const int TextureNameLength = 64;

  /// <summary>How many colours follow a skin's pixels.</summary>
  public const int PaletteEntries = 256;

  /// <summary>The smallest file that can carry the three fields the reader needs.</summary>
  public const int MinFileSize = TextureDataOffset + 4;

  static string IImageFormatMetadata<HalfLifeModelFile>.PrimaryExtension => ".mdl";
  static string[] IImageFormatMetadata<HalfLifeModelFile>.FileExtensions => [".mdl"];
  static HalfLifeModelFile IImageFormatReader<HalfLifeModelFile>.FromSpan(ReadOnlySpan<byte> data)
    => HalfLifeModelReader.FromSpan(data);

  static VideoMode[] IImageFormatMetadata<HalfLifeModelFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [256])
  ];

  static bool? IImageFormatMetadata<HalfLifeModelFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 8)
      return null;

    return header[..4].SequenceEqual(Signature)
           && (header[4] | (header[5] << 8) | (header[6] << 16) | (header[7] << 24)) == Version;
  }

  /// <summary>Pixels across in the skin this holds.</summary>
  public int Width { get; init; }

  /// <summary>Rows in the skin this holds.</summary>
  public int Height { get; init; }

  /// <summary>How many skins the model carries.</summary>
  public int SkinCount { get; init; }

  /// <summary>The skin's name, as the table gives it.</summary>
  public string Name { get; init; }

  /// <summary>One index a pixel, top row first.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>256 red-green-blue triples.</summary>
  public byte[] Palette { get; init; }

  public static RawImage ToRawImage(HalfLifeModelFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed8,
    PixelData = file.PixelData ?? [],
    Palette = file.Palette,
    PaletteCount = PaletteEntries,
  };
}
