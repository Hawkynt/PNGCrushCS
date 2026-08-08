using System;
using FileFormat.Core;

namespace FileFormat.Ioca;

/// <summary>In-memory representation of an IBM IOCA (Image Object Content Architecture) image.</summary>
public readonly record struct IocaFile : IImageFormatReader<IocaFile>, IImageToRawImage<IocaFile>, IImageFromRawImage<IocaFile>, IImageFormatWriter<IocaFile> {

  /// <summary>The shortest thing that can be one structured field: its header and nothing else.</summary>
  internal const int MinHeaderSize = 8;

  static string IImageFormatMetadata<IocaFile>.PrimaryExtension => ".ica";

  /// <summary><c>.mod</c> is claimed because the reader can now say no to one.</summary>
  /// <remarks>
  /// XnView's <c>ioca</c> row reads <c>.mod</c>, and the name was left unclaimed here for as long as
  /// the reader took any file's first four bytes as a width and a height — under which an Amiga
  /// music module, which is what a <c>.mod</c> usually is, would have been drawn as a picture. The
  /// reader now walks two chains that both have to land exactly on their end, so a module is refused
  /// on its first field.
  /// </remarks>
  static string[] IImageFormatMetadata<IocaFile>.FileExtensions => [".ica", ".ioca", ".ioc", ".mod"];

  /// <summary>Whether the file opens with a MO:DCA structured field or with IOCA's Begin Segment.</summary>
  static bool? IImageFormatMetadata<IocaFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < MinHeaderSize)
      return null;

    if (header[2] == StructuredFieldIntroducer && ((header[0] << 8) | header[1]) >= MinHeaderSize)
      return true;

    // A bare image content stream opens with Begin Segment, which is one byte and far too weak to
    // decide on — so this abstains rather than claiming it.
    return header[0] == IocaReader.SegmentBegin ? null : false;
  }

  /// <summary>The byte every MO:DCA structured field carries behind its length.</summary>
  internal const byte StructuredFieldIntroducer = 0xD3;

  static IocaFile IImageFormatReader<IocaFile>.FromSpan(ReadOnlySpan<byte> data) => IocaReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<IocaFile>.VideoModes => [new("Default", [(IntegerRange.Any, IntegerRange.Any)], [2])];
  static byte[] IImageFormatWriter<IocaFile>.ToBytes(IocaFile file) => IocaWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Packed 1bpp pixel data, most significant bit leftmost, a set bit being ink.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>A set bit is ink, which is what the fax coding underneath counts in.</summary>
  private static readonly byte[] _BlackWhitePalette = [255, 255, 255, 0, 0, 0];

  /// <summary>Converts to a bilevel raw image.</summary>
  public static RawImage ToRawImage(IocaFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed8,
    PixelData = BilevelRows.Unpack(file.PixelData ?? [], file.Width, file.Height),
    Palette = _BlackWhitePalette[..],
    PaletteCount = 2,
  };

  /// <summary>Creates an IOCA image from a picture, reduced to one bit a pixel.</summary>
  public static IocaFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = BilevelRows.Pack(BilevelRows.Threshold(image, setWhenDark: true), image.Width, image.Height),
    };
  }
}
