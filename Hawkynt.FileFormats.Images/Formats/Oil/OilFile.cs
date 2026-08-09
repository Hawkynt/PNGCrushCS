using System;
using FileFormat.Core;

namespace FileFormat.Oil;

/// <summary>In-memory representation of an OIL (Open Image Library) picture.</summary>
/// <remarks>
/// <c>.oil</c> was the native format of OpenIL, the library that became DevIL. It was new in OpenIL
/// 2.1.0b at the end of 2000 and taken back out in DevIL 1.1.9 in November 2001, so it existed for
/// under a year and almost nothing wrote it.
/// <para/>
/// Written from the format's own specification — <em>.OIL Specifications</em>, last revised
/// 9 February 2001, shipped in the DevIL 1.1.8 documentation as
/// <c>ImageLib/docs/oil_spec/index.htm</c>. No sample of the format could be found to check against;
/// what stands in for one is that a file built to the specification here is read by XnView's own
/// converter at the size and depth it was built with, which is a second reading of the same document
/// by someone else. That converter's pixels are not a check on ours — handed a picture of four
/// distinct rows it returns the last of them four times over — so the layout below is the
/// specification's and nothing else's.
/// <para/>
/// A file holds a header, a directory, and one image per directory entry, each of which may carry
/// mipmaps behind it. The first entry is the picture; the rest are the frames of an animation, and
/// the header's animation list, if it has one, only reorders them.
/// <para/>
/// Everything is little-endian and the structures are packed — no padding between the two-byte
/// version and the four-byte count behind it. That is not stated in the document, and it is what
/// the file's own eighty-three byte description string settles: it can only sit at offset 22, where
/// the packed layout puts it.
/// </remarks>
public readonly record struct OilFile : IImageFormatReader<OilFile>, IImageToRawImage<OilFile> {

  /// <summary>The four bytes a file opens with: <c>OIL</c> and its terminator.</summary>
  public static ReadOnlySpan<byte> Signature => "OIL\0"u8;

  /// <summary>The number behind the signature, which the document gives in this many words.</summary>
  public const uint MagicNumber = 0x693D71;

  /// <summary>The only version the document describes.</summary>
  public const ushort SupportedVersion = 1;

  /// <summary>
  /// The description string every file carries, which is also what says where the header ends.
  /// </summary>
  public const string HeadString = "This is a graphics file based on the Open Image Library file format specification.";

  /// <summary>Its length with the terminator, which the document fixes.</summary>
  public const int HeadStringLength = 83;

  /// <summary>Signature, magic, version, count, directory offset, animation offset, description.</summary>
  public const int HeaderSize = 4 + 4 + 2 + 4 + 4 + 4 + HeadStringLength;

  /// <summary>A name of 255 bytes, an offset and a length.</summary>
  public const int DirectoryEntrySize = 255 + 4 + 4;

  /// <summary>Three sizes, five bytes of description, a duration and a data length.</summary>
  public const int ImageHeaderSize = 4 + 4 + 4 + 5 + 4 + 4;

  /// <summary>The image is a run of indices into a palette that follows the header.</summary>
  public const byte TypePalette = 1;

  /// <summary>The image is a run of luminance values.</summary>
  public const byte TypeLuminance = 2;

  /// <summary>Blue, green and red a pixel, in that order.</summary>
  public const byte TypeBgr = 3;

  /// <summary>Blue, green, red and alpha a pixel, in that order.</summary>
  public const byte TypeBgra = 4;

  /// <summary>The data is the pixels as they stand.</summary>
  public const byte CompressionNone = 0;

  /// <summary>Run-length coding, which the document takes from the Targa specification.</summary>
  public const byte CompressionRle = 1;

  /// <summary>miniLZO, which is not decoded here.</summary>
  public const byte CompressionLzo = 2;

  /// <summary>zlib's own stream, as its <c>compress</c> writes it.</summary>
  public const byte CompressionZlib = 3;

  /// <summary>Bytes one palette entry takes: blue, green, red and alpha.</summary>
  public const int PaletteEntrySize = 4;

  static string IImageFormatMetadata<OilFile>.PrimaryExtension => ".oil";
  static string[] IImageFormatMetadata<OilFile>.FileExtensions => [".oil"];
  static OilFile IImageFormatReader<OilFile>.FromSpan(ReadOnlySpan<byte> data) => OilReader.FromSpan(data);

  static VideoMode[] IImageFormatMetadata<OilFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  static bool? IImageFormatMetadata<OilFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 8)
      return null;

    return header[..4].SequenceEqual(Signature)
      && header[4] == (MagicNumber & 0xFF)
      && header[5] == ((MagicNumber >> 8) & 0xFF)
      && header[6] == ((MagicNumber >> 16) & 0xFF)
      && header[7] == 0;
  }

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>What the picture is made of, which decides how the data is read.</summary>
  public PixelFormat Format { get; init; }

  /// <summary>The pixels, already the way <see cref="Format"/> says.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>The palette as red, green and blue triplets, where the picture has one.</summary>
  public byte[]? Palette { get; init; }

  /// <summary>How many entries of it the file stated.</summary>
  public int PaletteCount { get; init; }

  public static RawImage ToRawImage(OilFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = file.Format,
    PixelData = file.PixelData[..],
    Palette = file.Palette?[..],
    PaletteCount = file.PaletteCount,
  };
}
