using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Sff;

/// <summary>In-memory representation of a SFF (Structured Fax File).</summary>
[FormatMagicBytes([0x53, 0x46, 0x46, 0x46])]
public sealed class SffFile : IImageFileFormat<SffFile> {

  static string IImageFileFormat<SffFile>.PrimaryExtension => ".sff";
  static string[] IImageFileFormat<SffFile>.FileExtensions => [".sff"];
  static SffFile IImageFileFormat<SffFile>.FromFile(FileInfo file) => SffReader.FromFile(file);
  static SffFile IImageFileFormat<SffFile>.FromBytes(byte[] data) => SffReader.FromBytes(data);
  static SffFile IImageFileFormat<SffFile>.FromStream(Stream stream) => SffReader.FromStream(stream);
  static RawImage IImageFileFormat<SffFile>.ToRawImage(SffFile file) => file.ToRawImage();
  static byte[] IImageFileFormat<SffFile>.ToBytes(SffFile file) => SffWriter.ToBytes(file);

  /// <summary>File format version (typically 1).</summary>
  public byte Version { get; init; } = 1;

  /// <summary>The fax pages in this file.</summary>
  public IReadOnlyList<SffPage> Pages { get; init; } = [];

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  public RawImage ToRawImage() {
    if (this.Pages.Count == 0)
      throw new InvalidOperationException("SFF file contains no pages.");

    var page = this.Pages[0];
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
