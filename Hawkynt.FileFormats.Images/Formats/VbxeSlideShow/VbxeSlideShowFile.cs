using System;
using FileFormat.Core;

namespace FileFormat.VbxeSlideShow;

/// <summary>In-memory representation of a SlideShow for VBXE picture (.dap).</summary>
/// <remarks>
/// The VBXE is a video upgrade for the Atari 8-bit, and this format is what the machine looks like
/// once it has one: 320x240 with a byte per pixel and a palette of 256 colours chosen freely from
/// 24-bit RGB, none of the GTIA's fixed hues and luminances involved. The palette is stored as three
/// planes rather than as triplets — every red, then every green, then every blue — which is the
/// order the hardware's three colour registers are loaded in.
/// </remarks>
public readonly record struct VbxeSlideShowFile
  : IImageFormatReader<VbxeSlideShowFile>, IImageToRawImage<VbxeSlideShowFile>,
    IImageFromRawImage<VbxeSlideShowFile>, IImageFormatWriter<VbxeSlideShowFile> {

  /// <summary>Pixels across.</summary>
  public const int Width = 320;

  /// <summary>Rows.</summary>
  public const int Height = 240;

  /// <summary>Colours the palette holds.</summary>
  public const int ColorCount = 256;

  /// <summary>Size of the bitmap: one byte per pixel.</summary>
  public const int PixelDataSize = Width * Height;

  /// <summary>Offset of the palette's red plane; green and blue follow at 256-byte intervals.</summary>
  public const int PaletteOffset = PixelDataSize;

  /// <summary>Total file size.</summary>
  public const int FileSize = PaletteOffset + ColorCount * 3;

  static string IImageFormatMetadata<VbxeSlideShowFile>.PrimaryExtension => ".dap";
  static string[] IImageFormatMetadata<VbxeSlideShowFile>.FileExtensions => [".dap"];
  static VbxeSlideShowFile IImageFormatReader<VbxeSlideShowFile>.FromSpan(ReadOnlySpan<byte> data)
    => VbxeSlideShowReader.FromSpan(data);
  static byte[] IImageFormatWriter<VbxeSlideShowFile>.ToBytes(VbxeSlideShowFile file)
    => VbxeSlideShowWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<VbxeSlideShowFile>.VideoModes => [
    new("VBXE", [(Width, Height)], [ColorCount])
  ];

  /// <summary>The bitmap, one palette index per pixel.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>The palette as RGB triplets.</summary>
  public byte[] Palette { get; init; }

  public static RawImage ToRawImage(VbxeSlideShowFile file) {
    var pixels = new byte[PixelDataSize];
    (file.PixelData ?? []).AsSpan(0, Math.Min(file.PixelData?.Length ?? 0, PixelDataSize)).CopyTo(pixels);

    var palette = new byte[ColorCount * 3];
    (file.Palette ?? []).AsSpan(0, Math.Min(file.Palette?.Length ?? 0, palette.Length)).CopyTo(palette);

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = palette,
      PaletteCount = ColorCount,
    };
  }

  public static VbxeSlideShowFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != Width || image.Height != Height)
      throw new ArgumentException($"Expected {Width}x{Height} but got {image.Width}x{image.Height}.", nameof(image));

    var indexed = image.EnsureFormat(PixelFormat.Indexed8);
    var pixels = new byte[PixelDataSize];
    indexed.PixelData.AsSpan(0, Math.Min(indexed.PixelData.Length, PixelDataSize)).CopyTo(pixels);

    var palette = new byte[ColorCount * 3];
    var source = indexed.Palette ?? [];
    source.AsSpan(0, Math.Min(source.Length, palette.Length)).CopyTo(palette);

    return new() { PixelData = pixels, Palette = palette };
  }
}
