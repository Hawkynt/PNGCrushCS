using System;
using FileFormat.Core;

namespace FileFormat.SamCoupeMode4;

/// <summary>In-memory representation of a SAM Coupe mode 4 (.ss4) screen.</summary>
/// <remarks>
/// Mode 4 is the machine's 16-colour screen: 256x192 pixels at four bits each, high nibble
/// leftmost, 128 bytes per scanline. The bitmap is followed by the 16-entry palette, a block of
/// line-interrupt records that let the palette change part-way down the screen, and a 0xFF
/// terminator. We write no interrupt records, so the palette applies to the whole screen.
/// </remarks>
public readonly record struct SamCoupeMode4File
  : IImageFormatReader<SamCoupeMode4File>, IImageToRawImage<SamCoupeMode4File>,
    IImageFromRawImage<SamCoupeMode4File>, IImageFormatWriter<SamCoupeMode4File> {

  /// <summary>Screen width in pixels.</summary>
  public const int ScreenWidth = 256;

  /// <summary>Screen height in pixels.</summary>
  public const int ScreenHeight = 192;

  /// <summary>Bytes per scanline: two pixels per byte.</summary>
  public const int BytesPerRow = ScreenWidth / 2;

  /// <summary>Size of the bitmap.</summary>
  public const int BitmapDataSize = BytesPerRow * ScreenHeight;

  /// <summary>Offset of the 16-entry palette.</summary>
  public const int PaletteOffset = BitmapDataSize;

  /// <summary>Offset of the line-interrupt block.</summary>
  public const int InterruptOffset = 24616;

  /// <summary>Total size when no interrupt records are present.</summary>
  public const int FileSize = InterruptOffset + 1;

  /// <summary>Byte that closes the interrupt block.</summary>
  public const byte InterruptTerminator = 0xFF;

  static string IImageFormatMetadata<SamCoupeMode4File>.PrimaryExtension => ".ss4";
  static string[] IImageFormatMetadata<SamCoupeMode4File>.FileExtensions => [".ss4", ".scs4"];
  static SamCoupeMode4File IImageFormatReader<SamCoupeMode4File>.FromSpan(ReadOnlySpan<byte> data)
    => SamCoupeMode4Reader.FromSpan(data);
  static byte[] IImageFormatWriter<SamCoupeMode4File>.ToBytes(SamCoupeMode4File file)
    => SamCoupeMode4Writer.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<SamCoupeMode4File>.VideoModes => [
    new("Mode 4", [(ScreenWidth, ScreenHeight)], [SamCoupePalette.EntryCount])
  ];

  /// <summary>Nibble-packed bitmap.</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>The 16 hardware colour bytes.</summary>
  public byte[] Palette { get; init; }

  public static RawImage ToRawImage(SamCoupeMode4File file) {
    var pixels = new byte[ScreenWidth * ScreenHeight];
    for (var y = 0; y < ScreenHeight; ++y)
    for (var x = 0; x < ScreenWidth; ++x) {
      var b = file.BitmapData[y * BytesPerRow + (x >> 1)];
      pixels[y * ScreenWidth + x] = (byte)((x & 1) == 0 ? b >> 4 : b & 15);
    }

    return new() {
      Width = ScreenWidth,
      Height = ScreenHeight,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = SamCoupePalette.ToRgbTriplets(file.Palette),
      PaletteCount = SamCoupePalette.EntryCount,
    };
  }

  public static SamCoupeMode4File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != ScreenWidth || image.Height != ScreenHeight)
      throw new ArgumentException($"Expected {ScreenWidth}x{ScreenHeight} but got {image.Width}x{image.Height}.", nameof(image));

    var indexed = PixelConverter.Convert(image, PixelFormat.Indexed4);
    var rgb = indexed.Palette ?? [];

    var palette = new byte[SamCoupePalette.EntryCount];
    for (var i = 0; i < SamCoupePalette.EntryCount && i < indexed.PaletteCount; ++i)
      palette[i] = SamCoupePalette.FromRgb(rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2]);

    // Indexed4 is already two pixels per byte, high nibble first — the same packing mode 4 uses.
    var bitmap = new byte[BitmapDataSize];
    indexed.PixelData.AsSpan(0, Math.Min(indexed.PixelData.Length, BitmapDataSize)).CopyTo(bitmap);

    return new() { BitmapData = bitmap, Palette = palette };
  }
}
