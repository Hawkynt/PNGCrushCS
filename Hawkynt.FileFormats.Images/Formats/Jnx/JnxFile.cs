using System;
using System.Collections.Generic;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.Jnx;

/// <summary>In-memory representation of a Garmin JNX map.</summary>
/// <remarks>
/// A JNX is a tile set rather than a picture: one map is many JPEGs, each
/// covering a patch of ground, in one or more levels of detail. That is why this
/// is a multi-image format like MPO — the tiles are handed over as they are
/// rather than pasted into one raster, because the file says where each tile
/// sits on the globe and nothing about where it sits in a picture.
/// </remarks>
[FormatDetectionPriority(920)]
[FormatMimeType("application/x-garmin-jnx")]
public sealed class JnxFile : IImageFormatReader<JnxFile>, IImageToRawImage<JnxFile>, IImageFromRawImage<JnxFile>, IImageFormatWriter<JnxFile>, IMultiImageFileFormat<JnxFile> {

  static string IImageFormatMetadata<JnxFile>.PrimaryExtension => ".jnx";
  static string[] IImageFormatMetadata<JnxFile>.FileExtensions => [".jnx"];
  static JnxFile IImageFormatReader<JnxFile>.FromSpan(ReadOnlySpan<byte> data) => JnxReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<JnxFile>.Capabilities => FormatCapability.MultiImage;
  static byte[] IImageFormatWriter<JnxFile>.ToBytes(JnxFile file) => JnxWriter.ToBytes(file);

  /// <summary>
  /// Recognises a JNX by its version and the shape of what follows it.
  /// </summary>
  /// <remarks>
  /// The format opens with no magic at all, only a version of 3 or 4, which two
  /// bytes of any little-endian file could state. The level count that follows
  /// is checked with it: a map has at least one level and not many, so a file
  /// claiming none or thousands is not this. Detection stays a maybe rather than
  /// a yes for that reason — the extension has to agree.
  /// </remarks>
  static bool? IImageFormatMetadata<JnxFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 28)
      return null;

    var version = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(header);
    if (version is not (3 or 4))
      return null;

    var levels = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(header[24..]);
    return levels is > 0 and <= 32 ? null : false;
  }

  public int Version { get; init; } = 3;
  public int Serial { get; init; }

  /// <summary>The map's bounds, in the signed count of 180/0x7FFFFFFF degrees the file states.</summary>
  public int NorthEastX { get; init; }

  public int NorthEastY { get; init; }
  public int SouthWestX { get; init; }
  public int SouthWestY { get; init; }

  public int Expiry { get; init; }
  public int ProductId { get; init; }
  public int Crc { get; init; }
  public int Signature { get; init; }
  public int SignatureOffset { get; init; }

  /// <summary>The zoom order, which version 3 does not state and behaves as 30.</summary>
  public int ZoomOrder { get; init; } = 30;

  public int[] LevelScales { get; init; } = [];

  /// <summary>Every tile of every level, in the order the file lists them.</summary>
  public IReadOnlyList<JnxTile> Tiles { get; init; } = [];

  public static RawImage ToRawImage(JnxFile file) => ToRawImage(file, 0);

  public static int ImageCount(JnxFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return file.Tiles.Count;
  }

  public static RawImage ToRawImage(JnxFile file, int index) {
    ArgumentNullException.ThrowIfNull(file);
    if ((uint)index >= (uint)file.Tiles.Count)
      throw new ArgumentOutOfRangeException(nameof(index));

    return JpegFile.ToRawImage(JpegReader.FromBytes(file.Tiles[index].JpegData));
  }

  /// <summary>Makes a one-tile map of a picture.</summary>
  /// <remarks>
  /// The bounds are the whole world. A picture carries no ground truth, and a
  /// map that claimed a particular hillside for it would be stating something
  /// the caller never said.
  /// </remarks>
  public static JnxFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var jpeg = JpegWriter.ToBytes(JpegFile.FromRawImage(image));
    return new JnxFile {
      Version = 3,
      NorthEastX = int.MaxValue,
      NorthEastY = int.MaxValue,
      SouthWestX = int.MinValue + 1,
      SouthWestY = int.MinValue + 1,
      LevelScales = [0],
      Tiles = [
        new JnxTile {
          JpegData = jpeg,
          Width = image.Width,
          Height = image.Height,
          NorthEastX = int.MaxValue,
          NorthEastY = int.MaxValue,
          SouthWestX = int.MinValue + 1,
          SouthWestY = int.MinValue + 1,
        },
      ],
    };
  }
}
