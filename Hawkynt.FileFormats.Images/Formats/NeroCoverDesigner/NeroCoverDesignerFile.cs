using System;
using FileFormat.Core;
using FileFormat.Wrappers;

namespace FileFormat.NeroCoverDesigner;

/// <summary>In-memory representation of a Nero cover.</summary>
/// <remarks>
/// A wrapper around an ordinary picture: the name it opens with, some bookkeeping, and then a plain
/// JPEG or PNG. Every sample in the corpus was refused before, and every one of them now matches
/// XnView exactly once the wrapper is stepped over.
/// </remarks>
public readonly record struct NeroCoverDesignerFile
  : IImageFormatReader<NeroCoverDesignerFile>, IImageToRawImage<NeroCoverDesignerFile>,
    IImageFromRawImage<NeroCoverDesignerFile>, IImageFormatWriter<NeroCoverDesignerFile> {

  static string IImageFormatMetadata<NeroCoverDesignerFile>.PrimaryExtension => ".cde";
  static string[] IImageFormatMetadata<NeroCoverDesignerFile>.FileExtensions => [".cde", ".nct", ".ncd"];
  static NeroCoverDesignerFile IImageFormatReader<NeroCoverDesignerFile>.FromSpan(ReadOnlySpan<byte> data) => NeroCoverDesignerReader.FromSpan(data);
  static byte[] IImageFormatWriter<NeroCoverDesignerFile>.ToBytes(NeroCoverDesignerFile file) => NeroCoverDesignerWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<NeroCoverDesignerFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  static bool? IImageFormatMetadata<NeroCoverDesignerFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= Magic.Length && header[..Magic.Length].SequenceEqual(Magic) ? true : null;

  /// <summary>The bytes a file opens with.</summary>
  internal static ReadOnlySpan<byte> Magic => "COVER EDITOR"u8;

  /// <summary>The picture the wrapper carries, exactly as it stands in the file.</summary>
  public byte[] Embedded { get; init; }

  /// <summary>Whether that picture is a PNG rather than a JPEG.</summary>
  public bool IsPng { get; init; }

  public static RawImage ToRawImage(NeroCoverDesignerFile file) => WrappedPicture.Decode(file.Embedded, file.IsPng);

  public static NeroCoverDesignerFile FromRawImage(RawImage image) {
    var (embedded, isPng) = WrappedPicture.Encode(image);

    return new() { Embedded = embedded, IsPng = isPng };
  }
}
