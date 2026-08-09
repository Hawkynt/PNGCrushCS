using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.AirNav;

/// <summary>An AirNav picture (.anv): a 256-colour Windows bitmap whose signature has been changed to AN.</summary>
/// <remarks>
/// XnView calls this one AirNav and nothing has ever been published about it. What it is, is settled
/// by its reader: take an ordinary 256-colour Windows bitmap, write <c>AN</c> over the <c>BM</c> it
/// opens with, and XnView reads it as an AirNav picture of exactly the right size, with the colours
/// the bitmap's own table gives. That was checked pixel by pixel against a table built to be
/// distinguishable, and every one of the 77 came out as the entry the index names.
/// <para/>
/// The reader that does that does not read the bitmap's fields. It takes the width and the height
/// from where a Windows bitmap keeps them, at 18 and 22, and then goes to fixed places for the rest:
/// the colour table at 54 as 256 entries of four bytes, blue first with the fourth ignored, and the
/// picture at 1078 as one byte a pixel, rows padded out to a multiple of four and stored from the
/// bottom up. Handed a 24-bit bitmap renamed the same way it still reads it as eight-bit at those
/// same offsets. So the offsets are the format and the header fields are not, and that is what is
/// read here.
/// <para/>
/// Two letters is not a signature, so what really has to hold is the arithmetic: the file has to be
/// long enough for the table and for every row of the picture, and it has to describe itself as the
/// 256-colour bitmap it is — forty bytes of information header and eight bits a pixel. A file that
/// says anything else under this name is refused rather than read at offsets it never meant.
/// </remarks>
public readonly record struct AirNavFile
  : IImageFormatReader<AirNavFile>, IImageToRawImage<AirNavFile>,
    IImageFromRawImage<AirNavFile>, IImageFormatWriter<AirNavFile> {

  /// <summary>The two letters a file opens with, where a Windows bitmap has BM.</summary>
  public static ReadOnlySpan<byte> Magic => "AN"u8;

  /// <summary>Where the colour table stands.</summary>
  public const int PaletteOffset = 54;

  /// <summary>How many entries it has, of four bytes each.</summary>
  public const int PaletteEntries = 256;

  /// <summary>Where the picture stands.</summary>
  public const int PixelOffset = PaletteOffset + PaletteEntries * 4;

  /// <summary>The largest side the reader accepts.</summary>
  public const int MaximumSide = 16000;

  static string IImageFormatMetadata<AirNavFile>.PrimaryExtension => ".anv";
  static string[] IImageFormatMetadata<AirNavFile>.FileExtensions => [".anv"];
  static AirNavFile IImageFormatReader<AirNavFile>.FromSpan(ReadOnlySpan<byte> data) => AirNavReader.FromSpan(data);
  static byte[] IImageFormatWriter<AirNavFile>.ToBytes(AirNavFile file) => AirNavWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<AirNavFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [256])
  ];

  /// <summary>How wide the picture is.</summary>
  public int Width { get; init; }

  /// <summary>How tall it is.</summary>
  public int Height { get; init; }

  /// <summary>The indices, one byte a pixel, top row first with no padding.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>The colour table as 256 red, green and blue triplets.</summary>
  public byte[] Palette { get; init; }

  public static RawImage ToRawImage(AirNavFile file) {
    if (file.PixelData == null)
      throw new InvalidOperationException("No picture was read.");

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = file.Palette,
      PaletteCount = PaletteEntries,
    };
  }

  /// <summary>Builds the 256-colour picture, quantising anything that is not already one.</summary>
  public static AirNavFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    var source = image.EnsureFormat(PixelFormat.Indexed8);

    var palette = new byte[PaletteEntries * 3];
    if (source.Palette != null)
      source.Palette.AsSpan(0, Math.Min(source.Palette.Length, palette.Length)).CopyTo(palette);

    return new() {
      Width = source.Width,
      Height = source.Height,
      PixelData = source.PixelData[..],
      Palette = palette,
    };
  }
}
