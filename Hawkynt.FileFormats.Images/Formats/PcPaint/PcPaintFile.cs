using System;
using FileFormat.Core;

namespace FileFormat.PcPaint;

/// <summary>A PC Paint / Pictor page (.pic, .clp, .sim).</summary>
/// <remarks>
/// Seventeen bytes of header, then a palette whose size the header states, then a count of the
/// compressed blocks the picture is stored in. Byte 10 packs the depth: its low nibble is the bits
/// per pixel of a plane, its high nibble the number of planes past the first. Byte 11 is 0FFh on
/// everything the second version of the program and later wrote, and says that the palette fields
/// behind it are there to be read.
/// <para/>
/// This was read wrongly here for as long as it had been read at all. Byte 10 was taken as a count
/// of planes and byte 11 as a depth, so the only sample there is — which states one plane of two
/// bits and a CGA palette — was refused for having a depth of 255; the two words at 12 and 14 were
/// taken as an aspect ratio, which the format has no field for; and the compressed data was read as
/// a bare run of count-and-value pairs, where it is in fact blocks with their own headers and a
/// marker byte saying which byte introduces a run.
/// <para/>
/// Read as it is written, that sample accounts for itself three times over: the block states a size
/// that ends on the last byte of the file, it states a decompressed length equal to the picture's
/// rows times its row stride, and the decompression consumes the block exactly and produces exactly
/// that many bytes. The picture is a line of text and reads as one — the rows are stored bottom
/// upwards, which is what the header's offsets being a lower-left corner implies.
/// <para/>
/// Planes past the first are refused rather than guessed at: how the four-plane EGA modes interleave
/// is described two different ways in the sources that describe it at all, and there is no sample
/// here to tell them apart.
/// </remarks>
public readonly record struct PcPaintFile : IImageFormatReader<PcPaintFile>, IImageToRawImage<PcPaintFile>, IImageFromRawImage<PcPaintFile>, IImageFormatWriter<PcPaintFile> {

  /// <summary>The word every one of these opens with.</summary>
  internal const ushort Magic = 0x1234;

  /// <summary>The fixed part of the header, up to and including the palette's stated size.</summary>
  internal const int HeaderSize = 17;

  /// <summary>The value byte 11 carries on everything from version 2 onwards.</summary>
  internal const byte VersionTwoFlag = 0xFF;

  /// <summary>Size of a full 256-entry RGB palette once expanded to eight bits a channel.</summary>
  internal const int PaletteSize = 768;

  /// <summary>The palette kinds byte 13 names.</summary>
  internal const int PaletteNone = 0, PaletteCga = 1, PalettePcJr = 2, PaletteEga = 3, PaletteVga = 4;

  /// <summary>How many bytes of palette each kind stores.</summary>
  internal const int CgaPaletteBytes = 2, EgaPaletteBytes = 16, VgaPaletteBytes = 768;

  /// <summary>A block header: its own size, the length it decompresses to, and the run marker.</summary>
  internal const int BlockHeaderSize = 5;

  static string IImageFormatMetadata<PcPaintFile>.PrimaryExtension => ".pic";

  /// <summary>Every name a Pictor page arrives under.</summary>
  /// <remarks>
  /// <c>.sim</c> is the same file written by the flight simulator that used the format for its
  /// screen furniture, and the one sample of it is an ordinary version 2 page.
  /// </remarks>
  static string[] IImageFormatMetadata<PcPaintFile>.FileExtensions => [".pic", ".clp", ".sim"];
  static PcPaintFile IImageFormatReader<PcPaintFile>.FromSpan(ReadOnlySpan<byte> data) => PcPaintReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<PcPaintFile>.VideoModes => [new("Default", [(IntegerRange.Any, IntegerRange.Any)], [new IntegerRange(2, 256)])];
  static byte[] IImageFormatWriter<PcPaintFile>.ToBytes(PcPaintFile file) => PcPaintWriter.ToBytes(file);

  static bool? IImageFormatMetadata<PcPaintFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < HeaderSize || header[0] != 0x34 || header[1] != 0x12)
      return null;

    var width = (ushort)(header[2] | (header[3] << 8));
    var height = (ushort)(header[4] | (header[5] << 8));
    var bitsPerPixel = header[10] & 0x0F;
    return width > 0 && height > 0 && bitsPerPixel is 1 or 2 or 4 or 8 && header[11] == VersionTwoFlag ? true : null;
  }

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Column of the picture's lower-left corner on the screen it was cut from.</summary>
  public ushort XOffset { get; init; }

  /// <summary>Row of the picture's lower-left corner on the screen it was cut from.</summary>
  public ushort YOffset { get; init; }

  /// <summary>Bits per pixel: 1, 2, 4 or 8.</summary>
  public byte BitsPerPixel { get; init; }

  /// <summary>The screen mode the page was drawn in, as the letter the header carries.</summary>
  public byte VideoMode { get; init; }

  /// <summary>Which kind of palette the file stored: none, CGA, PCjr, EGA or VGA.</summary>
  public ushort PaletteType { get; init; }

  /// <summary>The palette as RGB triplets, expanded from whatever the file stored.</summary>
  public byte[] Palette { get; init; }

  /// <summary>
  /// The picture, top row first, one palette index a byte whatever depth the file packed it at.
  /// </summary>
  public byte[] PixelData { get; init; }

  /// <summary>Hands over the picture as indices into the palette the file carried.</summary>
  public static RawImage ToRawImage(PcPaintFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed8,
    PixelData = file.PixelData[..],
    Palette = file.Palette[..],
    PaletteCount = file.Palette.Length / 3,
  };

  /// <summary>Encodes a picture as a version 2 page with a VGA palette.</summary>
  public static PcPaintFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Indexed8);
    if (image.Palette == null || image.Palette.Length < 3)
      throw new ArgumentException("A Pictor page needs an RGB palette.", nameof(image));

    var palette = new byte[PaletteSize];
    image.Palette.AsSpan(0, Math.Min(image.Palette.Length, PaletteSize)).CopyTo(palette);

    return new() {
      Width = image.Width,
      Height = image.Height,
      BitsPerPixel = 8,
      VideoMode = (byte)'A',
      PaletteType = PaletteVga,
      Palette = palette,
      PixelData = image.PixelData[..],
    };
  }
}
