using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.Png;

namespace FileFormat.Mng;

/// <summary>In-memory representation of an MNG (VLC profile) animation.</summary>
[FormatMagicBytes([0x8A, 0x4D, 0x4E, 0x47])]
public sealed class MngFile : IImageFormatReader<MngFile>, IImageToRawImage<MngFile>, IImageFormatWriter<MngFile>, IMultiImageFileFormat<MngFile> {

  static string IImageFormatMetadata<MngFile>.PrimaryExtension => ".mng";
  static string[] IImageFormatMetadata<MngFile>.FileExtensions => [".mng"];
  static MngFile IImageFormatReader<MngFile>.FromSpan(ReadOnlySpan<byte> data) => MngReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<MngFile>.Capabilities => FormatCapability.VariableResolution | FormatCapability.MultiImage;
  static byte[] IImageFormatWriter<MngFile>.ToBytes(MngFile file) => MngWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public int TicksPerSecond { get; init; }
  public int NumPlays { get; init; }
  public MngTermAction TermAction { get; init; }

  /// <summary>Embedded PNG frames (each is a complete PNG file).</summary>
  public IReadOnlyList<byte[]> Frames { get; init; } = [];

  /// <summary>Returns the number of frames in this MNG file.</summary>
  public static int ImageCount(MngFile file) => file.Frames.Count;

  /// <summary>Converts the frame at the given index to a <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(MngFile file, int index) {
    ArgumentNullException.ThrowIfNull(file);
    if ((uint)index >= (uint)file.Frames.Count)
      throw new ArgumentOutOfRangeException(nameof(index));

    return PngFile.ToRawImage(PngReader.FromBytes(file.Frames[index]));
  }

  /// <summary>Converts the first frame of an MNG file to a <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(MngFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Frames.Count == 0)
      throw new ArgumentException("MNG file contains no frames.", nameof(file));

    return PngFile.ToRawImage(PngReader.FromBytes(file.Frames[0]));
  }

}
