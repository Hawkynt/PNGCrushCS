using System;
using FileFormat.Core;

namespace FileFormat.BioRadPic;

/// <summary>In-memory representation of a Bio-Rad PIC (confocal microscopy) image.</summary>
[FormatMagicBytes([0x39, 0x30], offset: 54)]
public readonly record struct BioRadPicFile : IImageFormatReader<BioRadPicFile>, IImageToRawImage<BioRadPicFile>, IImageFromRawImage<BioRadPicFile>, IImageFormatWriter<BioRadPicFile> {

  static string IImageFormatMetadata<BioRadPicFile>.PrimaryExtension => ".pic";
  static string[] IImageFormatMetadata<BioRadPicFile>.FileExtensions => [".pic"];
  static BioRadPicFile IImageFormatReader<BioRadPicFile>.FromSpan(ReadOnlySpan<byte> data) => BioRadPicReader.FromSpan(data);
  static byte[] IImageFormatWriter<BioRadPicFile>.ToBytes(BioRadPicFile file) => BioRadPicWriter.ToBytes(file);

  /// <summary>Width of the image in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Height of the image in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Number of images in the file (we read/write only the first).</summary>
  public int NumImages { get; init; }

  /// <summary>True if pixels are 8-bit unsigned bytes; false if 16-bit unsigned.</summary>
  public bool ByteFormat { get; init; }

  /// <summary>Null-terminated filename stored in the header (up to 32 characters).</summary>
  public string Name { get; init; }

  /// <summary>Objective lens magnification.</summary>
  public int Lens { get; init; }

  /// <summary>Additional magnification factor.</summary>
  public float MagFactor { get; init; }

  /// <summary>Minimum ramp1 value.</summary>
  public short Ramp1Min { get; init; }

  /// <summary>Maximum ramp1 value.</summary>
  public short Ramp1Max { get; init; }

  /// <summary>Minimum ramp2 value.</summary>
  public short Ramp2Min { get; init; }

  /// <summary>Maximum ramp2 value.</summary>
  public short Ramp2Max { get; init; }

  /// <summary>Color for first ramp.</summary>
  public ushort Color1 { get; init; }

  /// <summary>Color for second ramp.</summary>
  public ushort Color2 { get; init; }

  /// <summary>Merge status.</summary>
  public short Merged { get; init; }

  /// <summary>1 if notes have been edited.</summary>
  public short Edited { get; init; }

  /// <summary>Non-zero if notes exist appended after pixel data.</summary>
  public int Notes { get; init; }

  /// <summary>Raw pixel data for the first image. For 8-bit: nx*ny bytes. For 16-bit: nx*ny*2 bytes (LE unsigned).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(BioRadPicFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = file.ByteFormat ? PixelFormat.Gray8 : PixelFormat.Gray16,
      PixelData = file.PixelData[..],
    };
  }

  public static BioRadPicFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    bool isByte;
    switch (image.Format) {
      case PixelFormat.Gray8:
        isByte = true;
        break;
      case PixelFormat.Gray16:
        isByte = false;
        break;
      default:
        throw new ArgumentException($"Expected {PixelFormat.Gray8} or {PixelFormat.Gray16} but got {image.Format}.", nameof(image));
    }

    return new() {
      Width = image.Width,
      Height = image.Height,
      ByteFormat = isByte,
      PixelData = image.PixelData[..],
    };
  }
}
