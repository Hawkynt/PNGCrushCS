using System;
using FileFormat.Core;

namespace FileFormat.BennetYeeFace;

/// <summary>In-memory representation of a Bennet Yee Face (.ybm) monochrome bitmap image.</summary>
public readonly record struct BennetYeeFaceFile : IImageFormatReader<BennetYeeFaceFile>, IImageToRawImage<BennetYeeFaceFile>, IImageFromRawImage<BennetYeeFaceFile>, IImageFormatWriter<BennetYeeFaceFile> {

  static string IImageFormatMetadata<BennetYeeFaceFile>.PrimaryExtension => ".ybm";
  static string[] IImageFormatMetadata<BennetYeeFaceFile>.FileExtensions => [".ybm"];
  static BennetYeeFaceFile IImageFormatReader<BennetYeeFaceFile>.FromSpan(ReadOnlySpan<byte> data) => BennetYeeFaceReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<BennetYeeFaceFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [2])
  ];
  static byte[] IImageFormatWriter<BennetYeeFaceFile>.ToBytes(BennetYeeFaceFile file) => BennetYeeFaceWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>1bpp packed pixel data, MSB first, rows padded to 16-bit (word) boundary.</summary>
  public byte[] PixelData { get; init; }

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  /// <summary>Computes the row stride: ((width + 15) / 16) * 2 bytes.</summary>
  internal static int ComputeStride(int width) => ((width + 15) / 16) * 2;

  /// <summary>Draws the face as a picture.</summary>
  /// <remarks>
  /// A .ybm row is padded out to a whole word, where an <see cref="PixelFormat.Indexed1"/> picture
  /// has nothing between its rows at all. Handing the stored rows over unchanged left every row
  /// after the first out of step by up to fifteen pixels, which for the width this format is
  /// usually written at — a face, not a multiple of sixteen — is most of the picture.
  /// </remarks>
  public static RawImage ToRawImage(BennetYeeFaceFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed1,
    PixelData = PackedRows.DropRowPadding(file.PixelData, file.Width, file.Height, 1, ComputeStride(file.Width)),
    Palette = _BlackWhitePalette[..],
    PaletteCount = 2,
  };

  public static BennetYeeFaceFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Indexed1);

    // The picture's rows run into one another; the file's start on a word. Copying row for row at
    // the file's stride, as this used to, only happens to be right where the two strides agree.
    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = PackedRows.AddRowPadding(image.PixelData, image.Width, image.Height, 1, ComputeStride(image.Width)),
    };
  }
}
