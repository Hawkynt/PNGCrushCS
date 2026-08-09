using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Tiff;

/// <summary>In-memory representation of a TIFF image.</summary>
/// <remarks>
/// A note on <c>.xif</c>, which is not claimed here. Xerox's eXtended Image File is a TIFF: the
/// standard eight-byte header, and then, at offset eight, ten bytes reading <c>XEROX DIFF</c> or
/// <c>eXtended</c> that no tag in the file points at, so a plain TIFF reader walks straight past
/// them into the ordinary directory chain. The sample to hand does exactly that and its pages come
/// out with the right dimensions.
/// <para/>
/// What it does not come out with is any pixels. Its tiles are written with compression 34673, one
/// of Xerox's private mixed-raster schemes, which LibTiff does not decode and neither does
/// ImageMagick — it reports "compression not supported" and stops. Ours does not stop; it hands
/// back a page of white. Claiming the extension would therefore put a blank sheet into the corpus
/// under the name of a document, which is worse for a reader of these notes than the name being
/// absent. A file written with a compression the format does define would decode here perfectly
/// well, and if one turns up the extension can be added then.
/// <para/>
/// <c>.fx3</c> is claimed. It is Fugawi's packaged raster chart, from Northport Systems' moving-map
/// software, and Blue Marble's Global Mapper developer says of one that it "is a TIFF image" whose
/// positioning is not stored the GeoTIFF way. XnView's Fugawi reader is a TIFF reader: handed an
/// ordinary TIFF renamed <c>.fx3</c> and forced through it, XnView reports a TIFF of the right size,
/// and a JPEG under the same name is refused. So the pixels are a TIFF's pixels; what is lost is the
/// calibration, which lives in private tags nothing here reads and nothing published describes.
/// </remarks>
[FormatMimeType("image/tiff", "image/tif", "image/x-tiff")]
public sealed class TiffFile :
  IImageFormatReader<TiffFile>, IImageToRawImage<TiffFile>, IImageFromRawImage<TiffFile>, IImageFormatWriter<TiffFile>,
  IMultiImageFileFormat<TiffFile>, IFormatChunkLayout<TiffFile> {

  static string IImageFormatMetadata<TiffFile>.PrimaryExtension => ".tiff";
  static string[] IImageFormatMetadata<TiffFile>.FileExtensions => [".tif", ".tiff", ".ftf", ".stw", ".fx3"];
  static TiffFile IImageFormatReader<TiffFile>.FromSpan(ReadOnlySpan<byte> data) => TiffReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<TiffFile>.Capabilities => FormatCapability.HasDedicatedOptimizer | FormatCapability.MultiImage;

  static bool? IImageFormatMetadata<TiffFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 4)
      return null;
    if (header[0] == 0x49 && header[1] == 0x49 && header[2] == 0x2A && header[3] == 0x00)
      return true;
    if (header[0] == 0x4D && header[1] == 0x4D && header[2] == 0x00 && header[3] == 0x2A)
      return true;
    return null;
  }

  static byte[] IImageFormatWriter<TiffFile>.ToBytes(TiffFile file) => TiffWriter.ToBytes(file);

  static IEnumerable<ChunkSpan> IFormatChunkLayout<TiffFile>.EnumerateChunks(ReadOnlySpan<byte> data)
    => TiffChunkLayout.Enumerate(data);
  public int Width { get; init; }
  public int Height { get; init; }
  public int SamplesPerPixel { get; init; }
  public int BitsPerSample { get; init; }
  public byte[] PixelData { get; init; } = [];
  public byte[]? ColorMap { get; init; }
  public TiffColorMode ColorMode { get; init; }

  /// <summary>Whether a wide sample's high byte comes first, which the file's first two bytes say.</summary>
  public bool IsBigEndian { get; init; }

  /// <summary>Additional pages beyond the first IFD. Empty for single-page TIFFs.</summary>
  public IReadOnlyList<TiffPage> Pages { get; init; } = [];

  /// <summary>Everything the file carries beside its pixels, read from its own IFD tags.</summary>
  /// <remarks>
  /// A TIFF needs no separate container for this: EXIF is a TIFF stream, and XMP, the Photoshop IPTC
  /// block, an ICC profile and the resolution are ordinary tags of IFD0.
  /// </remarks>
  public ImageMetadata? Metadata { get; init; }

  /// <summary>Returns the total number of pages (IFDs) in the TIFF file.</summary>
  public static int ImageCount(TiffFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return 1 + file.Pages.Count;
  }

  /// <summary>Converts a specific page at the given index to a <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(TiffFile file, int index) {
    ArgumentNullException.ThrowIfNull(file);
    var total = 1 + file.Pages.Count;
    if ((uint)index >= (uint)total)
      throw new ArgumentOutOfRangeException(nameof(index));

    if (index == 0)
      return ToRawImage(file);

    var page = file.Pages[index - 1];
    return _PageToRawImage(page);
  }

  private static RawImage _PageToRawImage(TiffPage page) {
    PixelFormat format;
    byte[]? palette = null;
    var paletteCount = 0;

    switch (page.SamplesPerPixel) {
      case 3 when page.BitsPerSample == 8:
        format = PixelFormat.Rgb24;
        break;
      case 4 when page.BitsPerSample == 8:
        format = PixelFormat.Rgba32;
        break;
      case 1 when page.BitsPerSample == 8 && page.ColorMap != null:
        format = PixelFormat.Indexed8;
        palette = _ConvertTiffColorMap(page.ColorMap);
        paletteCount = page.ColorMap.Length / 3;
        break;
      case 1 when page.BitsPerSample == 8:
        format = PixelFormat.Gray8;
        break;
      case 1 when page.BitsPerSample == 16:
        format = PixelFormat.Gray16;
        break;
      case 1 when page.BitsPerSample is 4 or 2:
        return _FromShallowSamples(page.Width, page.Height, page.BitsPerSample, page.ColorMap, page.PixelData, null);
      case 1 when page.BitsPerSample == 1:
        format = PixelFormat.Indexed1;
        palette = page.ColorMap is { Length: >= 6 } ? _ConvertTiffColorMap(page.ColorMap) : [0, 0, 0, 255, 255, 255];
        paletteCount = 2;
        return new() {
          Width = page.Width,
          Height = page.Height,
          Format = format,
          PixelData = _Unpad(page.PixelData, page.Width, page.Height, 1),
          Palette = palette,
          PaletteCount = paletteCount,
        };
      default:
        throw new ArgumentException($"Unsupported TIFF page configuration: SamplesPerPixel={page.SamplesPerPixel}, BitsPerSample={page.BitsPerSample}.");
    }

    return new() {
      Width = page.Width,
      Height = page.Height,
      Format = format,
      PixelData = page.PixelData[..],
      Palette = palette,
      PaletteCount = paletteCount,
    };
  }

  /// <summary>Builds a picture from samples narrower than a byte — one, two or four bits of index.</summary>
  /// <remarks>
  /// Four bits with a colour map is an ordinary way to store a small picture and was refused
  /// outright, which is what kept every Neopaint stationery file out. Two bits has no format of its
  /// own here, so its indices are widened to four and its palette padded to sixteen entries, which
  /// draws the same picture.
  /// </remarks>
  private static RawImage _FromShallowSamples(int width, int height, int bitsPerSample, byte[]? colorMap, byte[] pixelData, ImageMetadata? metadata) {
    var indices = _Unpad(pixelData, width, height, bitsPerSample);
    if (bitsPerSample == 2)
      indices = _WidenPairsToNibbles(indices, width, height);

    byte[] palette;
    if (colorMap is { Length: >= 3 }) {
      palette = new byte[16 * 3];
      colorMap.AsSpan(0, Math.Min(colorMap.Length, palette.Length)).CopyTo(palette);
    } else {
      // No colour map means a grey ramp over the depth the file states.
      palette = new byte[16 * 3];
      var levels = 1 << bitsPerSample;
      for (var i = 0; i < levels; ++i) {
        var value = (byte)(i * 255 / (levels - 1));
        palette[i * 3] = palette[i * 3 + 1] = palette[i * 3 + 2] = value;
      }
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed4,
      PixelData = indices,
      Palette = palette,
      PaletteCount = 16,
      Metadata = metadata,
    };
  }

  /// <summary>Drops the padding TIFF puts at the end of every row of sub-byte samples.</summary>
  /// <remarks>
  /// A TIFF row starts on a byte boundary, so a picture 381 pixels wide at four bits carries half a
  /// byte of nothing at the end of each. The indexed formats here are a continuous bit stream with no
  /// such gap, so leaving it in shears every row after the first.
  /// </remarks>
  private static byte[] _Unpad(byte[] pixelData, int width, int height, int bitsPerSample) {
    var bytesPerRow = (width * bitsPerSample + 7) / 8;
    var bitsPerRow = width * bitsPerSample;
    if (bitsPerRow % 8 == 0 || height <= 1)
      return pixelData[..];

    var packed = new byte[(bitsPerRow * height + 7) / 8];
    var bit = 0;
    for (var row = 0; row < height; ++row) {
      var rowStart = row * bytesPerRow;
      for (var i = 0; i < bitsPerRow; ++i) {
        var sourceBit = rowStart * 8 + i;
        var sourceByte = sourceBit >> 3;
        if (sourceByte >= pixelData.Length)
          break;

        if ((pixelData[sourceByte] & (0x80 >> (sourceBit & 7))) != 0)
          packed[bit >> 3] |= (byte)(0x80 >> (bit & 7));

        ++bit;
      }
    }

    return packed;
  }

  /// <summary>Spreads two-bit indices one to a nibble so a four-bit palette can draw them.</summary>
  private static byte[] _WidenPairsToNibbles(byte[] pairs, int width, int height) {
    var total = width * height;
    var widened = new byte[(total + 1) / 2];
    for (var i = 0; i < total; ++i) {
      var sourceBit = i * 2;
      var sourceByte = sourceBit >> 3;
      if (sourceByte >= pairs.Length)
        break;

      var index = (pairs[sourceByte] >> (6 - (sourceBit & 7))) & 0x03;
      if ((i & 1) == 0)
        widened[i >> 1] |= (byte)(index << 4);
      else
        widened[i >> 1] |= (byte)index;
    }

    return widened;
  }

  /// <summary>Builds a picture from a file whose samples are sixteen bits wide.</summary>
  private static RawImage _FromDeepSamples(TiffFile file, PixelFormat format) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = format,
    PixelData = _NarrowSamples(file.PixelData, file.IsBigEndian),
  };

  /// <summary>Narrows sixteen-bit samples to eight, keeping the byte the magnitude lives in.</summary>
  /// <remarks>
  /// A deep TIFF is ordinary rather than exotic — a scanner or a camera writes one by default — so
  /// refusing it refuses a large share of real files. The low byte is dropped rather than rounded
  /// into the high one: the difference is under half a level at eight bits, and dropping it cannot
  /// carry a sample past its neighbour the way rounding can.
  /// </remarks>
  private static byte[] _NarrowSamples(ReadOnlySpan<byte> data, bool isBigEndian) {
    var narrowed = new byte[data.Length / 2];
    for (var i = 0; i < narrowed.Length; ++i)
      narrowed[i] = data[i * 2 + (isBigEndian ? 0 : 1)];

    return narrowed;
  }

  public static RawImage ToRawImage(TiffFile file) {
    ArgumentNullException.ThrowIfNull(file);

    PixelFormat format;
    byte[]? palette = null;
    var paletteCount = 0;

    switch (file.SamplesPerPixel) {
      case 3 when file.BitsPerSample == 8:
        format = PixelFormat.Rgb24;
        break;
      case 4 when file.BitsPerSample == 8:
        format = PixelFormat.Rgba32;
        break;
      case 1 when file.BitsPerSample == 8 && file.ColorMap != null:
        format = PixelFormat.Indexed8;
        palette = _ConvertTiffColorMap(file.ColorMap);
        paletteCount = file.ColorMap.Length / 3;
        break;
      case 1 when file.BitsPerSample == 8:
        format = PixelFormat.Gray8;
        break;
      case 1 when file.BitsPerSample == 16:
        format = PixelFormat.Gray16;
        break;
      case 3 when file.BitsPerSample == 16:
        return _FromDeepSamples(file, PixelFormat.Rgb24);
      case 4 when file.BitsPerSample == 16:
        return _FromDeepSamples(file, PixelFormat.Rgba32);
      case 1 when file.BitsPerSample is 4 or 2:
        return _FromShallowSamples(file.Width, file.Height, file.BitsPerSample, file.ColorMap, file.PixelData, file.Metadata);
      case 1 when file.BitsPerSample == 1:
        format = PixelFormat.Indexed1;
        palette = file.ColorMap is { Length: >= 6 } ? _ConvertTiffColorMap(file.ColorMap) : [0, 0, 0, 255, 255, 255];
        paletteCount = 2;
        return new() {
          Width = file.Width,
          Height = file.Height,
          Format = format,
          PixelData = _Unpad(file.PixelData, file.Width, file.Height, 1),
          Palette = palette,
          PaletteCount = paletteCount,
          Metadata = file.Metadata,
        };
      default:
        throw new ArgumentException($"Unsupported TIFF configuration: SamplesPerPixel={file.SamplesPerPixel}, BitsPerSample={file.BitsPerSample}.", nameof(file));
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = format,
      PixelData = file.PixelData[..],
      Palette = palette,
      PaletteCount = paletteCount,
      Metadata = file.Metadata,
    };
  }

  public static TiffFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    image = image.EnsureAnyFormat(
      PixelFormat.Rgb24, PixelFormat.Rgb48, PixelFormat.Gray16, PixelFormat.Gray8,
      PixelFormat.Indexed8, PixelFormat.Indexed1);

    int samplesPerPixel;
    int bitsPerSample;
    TiffColorMode colorMode;
    byte[]? colorMap = null;

    switch (image.Format) {
      case PixelFormat.Rgb24:
        samplesPerPixel = 3;
        bitsPerSample = 8;
        colorMode = TiffColorMode.Rgb;
        break;
      case PixelFormat.Rgba32:
        samplesPerPixel = 4;
        bitsPerSample = 8;
        colorMode = TiffColorMode.Rgb;
        break;
      case PixelFormat.Gray8:
        samplesPerPixel = 1;
        bitsPerSample = 8;
        colorMode = TiffColorMode.Grayscale;
        break;
      case PixelFormat.Gray16:
        samplesPerPixel = 1;
        bitsPerSample = 16;
        colorMode = TiffColorMode.Grayscale;
        break;
      case PixelFormat.Indexed8:
        samplesPerPixel = 1;
        bitsPerSample = 8;
        colorMode = TiffColorMode.Palette;
        colorMap = _ConvertToTiffColorMap(image.Palette, image.PaletteCount);
        break;
      case PixelFormat.Indexed1:
        samplesPerPixel = 1;
        bitsPerSample = 1;
        colorMode = TiffColorMode.BiLevel;
        break;
      default:
        throw new ArgumentException($"Unsupported pixel format for TIFF: {image.Format}.", nameof(image));
    }

    return new() {
      Width = image.Width,
      Height = image.Height,
      SamplesPerPixel = samplesPerPixel,
      BitsPerSample = bitsPerSample,
      PixelData = image.PixelData[..],
      ColorMap = colorMap,
      ColorMode = colorMode,
      Metadata = image.Metadata,
    };
  }

  /// <summary>Hands a stored colour map on as the RGB triplets everything either side of it holds.</summary>
  /// <remarks>
  /// <see cref="ColorMap"/> is eight-bit RGB triplets, one per palette entry. That is what
  /// <see cref="TiffReader"/> builds from the file's three sixteen-bit arrays and what
  /// <see cref="TiffWriter"/> and <see cref="TiffBinaryWriter"/> expand back into them. These two
  /// converters alone treated it as the file's own layout — three sixteen-bit planes — so a palette
  /// went out through one of them and came back scrambled: a third of the entries dropped for the
  /// length being divided by six instead of three, and the survivors taking a green byte for a red.
  /// Neither direction cancelled the other, so a palette TIFF read here and a palette TIFF written
  /// here were both wrong, and against ImageMagick a 256-colour gradient missed 29% and 35% of its
  /// pixels respectively.
  /// </remarks>
  private static byte[] _ConvertTiffColorMap(byte[] colorMap) => colorMap[..];

  /// <summary>Takes the palette's RGB triplets as the colour map, trimmed to the entries in use.</summary>
  private static byte[] _ConvertToTiffColorMap(byte[]? palette, int paletteCount) {
    if (palette == null)
      throw new ArgumentException("Palette must not be null for indexed images.");

    var wanted = paletteCount * 3;
    var colorMap = new byte[wanted];
    palette.AsSpan(0, Math.Min(wanted, palette.Length)).CopyTo(colorMap);
    return colorMap;
  }
}
