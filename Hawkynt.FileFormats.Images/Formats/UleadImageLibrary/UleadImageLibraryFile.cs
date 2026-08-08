using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.UleadImageLibrary;

/// <summary>In-memory representation of a Ulead image library (.pst).</summary>
/// <remarks>
/// A catalogue, not a picture: the library files of Ulead Photo Explorer 3, opening <c>IIO1</c> and
/// holding thirty to seventy separate items each. They were taken for one picture cut into tiles;
/// they are not. Extracting all forty-one of <c>mglib.pst</c> and looking at them shows forty-one
/// distinct fractal textures, and assembling them into a grid would draw a picture that never
/// existed.
/// <para/>
/// There is no directory. Every table that could hold one was searched for — absolute offsets,
/// offsets relative to the first record, record sizes and payload lengths, at eight strides,
/// anywhere in the file — and none exists; the trailing bytes are zero. The chain is computed
/// instead, which is not a search: the count stands at 0x100, the first record begins at
/// <c>0x210 + 4n</c>, and each record states the lengths that give the next.
/// <para/>
/// What confirms the walk is the payload confirming itself. Each record states the length of its
/// JPEG, and in all ten samples that length lands exactly on the JPEG's own end-of-image marker, for
/// every one of the 461 records between them — and the number of records walked is the number of
/// start-of-image markers in the file. A parse off by any amount would miss both.
/// <para/>
/// The pictures are thumbnails. The full-size artwork is the <c>extra</c> block each record carries
/// after its metadata, whose format is not established here — it is where the 2.2 MB of
/// <c>2DFrame.pst</c> goes.
/// </remarks>
public sealed class UleadImageLibraryFile
  : IImageFormatReader<UleadImageLibraryFile>, IImageToRawImage<UleadImageLibraryFile>,
    IMultiImageFileFormat<UleadImageLibraryFile> {

  /// <summary>The four bytes every one of these opens with.</summary>
  public static ReadOnlySpan<byte> Magic => [(byte)'I', (byte)'I', (byte)'O', (byte)'1'];

  /// <summary>Where the header states how many items the library holds.</summary>
  internal const int ItemCountAt = 0x100;

  /// <summary>Where the first record begins, once the count has been added four times over.</summary>
  internal const int FirstRecordBase = 0x210;

  /// <summary>The record header: a type, the length of the extra block, the size, and the JPEG's length.</summary>
  internal const int RecordHeaderSize = 40;
  internal const int RecordTypeAt = 0, ExtraLengthAt = 4, WidthAt = 20, HeightAt = 24, PlaneCountAt = 28, JpegLengthAt = 32;

  /// <summary>What a record states where its type belongs, in every record of every sample.</summary>
  internal const int RecordType = 20;

  /// <summary>The metadata between a record's JPEG and its extra block, holding the item's name.</summary>
  internal const int MetadataSize = 264;

  /// <summary>The most items a library may state, the samples showing thirty to seventy.</summary>
  internal const int MaximumItems = 4096;

  static string IImageFormatMetadata<UleadImageLibraryFile>.PrimaryExtension => ".pst";
  static string[] IImageFormatMetadata<UleadImageLibraryFile>.FileExtensions => [".pst"];
  static UleadImageLibraryFile IImageFormatReader<UleadImageLibraryFile>.FromSpan(ReadOnlySpan<byte> data) => UleadImageLibraryReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<UleadImageLibraryFile>.Capabilities => FormatCapability.MultiImage;
  static VideoMode[] IImageFormatMetadata<UleadImageLibraryFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  static bool? IImageFormatMetadata<UleadImageLibraryFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= Magic.Length && header[..Magic.Length].SequenceEqual(Magic) ? true : null;

  /// <summary>The JPEG each item carries, exactly as it stands in the file.</summary>
  public IReadOnlyList<byte[]> Items { get; init; } = [];

  /// <summary>How many items the library holds.</summary>
  public static int ImageCount(UleadImageLibraryFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return file.Items.Count;
  }

  public static RawImage ToRawImage(UleadImageLibraryFile file, int index) {
    ArgumentNullException.ThrowIfNull(file);
    if ((uint)index >= (uint)file.Items.Count)
      throw new ArgumentOutOfRangeException(nameof(index));

    return JpegFile.ToRawImage(JpegReader.FromBytes(file.Items[index]));
  }

  public static RawImage ToRawImage(UleadImageLibraryFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Items.Count == 0)
      throw new InvalidDataException("A Ulead image library holds no items.");

    return ToRawImage(file, 0);
  }
}
