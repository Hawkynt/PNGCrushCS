using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.ClipArtCatalog;

/// <summary>In-memory representation of a clip-art catalogue (.cat) and the thumbnails it holds.</summary>
/// <remarks>
/// A catalogue is not a picture but an index of them: the drawings themselves are the <c>.pcx</c> files
/// beside it and the catalogue keeps a thumbnail of each so a browser has something to show. The file
/// is chunked — a four-letter tag and a little-endian length — and opens with <c>CAT&#160;</c> stating
/// the length of everything after it, followed by <c>CLIP</c>. Then one <c>FORM</c> per drawing, and
/// inside each a name (<c>CLIPINFO</c> or <c>XXXXINFO</c> — the first four letters vary, the second
/// four do not), a <c>PATH</c>, and a <c>DIB&#160;</c> holding an ordinary Windows bitmap.
/// <para/>
/// Every byte is accounted for. In all four files the stated length is the file's length less eight to
/// the byte, and walking the chunks — each padded to an even boundary, as this family of formats does
/// — lands exactly on the end and nowhere else. The names bear it out too: the thumbnail read out of
/// the chunk called <c>ape.pcx</c> is an ape.
/// <para/>
/// Writing puts the chunks back round a thumbnail with every length written from what follows it. It
/// is an index with one entry in it: the drawings a real catalogue stands for are the files beside
/// it, and those are not in the catalogue and are not invented on the way out.
/// </remarks>
[FormatMagicBytes([(byte)'C', (byte)'A', (byte)'T', (byte)' '])]
public sealed class ClipArtCatalogFile
  : IImageFormatReader<ClipArtCatalogFile>, IImageToRawImage<ClipArtCatalogFile>,
    IImageFromRawImage<ClipArtCatalogFile>, IImageFormatWriter<ClipArtCatalogFile>,
    IMultiImageFileFormat<ClipArtCatalogFile> {

  /// <summary>The four bytes every one of these opens with.</summary>
  public static ReadOnlySpan<byte> Magic => [(byte)'C', (byte)'A', (byte)'T', (byte)' '];

  /// <summary>The tag that follows the length and says what kind of catalogue this is.</summary>
  public static ReadOnlySpan<byte> ClipTag => [(byte)'C', (byte)'L', (byte)'I', (byte)'P'];

  /// <summary>A tag and a length.</summary>
  public const int ChunkHeaderSize = 8;

  /// <summary>No thumbnail in one of these comes near this, and it keeps a false match cheap.</summary>
  public const int MaxDimension = 4096;

  static string IImageFormatMetadata<ClipArtCatalogFile>.PrimaryExtension => ".cat";
  static string[] IImageFormatMetadata<ClipArtCatalogFile>.FileExtensions => [".cat"];
  static ClipArtCatalogFile IImageFormatReader<ClipArtCatalogFile>.FromSpan(ReadOnlySpan<byte> data)
    => ClipArtCatalogReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<ClipArtCatalogFile>.Capabilities => FormatCapability.MultiImage;
  static VideoMode[] IImageFormatMetadata<ClipArtCatalogFile>.VideoModes => [
    new("Thumbnail", [(new IntegerRange(1, MaxDimension), new IntegerRange(1, MaxDimension))])
  ];

  /// <summary>One entry per catalogued drawing.</summary>
  public IReadOnlyList<ClipArtCatalogEntry> Entries { get; init; } = [];

  public static int ImageCount(ClipArtCatalogFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return file.Entries.Count;
  }

  public static RawImage ToRawImage(ClipArtCatalogFile file, int index) {
    ArgumentNullException.ThrowIfNull(file);
    if ((uint)index >= (uint)file.Entries.Count)
      throw new ArgumentOutOfRangeException(nameof(index));

    return file.Entries[index].Thumbnail;
  }

  public static RawImage ToRawImage(ClipArtCatalogFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Entries.Count == 0)
      throw new InvalidDataException("A clip-art catalogue holds no thumbnails.");

    return file.Entries[0].Thumbnail;
  }

  /// <summary>A catalogue of one drawing, whose thumbnail is this picture.</summary>
  public static ClipArtCatalogFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    return new() { Entries = [new("clipart.pcx", image)] };
  }

  static byte[] IImageFormatWriter<ClipArtCatalogFile>.ToBytes(ClipArtCatalogFile file) => ClipArtCatalogWriter.ToBytes(file);
}

/// <summary>One catalogued drawing: the name of the file it stands for and the thumbnail of it.</summary>
/// <param name="Name">The drawing's file name, as the catalogue records it.</param>
/// <param name="Thumbnail">The thumbnail, already decoded by the bitmap reader.</param>
public readonly record struct ClipArtCatalogEntry(string Name, RawImage Thumbnail);
