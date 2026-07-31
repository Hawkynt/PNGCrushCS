using System;
using FileFormat.Core;

namespace FileFormat.Hp48Grob;

/// <summary>In-memory representation of an HP 48 graphics object (.grb, .gro).</summary>
/// <remarks>
/// A calculator's screen, in one of the two forms the machine itself will accept. The binary one
/// counts its own size in nibbles rather than bytes, the HP 48 being a four-bit machine throughout,
/// and stores its dimensions across nibble boundaries so that no field starts where a byte does.
/// The text one is what the machine prints when asked to transmit an object over a serial line: a
/// header naming the size in decimal, then the bitmap as hexadecimal digits.
/// <para/>
/// In both, a byte's bits run from the least significant end, because the display is scanned that
/// way. In the text form the two nibbles of each byte are swapped as well — the machine's nibbles
/// are little-endian and printing them as a byte reverses the pair — so the bit order there is
/// 4, 5, 6, 7, 0, 1, 2, 3.
/// </remarks>
public readonly record struct Hp48GrobFile
  : IImageFormatReader<Hp48GrobFile>, IImageToRawImage<Hp48GrobFile> {

  /// <summary>Where the binary form's bitmap starts.</summary>
  public const int BinaryBitmapOffset = 18;

  /// <summary>What the text form begins with, the size following it in decimal.</summary>
  public const string TextSignature = "%%HP: T(0)A(D)F(.);\rGROB ";

  static string IImageFormatMetadata<Hp48GrobFile>.PrimaryExtension => ".grb";
  static string[] IImageFormatMetadata<Hp48GrobFile>.FileExtensions => [".grb", ".gro"];
  static Hp48GrobFile IImageFormatReader<Hp48GrobFile>.FromSpan(ReadOnlySpan<byte> data)
    => Hp48GrobReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<Hp48GrobFile>.VideoModes => [
    new("HP 48", [(IntegerRange.Any, IntegerRange.Any)], [2])
  ];

  /// <summary>The bitmap, one bit a pixel with each row padded out to a whole byte.</summary>
  public byte[] Bitmap { get; init; }

  /// <summary>Pixels across.</summary>
  public int Width { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  /// <summary>Whether the nibbles of each byte are swapped, as the text form's are.</summary>
  public bool SwappedNibbles { get; init; }

  public static RawImage ToRawImage(Hp48GrobFile file) {
    var bitmap = file.Bitmap ?? [];
    var stride = (file.Width + 7) >> 3;
    var pixels = new byte[file.Width * file.Height];
    var flip = file.SwappedNibbles ? 4 : 0;

    for (var y = 0; y < file.Height; ++y)
    for (var x = 0; x < file.Width; ++x) {
      var at = y * stride + (x >> 3);
      pixels[y * file.Width + x] = (byte)(at < bitmap.Length ? (bitmap[at] >> ((x & 7) ^ flip)) & 1 : 0);
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = [255, 255, 255, 0, 0, 0],
      PaletteCount = 2,
    };
  }
}
