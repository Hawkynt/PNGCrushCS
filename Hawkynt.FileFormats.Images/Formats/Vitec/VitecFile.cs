using System;
using FileFormat.Core;

namespace FileFormat.Vitec;

/// <summary>In-memory representation of a VITec image (.vit).</summary>
/// <remarks>
/// Big-endian throughout, and made of two headers rather than one: four bytes give the length of the
/// first, which is followed by four more giving the length of the second, and the picture starts
/// after both. The second header is the one that describes the picture — its width, its height, how
/// many samples make up a pixel and whether it is grey — and it also repeats the size of the data,
/// which is the second header and the samples together.
/// <para/>
/// Both statements are checked, and in the sample both are exact: four plus a hundred and twenty
/// plus a hundred and forty-four plus four hundred by four hundred by one sample is the file's
/// length to the byte, and the stated data size is the second header plus that same product.
/// <para/>
/// The string <c>VITec</c> sits at offset thirty-two. It is not part of the published layout, but it
/// is what the format is called and it is a far better signature than four bytes of which two are
/// small numbers, so it is required.
/// </remarks>
public readonly record struct VitecFile
  : IImageFormatReader<VitecFile>, IImageToRawImage<VitecFile>,
    IImageFromRawImage<VitecFile>, IImageFormatWriter<VitecFile> {

  /// <summary>The four bytes the file opens with.</summary>
  public static ReadOnlySpan<byte> Magic => [0x00, 0x5B, 0x07, 0x20];

  /// <summary>The name the format goes by, which the header carries in the clear.</summary>
  public static ReadOnlySpan<byte> Name => [(byte)'V', (byte)'I', (byte)'T', (byte)'e', (byte)'c'];

  /// <summary>Where that name sits.</summary>
  public const int NameOffset = 32;

  /// <summary>The length of the first header sits directly after the magic.</summary>
  public const int FirstHeaderLengthOffset = 4;

  static string IImageFormatMetadata<VitecFile>.PrimaryExtension => ".vit";
  static string[] IImageFormatMetadata<VitecFile>.FileExtensions => [".vit"];
  static VitecFile IImageFormatReader<VitecFile>.FromSpan(ReadOnlySpan<byte> data) => VitecReader.FromSpan(data);
  static byte[] IImageFormatWriter<VitecFile>.ToBytes(VitecFile file) => VitecWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<VitecFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [256, 16777216])
  ];

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>How many samples make up a pixel: one for grey, three for colour.</summary>
  public int Samples { get; init; }

  /// <summary>The samples as they stand in the file.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(VitecFile file) {
    if (file.Samples == 1)
      return new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Gray8,
        PixelData = file.PixelData[..],
      };

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  /// <summary>A grey goes out as the one-sample case, anything else as the three.</summary>
  public static VitecFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    if (image.Format == PixelFormat.Gray8)
      return new() {
        Width = image.Width,
        Height = image.Height,
        Samples = 1,
        PixelData = image.PixelData,
      };

    return new() {
      Width = image.Width,
      Height = image.Height,
      Samples = 3,
      PixelData = image.ToRgb24(),
    };
  }
}
