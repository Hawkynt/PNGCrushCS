using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Pax;

/// <summary>In-memory representation of a Smaller Animals Pick Ax (PAX) picture.</summary>
/// <remarks>
/// A PAX is a picture locked under a password: <c>PAX</c>, a 128-byte identifier left in the clear
/// for password managers, and then everything else — header, palette, tags and the
/// deflate-compressed rows — encrypted under a key the password is hashed into. The password is not
/// in the file, so a reader needs it: <see cref="PaxReader.FromBytes(byte[], string)"/> takes it, and
/// the password-less entry points used by the registry try the empty password and otherwise refuse
/// the file by name. <see cref="Password"/> is what the writer encrypts under.
/// </remarks>
[FormatMagicBytes([0x50, 0x41, 0x58])]
[FormatMimeType("image/x-pax")]
public readonly record struct PaxFile : IImageFormatReader<PaxFile>, IImageToRawImage<PaxFile>, IImageFromRawImage<PaxFile>, IImageFormatWriter<PaxFile> {

  /// <summary>Bytes in the identifier that follows the <c>PAX</c> tag.</summary>
  public const int FileIdSize = 128;

  static string IImageFormatMetadata<PaxFile>.PrimaryExtension => ".pax";
  static string[] IImageFormatMetadata<PaxFile>.FileExtensions => [".pax"];
  static PaxFile IImageFormatReader<PaxFile>.FromSpan(ReadOnlySpan<byte> data) => PaxReader.FromSpan(data);
  static byte[] IImageFormatWriter<PaxFile>.ToBytes(PaxFile file) => PaxWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>How the rows store a pixel.</summary>
  public PaxColorMode ColorMode { get; init; }

  /// <summary>The cipher the rows are encrypted with.</summary>
  public PaxEncryption Encryption { get; init; }

  /// <summary>The password the file was read with, and the one the writer encrypts under.</summary>
  public string Password { get; init; }

  /// <summary>The 128-byte identifier stored unencrypted after the tag; all zero when unused.</summary>
  public byte[] FileId { get; init; }

  /// <summary>The application that wrote the file, at most 32 characters.</summary>
  public string ApplicationName { get; init; }

  /// <summary>The author recorded in the file, at most 32 characters.</summary>
  public string AuthorName { get; init; }

  /// <summary>The date the header states, or <c>null</c> where it states none.</summary>
  public DateOnly? Date { get; init; }

  /// <summary>Palette as RGB triplets for <see cref="PaxColorMode.Indexed8"/>.</summary>
  public byte[]? Palette { get; init; }

  /// <summary>Entries in <see cref="Palette"/>.</summary>
  public int PaletteCount { get; init; }

  /// <summary>The named metadata records between the header and the rows.</summary>
  public IReadOnlyList<PaxTag> Tags { get; init; }

  /// <summary>
  /// Rows top to bottom without padding: one index a pixel, a little-endian <c>xRRRRRGGGGGBBBBB</c>
  /// word a pixel, or R, G, B a pixel, as <see cref="ColorMode"/> says.
  /// </summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(PaxFile file) {
    var count = file.Width * file.Height;
    switch (file.ColorMode) {
      case PaxColorMode.Indexed8:
        return new() {
          Width = file.Width,
          Height = file.Height,
          Format = PixelFormat.Indexed8,
          PixelData = file.PixelData[..count],
          Palette = file.Palette?[..] ?? [],
          PaletteCount = file.PaletteCount,
        };
      case PaxColorMode.Rgb555: {
        var rgb = new byte[count * 3];
        for (var i = 0; i < count; ++i) {
          var word = file.PixelData[2 * i] | (file.PixelData[2 * i + 1] << 8);
          rgb[3 * i] = _Expand5(word >> 10);
          rgb[3 * i + 1] = _Expand5(word >> 5);
          rgb[3 * i + 2] = _Expand5(word);
        }

        return new() { Width = file.Width, Height = file.Height, Format = PixelFormat.Rgb24, PixelData = rgb };
      }
      default:
        return new() { Width = file.Width, Height = file.Height, Format = PixelFormat.Rgb24, PixelData = file.PixelData[..(count * 3)] };
    }
  }

  /// <summary>Five bits to eight by repeating the top bits into the bottom, so 31 reaches 255.</summary>
  private static byte _Expand5(int value) {
    value &= 0x1F;
    return (byte)((value << 3) | (value >> 2));
  }

  /// <summary>
  /// A PAX of <paramref name="image"/>, encrypted with chained Blowfish under the empty password —
  /// set <see cref="Password"/> (and <see cref="Encryption"/>) on the result to lock it properly.
  /// </summary>
  public static PaxFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    var indexed = image.Format is PixelFormat.Indexed8 or PixelFormat.Indexed4 or PixelFormat.Indexed1;
    image = image.EnsureFormat(indexed ? PixelFormat.Indexed8 : PixelFormat.Rgb24);

    return new() {
      Width = image.Width,
      Height = image.Height,
      ColorMode = indexed ? PaxColorMode.Indexed8 : PaxColorMode.Rgb24,
      Encryption = PaxEncryption.BlowfishCbc,
      Password = string.Empty,
      FileId = new byte[FileIdSize],
      ApplicationName = string.Empty,
      AuthorName = string.Empty,
      Palette = indexed ? image.Palette?[..(image.PaletteCount * 3)] : null,
      PaletteCount = indexed ? image.PaletteCount : 0,
      Tags = [],
      PixelData = image.PixelData[..],
    };
  }
}
