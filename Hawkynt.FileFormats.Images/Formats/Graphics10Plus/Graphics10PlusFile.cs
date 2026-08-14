using System;
using FileFormat.Core;

namespace FileFormat.Graphics10Plus;

/// <summary>In-memory representation of an Atari 8-bit Graphics 10+ (.gr10p) screen.</summary>
/// <remarks>
/// One fixed length and nothing else to identify it: 2400 bytes of ANTIC mode F bitmap read as GTIA
/// mode 10, then the nine colour registers, 2409 in all. Sixty stored rows of forty bytes, each byte
/// holding two four-bit pixels, high nibble first.
/// <para/>
/// A stored pixel is four screen pixels wide and a stored row four scanlines tall — the "plus" of
/// the name — so eighty by sixty stored is three hundred and twenty by two hundred and forty shown.
/// That is the whole difference from the plain Graphics 10 screen beside it, which stores the same
/// rows and shows each of them once.
/// <para/>
/// A four-bit pixel has sixteen values and the chip has nine registers, so seven of the sixteen are
/// aliases: the background repeats across four of them and each playfield register appears a second
/// time near the top. Reading the missing seven as black — the obvious view of a four-bit index
/// against a nine-entry table — punches a hole in the picture wherever one lands.
/// </remarks>
public readonly record struct Graphics10PlusFile
  : IImageFormatReader<Graphics10PlusFile>, IImageToRawImage<Graphics10PlusFile>,
    IImageFromRawImage<Graphics10PlusFile>, IImageFormatWriter<Graphics10PlusFile> {

  /// <summary>Logical pixels across a stored row.</summary>
  public const int StoredWidth = 80;

  /// <summary>Stored rows.</summary>
  public const int ScreenRows = 60;

  /// <summary>Bytes a stored row takes, at two four-bit pixels each.</summary>
  public const int BytesPerRow = StoredWidth / 2;

  /// <summary>Size of the bitmap.</summary>
  public const int ScreenDataSize = BytesPerRow * ScreenRows;

  /// <summary>Colour registers the file ends with: PM0 to PM3, PF0 to PF3, then the background.</summary>
  public const int RegisterCount = 9;

  /// <summary>Where those registers start.</summary>
  public const int RegisterOffset = ScreenDataSize;

  /// <summary>The one length a Graphics 10+ file has.</summary>
  public const int FileSize = ScreenDataSize + RegisterCount;

  /// <summary>How many screen pixels one stored pixel covers, each way.</summary>
  public const int Scale = 4;

  /// <summary>Displayed width.</summary>
  public const int DisplayWidth = StoredWidth * Scale;

  /// <summary>Displayed height.</summary>
  public const int DisplayHeight = ScreenRows * Scale;

  /// <summary>Colours a GTIA mode 10 screen can show at once.</summary>
  public const int ColorCount = RegisterCount;

  /// <summary>
  /// How far right the mode's own timing pushes the picture, which mode 10 pays for its ninth
  /// colour with.
  /// </summary>
  public const int LeftSkip = 2;

  static string IImageFormatMetadata<Graphics10PlusFile>.PrimaryExtension => ".gr10p";
  static string[] IImageFormatMetadata<Graphics10PlusFile>.FileExtensions => [".gr10p"];
  static Graphics10PlusFile IImageFormatReader<Graphics10PlusFile>.FromSpan(ReadOnlySpan<byte> data)
    => Graphics10PlusReader.FromSpan(data);
  static byte[] IImageFormatWriter<Graphics10PlusFile>.ToBytes(Graphics10PlusFile file)
    => Graphics10PlusWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<Graphics10PlusFile>.VideoModes => [
    new("Graphics 10", [(DisplayWidth, DisplayHeight)], [ColorCount])
  ];

  /// <summary>The bitmap, forty bytes a row for sixty rows.</summary>
  public byte[] ScreenData { get; init; }

  /// <summary>The nine GTIA registers: PM0 to PM3, then PF0 to PF3, then the background.</summary>
  public byte[] Registers { get; init; }

  public static RawImage ToRawImage(Graphics10PlusFile file) {
    var entries = Atari8BitGraphics.ExpandGr10Registers(file.Registers);

    // Decoded once at the width it is shown at, then each row repeated: the mode's four-pixel-wide
    // pixels are the decoder's business and its four-scanline-tall rows are not, so doing the two
    // in one pass would hide which of them belongs to the mode and which to this format.
    var rows = new byte[DisplayWidth * ScreenRows];
    Atari8BitGraphics.DecodeGr10Into(
      file.ScreenData, 0, rows, 0, DisplayWidth, DisplayWidth, ScreenRows, entries, LeftSkip);

    var frame = new byte[DisplayWidth * DisplayHeight];
    for (var y = 0; y < DisplayHeight; ++y)
      rows.AsSpan(y / Scale * DisplayWidth, DisplayWidth).CopyTo(frame.AsSpan(y * DisplayWidth));

    return new() {
      Width = DisplayWidth,
      Height = DisplayHeight,
      Format = PixelFormat.Rgb24,
      PixelData = Atari8BitGraphics.ApplyPalette(frame),
    };
  }

  /// <summary>Builds a Graphics 10+ screen from a picture, choosing the nine registers for it.</summary>
  /// <remarks>
  /// The registers are stored, so they are chosen for the picture rather than fixed: nine colours
  /// quantised out of it and each snapped to the nearest the machine can hold. A pixel then names
  /// whichever of the nine it is closest to.
  /// </remarks>
  public static Graphics10PlusFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(StoredWidth, ScreenRows).EnsureFormat(PixelFormat.Rgb24);
    var bgra = PixelConverter.Convert(rgb, PixelFormat.Bgra32);
    var registers = Atari8BitGraphics.ChooseGr15Registers(bgra.PixelData, StoredWidth * ScreenRows, RegisterCount);

    return new() {
      ScreenData = Graphics10PlusWriter.Pack(rgb.PixelData, registers),
      Registers = registers,
    };
  }
}
