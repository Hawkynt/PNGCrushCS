using System;
using FileFormat.Core;

namespace FileFormat.MsxScreen10;

/// <summary>In-memory representation of an MSX2+ Screen 10 picture (.sca, .scb).</summary>
/// <remarks>
/// A BSAVE header, then one byte per pixel across 256x212, holding the V9958's YJK colour: five
/// bits of luma per pixel and three bits of chroma pooled four pixels at a time. Screen 10 spends
/// one luma bit on an escape — an odd luma names one of sixteen palette colours instead — which is
/// what makes it usable for pictures with flat, exact areas next to photographic ones. The palette
/// sits near the end of the video page, well past the bitmap.
/// </remarks>
[FormatMagicBytes([0xFE])]
public readonly record struct MsxScreen10File
  : IImageFormatReader<MsxScreen10File>, IImageToRawImage<MsxScreen10File>,
    IImageFromRawImage<MsxScreen10File>, IImageFormatWriter<MsxScreen10File> {

  /// <summary>Pixels per row.</summary>
  public const int Width = 256;

  /// <summary>Rows.</summary>
  public const int Height = 212;

  /// <summary>Size of the bitmap: one byte per pixel.</summary>
  public const int PixelDataSize = Width * Height;

  /// <summary>Offset of the bitmap, immediately after the BSAVE header.</summary>
  public const int PixelDataOffset = MsxGraphics.BsaveHeaderSize;

  /// <summary>Colours the palette holds.</summary>
  public const int ColorCount = 16;

  /// <summary>Size of the stored palette.</summary>
  public const int PaletteSize = ColorCount * MsxGraphics.PaletteEntrySize;

  /// <summary>Offset of the palette within the file.</summary>
  public const int PaletteOffset = 64135;

  /// <summary>Total file size.</summary>
  public const int FileSize = PaletteOffset + PaletteSize;

  /// <summary>The end address the BSAVE header carries; it describes the bitmap, not the file.</summary>
  public const int BsaveEndAddress = PixelDataSize - 1;

  static string IImageFormatMetadata<MsxScreen10File>.PrimaryExtension => ".sca";
  static string[] IImageFormatMetadata<MsxScreen10File>.FileExtensions => [".sca", ".scb"];
  static MsxScreen10File IImageFormatReader<MsxScreen10File>.FromSpan(ReadOnlySpan<byte> data) => MsxScreen10Reader.FromSpan(data);
  static byte[] IImageFormatWriter<MsxScreen10File>.ToBytes(MsxScreen10File file) => MsxScreen10Writer.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<MsxScreen10File>.VideoModes => [
    new("Screen 10", [(Width, Height)], [12515])
  ];

  /// <summary>The bitmap, one YJK byte per pixel.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>The sixteen palette colours, two bytes each.</summary>
  public byte[] Palette { get; init; }

  public static RawImage ToRawImage(MsxScreen10File file) {
    var data = file.PixelData ?? [];
    var palette = MsxGraphics.PaletteToRgb(file.Palette ?? [], ColorCount);
    var rgb = new byte[Width * Height * 3];

    for (var y = 0; y < Height; ++y) {
      var offset = y * Width;
      if (offset + Width > data.Length)
        break;

      MsxGraphics.DecodeYjkRow(data.AsSpan(offset, Width), Width, true, palette, rgb.AsSpan(offset * 3, Width * 3));
    }

    return new() { Width = Width, Height = Height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  public static MsxScreen10File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != Width || image.Height != Height)
      throw new ArgumentException($"Expected {Width}x{Height} but got {image.Width}x{image.Height}.", nameof(image));

    var rgb = PixelConverter.Convert(image, PixelFormat.Rgb24);
    var data = new byte[PixelDataSize];

    // The sixteen palette entries are the escape hatch, not the picture: encoding everything as
    // YJK and leaving the palette black keeps every pixel on the chroma path, which is the only
    // reading that survives a round trip through a decoder that has no companion palette file.
    for (var y = 0; y < Height; ++y)
      MsxGraphics.EncodeYjkRow(rgb.PixelData.AsSpan(y * Width * 3, Width * 3), Width, true, data.AsSpan(y * Width, Width));

    return new() { PixelData = data, Palette = new byte[PaletteSize] };
  }
}
