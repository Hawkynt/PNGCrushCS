using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Pdf;

/// <summary>In-memory representation of a PDF file's extracted raster images.</summary>
[FormatMagicBytes([0x25, 0x50, 0x44, 0x46])] // %PDF
public sealed class PdfFile : IImageFileFormat<PdfFile>, IMultiImageFileFormat<PdfFile> {

  static string IImageFileFormat<PdfFile>.PrimaryExtension => ".pdf";
  static string[] IImageFileFormat<PdfFile>.FileExtensions => [".pdf"];
  static FormatCapability IImageFileFormat<PdfFile>.Capabilities => FormatCapability.VariableResolution | FormatCapability.MultiImage;

  static bool? IImageFileFormat<PdfFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 4)
      return null;

    return header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46;
  }

  static PdfFile IImageFileFormat<PdfFile>.FromFile(FileInfo file) => PdfReader.FromFile(file);
  static PdfFile IImageFileFormat<PdfFile>.FromBytes(byte[] data) => PdfReader.FromBytes(data);
  static PdfFile IImageFileFormat<PdfFile>.FromStream(Stream stream) => PdfReader.FromStream(stream);
  static byte[] IImageFileFormat<PdfFile>.ToBytes(PdfFile file) => PdfWriter.ToBytes(file);

  /// <summary>All extracted raster images from the PDF.</summary>
  public IReadOnlyList<PdfImage> Images { get; init; } = [];

  /// <summary>Converts the first extracted image to a <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(PdfFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Images.Count == 0)
      throw new InvalidOperationException("PDF contains no extractable images.");

    return file.Images[0].ToRawImage();
  }

  /// <summary>Creates a single-image PDF from a <see cref="RawImage"/>.</summary>
  public static PdfFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format is not (PixelFormat.Rgb24 or PixelFormat.Gray8))
      throw new ArgumentException($"Expected Rgb24 or Gray8 but got {image.Format}.", nameof(image));

    var pdfImage = new PdfImage {
      Width = image.Width,
      Height = image.Height,
      BitsPerComponent = 8,
      ColorSpace = image.Format == PixelFormat.Rgb24 ? PdfColorSpace.DeviceRGB : PdfColorSpace.DeviceGray,
      PixelData = image.PixelData[..],
    };

    return new PdfFile { Images = [pdfImage] };
  }

  /// <summary>Returns the number of images extracted from this PDF file.</summary>
  public static int ImageCount(PdfFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return file.Images.Count;
  }

  /// <summary>Converts the image at the given index to a <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(PdfFile file, int index) {
    ArgumentNullException.ThrowIfNull(file);
    if ((uint)index >= (uint)file.Images.Count)
      throw new ArgumentOutOfRangeException(nameof(index));

    return file.Images[index].ToRawImage();
  }
}
