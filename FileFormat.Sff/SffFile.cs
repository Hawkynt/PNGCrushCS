using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Sff;

/// <summary>In-memory representation of a SFF (Structured Fax File).</summary>
[FormatMagicBytes([0x53, 0x46, 0x46, 0x46])]
public readonly record struct SffFile : IImageFormatReader<SffFile>, IImageToRawImage<SffFile>, IImageFromRawImage<SffFile>, IImageFormatWriter<SffFile> {

  static string IImageFormatMetadata<SffFile>.PrimaryExtension => ".sff";
  static string[] IImageFormatMetadata<SffFile>.FileExtensions => [".sff"];
  static SffFile IImageFormatReader<SffFile>.FromSpan(ReadOnlySpan<byte> data) => SffReader.FromSpan(data);
  static byte[] IImageFormatWriter<SffFile>.ToBytes(SffFile file) => SffWriter.ToBytes(file);

  /// <summary>File format version (typically 1).</summary>
  public byte Version { get; init; }

  /// <summary>The fax pages in this file.</summary>
  public IReadOnlyList<SffPage> Pages { get; init; }

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  public static RawImage ToRawImage(SffFile file) {
    if (file.Pages.Count == 0)
      throw new InvalidOperationException("SFF file contains no pages.");

    var page = file.Pages[0];
    return new() {
      Width = page.Width,
      Height = page.Height,
      Format = PixelFormat.Indexed1,
      PixelData = page.PixelData[..],
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  public static SffFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException("RawImage must use PixelFormat.Indexed1.", nameof(image));

    return new() {
      Pages = [
        new() {
          Width = image.Width,
          Height = image.Height,
          PixelData = image.PixelData[..],
        }
      ],
    };
  }
}
