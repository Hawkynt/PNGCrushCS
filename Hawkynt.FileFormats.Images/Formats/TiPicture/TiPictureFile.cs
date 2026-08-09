using System;
using FileFormat.Core;

namespace FileFormat.TiPicture;

/// <summary>In-memory representation of a picture variable from a TI-73/82/83/85/86 calculator.</summary>
/// <remarks>
/// These are transfer files, the thing the link cable writes: <c>**TI82**</c> and a byte pair, a
/// forty-two byte comment, and then a run of variable entries with a checksum after them. A picture
/// is one entry among possibly several, and the screen it holds is the calculator's own — ninety-six
/// by sixty-three on the TI-73, TI-82 and TI-83, a hundred and twenty-eight by sixty-three on the
/// TI-85 and TI-86 — one bit to the pixel, set meaning lit.
/// <para/>
/// Nothing about the size is guessed. Every entry states its length twice and the picture inside it
/// states its own again, the header and the entries and the checksum account for the file to the
/// byte, and the byte count that leaves is exactly the screen of the calculator whose name is in the
/// signature. The one sample carrying two pictures accounts the same way for both.
/// <para/>
/// The TI-73 stands with the TI-82 and TI-83 rather than beside them: same container, same entry
/// type, same ninety-six by sixty-three screen. It was left out because no sample carried it, and
/// that was settled instead by building the same picture under both signatures and handing the pair
/// to XnView's converter, which read them identically. Only the signature differs.
/// <para/>
/// The <c>**TI92**</c> files are a different container — a folder of named entries rather than a run
/// of them — and their picture data is compressed, so they are not read here.
/// </remarks>
public readonly record struct TiPictureFile
  : IImageFormatReader<TiPictureFile>, IImageToRawImage<TiPictureFile>,
    IImageFromRawImage<TiPictureFile>, IImageFormatWriter<TiPictureFile> {

  /// <summary>The signature is eight bytes, then two more of format and a nought.</summary>
  public const int SignatureSize = 11;

  /// <summary>The comment sits between the signature and the length of the data that follows.</summary>
  public const int CommentSize = 42;

  /// <summary>Signature, comment and the two bytes giving the length of the entries.</summary>
  public const int HeaderSize = SignatureSize + CommentSize + 2;

  /// <summary>The type byte a picture entry carries on the TI-73, TI-82 and TI-83.</summary>
  public const byte PictureType8283 = 0x07;

  /// <summary>The type byte a picture entry carries on the TI-85 and TI-86.</summary>
  public const byte PictureType8586 = 0x11;

  /// <summary>Screen width of a TI-73, TI-82 or TI-83.</summary>
  public const int Width8283 = 96;

  /// <summary>Screen width of a TI-85 or TI-86.</summary>
  public const int Width8586 = 128;

  /// <summary>Screen height of all four, which is the display less its top row of indicators.</summary>
  public const int ScreenHeight = 63;

  /// <summary>Nought is the unlit background and one the lit pixel.</summary>
  private static readonly byte[] _Palette = [255, 255, 255, 0, 0, 0];

  static string IImageFormatMetadata<TiPictureFile>.PrimaryExtension => ".82i";
  static string[] IImageFormatMetadata<TiPictureFile>.FileExtensions => [".73i", ".82i", ".83i", ".85i", ".86i"];
  static TiPictureFile IImageFormatReader<TiPictureFile>.FromSpan(ReadOnlySpan<byte> data) => TiPictureReader.FromSpan(data);
  static byte[] IImageFormatWriter<TiPictureFile>.ToBytes(TiPictureFile file) => TiPictureWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<TiPictureFile>.VideoModes => [
    new("TI-82/83", [(new(Width8283, Width8283), new(ScreenHeight, ScreenHeight))], [2]),
    new("TI-85/86", [(new(Width8586, Width8586), new(ScreenHeight, ScreenHeight))], [2]),
  ];

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>The two digits the signature names the calculator with, such as <c>86</c>.</summary>
  public string? Model { get; init; }

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

  public static TiPictureFile FromRawImage(RawImage image) => FromRawImage(image, ".82i");

  /// <summary>Fits the picture to the screen of the calculator the extension names.</summary>
  /// <remarks>
  /// The extension is the only thing that says which of the two screens a file is for — the layout is
  /// otherwise identical — so a picture written as <c>.85i</c> or <c>.86i</c> gets the wider one and
  /// everything else the narrower. There is nothing to refuse: a picture of some other size is
  /// sampled to the screen, because the screen is a fact about the calculator rather than a fault in
  /// the picture.
  /// </remarks>
  public static TiPictureFile FromRawImage(RawImage image, string extension) {
    ArgumentNullException.ThrowIfNull(image);

    var model = extension?.ToLowerInvariant() switch {
      ".83i" => "83",
      ".85i" => "85",
      ".86i" => "86",
      _ => "82",
    };

    var width = model is "85" or "86" ? Width8586 : Width8283;
    var screen = image.Width == width && image.Height == ScreenHeight ? image : image.SampleTo(width, ScreenHeight);

    // Index one is the lit pixel, which the palette here draws black.
    return new() {
      Width = width,
      Height = ScreenHeight,
      Model = model,
      PixelData = BilevelRows.Pack(BilevelRows.Threshold(screen, setWhenDark: true), width, ScreenHeight),
    };
  }
}
