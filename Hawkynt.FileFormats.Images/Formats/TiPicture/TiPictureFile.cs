using System;
using FileFormat.Core;

namespace FileFormat.TiPicture;

/// <summary>In-memory representation of a picture variable from a TI-82/83/85/86 calculator.</summary>
/// <remarks>
/// These are transfer files, the thing the link cable writes: <c>**TI82**</c> and a byte pair, a
/// forty-two byte comment, and then a run of variable entries with a checksum after them. A picture
/// is one entry among possibly several, and the screen it holds is the calculator's own — ninety-six
/// by sixty-three on the TI-82 and TI-83, a hundred and twenty-eight by sixty-three on the TI-85 and
/// TI-86 — one bit to the pixel, set meaning lit.
/// <para/>
/// Nothing about the size is guessed. Every entry states its length twice and the picture inside it
/// states its own again, the header and the entries and the checksum account for the file to the
/// byte, and the byte count that leaves is exactly the screen of the calculator whose name is in the
/// signature. The one sample carrying two pictures accounts the same way for both.
/// <para/>
/// The <c>**TI92**</c> files are a different container — a folder of named entries rather than a run
/// of them — and their picture data is compressed, so they are not read here.
/// </remarks>
public readonly record struct TiPictureFile : IImageFormatReader<TiPictureFile>, IImageToRawImage<TiPictureFile> {

  /// <summary>The signature is eight bytes, then two more of format and a nought.</summary>
  public const int SignatureSize = 11;

  /// <summary>The comment sits between the signature and the length of the data that follows.</summary>
  public const int CommentSize = 42;

  /// <summary>Signature, comment and the two bytes giving the length of the entries.</summary>
  public const int HeaderSize = SignatureSize + CommentSize + 2;

  /// <summary>The type byte a picture entry carries on the TI-82 and TI-83.</summary>
  public const byte PictureType8283 = 0x07;

  /// <summary>The type byte a picture entry carries on the TI-85 and TI-86.</summary>
  public const byte PictureType8586 = 0x11;

  /// <summary>Screen width of a TI-82 or TI-83.</summary>
  public const int Width8283 = 96;

  /// <summary>Screen width of a TI-85 or TI-86.</summary>
  public const int Width8586 = 128;

  /// <summary>Screen height of all four, which is the display less its top row of indicators.</summary>
  public const int ScreenHeight = 63;

  /// <summary>Nought is the unlit background and one the lit pixel.</summary>
  private static readonly byte[] _Palette = [255, 255, 255, 0, 0, 0];

  static string IImageFormatMetadata<TiPictureFile>.PrimaryExtension => ".82i";
  static string[] IImageFormatMetadata<TiPictureFile>.FileExtensions => [".82i", ".83i", ".85i", ".86i"];
  static TiPictureFile IImageFormatReader<TiPictureFile>.FromSpan(ReadOnlySpan<byte> data) => TiPictureReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<TiPictureFile>.VideoModes => [
    new("TI-82/83", [(new(Width8283, Width8283), new(ScreenHeight, ScreenHeight))], [2]),
    new("TI-85/86", [(new(Width8586, Width8586), new(ScreenHeight, ScreenHeight))], [2]),
  ];

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>The screen as it stands in the file, one bit to the pixel, rows padded to whole bytes.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(TiPictureFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed1,
      PixelData = file.PixelData[..],
      Palette = _Palette[..],
      PaletteCount = 2,
    };
  }
}
