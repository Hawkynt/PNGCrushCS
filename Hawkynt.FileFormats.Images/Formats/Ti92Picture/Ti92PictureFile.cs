using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Ti92Picture;

/// <summary>One picture variable of a TI-89, TI-92, TI-92 Plus or Voyage 200 transfer file.</summary>
/// <param name="Name">The variable's name, up to eight characters.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="PixelData">One bit to the pixel, set meaning lit, rows padded to whole bytes.</param>
public sealed record Ti92Picture(string Name, int Width, int Height, byte[] PixelData);

/// <summary>A TI-89 / TI-92 / TI-92 Plus / Voyage 200 transfer file holding picture variables.</summary>
/// <remarks>
/// These are the files TI-Graph Link and TI Connect write for the 68000 calculators, and a different
/// container from the TI-82's: an eight-byte signature (<c>**TI92**</c>, <c>**TI89**</c>,
/// <c>**TI92P*</c> or <c>**TIV2**</c>), two bytes, the default folder's name in eight and a
/// forty-byte comment, then a count and a directory of sixteen-byte entries — the variable's
/// absolute offset, its name, its type and its attribute — the file's size and the marker
/// <c>A5 5A</c>. Each variable is four bytes of nought, a big-endian length, the data and a
/// little-endian checksum, the low sixteen bits of the sum of the length bytes and the data. A
/// picture (type <c>0x10</c>) is its height and width, big-endian, the bitmap, and the tag byte
/// <c>0xDF</c>; any size up to the screen is allowed, and nothing in it is compressed.
/// <para/>
/// Every offset is absolute and is followed rather than assumed, and the checksum and the stated file
/// size are not held against a file: the one TI-92 sample there is, written by a third-party tool,
/// has no checksum after its first picture and states a file size the file does not have, yet both
/// its pictures are where the directory says. Files the calculators' own link software wrote account
/// to the byte, and so does every file written here.
/// </remarks>
public readonly record struct Ti92PictureFile
  : IImageFormatReader<Ti92PictureFile>, IImageToRawImage<Ti92PictureFile>, IImageFromRawImage<Ti92PictureFile>,
    IImageFormatWriter<Ti92PictureFile>, IMultiImageFileFormat<Ti92PictureFile> {

  /// <summary>Signature, two bytes, folder name and comment, before the entry count.</summary>
  public const int HeaderSize = 0x3A;

  /// <summary>Each directory entry: offset, name, type, attribute and two bytes.</summary>
  public const int EntrySize = 16;

  /// <summary>The type byte of a picture variable.</summary>
  public const byte PictureType = 0x10;

  /// <summary>The byte that closes a picture's data.</summary>
  public const byte PictureTag = 0xDF;

  /// <summary>Nought is the unlit background and one the lit pixel.</summary>
  private static readonly byte[] _Palette = [255, 255, 255, 0, 0, 0];

  static string IImageFormatMetadata<Ti92PictureFile>.PrimaryExtension => ".92i";
  static string[] IImageFormatMetadata<Ti92PictureFile>.FileExtensions => [".92i", ".89i", ".9xi", ".v2i"];
  static Ti92PictureFile IImageFormatReader<Ti92PictureFile>.FromSpan(ReadOnlySpan<byte> data) => Ti92PictureReader.FromSpan(data);
  static byte[] IImageFormatWriter<Ti92PictureFile>.ToBytes(Ti92PictureFile file) => Ti92PictureWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<Ti92PictureFile>.VideoModes => [
    new("TI-89", [(new(1, 160), new(1, 100))], [2]),
    new("TI-92 / TI-92 Plus / Voyage 200", [(new(1, 240), new(1, 128))], [2]),
  ];

  static bool? IImageFormatMetadata<Ti92PictureFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => Ti92PictureReader.ModelOf(header) != null ? true : null;

  /// <summary>The calculator the signature names: <c>92</c>, <c>89</c>, <c>92P</c> or <c>V2</c>.</summary>
  public string Model { get; init; }

  /// <summary>The default folder the file names, up to eight characters.</summary>
  public string Folder { get; init; }

  /// <summary>The picture variables, in directory order.</summary>
  public IReadOnlyList<Ti92Picture> Pictures { get; init; }

  public static int ImageCount(Ti92PictureFile file) => file.Pictures.Count;

  public static RawImage ToRawImage(Ti92PictureFile file) => ToRawImage(file, 0);

  public static RawImage ToRawImage(Ti92PictureFile file, int index) {
    var picture = file.Pictures[index];
    return new() {
      Width = picture.Width,
      Height = picture.Height,
      Format = PixelFormat.Indexed1,
      PixelData = picture.PixelData[..],
      Palette = _Palette[..],
      PaletteCount = 2,
    };
  }

  /// <summary>One picture named <c>pic1</c> in folder <c>main</c>, for the TI-92 Plus, whose screen holds any picture up to 240 by 128.</summary>
  public static Ti92PictureFile FromRawImage(RawImage image) => FromRawImage(image, ".9xi");

  /// <summary>One picture for the calculator the extension names, sampled down only where it is larger than that calculator's screen.</summary>
  public static Ti92PictureFile FromRawImage(RawImage image, string extension) {
    ArgumentNullException.ThrowIfNull(image);
    var model = extension?.ToLowerInvariant() switch {
      ".92i" => "92",
      ".89i" => "89",
      ".v2i" => "V2",
      _ => "92P",
    };

    var (maxWidth, maxHeight) = model == "89" ? (160, 100) : (240, 128);
    var width = Math.Min(image.Width, maxWidth);
    var height = Math.Min(image.Height, maxHeight);
    var screen = width == image.Width && height == image.Height ? image : image.SampleTo(width, height);
    return new() {
      Model = model,
      Folder = "main",
      Pictures = [new("pic1", width, height, BilevelRows.Pack(BilevelRows.Threshold(screen, setWhenDark: true), width, height))],
    };
  }
}
