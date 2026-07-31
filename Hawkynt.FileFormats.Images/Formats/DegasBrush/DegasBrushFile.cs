using System;
using FileFormat.Core;

namespace FileFormat.DegasBrush;

/// <summary>In-memory representation of a DEGAS Elite brush (.bru).</summary>
/// <remarks>
/// The smallest picture in the catalogue: an eight-by-eight brush shape stored as sixty-four bytes
/// that are each exactly 0 or 1. Spending a whole byte on a bit is what makes the format
/// self-identifying — nothing else of that length is made only of those two values — and it is why
/// there is no header at all.
/// </remarks>
public readonly record struct DegasBrushFile
  : IImageFormatReader<DegasBrushFile>, IImageToRawImage<DegasBrushFile>,
    IImageFromRawImage<DegasBrushFile>, IImageFormatWriter<DegasBrushFile> {

  /// <summary>Pixels across and down.</summary>
  public const int Size = 8;

  /// <summary>Total file size.</summary>
  public const int FileSize = Size * Size;

  static string IImageFormatMetadata<DegasBrushFile>.PrimaryExtension => ".bru";
  static string[] IImageFormatMetadata<DegasBrushFile>.FileExtensions => [".bru"];
  static DegasBrushFile IImageFormatReader<DegasBrushFile>.FromSpan(ReadOnlySpan<byte> data)
    => DegasBrushReader.FromSpan(data);
  static byte[] IImageFormatWriter<DegasBrushFile>.ToBytes(DegasBrushFile file)
    => DegasBrushWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<DegasBrushFile>.VideoModes => [
    new("Brush", [(Size, Size)], [2])
  ];

  /// <summary>The shape, one byte per pixel holding 0 or 1.</summary>
  public byte[] Shape { get; init; }

  private static readonly byte[] _Palette = [0, 0, 0, 255, 255, 255];

  public static RawImage ToRawImage(DegasBrushFile file) {
    var pixels = new byte[FileSize];
    (file.Shape ?? []).AsSpan(0, Math.Min(file.Shape?.Length ?? 0, FileSize)).CopyTo(pixels);

    return new() {
      Width = Size,
      Height = Size,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = (byte[])_Palette.Clone(),
      PaletteCount = 2,
    };
  }

  public static DegasBrushFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != Size || image.Height != Size)
      throw new ArgumentException($"Expected {Size}x{Size} but got {image.Width}x{image.Height}.", nameof(image));

    var gray = PixelConverter.Convert(image, PixelFormat.Gray8);
    var shape = new byte[FileSize];
    for (var i = 0; i < FileSize; ++i)
      shape[i] = (byte)(gray.PixelData[i] >= 128 ? 1 : 0);

    return new() { Shape = shape };
  }
}
