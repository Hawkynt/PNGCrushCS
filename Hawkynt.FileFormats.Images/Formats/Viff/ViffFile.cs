using System;
using FileFormat.Core;

namespace FileFormat.Viff;

/// <summary>In-memory representation of a VIFF (Khoros Visualization Image File Format) image.</summary>
public readonly record struct ViffFile : IImageFormatReader<ViffFile>, IImageToRawImage<ViffFile>, IImageFromRawImage<ViffFile>, IImageFormatWriter<ViffFile> {

  static string IImageFormatMetadata<ViffFile>.PrimaryExtension => ".viff";
  static string[] IImageFormatMetadata<ViffFile>.FileExtensions => [".viff", ".xv"];
  static ViffFile IImageFormatReader<ViffFile>.FromSpan(ReadOnlySpan<byte> data) => ViffReader.FromSpan(data);
  static byte[] IImageFormatWriter<ViffFile>.ToBytes(ViffFile file) => ViffWriter.ToBytes(file);

  static bool? IImageFormatMetadata<ViffFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 2 && header[0] == 0xAB && header[1] == 0x01
      ? true : null;

  /// <summary>Image width in pixels (RowSize).</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels (ColSize).</summary>
  public int Height { get; init; }

  /// <summary>Number of data bands: 1 for greyscale or paletted, 3 for RGB.</summary>
  public int Bands { get; init; }

  /// <summary>Pixel data storage type.</summary>
  public ViffStorageType StorageType { get; init; }

  /// <summary>Color space model.</summary>
  public ViffColorSpaceModel ColorSpaceModel { get; init; }

  /// <summary>512-byte ASCII comment from the header.</summary>
  public string Comment { get; init; }

  /// <summary>Raw pixel data bytes (band-sequential: a whole plane at a time, not interleaved).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Optional color map data, band-sequential like the pixels.</summary>
  public byte[]? MapData { get; init; }

  /// <summary>Whether the color map applies, and how.</summary>
  public ViffMapScheme MapScheme { get; init; }

  /// <summary>Element type of the color map.</summary>
  public ViffMapType MapType { get; init; }

  /// <summary>Channels in the color map: 3 for an RGB palette, 1 for a grey ramp.</summary>
  public int MapRowSize { get; init; }

  /// <summary>Entries in the color map.</summary>
  public int MapColSize { get; init; }

  public static RawImage ToRawImage(ViffFile file) {

    // A one-bit-a-pixel VIFF is an ordinary bitmap and what a writer produces for a two-colour image,
    // but only Byte storage was handled — so those were refused rather than unpacked.
    if (file.StorageType == ViffStorageType.Bit && file.Bands == 1)
      return new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Gray8,
        PixelData = _UnpackBits(file.PixelData, file.Width, file.Height),
      };

    if (file.StorageType != ViffStorageType.Byte)
      throw new ArgumentException($"Only Bit and Byte storage are supported for conversion, got {file.StorageType}.", nameof(file));

    if (file.Bands == 1) {

      // One band plus a map is a paletted image, which is what ImageMagick writes whenever the
      // picture has few enough colours. Read as bare greys the indices came out as near-black
      // nonsense, so a four-colour image decoded to a four-shade one.
      var palette = _BuildPalette(file);
      if (palette != null)
        return new() {
          Width = file.Width,
          Height = file.Height,
          Format = PixelFormat.Indexed8,
          PixelData = file.PixelData[..],
          Palette = palette,
          PaletteCount = 256,
        };

      return new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Gray8,
        PixelData = file.PixelData[..],
      };
    }

    if (file.Bands == 3) {
      var pixelCount = file.Width * file.Height;
      var result = new byte[pixelCount * 3];
      for (var i = 0; i < pixelCount; ++i) {
        result[i * 3] = file.PixelData[i];
        result[i * 3 + 1] = file.PixelData[pixelCount + i];
        result[i * 3 + 2] = file.PixelData[pixelCount * 2 + i];
      }

      return new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Rgb24,
        PixelData = result,
      };
    }

    throw new ArgumentException($"Unsupported band count for conversion: {file.Bands}", nameof(file));
  }

  public static ViffFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureAnyFormat(PixelFormat.Rgb24, PixelFormat.Gray8, PixelFormat.Indexed8);

    switch (image.Format) {
      case PixelFormat.Indexed8: {
        var (map, entries) = _BuildMap(image);
        return new() {
          Width = image.Width,
          Height = image.Height,
          Bands = 1,
          StorageType = ViffStorageType.Byte,
          ColorSpaceModel = ViffColorSpaceModel.None,
          PixelData = image.PixelData[..],
          MapData = map,
          MapScheme = ViffMapScheme.OnePerBand,
          MapType = ViffMapType.Byte,
          MapRowSize = 3,
          MapColSize = entries,
        };
      }
      case PixelFormat.Gray8:
        return new() {
          Width = image.Width,
          Height = image.Height,
          Bands = 1,
          StorageType = ViffStorageType.Byte,
          ColorSpaceModel = ViffColorSpaceModel.None,
          PixelData = image.PixelData[..],
        };
      case PixelFormat.Rgb24: {
        var pixelCount = image.Width * image.Height;
        var bandSeq = new byte[pixelCount * 3];
        for (var i = 0; i < pixelCount; ++i) {
          bandSeq[i] = image.PixelData[i * 3];
          bandSeq[pixelCount + i] = image.PixelData[i * 3 + 1];
          bandSeq[pixelCount * 2 + i] = image.PixelData[i * 3 + 2];
        }

        return new() {
          Width = image.Width,
          Height = image.Height,
          Bands = 3,
          StorageType = ViffStorageType.Byte,
          ColorSpaceModel = ViffColorSpaceModel.GenericRgb,
          PixelData = bandSeq,
        };
      }
      default:
        throw new ArgumentException($"Unsupported pixel format for VIFF: {image.Format}", nameof(image));
    }
  }

  /// <summary>Turns a VIFF colour map into a 256-entry RGB palette, or returns null when there is none.</summary>
  /// <remarks>
  /// The map is stored the way the pixels are — one plane per channel, not one triplet per entry — so
  /// the reds all come first. Only byte entries are handled: wider ones would need the file's byte
  /// order, which is spent by the time the map reaches here, and nothing in the wild writes them.
  /// </remarks>
  private static byte[]? _BuildPalette(ViffFile file) {
    if (file.MapScheme == ViffMapScheme.None || file.MapType != ViffMapType.Byte)
      return null;
    if (file.MapData is not { Length: > 0 } map || file.MapColSize <= 0)
      return null;
    if (file.MapRowSize is not (1 or 3))
      return null;

    var entries = Math.Min(file.MapColSize, 256);
    var palette = new byte[256 * 3];
    var green = file.MapRowSize == 3 ? file.MapColSize : 0;
    var blue = file.MapRowSize == 3 ? file.MapColSize * 2 : 0;
    for (var i = 0; i < entries; ++i) {
      if (blue + i >= map.Length)
        break;

      palette[i * 3] = map[i];
      palette[i * 3 + 1] = map[green + i];
      palette[i * 3 + 2] = map[blue + i];
    }

    return palette;
  }

  /// <summary>Lays an RGB palette out band-sequentially, the way a VIFF colour map is stored.</summary>
  private static (byte[] Map, int Entries) _BuildMap(RawImage image) {
    var entries = image.PaletteCount > 0 ? Math.Min(image.PaletteCount, 256) : 256;
    var palette = image.Palette ?? [];
    var map = new byte[entries * 3];
    for (var i = 0; i < entries; ++i) {
      var at = i * 3;
      if (at + 2 >= palette.Length)
        break;

      map[i] = palette[at];
      map[entries + i] = palette[at + 1];
      map[entries * 2 + i] = palette[at + 2];
    }

    return (map, entries);
  }

  /// <summary>Expands a packed one-bit-a-pixel plane to one grey byte a pixel.</summary>
  /// <remarks>Rows are padded to a whole byte, and a set bit is white.</remarks>
  private static byte[] _UnpackBits(byte[] packed, int width, int height) {
    var stride = (width + 7) / 8;
    var pixels = new byte[width * height];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var at = (y * stride) + (x >> 3);
        var bit = at < packed.Length && ((packed[at] >> (7 - (x & 7))) & 1) != 0;
        pixels[(y * width) + x] = bit ? (byte)255 : (byte)0;
      }

    return pixels;
  }
}
