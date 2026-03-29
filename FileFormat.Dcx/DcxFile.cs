using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.Pcx;

namespace FileFormat.Dcx;

/// <summary>In-memory representation of a DCX (multi-page PCX) file.</summary>
[FormatMagicBytes([0xB1, 0x68, 0xDE, 0x3A])]
public sealed class DcxFile : IImageFileFormat<DcxFile>, IMultiImageFileFormat<DcxFile> {

  static string IImageFileFormat<DcxFile>.PrimaryExtension => ".dcx";
  static string[] IImageFileFormat<DcxFile>.FileExtensions => [".dcx"];
  static FormatCapability IImageFileFormat<DcxFile>.Capabilities => FormatCapability.VariableResolution | FormatCapability.MultiImage;
  static DcxFile IImageFileFormat<DcxFile>.FromFile(FileInfo file) => DcxReader.FromFile(file);
  static DcxFile IImageFileFormat<DcxFile>.FromBytes(byte[] data) => DcxReader.FromBytes(data);
  static DcxFile IImageFileFormat<DcxFile>.FromStream(Stream stream) => DcxReader.FromStream(stream);
  static byte[] IImageFileFormat<DcxFile>.ToBytes(DcxFile file) => DcxWriter.ToBytes(file);
  /// <summary>The PCX pages contained in this file.</summary>
  public IReadOnlyList<PcxFile> Pages { get; init; } = [];

  /// <summary>Returns the number of pages in this DCX file.</summary>
  public static int ImageCount(DcxFile file) => file.Pages.Count;

  /// <summary>Converts the page at the given index to a <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(DcxFile file, int index) {
    ArgumentNullException.ThrowIfNull(file);
    if ((uint)index >= (uint)file.Pages.Count)
      throw new ArgumentOutOfRangeException(nameof(index));

    return PcxFile.ToRawImage(file.Pages[index]);
  }

  /// <summary>Converts the first page of a DCX file to a <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(DcxFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Pages.Count == 0)
      throw new ArgumentException("DCX file contains no pages.", nameof(file));

    return PcxFile.ToRawImage(file.Pages[0]);
  }

  /// <summary>Creates a single-page DCX file from a <see cref="RawImage"/>.</summary>
  public static DcxFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    return new DcxFile {
      Pages = [PcxFile.FromRawImage(image)]
    };
  }
}
