using System;
using FileFormat.Core;

namespace FileFormat.PocketPcTheme;

/// <summary>The picture inside a Pocket PC theme (<c>.tsk</c>).</summary>
/// <remarks>
/// A theme is a Microsoft cabinet: <c>MSCF</c>, a header naming the folders and the files in them,
/// and the files themselves compressed with MSZIP or LZX. The pictures a theme dresses the Today
/// screen with are ordinary GIF, PNG and JPEG files stored in it.
/// <para/>
/// XnView does not unpack the cabinet. Its reader checks the four signature bytes and then scans the
/// rest of the file byte by byte for the opening bytes of a picture — <c>GIF8</c>, PNG's
/// <c>0x89 P N G</c>, or a JFIF's <c>FF D8 FF E0</c> — and decodes from the first one it finds. That
/// only ever finds a picture the cabinet happened to store uncompressed, which is what a cabinet
/// does for a file that would not get smaller; anything MSZIP or LZX packed is invisible to it. This
/// reader does the same thing, so it reads the themes XnView reads and no others.
/// <para/>
/// The JPEG test is on all four bytes and not three. A JPEG carrying an Exif segment opens
/// <c>FF D8 FF E1</c> and is not matched — checked against XnView's converter, which refuses such a
/// file under this format's name and falls through to its own general JPEG scan instead.
/// </remarks>
public readonly record struct PocketPcThemeFile
  : IImageFormatReader<PocketPcThemeFile>, IImageToRawImage<PocketPcThemeFile> {

  /// <summary>The four bytes a Microsoft cabinet opens with.</summary>
  public static ReadOnlySpan<byte> Signature => "MSCF"u8;

  /// <summary>Where the search for a picture begins, which is directly behind the signature.</summary>
  public const int ScanStart = 4;

  /// <summary>The four bytes a GIF opens with, whichever version it is.</summary>
  public static ReadOnlySpan<byte> GifSignature => "GIF8"u8;

  /// <summary>The first four of the eight bytes a PNG opens with.</summary>
  public static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47];

  /// <summary>The four bytes a JFIF opens with.</summary>
  public static ReadOnlySpan<byte> JfifSignature => [0xFF, 0xD8, 0xFF, 0xE0];

  static string IImageFormatMetadata<PocketPcThemeFile>.PrimaryExtension => ".tsk";
  static string[] IImageFormatMetadata<PocketPcThemeFile>.FileExtensions => [".tsk"];
  static PocketPcThemeFile IImageFormatReader<PocketPcThemeFile>.FromSpan(ReadOnlySpan<byte> data)
    => PocketPcThemeReader.FromSpan(data);

  static VideoMode[] IImageFormatMetadata<PocketPcThemeFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  /// <summary>
  /// Abstains rather than claiming a cabinet: an installer and a theme open with the same four
  /// bytes, and whether this one stores a picture uncompressed is not known until it is scanned.
  /// </summary>
  static bool? IImageFormatMetadata<PocketPcThemeFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < Signature.Length)
      return null;

    return header[..Signature.Length].SequenceEqual(Signature) ? null : false;
  }

  /// <summary>Image width in pixels, as the picture inside states it.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>The decoded picture, three bytes a pixel, red first.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(PocketPcThemeFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Rgb24,
    PixelData = file.PixelData[..],
  };
}
