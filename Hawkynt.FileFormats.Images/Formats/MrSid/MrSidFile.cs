using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.MrSid;

/// <summary>
/// The header and metadata of a MrSID (<c>.sid</c>) raster: LizardTech's, now Extensis', wavelet format for orthophotos
/// and scanned maps, in its generation 2, 3 and 4 containers.
/// </summary>
/// <remarks>
/// <para>
/// Only the container is read. The size, band count, resolution-level count, sample depth, the metadata database
/// (GeoTIFF keys, WKT, encoder settings) and the georeferencing come back; the pixels do not.
/// <see cref="ToRawImage"/> refuses by name. The codestream has never been published. The only decoder is
/// Extensis' Decode SDK, and its licence forbids reverse engineering and allows the SDK to be used only for building
/// products that view MrSID files through it. That rules out disassembly, and it also rules out running the SDK as an
/// oracle for an independent decoder. Without an oracle a decoder could not be shown to be right. See <c>SPEC.md</c>
/// beside this file for what was recovered from the files and what is still unknown.
/// </para>
/// <para>
/// A generation 4 LiDAR file (version byte 4) is a point cloud, not a raster, and the reader refuses it by name.
/// </para>
/// </remarks>
[FormatMagicBytes([0x6D, 0x73, 0x69, 0x64])]
[FormatMimeType("image/x-mrsid-image", "image/x-mrsid")]
public sealed record MrSidFile : IImageFormatReader<MrSidFile>, IImageToRawImage<MrSidFile>, IImageInfoReader<MrSidFile> {

  static string IImageFormatMetadata<MrSidFile>.PrimaryExtension => ".sid";
  static string[] IImageFormatMetadata<MrSidFile>.FileExtensions => [".sid"];
  static MrSidFile IImageFormatReader<MrSidFile>.FromSpan(ReadOnlySpan<byte> data) => MrSidReader.FromSpan(data);

  /// <summary>The generation the container was written in.</summary>
  public MrSidGeneration Generation { get; init; }

  /// <summary>Width of the full-resolution picture.</summary>
  public int Width { get; init; }

  /// <summary>Height of the full-resolution picture.</summary>
  public int Height { get; init; }

  /// <summary>Number of bands: 1 for grey, 3 for colour; generation 4 adds more (such as an alpha band).</summary>
  public int BandCount { get; init; }

  /// <summary>Bits per band sample. Generation 2 has no header field for it and is 8 unless its metadata says otherwise.</summary>
  public int BitsPerSample { get; init; }

  /// <summary>Number of wavelet levels: the picture can be decoded at 1/2, 1/4, … 1/2^levels of its size.</summary>
  public int LevelCount { get; init; }

  /// <summary>
  /// The number of separately coded rectangles of the lowest resolution level in a generation 2 file (pictures wider
  /// or taller than about 4000 pixels are split); <see langword="null"/> for the later generations, whose codestream
  /// layout is not known.
  /// </summary>
  public int? TileCount { get; init; }

  /// <summary>The metadata records in file order; empty when the file carries none.</summary>
  public IReadOnlyList<MrSidMetadataRecord> Metadata { get; init; } = [];

  /// <summary>
  /// Whether the metadata walk stopped at a record of a value type no file has shown, so that records after it were
  /// not read.
  /// </summary>
  public bool HasUnreadMetadata { get; init; }

  /// <summary>The georeferencing, from the generation 3/4 georeference box or else from the metadata; <see langword="null"/> when the file has neither.</summary>
  public MrSidGeoreference? Georeference { get; init; }

  /// <summary>The coordinate system as WKT (<c>IMAGE::WKT</c>), when the file stores one.</summary>
  public string? Wkt => this.Find("IMAGE::WKT") is { Strings.Count: > 0 } record ? record.Strings[0] : null;

  /// <summary>The first metadata record of that name, or <see langword="null"/>.</summary>
  public MrSidMetadataRecord? Find(string key) {
    foreach (var record in this.Metadata)
      if (record.Key == key)
        return record;
    return null;
  }

  /// <summary>Reads size, band count, depth and generation from the fixed header alone.</summary>
  public static ImageInfo? ReadImageInfo(ReadOnlySpan<byte> header) {
    MrSidReader.FixedHeader fixedHeader;
    try {
      fixedHeader = MrSidReader.ReadFixedHeader(header);
    } catch (Exception exception) when (exception is System.IO.InvalidDataException or NotSupportedException) {
      return null;
    }

    return new(
      fixedHeader.Width,
      fixedHeader.Height,
      fixedHeader.BandCount * fixedHeader.BitsPerSample,
      ColourMode(fixedHeader.BandCount),
      $"MrSID {Name(fixedHeader.Generation)} wavelet");
  }

  /// <summary>Always refuses: the MrSID codestream is unpublished, and no decoder may be used to recover it.</summary>
  /// <exception cref="NotSupportedException">Always, naming the generation.</exception>
  public static RawImage ToRawImage(MrSidFile file) {
    ArgumentNullException.ThrowIfNull(file);
    throw new NotSupportedException(
      $"MrSID {Name(file.Generation)} pixel decoding is not implemented: the wavelet codestream is unpublished and the only "
      + "decoder, Extensis' Decode SDK, is licensed on terms that forbid recovering it. The header, metadata and "
      + "georeferencing are read.");
  }

  internal static string Name(MrSidGeneration generation) => generation switch {
    MrSidGeneration.Mg2 => "MG2",
    MrSidGeneration.Mg3 => "MG3",
    MrSidGeneration.Mg4 => "MG4",
    _ => generation.ToString(),
  };

  internal static string ColourMode(int bands) => bands switch {
    1 => "Grey",
    3 => "RGB",
    _ => $"{bands} bands",
  };
}
