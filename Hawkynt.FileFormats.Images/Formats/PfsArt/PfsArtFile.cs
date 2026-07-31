using System;
using FileFormat.Core;

namespace FileFormat.PfsArt;

/// <summary>In-memory representation of a PFS: 1st Publisher clip-art image (.art).</summary>
/// <remarks>
/// <para>
/// 1st Publisher was a late-1980s desktop-publishing package for DOS, and its clip art is about as
/// simple as a raster format gets: four 16-bit words of header, of which two are the dimensions, and
/// then one bit a pixel with rows padded out to a whole 16-bit word. A set bit is black.
/// </para>
/// <para>
/// It shares the .art extension with the Build Engine tile archives that
/// <see cref="FileFormat.Art.ArtFile"/> reads, which are an unrelated format — so a file written by
/// any modern tool as "ART" was being handed to a reader expecting Duke Nukem tile sheets, and
/// rejected for having a version field of 2424832. The two are told apart by content: a Build archive
/// opens with a version word of 1, this opens with a zero word.
/// </para>
/// </remarks>
public readonly record struct PfsArtFile : IImageFormatReader<PfsArtFile>, IImageToRawImage<PfsArtFile> {

  /// <summary>Header size: two reserved words interleaved with the two dimensions.</summary>
  internal const int HeaderSize = 8;

  static string IImageFormatMetadata<PfsArtFile>.PrimaryExtension => ".art";
  static string[] IImageFormatMetadata<PfsArtFile>.FileExtensions => [".art"];
  static PfsArtFile IImageFormatReader<PfsArtFile>.FromSpan(ReadOnlySpan<byte> data)
    => PfsArtReader.FromSpan(data);

  static bool? IImageFormatMetadata<PfsArtFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < HeaderSize)
      return null;

    // No magic to go on, so the shape of the header is the evidence: the first and third words are
    // reserved and zero, and the two dimensions are positive and sane.
    var reservedAreZero = header[0] == 0 && header[1] == 0 && header[4] == 0 && header[5] == 0;
    var width = header[2] | (header[3] << 8);
    var height = header[6] | (header[7] << 8);
    return reservedAreZero && width is > 0 and <= 4096 && height is > 0 and <= 4096 ? true : null;
  }

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>One byte a pixel: 0 for black, 255 for white.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(PfsArtFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Gray8,
    PixelData = file.PixelData[..],
  };
}
