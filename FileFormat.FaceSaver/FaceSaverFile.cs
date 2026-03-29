using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.FaceSaver;

/// <summary>In-memory representation of a Usenix FaceSaver image.</summary>
/// <remarks>
/// FaceSaver is a text-based grayscale image format created by Metron Computerware
/// for storing facial portraits of Usenix conference attendees. The format consists
/// of an ASCII header with key: value fields followed by hex-encoded pixel data.
/// Pixels are 8-bit grayscale stored bottom-to-top.
/// </remarks>
public sealed class FaceSaverFile : IImageFileFormat<FaceSaverFile> {

  static string IImageFileFormat<FaceSaverFile>.PrimaryExtension => ".face";
  static string[] IImageFileFormat<FaceSaverFile>.FileExtensions => [".face", ".fac"];
  static FaceSaverFile IImageFileFormat<FaceSaverFile>.FromFile(FileInfo file) => FaceSaverReader.FromFile(file);
  static FaceSaverFile IImageFileFormat<FaceSaverFile>.FromBytes(byte[] data) => FaceSaverReader.FromBytes(data);
  static FaceSaverFile IImageFileFormat<FaceSaverFile>.FromStream(Stream stream) => FaceSaverReader.FromStream(stream);
  static byte[] IImageFileFormat<FaceSaverFile>.ToBytes(FaceSaverFile file) => FaceSaverWriter.ToBytes(file);

  /// <summary>Image width in pixels (from PicData field).</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels (from PicData field).</summary>
  public int Height { get; init; }

  /// <summary>Bits per pixel (typically 8).</summary>
  public int BitsPerPixel { get; init; } = 8;

  /// <summary>Display width for square-pixel correction (from Image field). 0 if same as Width.</summary>
  public int ImageWidth { get; init; }

  /// <summary>Display height for square-pixel correction (from Image field). 0 if same as Height.</summary>
  public int ImageHeight { get; init; }

  /// <summary>First name from header.</summary>
  public string FirstName { get; init; } = string.Empty;

  /// <summary>Last name from header.</summary>
  public string LastName { get; init; } = string.Empty;

  /// <summary>Email from header.</summary>
  public string Email { get; init; } = string.Empty;

  /// <summary>Telephone from header.</summary>
  public string Telephone { get; init; } = string.Empty;

  /// <summary>Company from header.</summary>
  public string Company { get; init; } = string.Empty;

  /// <summary>Address line 1 from header.</summary>
  public string Address1 { get; init; } = string.Empty;

  /// <summary>Address line 2 from header.</summary>
  public string Address2 { get; init; } = string.Empty;

  /// <summary>City/state/zip from header.</summary>
  public string CityStateZip { get; init; } = string.Empty;

  /// <summary>Date from header.</summary>
  public string Date { get; init; } = string.Empty;

  /// <summary>Grayscale pixel data (1 byte per pixel, bottom-to-top order in file but stored top-to-bottom here).</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(FaceSaverFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Gray8,
      PixelData = file.PixelData[..],
    };
  }

  public static FaceSaverFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    byte[] pixels;
    if (image.Format == PixelFormat.Gray8)
      pixels = image.PixelData[..];
    else {
      var converted = PixelConverter.Convert(image, PixelFormat.Gray8);
      pixels = converted.PixelData;
    }

    return new() {
      Width = image.Width,
      Height = image.Height,
      BitsPerPixel = 8,
      PixelData = pixels,
    };
  }

  public static bool? MatchesSignature(ReadOnlySpan<byte> data) {
    if (data.Length < 10)
      return null;

    // FaceSaver files start with ASCII header lines like "FirstName: " or "LastName: " or "PicData: "
    // Check for ASCII printable text with a colon somewhere in the first line
    for (var i = 0; i < Math.Min(data.Length, 80); ++i)
      if (data[i] == '\n') {
        // Check if we found "Key: value" pattern before the newline
        for (var j = 0; j < i; ++j)
          if (data[j] == ':' && j > 0)
            return null; // Possible but not certain - text-based format

        return false;
      }

    return null;
  }
}
