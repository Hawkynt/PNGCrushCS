using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.PictureEditor;

/// <summary>In-memory representation of a Picture Editor (Atari 8-bit) image (ANTIC Mode E, 160x192, 4-color).</summary>
public sealed class PictureEditorFile : IImageFileFormat<PictureEditorFile> {

  /// <summary>The exact file size: 40 bytes/line x 192 lines.</summary>
  public const int ExpectedFileSize = 7680;

  /// <summary>The fixed width in pixels.</summary>
  public const int FixedWidth = 160;

  /// <summary>The fixed height in pixels.</summary>
  public const int FixedHeight = 192;

  /// <summary>Bytes per scanline (4 pixels per byte, 160/4 = 40).</summary>
  internal const int BytesPerRow = 40;

  /// <summary>Default Atari 4-color palette as 0xRRGGBB values.</summary>
  private static readonly int[] _DefaultPalette = [0x000000, 0x884400, 0x00AA44, 0xDDCC88];

  static string IImageFileFormat<PictureEditorFile>.PrimaryExtension => ".ped";
  static string[] IImageFileFormat<PictureEditorFile>.FileExtensions => [".ped"];
  static FormatCapability IImageFileFormat<PictureEditorFile>.Capabilities => FormatCapability.IndexedOnly;
  static PictureEditorFile IImageFileFormat<PictureEditorFile>.FromFile(FileInfo file) => PictureEditorReader.FromFile(file);
  static PictureEditorFile IImageFileFormat<PictureEditorFile>.FromBytes(byte[] data) => PictureEditorReader.FromBytes(data);
  static PictureEditorFile IImageFileFormat<PictureEditorFile>.FromStream(Stream stream) => PictureEditorReader.FromStream(stream);
  static byte[] IImageFileFormat<PictureEditorFile>.ToBytes(PictureEditorFile file) => PictureEditorWriter.ToBytes(file);

  /// <summary>Always 160.</summary>
  public int Width => FixedWidth;

  /// <summary>Always 192.</summary>
  public int Height => FixedHeight;

  /// <summary>Raw pixel data (7680 bytes, 2bpp packed: 4 pixels per byte, 40 bytes per row, 192 rows).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Converts this image to a platform-independent <see cref="RawImage"/> in Indexed8 format with a 4-entry palette.</summary>
  public static RawImage ToRawImage(PictureEditorFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var indices = new byte[FixedWidth * FixedHeight];

    for (var y = 0; y < FixedHeight; ++y)
      for (var x = 0; x < FixedWidth; ++x) {
        var byteIndex = y * BytesPerRow + x / 4;
        var shift = (3 - (x % 4)) * 2;
        var colorIndex = (file.PixelData[byteIndex] >> shift) & 0x03;
        indices[y * FixedWidth + x] = (byte)colorIndex;
      }

    var palette = new byte[4 * 3];
    for (var i = 0; i < 4; ++i) {
      palette[i * 3] = (byte)((_DefaultPalette[i] >> 16) & 0xFF);
      palette[i * 3 + 1] = (byte)((_DefaultPalette[i] >> 8) & 0xFF);
      palette[i * 3 + 2] = (byte)(_DefaultPalette[i] & 0xFF);
    }

    return new() {
      Width = FixedWidth,
      Height = FixedHeight,
      Format = PixelFormat.Indexed8,
      PixelData = indices,
      Palette = palette,
      PaletteCount = 4,
    };
  }

  /// <summary>Creates a Picture Editor file from a platform-independent <see cref="RawImage"/>.</summary>
  public static PictureEditorFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"Expected {PixelFormat.Indexed8} but got {image.Format}.", nameof(image));
    if (image.Width != FixedWidth || image.Height != FixedHeight)
      throw new ArgumentException($"Expected {FixedWidth}x{FixedHeight} but got {image.Width}x{image.Height}.", nameof(image));
    if (image.PaletteCount > 4)
      throw new ArgumentException($"Expected at most 4 palette entries but got {image.PaletteCount}.", nameof(image));

    var pixelData = new byte[ExpectedFileSize];

    for (var y = 0; y < FixedHeight; ++y)
      for (var x = 0; x < FixedWidth; ++x) {
        var index = image.PixelData[y * FixedWidth + x] & 0x03;
        var byteIndex = y * BytesPerRow + x / 4;
        var shift = (3 - (x % 4)) * 2;
        pixelData[byteIndex] |= (byte)(index << shift);
      }

    return new() { PixelData = pixelData };
  }
}
