using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.TilezTexture;

/// <summary>In-memory representation of a Tilez texture (.til).</summary>
/// <remarks>
/// Four bytes of "QDB", four of length, and then an ordinary JPEG. Nothing here decodes anything
/// itself; all three samples come out of the JPEG reader matching XnView exactly.
/// </remarks>
public readonly record struct TilezTextureFile
  : IImageFormatReader<TilezTextureFile>, IImageToRawImage<TilezTextureFile> {

  static string IImageFormatMetadata<TilezTextureFile>.PrimaryExtension => ".til";
  static string[] IImageFormatMetadata<TilezTextureFile>.FileExtensions => [".til"];
  static TilezTextureFile IImageFormatReader<TilezTextureFile>.FromSpan(ReadOnlySpan<byte> data) => TilezTextureReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<TilezTextureFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  static bool? IImageFormatMetadata<TilezTextureFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 4 && header[..4].SequenceEqual(Magic) ? true : null;

  /// <summary>The four bytes a texture opens with.</summary>
  internal static ReadOnlySpan<byte> Magic => [(byte)'Q', (byte)'D', (byte)'B', 0x00];

  /// <summary>Bytes ahead of the JPEG: the magic and a length.</summary>
  internal const int HeaderSize = 8;

  /// <summary>The JPEG the wrapper carries, exactly as it stands in the file.</summary>
  public byte[] Embedded { get; init; }

  public static RawImage ToRawImage(TilezTextureFile file)
    => JpegFile.ToRawImage(JpegReader.FromBytes(file.Embedded ?? throw new InvalidDataException("A Tilez texture carries no picture.")));
}
