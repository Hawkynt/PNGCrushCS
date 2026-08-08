using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.EmbeddedPicture;

namespace FileFormat.AxialisScreensaver;

/// <summary>An Axialis Pro Screensaver Producer project (.ssp), and the pictures it embeds.</summary>
/// <remarks>
/// The file opens with <c>AXSSP</c> and four digits of version, and is then a serialised document
/// rather than an archive: there is no directory anywhere in it. Its media records are written in
/// sequence, each of them the path the picture came from, the length that picture had, and then the
/// picture file itself copied in whole.
/// <para/>
/// What that gives a reader is better than a directory. Every payload is a complete picture file, so
/// the length the record states has to land exactly on that picture's own last byte — a JPEG's
/// end-of-image or a PNG's <c>IEND</c>. Requiring that agreement finds every embedded picture in all
/// twelve samples and nothing else: the length stands as a little-endian word immediately before the
/// payload in both record shapes the format uses, and a run of bytes that only happens to begin with
/// a picture's signature does not have its own length written in front of it.
/// <para/>
/// It holds several pictures and they are not versions of one. A project embeds the tile its
/// background is drawn from, the frames of its sprites, and whatever else it shows; the first of
/// them is usually the tile, at 82 by 71 or 128 by 64, so a reader that took the first picture it
/// found would be drawing wallpaper. Records that only reference a file on the author's disk carry
/// no bytes at all and contribute nothing.
/// <para/>
/// It does not write: the pictures are the smallest part of a project and nothing here models the
/// rest of one.
/// </remarks>
public sealed class AxialisScreensaverFile
  : IImageFormatReader<AxialisScreensaverFile>, IImageToRawImage<AxialisScreensaverFile>,
    IMultiImageFileFormat<AxialisScreensaverFile> {

  /// <summary>The five bytes every one of these opens with, before four digits of version.</summary>
  public static ReadOnlySpan<byte> Magic => "AXSSP"u8;

  /// <summary>Magic and four ASCII digits.</summary>
  public const int SignatureSize = 9;

  /// <summary>The smallest a record's length word can stand at.</summary>
  public const int MinimumPayloadOffset = SignatureSize + 4;

  static string IImageFormatMetadata<AxialisScreensaverFile>.PrimaryExtension => ".ssp";
  static string[] IImageFormatMetadata<AxialisScreensaverFile>.FileExtensions => [".ssp"];
  static AxialisScreensaverFile IImageFormatReader<AxialisScreensaverFile>.FromSpan(ReadOnlySpan<byte> data) => AxialisScreensaverReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<AxialisScreensaverFile>.Capabilities => FormatCapability.MultiImage;
  static VideoMode[] IImageFormatMetadata<AxialisScreensaverFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  static bool? IImageFormatMetadata<AxialisScreensaverFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= Magic.Length && header[..Magic.Length].SequenceEqual(Magic) ? true : null;

  /// <summary>The version digits the signature carries, as they stand.</summary>
  public string Version { get; init; } = string.Empty;

  /// <summary>Each embedded picture, whole, in the order the document stores them.</summary>
  public IReadOnlyList<byte[]> Embedded { get; init; } = [];

  public static int ImageCount(AxialisScreensaverFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return file.Embedded.Count;
  }

  public static RawImage ToRawImage(AxialisScreensaverFile file, int index) {
    ArgumentNullException.ThrowIfNull(file);
    if ((uint)index >= (uint)file.Embedded.Count)
      throw new ArgumentOutOfRangeException(nameof(index));

    return EmbeddedPictureReader.Decode(file.Embedded[index]);
  }

  public static RawImage ToRawImage(AxialisScreensaverFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Embedded.Count == 0)
      throw new InvalidDataException("An Axialis screensaver project embeds no pictures.");

    return ToRawImage(file, 0);
  }
}
