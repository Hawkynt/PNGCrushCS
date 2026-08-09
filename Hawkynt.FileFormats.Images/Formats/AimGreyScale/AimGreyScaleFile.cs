using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.AimGreyScale;

/// <summary>In-memory representation of an AIM grey scale image.</summary>
/// <remarks>
/// The picture file is nothing but samples: one byte a pixel, zero is black, the first byte is the
/// top-left corner. It states no size and carries no signature — not one byte of it is read before
/// the rows are. The size lives in a companion file beside it, the same name with <c>.hd</c> in place
/// of the extension, which is the same arrangement Graph Saurus uses for its palette and OCP Art
/// Studio for its.
/// <para/>
/// Worth saying plainly, because this library got it wrong once: there is no <c>AIM\0</c> magic. A
/// reader carrying that signature stood here for a while and could not have read a single real file,
/// and was deleted rather than corrected. This is the format itself, built from the loader that reads
/// it and checked against files that loader accepts.
/// <para/>
/// When the companion is missing or does not describe the picture, one length is still readable: a
/// file of exactly 65,536 bytes is 256 by 256, and every other length is refused. That is the whole
/// of the fallback — not a table, one entry — and it is why reading by bytes alone works for that one
/// size and nothing else.
/// </remarks>
public readonly record struct AimGreyScaleFile : IImageFormatReader<AimGreyScaleFile>, IImageToRawImage<AimGreyScaleFile> {

  static string IImageFormatMetadata<AimGreyScaleFile>.PrimaryExtension => ".ima";

  /// <summary>
  /// Only .ima, though the catalogue lists .im beside it.
  /// </summary>
  /// <remarks>
  /// The Atari Image Manager already holds that name here, and it has a header to be recognised by
  /// where this has none at all — so under <c>.im</c> a file with a header would be taken from the
  /// reader that can read it and given to one that would accept any length it happened to like.
  /// </remarks>
  static string[] IImageFormatMetadata<AimGreyScaleFile>.FileExtensions => [".ima"];

  static AimGreyScaleFile IImageFormatReader<AimGreyScaleFile>.FromSpan(ReadOnlySpan<byte> data) => AimGreyScaleReader.FromSpan(data);

  /// <summary>Reads a named file, which is the only way the companion stating the size is found.</summary>
  static AimGreyScaleFile IImageFormatReader<AimGreyScaleFile>.FromFile(FileInfo file) => AimGreyScaleReader.FromFile(file);

  // Any size the companion cares to state, and 256 by 256 when there is none.
  static VideoMode[] IImageFormatMetadata<AimGreyScaleFile>.VideoModes => [
    new("Grey scale", [(IntegerRange.Any, IntegerRange.Any)], [256]),
  ];

  /// <summary>The companion stating the size lives under this extension.</summary>
  public const string CompanionExtension = ".hd";

  /// <summary>Bytes of the companion that are read.</summary>
  public const int CompanionSize = 26;

  /// <summary>The two characters at offset four that a companion has to carry.</summary>
  public const string CompanionMark = "AA";

  /// <summary>The one length that needs no companion, and the size it is.</summary>
  public const int FallbackLength = 65536;

  public const int FallbackExtent = 256;

  public int Width { get; init; }

  public int Height { get; init; }

  /// <summary>The picture as it lies, one byte a pixel from the top-left corner.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(AimGreyScaleFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Gray8,
    PixelData = file.PixelData[..],
  };
}
