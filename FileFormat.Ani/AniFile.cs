using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.Ico;

namespace FileFormat.Ani;

/// <summary>In-memory representation of an ANI animated cursor file.</summary>
public sealed class AniFile : IImageFormatReader<AniFile>, IImageToRawImage<AniFile>, IImageFormatWriter<AniFile>, IMultiImageFileFormat<AniFile> {

  public required AniHeader Header { get; init; }
  public IReadOnlyList<IcoFile> Frames { get; init; } = [];
  public int[]? Rates { get; init; }
  public int[]? Sequence { get; init; }

  public static string PrimaryExtension => ".ani";
  public static string[] FileExtensions => [".ani"];
  static AniFile IImageFormatReader<AniFile>.FromSpan(ReadOnlySpan<byte> data) => AniReader.FromSpan(data);

  public static bool? MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 12
       && header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46
       && header[8] == 0x41 && header[9] == 0x43 && header[10] == 0x4F && header[11] == 0x4E
       ? true
       : null;

  public static AniFile FromFile(FileInfo file) => AniReader.FromFile(file);
  public static AniFile FromBytes(byte[] data) => AniReader.FromBytes(data);
  public static AniFile FromStream(Stream stream) => AniReader.FromStream(stream);
  public static byte[] ToBytes(AniFile file) => AniWriter.ToBytes(file);

  /// <summary>Returns the number of frames in this ANI file.</summary>
  public static int ImageCount(AniFile file) => file.Frames.Count;

  /// <summary>Converts the frame at the given index to a <see cref="RawImage"/> (uses largest image in each ICO frame).</summary>
  public static RawImage ToRawImage(AniFile file, int index) {
    ArgumentNullException.ThrowIfNull(file);
    if ((uint)index >= (uint)file.Frames.Count)
      throw new ArgumentOutOfRangeException(nameof(index));

    return IcoFile.ToRawImage(file.Frames[index]);
  }

  public static RawImage ToRawImage(AniFile file)
    => file.Frames.Count > 0
      ? ToRawImage(file, 0)
      : throw new NotSupportedException("ANI file contains no frames.");

}
