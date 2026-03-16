using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using FileFormat.Ico;

namespace FileFormat.Cur;

/// <summary>In-memory representation of a CUR file.</summary>
public sealed class CurFile : IImageFileFormat<CurFile> {

  static string IImageFileFormat<CurFile>.PrimaryExtension => ".cur";
  static string[] IImageFileFormat<CurFile>.FileExtensions => [".cur"];
  static CurFile IImageFileFormat<CurFile>.FromFile(FileInfo file) => CurReader.FromFile(file);
  static byte[] IImageFileFormat<CurFile>.ToBytes(CurFile file) => CurWriter.ToBytes(file);
  public IReadOnlyList<CurImage> Images { get; init; } = [];

  /// <summary>Converts the largest cursor image entry to a <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(CurFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Images.Count == 0)
      throw new ArgumentException("CUR file contains no images.", nameof(file));

    var best = file.Images
      .OrderByDescending(i => i.Width * i.Height)
      .ThenByDescending(i => i.BitsPerPixel)
      .First();

    var icoImage = new IcoImage {
      Width = best.Width,
      Height = best.Height,
      BitsPerPixel = best.BitsPerPixel,
      Format = best.Format,
      Data = best.Data
    };

    var icoFile = new IcoFile { Images = [icoImage] };
    return IcoFile.ToRawImage(icoFile);
  }

  /// <summary>CUR files require multiple resolutions and hotspot data; single-image creation is not supported.</summary>
  public static CurFile FromRawImage(RawImage image) => throw new NotSupportedException("CUR encoding from a single raw image is not supported.");
}
