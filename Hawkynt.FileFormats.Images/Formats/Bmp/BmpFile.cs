using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Bmp;

/// <summary>In-memory representation of a BMP image.</summary>
[FormatMagicBytes([0x42, 0x4D])]
[FormatMimeType("image/bmp", "image/x-bmp", "image/x-ms-bmp")]
public readonly record struct BmpFile :
  IImageFormatReader<BmpFile>, IImageToRawImage<BmpFile>, IImageFromRawImage<BmpFile>, IImageFormatWriter<BmpFile>,
  IImageInfoReader<BmpFile>, IFormatChunkLayout<BmpFile> {

  static string IImageFormatMetadata<BmpFile>.PrimaryExtension => ".bmp";

  /// <summary><c>.bum</c> is a Poser bump map and <c>.thb</c> a KinuPix skin, which are Windows DIBs and nothing else.</summary>
  /// <remarks>
  /// All three Poser samples are ordinary uncompressed 32-bit DIBs whose stated file size is the
  /// file's own. The fourth byte of each pixel is padding rather than an alpha channel — the height
  /// is in the colour — but that is a matter of what the picture means, not of how it is stored, so
  /// the reader is this one and the name is claimed here. The signature still decides: anything
  /// under the name that does not open with a DIB header is refused.
  /// <para/>
  /// <c>.thb</c>, <c>.2d</c> and <c>.bmc</c> arrived the same way. XnView lists each of them under a
  /// format of its own — KinuPix Skin, Amapi and Embroidery — and its reader for all three is the one
  /// it uses for <c>.bmp</c>: a Windows bitmap renamed to any of them and forced through that reader
  /// is reported as a Windows Bitmap of the right size, and a JPEG under the same name is refused by
  /// it. So each is a DIB and the names are claimed on the same terms as the others.
  /// <para/>
  /// What those three names are elsewhere is not this. Amapi's own drawings are <c>.a3d</c> and are
  /// three-dimensional geometry; <c>.bmc</c> in the embroidery world is a Bitmap Cache of stitches,
  /// which libembroidery registers as stitch-only and refuses to read for want of any description of
  /// it. Neither of those is a picture, and neither of them opens with <c>BM</c>, so neither is drawn
  /// here — which is the point of claiming the name on the signature rather than on the name.
  /// <para/>
  /// <c>.stm</c> and <c>.upi</c> joined them on the same test. XnView lists them as PhotoStudio Stamp
  /// and Ulead PhotoImpact, and the reader it uses for both is the one it uses for <c>.bmp</c>: a
  /// Windows bitmap renamed to either is reported as a Windows Bitmap of the right size, and a JPEG
  /// under either name is refused by it.
  /// <para/>
  /// <c>.msk</c> is the same story and was got wrong once. XnView lists it as PaintShopPro Mask, and
  /// the name was claimed here for the Paint Shop Pro reader on the strength of that title — but the
  /// entry runs the same Windows bitmap reader as the twelve above it, and Paint Shop Pro's own mask
  /// under XnView is <c>.pspmask</c>, a separate entry on its separate reader. So a <c>.msk</c> is a
  /// DIB, and the claim that closed that row was against a reader that would refuse the file. Both
  /// readers hold the name now: this one takes the DIB and the other one takes anything that really
  /// does open with Paint Shop Pro's header.
  /// </remarks>
  static string[] IImageFormatMetadata<BmpFile>.FileExtensions =>
    [".bmp", ".dib", ".bga", ".rl4", ".rl8", ".vga", ".sys", ".bum", ".thb", ".2d", ".bmc", ".stm", ".upi", ".msk"];
  static BmpFile IImageFormatReader<BmpFile>.FromSpan(ReadOnlySpan<byte> data) => BmpReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<BmpFile>.Capabilities => FormatCapability.HasDedicatedOptimizer;
  static VideoMode[] IImageFormatMetadata<BmpFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)])
  ];
  static byte[] IImageFormatWriter<BmpFile>.ToBytes(BmpFile file) => BmpWriter.ToBytes(file);

  static IEnumerable<ChunkSpan> IFormatChunkLayout<BmpFile>.EnumerateChunks(ReadOnlySpan<byte> data)
    => BmpChunkLayout.Enumerate(data);

  public static ImageInfo? ReadImageInfo(ReadOnlySpan<byte> header) {
    if (header.Length < 26 || header[0] != 0x42 || header[1] != 0x4D)
      return null;

    var width = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(header[18..]);
    var height = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(header[22..]);
    if (height < 0) height = -height;
    var bpp = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(header[28..]);

    return new(width, height, bpp, bpp switch {
      1 => "Monochrome",
      4 => "Indexed4",
      8 => "Indexed8",
      16 => "Rgb16",
      24 => "Rgb24",
      32 => "Rgba32",
      _ => null
    });
  }
  public int Width { get; init; }
  public int Height { get; init; }
  public int BitsPerPixel { get; init; }
  public byte[] PixelData { get; init; }
  public byte[]? Palette { get; init; }
  public int PaletteColorCount { get; init; }
  public BmpRowOrder RowOrder { get; init; }
  public BmpCompression Compression { get; init; }
  public BmpColorMode ColorMode { get; init; }

  public static RawImage ToRawImage(BmpFile file) {

    var mode = file.ColorMode;
    if (mode == BmpColorMode.Original)
      mode = file.BitsPerPixel switch {
        24 => BmpColorMode.Rgb24,
        16 => BmpColorMode.Rgb16_565,
        8 when file.Palette != null => BmpColorMode.Palette8,
        8 => BmpColorMode.Grayscale8,
        4 => BmpColorMode.Palette4,
        _ => BmpColorMode.Palette1
      };

    PixelFormat format;
    byte[]? palette = null;
    int paletteCount = 0;

    switch (mode) {
      case BmpColorMode.Rgb24:
        format = PixelFormat.Bgr24;
        break;
      case BmpColorMode.Rgb16_565:
        format = PixelFormat.Rgb565;
        break;
      case BmpColorMode.Palette8:
        format = PixelFormat.Indexed8;
        palette = file.Palette;
        paletteCount = file.PaletteColorCount;
        break;
      case BmpColorMode.Palette4:
        format = PixelFormat.Indexed4;
        palette = file.Palette;
        paletteCount = file.PaletteColorCount;
        break;
      case BmpColorMode.Palette1:
        format = PixelFormat.Indexed1;
        palette = file.Palette;
        paletteCount = file.PaletteColorCount;
        break;
      case BmpColorMode.Grayscale8:
        format = PixelFormat.Gray8;
        break;
      default:
        throw new ArgumentException($"Unsupported BmpColorMode: {mode}", nameof(file));
    }

    var bpp = file.BitsPerPixel;
    var stride = bpp >= 8 ? file.Width * (bpp / 8) : bpp == 4 ? (file.Width + 1) / 2 : (file.Width + 7) / 8;
    var pixelData = file.RowOrder == BmpRowOrder.BottomUp
      ? _FlipRows(file.PixelData, stride, file.Height)
      : file.PixelData;

    return new RawImage {
      Width = file.Width,
      Height = file.Height,
      Format = format,
      PixelData = pixelData,
      Palette = palette,
      PaletteCount = paletteCount
    };
  }

  public static BmpFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    // The writer has no 32-bpp path, so alpha-bearing input lands on Bgr24 and loses transparency.
    image = image.EnsureAnyFormat(
      PixelFormat.Bgr24, PixelFormat.Rgb565, PixelFormat.Gray8,
      PixelFormat.Indexed8, PixelFormat.Indexed4, PixelFormat.Indexed1);

    BmpColorMode colorMode;
    int bpp;
    byte[]? palette = null;
    int paletteCount = 0;

    switch (image.Format) {
      case PixelFormat.Bgr24:
        colorMode = BmpColorMode.Rgb24;
        bpp = 24;
        break;
      case PixelFormat.Rgb565:
        colorMode = BmpColorMode.Rgb16_565;
        bpp = 16;
        break;
      case PixelFormat.Indexed8:
        colorMode = BmpColorMode.Palette8;
        bpp = 8;
        palette = image.Palette;
        paletteCount = image.PaletteCount;
        break;
      case PixelFormat.Indexed4:
        colorMode = BmpColorMode.Palette4;
        bpp = 4;
        palette = image.Palette;
        paletteCount = image.PaletteCount;
        break;
      case PixelFormat.Indexed1:
        colorMode = BmpColorMode.Palette1;
        bpp = 1;
        palette = image.Palette;
        paletteCount = image.PaletteCount;
        break;
      case PixelFormat.Gray8:
        colorMode = BmpColorMode.Grayscale8;
        bpp = 8;
        break;
      default:
        throw new ArgumentException($"Unsupported pixel format for BMP: {image.Format}", nameof(image));
    }

    return new BmpFile {
      Width = image.Width,
      Height = image.Height,
      BitsPerPixel = bpp,
      PixelData = image.PixelData,
      Palette = palette,
      PaletteColorCount = paletteCount,
      RowOrder = BmpRowOrder.TopDown,
      Compression = BmpCompression.None,
      ColorMode = colorMode
    };
  }

  private static byte[] _FlipRows(byte[] data, int stride, int height) {
    var result = new byte[data.Length];
    for (var y = 0; y < height; ++y)
      data.AsSpan((height - 1 - y) * stride, stride).CopyTo(result.AsSpan(y * stride));
    return result;
  }
}
