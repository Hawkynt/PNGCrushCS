using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Xar;

/// <summary>A Xara drawing (.xar), read by the preview it carries.</summary>
/// <remarks>
/// Xara's own published format is a flat list of records, each a 32-bit tag, a 32-bit length and
/// that many bytes. The first record is always the file header, tag 2, which names the producer;
/// straight after it a drawing saved for the web carries its page preview, and the three tags that
/// hold one — 61, 62 and 63 — say outright which picture format it is in: a GIF, a JPEG or a PNG.
/// The record body is that file, whole, from its first byte.
/// <para/>
/// That is what is read here. The drawing itself is a tree of several hundred record types with
/// its own colour model, live effects and a compressed tail, and rendering it is a different piece
/// of work from reading it; the preview is stated by the file at a stated length and decodes to a
/// picture that can be checked against any other tool, which the drawing would not be.
/// <para/>
/// The walk stops at tag 30, which is where the file says compression begins: past that point the
/// records are deflated and reading lengths raw would be reading noise. Every preview in the seven
/// samples here is in front of it, which is where the format puts it.
/// <para/>
/// It does not write. Emitting a preview and no drawing would produce a file Xara would open empty.
/// </remarks>
public readonly record struct XarFile : IImageFormatReader<XarFile>, IImageToRawImage<XarFile> {

  /// <summary>The eight bytes every Xara drawing opens with.</summary>
  public static ReadOnlySpan<byte> Magic => [(byte)'X', (byte)'A', (byte)'R', (byte)'A', 0xA3, 0xA3, (byte)'\r', (byte)'\n'];

  /// <summary>A record's tag and length, ahead of its body.</summary>
  public const int RecordHeaderSize = 8;

  /// <summary>The record that must come first, and which names the producer.</summary>
  public const int TagFileHeader = 2;

  /// <summary>Where the deflated part of the file begins, and so where a raw walk has to stop.</summary>
  public const int TagStartCompression = 30;

  /// <summary>The three tags that carry a page preview, one per picture format.</summary>
  public const int TagPreviewGif = 61, TagPreviewJpeg = 62, TagPreviewPng = 63;

  static string IImageFormatMetadata<XarFile>.PrimaryExtension => ".xar";
  static string[] IImageFormatMetadata<XarFile>.FileExtensions => [".xar"];
  static XarFile IImageFormatReader<XarFile>.FromSpan(ReadOnlySpan<byte> data) => XarReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<XarFile>.VideoModes => [
    new("Preview", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  static bool? IImageFormatMetadata<XarFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length < 8 ? null : header[..8].SequenceEqual(Magic);

  /// <summary>The preview, already decoded.</summary>
  public RawImage Preview { get; init; }

  /// <summary>Which of the three preview tags carried it.</summary>
  public int PreviewTag { get; init; }

  /// <summary>What the file header says produced the drawing.</summary>
  public string? Producer { get; init; }

  public static RawImage ToRawImage(XarFile file)
    => file.Preview ?? throw new InvalidDataException("A Xara drawing carries no preview this could read.");
}
