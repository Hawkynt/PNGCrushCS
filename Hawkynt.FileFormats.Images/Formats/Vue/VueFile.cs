using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Gif;

namespace FileFormat.Vue;

/// <summary>In-memory representation of a Vue d'Esprit object file (.vob).</summary>
/// <remarks>
/// A scene object rather than a picture: a name, a description of what it is, and the picture the
/// program shows for it. Nothing about the layout is guessed. The file opens with the program's own
/// name and version, then two strings each preceded by its length in two bytes, then the picture's
/// width and height in four bytes each, and then a GIF. Following those lengths lands exactly on the
/// GIF's own signature in both samples, and the size stated before it is the size the GIF states for
/// itself — which is what says the fields are being read as the format means them rather than found
/// by searching for a signature.
/// <para/>
/// The picture is small because a Vue object has no larger one; it is what the file holds, not a
/// reduction of something else.
/// </remarks>
public readonly record struct VueFile
  : IImageFormatReader<VueFile>, IImageToRawImage<VueFile>,
    IImageFromRawImage<VueFile>, IImageFormatWriter<VueFile> {

  /// <summary>What every one of these opens with.</summary>
  public static ReadOnlySpan<byte> Magic => "Vue d'Esprit\0"u8;

  static string IImageFormatMetadata<VueFile>.PrimaryExtension => ".vob";
  static string[] IImageFormatMetadata<VueFile>.FileExtensions => [".vob"];
  static VueFile IImageFormatReader<VueFile>.FromSpan(ReadOnlySpan<byte> data) => VueReader.FromSpan(data);

  static bool? IImageFormatMetadata<VueFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= Magic.Length && header[..Magic.Length].SequenceEqual(Magic) ? true : null;

  /// <summary>What the file calls the object.</summary>
  public string Name { get; init; }

  /// <summary>What the file says the object is.</summary>
  public string Description { get; init; }

  /// <summary>The width the file states for its picture.</summary>
  public int Width { get; init; }

  /// <summary>The height the file states for its picture.</summary>
  public int Height { get; init; }

  /// <summary>The GIF the file carries, exactly as it stands in it.</summary>
  public byte[] Embedded { get; init; }

  static byte[] IImageFormatWriter<VueFile>.ToBytes(VueFile file) => VueWriter.ToBytes(file);

  public static RawImage ToRawImage(VueFile file)
    => GifFile.ToRawImage(GifReader.FromBytes(file.Embedded ?? throw new InvalidDataException("A Vue object carries no picture.")));

  /// <summary>An object file whose picture is this one, coded as the GIF the format keeps.</summary>
  public static VueFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    return new() {
      Name = "object",
      Description = string.Empty,
      Width = image.Width,
      Height = image.Height,
      Embedded = GifWriter.ToBytes(GifFile.FromRawImage(image)),
    };
  }
}
