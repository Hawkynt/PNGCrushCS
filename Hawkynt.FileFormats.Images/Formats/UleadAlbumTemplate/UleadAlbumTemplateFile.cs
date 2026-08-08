using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.UleadAlbumTemplate;

/// <summary>In-memory representation of a Ulead album template set (.pe4).</summary>
/// <remarks>
/// The page-layout templates of Ulead Photo Explorer 4 and 5, opening <c>IIO2</c>. Not one picture
/// and not tiles of one: each file holds six small diagrams showing how many photographs go on a
/// page and in what arrangement, named <c>P6</c>, <c>L2</c>, <c>LH</c> and so on for portrait and
/// landscape.
/// <para/>
/// Unlike the older library these carry a real directory, and it accounts for the file exactly: the
/// header states where it begins and how long it is, and those two add up to the length of the file
/// to the byte in both samples. Each of its entries is an offset to a record and an offset to that
/// record's name, so nothing is searched for.
/// <para/>
/// A record states its size, its plane count and the length of its JPEG, and that length lands
/// exactly on the picture's own end-of-image marker in all twelve records. After it come four bytes
/// that describe the layout the diagram draws, and they agree with it: <c>LH</c> states twelve cells
/// in three rows of four, <c>L1</c> states one cell in one row of one.
/// <para/>
/// Writing puts the directory back at the end, because the offset and the length the header states
/// have to add up to the file. What it does not state is the cell layout: that is a description of
/// how a page is arranged, and a diagram this library was handed says nothing about one.
/// </remarks>
public sealed class UleadAlbumTemplateFile
  : IImageFormatReader<UleadAlbumTemplateFile>, IImageToRawImage<UleadAlbumTemplateFile>,
    IImageFromRawImage<UleadAlbumTemplateFile>, IImageFormatWriter<UleadAlbumTemplateFile>,
    IMultiImageFileFormat<UleadAlbumTemplateFile> {

  /// <summary>The four bytes every one of these opens with.</summary>
  public static ReadOnlySpan<byte> Magic => [(byte)'I', (byte)'I', (byte)'O', (byte)'2'];

  /// <summary>Where the header points at the directory and states how long it is.</summary>
  internal const int DirectoryOffsetAt = 0x28, DirectoryLengthAt = 0x2C;

  /// <summary>Where the header states how long a record's own header is, and how many there are.</summary>
  internal const int RecordHeaderSizeAt = 0x34, EntryCountAt = 0x124;

  /// <summary>A directory entry: where the record is, and where its name is within the directory.</summary>
  internal const int DirectoryEntrySize = 8;

  /// <summary>Within a record: the size, the planes, the quality, then the JPEG's length.</summary>
  internal const int WidthAt = 0, HeightAt = 2, PlaneCountAt = 4, QualityAt = 6, JpegLengthAt = 8, TrailerLengthAt = 12;

  /// <summary>How long a record's header is in both samples, which the header also states.</summary>
  internal const int DefaultRecordHeaderSize = 24;

  /// <summary>The most templates a file may state; both samples hold six.</summary>
  internal const int MaximumEntries = 1024;

  static string IImageFormatMetadata<UleadAlbumTemplateFile>.PrimaryExtension => ".pe4";
  static string[] IImageFormatMetadata<UleadAlbumTemplateFile>.FileExtensions => [".pe4"];
  static UleadAlbumTemplateFile IImageFormatReader<UleadAlbumTemplateFile>.FromSpan(ReadOnlySpan<byte> data) => UleadAlbumTemplateReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<UleadAlbumTemplateFile>.Capabilities => FormatCapability.MultiImage;
  static VideoMode[] IImageFormatMetadata<UleadAlbumTemplateFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  static bool? IImageFormatMetadata<UleadAlbumTemplateFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= Magic.Length && header[..Magic.Length].SequenceEqual(Magic) ? true : null;

  /// <summary>One template: what it is called, and the JPEG of the diagram it draws.</summary>
  public readonly record struct Template(string Name, byte[] Embedded);

  /// <summary>The templates the directory lists, in the order it lists them.</summary>
  public IReadOnlyList<Template> Templates { get; init; } = [];

  public static int ImageCount(UleadAlbumTemplateFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return file.Templates.Count;
  }

  public static RawImage ToRawImage(UleadAlbumTemplateFile file, int index) {
    ArgumentNullException.ThrowIfNull(file);
    if ((uint)index >= (uint)file.Templates.Count)
      throw new ArgumentOutOfRangeException(nameof(index));

    return JpegFile.ToRawImage(JpegReader.FromBytes(file.Templates[index].Embedded));
  }

  public static RawImage ToRawImage(UleadAlbumTemplateFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Templates.Count == 0)
      throw new InvalidDataException("A Ulead album template set holds no templates.");

    return ToRawImage(file, 0);
  }

  /// <summary>A set of one template, whose diagram is this picture.</summary>
  public static UleadAlbumTemplateFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    return new() { Templates = [new("P1", JpegWriter.ToBytes(JpegFile.FromRawImage(image)))] };
  }

  static byte[] IImageFormatWriter<UleadAlbumTemplateFile>.ToBytes(UleadAlbumTemplateFile file)
    => UleadAlbumTemplateWriter.ToBytes(file);
}
