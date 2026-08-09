using System;
using FileFormat.Core;

namespace FileFormat.PhotoSuiteProject;

/// <summary>In-memory representation of the picture in an MGI PhotoSuite project (.pzp).</summary>
/// <remarks>
/// A PhotoSuite project is a Microsoft compound document — the same container Word and Excel use —
/// and the pictures the project is built from sit inside it as whole PNG files. There is no
/// published inventory of the storages and streams a <c>.pzp</c> holds, and XnView does not use one:
/// its reader checks the compound-document signature, then walks the file from offset 512 looking
/// for the eight bytes a PNG opens with, and decodes the first one it finds. Later ones are counted
/// as further pages.
/// <para/>
/// This reader does the same, and refuses anything that is not a compound document or carries no PNG
/// behind its header, so a project of nothing but text is refused rather than drawn empty. The scan
/// steps four bytes at a time, which is where a compound document puts the start of a stream and
/// where XnView looks.
/// </remarks>
public readonly record struct PhotoSuiteProjectFile
  : IImageFormatReader<PhotoSuiteProjectFile>, IImageToRawImage<PhotoSuiteProjectFile> {

  /// <summary>The eight bytes a Microsoft compound document opens with.</summary>
  public static ReadOnlySpan<byte> Signature => [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

  /// <summary>The eight bytes a PNG opens with.</summary>
  public static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

  /// <summary>Where the search for a picture begins, which is behind the container's own header.</summary>
  public const int ScanStart = 512;

  /// <summary>The step the search takes, which is where a stream can begin.</summary>
  public const int ScanStep = 4;

  static string IImageFormatMetadata<PhotoSuiteProjectFile>.PrimaryExtension => ".pzp";
  static string[] IImageFormatMetadata<PhotoSuiteProjectFile>.FileExtensions => [".pzp"];
  static PhotoSuiteProjectFile IImageFormatReader<PhotoSuiteProjectFile>.FromSpan(ReadOnlySpan<byte> data)
    => PhotoSuiteProjectReader.FromSpan(data);

  static VideoMode[] IImageFormatMetadata<PhotoSuiteProjectFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  /// <summary>
  /// Abstains rather than claiming a compound document: every Office file in the world opens with
  /// the same eight bytes, and whether this one holds a picture is not known until it is walked.
  /// </summary>
  static bool? IImageFormatMetadata<PhotoSuiteProjectFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < Signature.Length)
      return null;

    return header[..Signature.Length].SequenceEqual(Signature) ? null : false;
  }

  /// <summary>Image width in pixels, as the PNG inside states it.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>The decoded picture, three bytes a pixel, red first.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(PhotoSuiteProjectFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Rgb24,
    PixelData = file.PixelData[..],
  };
}
