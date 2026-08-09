using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.ApolloHdru;

/// <summary>An Apollo HDRU document raster (.hdru, .gn).</summary>
/// <remarks>
/// The name is XnView's and nothing is published under it. Nothing ties it to Apollo Computer
/// either: the machine's own graphics documentation describes GPR and GMR and nothing of this shape,
/// and the format's own sibling HRU is described on the file-format wiki as being of unknown origin.
/// What it plainly is, is a scanned page: one bit a pixel, with a choice of no compression or Group
/// 3 or Group 4 fax coding.
/// <para/>
/// The header was recovered by handing XnView's own converter files built to a hypothesis. Sixteen
/// bytes, big-endian: two of signature that have to be <c>01 01</c>, a compression code, a
/// resolution that becomes both the horizontal and the vertical dots per inch, the width in pixels,
/// the height in lines, a word that changes nothing observable, and four bytes that are not read.
/// A file built that way is reported as an Apollo HDRU of the size written, at one bit a pixel, with
/// the resolution written; changing the signature to <c>01 02</c> makes it refuse the file, and a
/// compression code of 2 makes it say Group 4.
/// <para/>
/// Rows are whole bytes and a set bit is white. That last is measured rather than assumed: XnView's
/// portable bitmap of the sample built here is the file's own bytes inverted, and a portable bitmap
/// is one-for-black.
/// <para/>
/// Only the uncompressed case is read. The header says which of the three a file is, but where a fax
/// code stream begins and how its ends of line are framed is not visible from the header alone, and
/// no file of either kind could be had to settle it. A compressed one is refused by name rather than
/// decoded on a guess.
/// <para/>
/// The two bytes it opens with are not registered as a signature the way most formats' are. Reading
/// a file by its bytes alone takes the first format whose signature matches and does not try a
/// second, so a two-byte one this common would take files away from formats that really are what
/// they say they are. The reader still requires them; only content sniffing is left out of it, and
/// a page under its own name is read as it should be.
/// </remarks>
public readonly record struct ApolloHdruFile
  : IImageFormatReader<ApolloHdruFile>, IImageToRawImage<ApolloHdruFile>,
    IImageFromRawImage<ApolloHdruFile>, IImageFormatWriter<ApolloHdruFile> {

  /// <summary>The two bytes a file opens with.</summary>
  public static ReadOnlySpan<byte> Magic => [0x01, 0x01];

  /// <summary>The header, in front of the picture.</summary>
  public const int HeaderSize = 16;

  /// <summary>The compression code an uncompressed page carries.</summary>
  public const int Uncompressed = 0;

  /// <summary>The largest side the reader accepts.</summary>
  public const int MaximumSide = 32767;

  /// <summary>Black then white, which is the order a set bit's meaning puts them in.</summary>
  private static readonly byte[] _Palette = [0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF];

  static string IImageFormatMetadata<ApolloHdruFile>.PrimaryExtension => ".hdru";
  static string[] IImageFormatMetadata<ApolloHdruFile>.FileExtensions => [".hdru", ".gn"];
  static ApolloHdruFile IImageFormatReader<ApolloHdruFile>.FromSpan(ReadOnlySpan<byte> data) => ApolloHdruReader.FromSpan(data);
  static byte[] IImageFormatWriter<ApolloHdruFile>.ToBytes(ApolloHdruFile file) => ApolloHdruWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<ApolloHdruFile>.VideoModes => [
    new("Bilevel", [(IntegerRange.Any, IntegerRange.Any)], [2])
  ];

  /// <summary>How wide the page is, in pixels.</summary>
  public int Width { get; init; }

  /// <summary>How many lines it has.</summary>
  public int Height { get; init; }

  /// <summary>The dots per inch the header states, used for both directions.</summary>
  public int Resolution { get; init; }

  /// <summary>The rows, one bit a pixel with a set bit white, each row padded out to a whole byte.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(ApolloHdruFile file) {
    if (file.PixelData == null)
      throw new InvalidOperationException("No page was read.");

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed1,
      PixelData = file.PixelData[..],
      Palette = _Palette[..],
      PaletteCount = 2,
    };
  }

  /// <summary>Builds the uncompressed page from a one-bit picture.</summary>
  public static ApolloHdruFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    var source = image.EnsureFormat(PixelFormat.Indexed1);
    return new() {
      Width = source.Width,
      Height = source.Height,
      Resolution = 300,
      PixelData = source.PixelData[..],
    };
  }
}
