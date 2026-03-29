using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using FileFormat.Ico;

namespace FileFormat.Cur;

/// <summary>In-memory representation of a CUR file.</summary>
[FormatMagicBytes([0x00, 0x00, 0x02, 0x00])]
public sealed class CurFile : IImageFileFormat<CurFile>, IMultiImageFileFormat<CurFile> {

  static string IImageFileFormat<CurFile>.PrimaryExtension => ".cur";
  static string[] IImageFileFormat<CurFile>.FileExtensions => [".cur"];
  static FormatCapability IImageFileFormat<CurFile>.Capabilities => FormatCapability.HasDedicatedOptimizer | FormatCapability.MultiImage;
  static CurFile IImageFileFormat<CurFile>.FromFile(FileInfo file) => CurReader.FromFile(file);
  static CurFile IImageFileFormat<CurFile>.FromBytes(byte[] data) => CurReader.FromBytes(data);
  static CurFile IImageFileFormat<CurFile>.FromStream(Stream stream) => CurReader.FromStream(stream);
  static byte[] IImageFileFormat<CurFile>.ToBytes(CurFile file) => CurWriter.ToBytes(file);
  public IReadOnlyList<CurImage> Images { get; init; } = [];

  /// <summary>Returns the number of cursor entries in this CUR file.</summary>
  public static int ImageCount(CurFile file) => file.Images.Count;

  /// <summary>Converts the cursor entry at the given index to a <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(CurFile file, int index) {
    ArgumentNullException.ThrowIfNull(file);
    if ((uint)index >= (uint)file.Images.Count)
      throw new ArgumentOutOfRangeException(nameof(index));

    var entry = file.Images[index];
    var icoImage = new IcoImage {
      Width = entry.Width,
      Height = entry.Height,
      BitsPerPixel = entry.BitsPerPixel,
      Format = entry.Format,
      Data = entry.Data
    };
    return IcoFile.ToRawImage(new IcoFile { Images = [icoImage] });
  }

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
