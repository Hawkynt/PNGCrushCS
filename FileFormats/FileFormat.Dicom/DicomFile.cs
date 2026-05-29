using System;
using FileFormat.Core;

namespace FileFormat.Dicom;

/// <summary>In-memory representation of a DICOM image (basic subset).</summary>
[FormatMagicBytes([0x44, 0x49, 0x43, 0x4D], offset: 128)]
public readonly record struct DicomFile : IImageFormatReader<DicomFile>, IImageToRawImage<DicomFile>, IImageFromRawImage<DicomFile>, IImageFormatWriter<DicomFile> {

  static string IImageFormatMetadata<DicomFile>.PrimaryExtension => ".dcm";
  static string[] IImageFormatMetadata<DicomFile>.FileExtensions => [".dcm", ".dicom", ".acr", ".dic", ".dc3"];
  static DicomFile IImageFormatReader<DicomFile>.FromSpan(ReadOnlySpan<byte> data) => DicomReader.FromSpan(data);
  static byte[] IImageFormatWriter<DicomFile>.ToBytes(DicomFile file) => DicomWriter.ToBytes(file);
  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }
  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }
  /// <summary>Bits allocated per sample (8 or 16).</summary>
  public int BitsAllocated { get; init; }
  /// <summary>Bits stored per sample (may be less than BitsAllocated).</summary>
  public int BitsStored { get; init; }
  /// <summary>Number of samples per pixel (1 for mono, 3 for RGB).</summary>
  public int SamplesPerPixel { get; init; }
  /// <summary>Photometric interpretation.</summary>
  public DicomPhotometricInterpretation PhotometricInterpretation { get; init; }
  /// <summary>Raw pixel data.</summary>
  public byte[] PixelData { get; init; }
  /// <summary>Window center for display mapping.</summary>
  public double WindowCenter { get; init; }
  /// <summary>Window width for display mapping.</summary>
  public double WindowWidth { get; init; }

  public static RawImage ToRawImage(DicomFile file) {

    PixelFormat format;
    if (file.SamplesPerPixel == 3)
      format = PixelFormat.Rgb24;
    else if (file.BitsAllocated == 16)
      format = PixelFormat.Gray16;
    else if (file.BitsAllocated == 8)
      format = PixelFormat.Gray8;
    else
      throw new ArgumentException($"Unsupported DICOM pixel layout: BitsAllocated={file.BitsAllocated}, SamplesPerPixel={file.SamplesPerPixel}.", nameof(file));

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = format,
      PixelData = file.PixelData[..],
    };
  }

  public static DicomFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    return image.Format switch {
      PixelFormat.Gray8 => new() {
        Width = image.Width,
        Height = image.Height,
        BitsAllocated = 8,
        BitsStored = 8,
        SamplesPerPixel = 1,
        PhotometricInterpretation = DicomPhotometricInterpretation.Monochrome2,
        PixelData = image.PixelData[..],
      },
      PixelFormat.Gray16 => new() {
        Width = image.Width,
        Height = image.Height,
        BitsAllocated = 16,
        BitsStored = 16,
        SamplesPerPixel = 1,
        PhotometricInterpretation = DicomPhotometricInterpretation.Monochrome2,
        PixelData = image.PixelData[..],
      },
      PixelFormat.Rgb24 => new() {
        Width = image.Width,
        Height = image.Height,
        BitsAllocated = 8,
        BitsStored = 8,
        SamplesPerPixel = 3,
        PhotometricInterpretation = DicomPhotometricInterpretation.Rgb,
        PixelData = image.PixelData[..],
      },
      _ => throw new ArgumentException($"Unsupported pixel format for DICOM: {image.Format}", nameof(image)),
    };
  }
}
