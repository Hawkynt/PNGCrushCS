using System;
using FileFormat.Core;

namespace FileFormat.Heif;

/// <summary>In-memory representation of a HEIF/HEIC (ISO/IEC 23008-12) image at the container level.</summary>
/// <remarks>
/// This reads the container and does not write, and the writer beside it is not registered.
/// <para/>
/// There is no HEVC codec here in either direction. Every HEIF anything else wrote holds an
/// HEVC-coded item, so <see cref="HeifReader.FromSpan"/> refuses one rather than hand back a raster
/// it could not fill; <see cref="ReadImageInfo"/> still reports the extent, which ispe and clap
/// state in the container and not in the codestream.
/// <para/>
/// The writer is unregistered for the mirror-image reason. What it produced was an ISO base media
/// container with the picture's own bytes inside it and no iinf box that names the item to decode,
/// which is not HEIF: nothing that reads HEIF can read one, and the reference tool says so. It
/// round-tripped only because our own reader took the same bytes back out again, which is the exact
/// thing the writer-acceptance fixture exists to catch.
/// <para/>
/// Registering it would count a format as writable on the strength of a file no other program will
/// open. The encoder is the missing piece, and until there is one this reads.
/// </remarks>
public readonly record struct HeifFile : IImageFormatReader<HeifFile>, IImageToRawImage<HeifFile>, IImageInfoReader<HeifFile> {

  static string IImageFormatMetadata<HeifFile>.PrimaryExtension => ".heic";
  static string[] IImageFormatMetadata<HeifFile>.FileExtensions => [".heic", ".heif"];
  static HeifFile IImageFormatReader<HeifFile>.FromSpan(ReadOnlySpan<byte> data) => HeifReader.FromSpan(data);

  /// <summary>
  /// The extent the container states, which stays readable for an HEVC-coded item whose pixels are
  /// not. <see cref="HeifReader.FromSpan"/> refuses those; this does not have to.
  /// </summary>
  public static ImageInfo? ReadImageInfo(ReadOnlySpan<byte> header) => HeifReader.ReadImageInfo(header);

  static bool? IImageFormatMetadata<HeifFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 12 || header[4] != 0x66 || header[5] != 0x74 || header[6] != 0x79 || header[7] != 0x70)
      return null;
    if (header[8] == (byte)'h' && header[9] == (byte)'e' && header[10] == (byte)'i' && header[11] == (byte)'c')
      return true;
    if (header[8] == (byte)'h' && header[9] == (byte)'e' && header[10] == (byte)'i' && header[11] == (byte)'x')
      return true;
    if (header[8] == (byte)'h' && header[9] == (byte)'e' && header[10] == (byte)'v' && header[11] == (byte)'c')
      return true;
    if (header[8] == (byte)'m' && header[9] == (byte)'i' && header[10] == (byte)'f' && header[11] == (byte)'1')
      return true;
    return null;
  }

  /// <summary>Image width in pixels: the clean aperture where one is given, else the ispe extent.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels: the clean aperture where one is given, else the ispe extent.</summary>
  public int Height { get; init; }

  /// <summary>
  /// Raw pixel data (Rgb24 format, 3 bytes per pixel) for container-level round-trip, cropped to
  /// <see cref="Width"/> by <see cref="Height"/> so the encoder's padding is not handed back.
  /// </summary>
  public byte[] PixelData { get; init; }

  /// <summary>The major brand from the ftyp box (e.g. "heic", "heix", "hevc", "mif1").</summary>
  public string Brand { get; init; }

  /// <summary>Raw image payload data stored in the mdat box (HEVC NAL units or uncompressed).</summary>
  public byte[] RawImageData { get; init; }

  public static RawImage ToRawImage(HeifFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static HeifFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Rgb24);

    var pixelData = image.PixelData[..];
    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = pixelData,
      RawImageData = pixelData[..],
    };
  }
}
