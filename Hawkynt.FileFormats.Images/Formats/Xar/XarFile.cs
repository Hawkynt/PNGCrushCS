using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Xar;

/// <summary>A Xara drawing (.xar), including real embedded bitmap objects and optional page previews.</summary>
/// <remarks>
/// Xara's published format is a stream of tagged records. Preview records (61-63) are framework
/// thumbnails and are still understood, but bitmap definitions (JPEG/PNG) plus
/// <see cref="TagNodeBitmap"/> are actual editable objects in the document. The writer uses that
/// standards-defined bitmap-object subset rather than fabricating a thumbnail-only file.
/// </remarks>
public readonly record struct XarFile :
  IImageFormatReader<XarFile>, IImageToRawImage<XarFile>, IImageFromRawImage<XarFile>, IImageFormatWriter<XarFile> {

  /// <summary>The eight bytes every Xara drawing opens with.</summary>
  public static ReadOnlySpan<byte> Magic => [(byte)'X', (byte)'A', (byte)'R', (byte)'A', 0xA3, 0xA3, (byte)'\r', (byte)'\n'];

  /// <summary>A record's tag and length, ahead of its body.</summary>
  public const int RecordHeaderSize = 8;

  /// <summary>Compulsory file delimiters.</summary>
  public const uint TagFileHeader = 2, TagEndOfFile = 3;

  /// <summary>Where the deflated part of the file begins, and so where a raw walk has to stop.</summary>
  public const uint TagStartCompression = 30;

  /// <summary>The three framework tags that carry page previews.</summary>
  public const uint TagPreviewGif = 61, TagPreviewJpeg = 62, TagPreviewPng = 63;

  /// <summary>Reusable bitmap-definition records.</summary>
  public const uint TagDefineBitmapJpeg = 67, TagDefineBitmapPng = 68, TagDefineBitmapPngReal = 4138;

  /// <summary>Image records used by the one-bitmap writable subset.</summary>
  public const uint TagFlatFillNone = 190, TagLineColourNone = 193, TagNodeBitmap = 198, TagBitmapProperties = 4115;

  static string IImageFormatMetadata<XarFile>.PrimaryExtension => ".xar";
  static string[] IImageFormatMetadata<XarFile>.FileExtensions => [".xar"];
  static XarFile IImageFormatReader<XarFile>.FromSpan(ReadOnlySpan<byte> data) => XarReader.FromSpan(data);
  static byte[] IImageFormatWriter<XarFile>.ToBytes(XarFile file) => XarWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<XarFile>.VideoModes => [
    new("Bitmap object", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  static bool? IImageFormatMetadata<XarFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length < 8 ? null : header[..8].SequenceEqual(Magic);

  /// <summary>An actual bitmap object defined and referenced by the XAR record tree, when available.</summary>
  public RawImage? Bitmap { get; init; }

  /// <summary>The optional framework preview, already decoded.</summary>
  public RawImage? Preview { get; init; }

  /// <summary>Which of the three preview tags carried it.</summary>
  public int PreviewTag { get; init; }

  /// <summary>What the file header says produced the drawing.</summary>
  public string? Producer { get; init; }

  public static RawImage ToRawImage(XarFile file)
    => file.Bitmap ?? file.Preview ?? throw new InvalidDataException("A Xara drawing carries no bitmap object or preview this reader can draw.");

  public static XarFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    return new() { Bitmap = image, Producer = "PNGCrushCS" };
  }
}
