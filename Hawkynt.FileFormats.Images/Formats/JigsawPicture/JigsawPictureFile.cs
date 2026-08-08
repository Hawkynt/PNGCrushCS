using System;
using FileFormat.Core;

namespace FileFormat.JigsawPicture;

/// <summary>The picture a Jigsaw 2 puzzle is cut from.</summary>
/// <remarks>
/// The whole of the format is a Windows DIB with its two signature bytes replaced: where a bitmap
/// says <c>BM</c> a Jigsaw picture says <c>JG</c>, and everything from the file size onwards is a
/// bitmap file header and an information header exactly as written.
/// <para/>
/// Two bytes are not a signature, and <c>JG</c> at the front of a file is also what AOL's ART files
/// carry — so the whole of the fixed part of the two headers is checked before the name is accepted:
/// the reserved words are zero, the pixel offset and the information header size are small enough
/// that their top bytes are zero, there is one plane, and the stated file size is one the file can
/// actually hold. A file that satisfies all of that and is not a bitmap would be a coincidence
/// several fields deep.
/// </remarks>
public readonly record struct JigsawPictureFile
  : IImageFormatReader<JigsawPictureFile>, IImageToRawImage<JigsawPictureFile> {

  /// <summary>What the two bytes of a bitmap's signature were replaced with.</summary>
  internal static ReadOnlySpan<byte> Signature => "JG"u8;

  /// <summary>The bitmap file header and the smallest information header behind it.</summary>
  internal const int MinimumSize = 14 + 40;

  static string IImageFormatMetadata<JigsawPictureFile>.PrimaryExtension => ".jig";
  static string[] IImageFormatMetadata<JigsawPictureFile>.FileExtensions => [".jig"];

  static bool? IImageFormatMetadata<JigsawPictureFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 2 && header[0] == (byte)'J' && header[1] == (byte)'G' ? null : false;

  static JigsawPictureFile IImageFormatReader<JigsawPictureFile>.FromSpan(ReadOnlySpan<byte> data)
    => JigsawPictureReader.FromSpan(data);

  static VideoMode[] IImageFormatMetadata<JigsawPictureFile>.VideoModes
    => [new("Default", [(IntegerRange.Any, IntegerRange.Any)])];

  /// <summary>The picture, read by the bitmap reader once the signature is put back.</summary>
  public RawImage Image { get; init; }

  public static RawImage ToRawImage(JigsawPictureFile file) => file.Image;
}
