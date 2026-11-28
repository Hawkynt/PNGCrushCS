using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Fif;

/// <summary>Which member of the Iterated Systems fractal family a file is.</summary>
public enum FifVariant {
  /// <summary>FTC 1.1, the fixed 67-byte-header profile.</summary>
  Fixed,
  /// <summary>FTC 3.1–3.3, region-based, bit or arithmetic coded, optionally on an FTT template.</summary>
  Region,
  /// <summary>FIF1, the progressive tiled container.</summary>
  Progressive,
}

/// <summary>In-memory representation of an Iterated Systems fractal image (FTC / FIF).</summary>
/// <remarks>
/// Iterated Systems' fractal transform coding (1990s; Microsoft Encarta, the ColorBox and Fractal
/// Imager products): every 4×4 block is an affine copy of a larger block elsewhere in the picture,
/// and the picture is whatever those copies converge to. The pixels held here are the decode at the
/// image's original size, as Iterated's own decoder produces it, or at the size passed to
/// <see cref="FifReader.FromFile(FileInfo, int, int)"/>. Writing produces an FTC 3.1 file
/// from a fractal search of the picture.
/// </remarks>
[FormatMagicBytes([0x46, 0x54, 0x43, 0x00])]
[FormatMagicBytes([0x46, 0x49, 0x46, 0x01])]
[FormatMimeType("image/x-fif", "image/fif")]
public readonly record struct FifFile : IImageFormatReader<FifFile>, IImageToRawImage<FifFile>, IImageFromRawImage<FifFile>, IImageFormatWriter<FifFile> {

  static string IImageFormatMetadata<FifFile>.PrimaryExtension => ".fif";
  static string[] IImageFormatMetadata<FifFile>.FileExtensions => [".fif", ".fi", ".fsm"];
  static FifFile IImageFormatReader<FifFile>.FromSpan(ReadOnlySpan<byte> data) => FifReader.FromSpan(data);
  static FifFile IImageFormatReader<FifFile>.FromFile(FileInfo file) => FifReader.FromFile(file);
  static byte[] IImageFormatWriter<FifFile>.ToBytes(FifFile file) => FifWriter.ToBytes(file);

  /// <summary>
  /// An FTC signature only counts with a profile behind it — the fixed 1.1 header, a 3.x outer
  /// header, or the video profiles this reader names and refuses — since other formats (Atari Falcon
  /// <c>.ftc</c> among them) use the extension without the signature.
  /// </summary>
  static bool? IImageFormatMetadata<FifFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (FifProgressive.Matches(header))
      return header.Length < 6 || header[4] == (byte)'u';
    if (header.Length < 6 || header[0] != 'F' || header[1] != 'T' || header[2] != 'C' || header[3] != 0)
      return null;

    return header[5] == 3 && header[4] is >= 1 and <= 4 || header[4] is 1 or 2 && header[5] is 1 or 2;
  }

  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>The family member the file was read from; the writer always produces <see cref="FifVariant.Region"/>.</summary>
  public FifVariant Variant { get; init; }

  /// <summary>RGB, three bytes a pixel, rows top to bottom.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(FifFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Rgb24,
    PixelData = file.PixelData[..(file.Width * file.Height * 3)],
  };

  public static FifFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Rgb24);
    return new() { Width = image.Width, Height = image.Height, Variant = FifVariant.Region, PixelData = image.PixelData[..] };
  }
}
