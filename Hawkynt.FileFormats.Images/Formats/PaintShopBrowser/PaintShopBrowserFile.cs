using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.PaintShopBrowser;

/// <summary>A Paint Shop Pro browser cache (<c>pspbrwse.jbf</c>).</summary>
/// <remarks>
/// Jasc never published this. What it is built from is three independent readers that agree with
/// each other field for field: Deark's <c>modules/jbf.c</c>, <c>jbfinspect.c</c>, and
/// <c>jbf2html</c>'s <c>jbf.h</c>.
/// <para/>
/// A thumbnail is not the picture — except here, where it is. This file is the cache Paint Shop
/// Pro's browser wrote for a folder, and thumbnails are the whole of what it holds; there is no
/// larger picture in it being stood in for. What is drawn is the first of them, and the rest are
/// reachable as further images the same way a multi-page file's are.
/// <para/>
/// The file opens with the fifteen letters <c>JASC BROWS FILE</c> and a nul, then a major and a
/// minor version most significant byte first — the only two numbers in the format written that way
/// round — then a count of thumbnails least significant byte first. The header is a kilobyte
/// whatever it carries, and the records follow it back to back with nothing between them.
/// <para/>
/// Only the version 2 files are read, which is where each thumbnail is a whole JPEG with its own
/// length in front of it and a sentinel of four set bytes before that. Version 1 stores a Windows
/// bitmap header copied out of the picture followed by a run-length coding of its own, against a
/// 256-colour palette that lives in the reader rather than in the file, and whose two variants are
/// told apart by the file's minor version. No version 1 file was available, so it is refused by
/// name rather than read from a layout nobody here has seen work.
/// <para/>
/// No sample of either version was available. The fixtures in the tests are caches built byte by
/// byte from those three readers' field lists.
/// <para/>
/// It does not write.
/// </remarks>
public sealed class PaintShopBrowserFile : IImageFormatReader<PaintShopBrowserFile>, IImageToRawImage<PaintShopBrowserFile>, IMultiImageFileFormat<PaintShopBrowserFile> {

  /// <summary>The letters the file opens with.</summary>
  public const string Magic = "JASC BROWS FILE";

  /// <summary>How long the header is, whatever it carries.</summary>
  public const int HeaderLength = 1024;

  /// <summary>The four set bytes that stand before a thumbnail's length.</summary>
  public const uint ThumbnailSentinel = 0xFFFFFFFF;

  static string IImageFormatMetadata<PaintShopBrowserFile>.PrimaryExtension => ".jbf";
  static string[] IImageFormatMetadata<PaintShopBrowserFile>.FileExtensions => [".jbf"];
  static PaintShopBrowserFile IImageFormatReader<PaintShopBrowserFile>.FromSpan(ReadOnlySpan<byte> data) => PaintShopBrowserReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<PaintShopBrowserFile>.Capabilities => FormatCapability.MultiImage;
  static VideoMode[] IImageFormatMetadata<PaintShopBrowserFile>.VideoModes => [
    new("Thumbnail", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  /// <summary>Which version of the format the file states, as major and minor.</summary>
  public (int Major, int Minor) Version { get; init; }

  /// <summary>The folder the cache was written for, as the header records it.</summary>
  public string Directory { get; init; } = string.Empty;

  /// <summary>Each thumbnail: the picture it was made from, and the JPEG that stands for it.</summary>
  public IReadOnlyList<PaintShopThumbnail> Thumbnails { get; init; } = [];

  public static RawImage ToRawImage(PaintShopBrowserFile file) => ToRawImage(file, 0);

  public static int ImageCount(PaintShopBrowserFile file) {
    ArgumentNullException.ThrowIfNull(file);

    return file.Thumbnails.Count;
  }

  public static RawImage ToRawImage(PaintShopBrowserFile file, int index) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Thumbnails.Count == 0)
      throw new InvalidDataException("This browser cache holds no thumbnails.");

    if ((uint)index >= (uint)file.Thumbnails.Count)
      throw new ArgumentOutOfRangeException(nameof(index));

    return JpegFile.ToRawImage(JpegReader.FromBytes(file.Thumbnails[index].Jpeg));
  }
}

/// <summary>One cached thumbnail and what the cache says about the picture it came from.</summary>
/// <param name="Name">What the picture was called.</param>
/// <param name="Width">How wide the picture was, as the cache recorded it.</param>
/// <param name="Height">How tall it was.</param>
/// <param name="Jpeg">The thumbnail, as a whole JPEG file.</param>
public readonly record struct PaintShopThumbnail(string Name, int Width, int Height, byte[] Jpeg);
