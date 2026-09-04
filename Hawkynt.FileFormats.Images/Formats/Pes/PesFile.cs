using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Pes;

/// <summary>In-memory representation of a Brother PES embroidery file.</summary>
/// <remarks>
/// A PES is a needle path. It is read here and not written, and that is a
/// decision about what the file is rather than about the work: turning a picture
/// into a PES means deciding where to put every stitch, which is a raster-to-
/// needlework conversion and not a serialiser. Writing one from stitches that a
/// caller already has is a different matter, and <see cref="PesWriter"/> does
/// exactly that without claiming the registry's writer contract.
/// </remarks>
[FormatDetectionPriority(180)]
[FormatMimeType("application/x-melco-pes", "image/x-pes")]
public sealed class PesFile : IImageFormatReader<PesFile>, IImageToRawImage<PesFile> {

  static string IImageFormatMetadata<PesFile>.PrimaryExtension => ".pes";
  static string[] IImageFormatMetadata<PesFile>.FileExtensions => [".pes"];
  static PesFile IImageFormatReader<PesFile>.FromSpan(ReadOnlySpan<byte> data) => PesReader.FromSpan(data);

  static bool? IImageFormatMetadata<PesFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 4
       && header[0] == (byte)'#' && header[1] == (byte)'P' && header[2] == (byte)'E' && header[3] == (byte)'S'
      ? true : null;

  public string Version { get; init; } = "0001";

  public IReadOnlyList<PesStitchBlock> Blocks { get; init; } = [];

  public int MinX { get; init; }
  public int MinY { get; init; }
  public int MaxX { get; init; }
  public int MaxY { get; init; }

  /// <summary>The canvas the stitches need, which is the extent they cover.</summary>
  public int Width => Math.Max(1, this.MaxX - this.MinX + 1);

  public int Height => Math.Max(1, this.MaxY - this.MinY + 1);

  public static RawImage ToRawImage(PesFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Blocks.Count == 0)
      throw new ArgumentException("PES file carries no stitches.", nameof(file));

    var width = file.Width;
    var height = file.Height;
    return new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = PesRenderer.Render(file, width, height),
    };
  }
}
