using System;
using FileFormat.Core;
using FileFormat.Wrappers;

namespace FileFormat.PhotoLine;

/// <summary>In-memory representation of a Photo Line document.</summary>
/// <remarks>
/// A wrapper around an ordinary picture: the name it opens with, some bookkeeping, and then a plain
/// JPEG or PNG. Every sample in the corpus was refused before, and every one of them now matches
/// XnView exactly once the wrapper is stepped over.
/// </remarks>
public readonly record struct PhotoLineFile
  : IImageFormatReader<PhotoLineFile>, IImageToRawImage<PhotoLineFile> {

  static string IImageFormatMetadata<PhotoLineFile>.PrimaryExtension => ".pld";
  static string[] IImageFormatMetadata<PhotoLineFile>.FileExtensions => [".pld"];
  static PhotoLineFile IImageFormatReader<PhotoLineFile>.FromSpan(ReadOnlySpan<byte> data) => PhotoLineReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<PhotoLineFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  static bool? IImageFormatMetadata<PhotoLineFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= Magic.Length && header[..Magic.Length].SequenceEqual(Magic) ? true : null;

  /// <summary>The bytes a file opens with.</summary>
  internal static ReadOnlySpan<byte> Magic => "Photo Line Docum"u8;

  /// <summary>The picture the wrapper carries, exactly as it stands in the file.</summary>
  public byte[] Embedded { get; init; }

  /// <summary>Whether that picture is a PNG rather than a JPEG.</summary>
  public bool IsPng { get; init; }

  public static RawImage ToRawImage(PhotoLineFile file) => WrappedPicture.Decode(file.Embedded, file.IsPng);
}
