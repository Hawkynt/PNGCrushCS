using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.AtariAgp;

/// <summary>In-memory representation of an Atari 8-bit AGP (Any Graphics Picture) image.</summary>
/// <remarks>
/// A mode byte, the nine GTIA colour registers, and a full 40-by-192 bitmap: 7690 bytes exactly,
/// always. This used to be read as a bare bitmap whose mode was guessed from one of three invented
/// lengths, with the registers nowhere at all — so every picture came out in the wrong colours if
/// it could be opened, and no file another program wrote was the right length to open.
/// </remarks>
public readonly record struct AtariAgpFile : IImageFormatReader<AtariAgpFile>, IImageToRawImage<AtariAgpFile>, IImageFromRawImage<AtariAgpFile>, IImageFormatWriter<AtariAgpFile> {

  /// <summary>Screen pixels across. Modes below 320 draw their pixels correspondingly wider.</summary>
  internal const int PixelWidth = 320;

  /// <summary>Scanlines.</summary>
  internal const int PixelHeight = 192;

  /// <summary>Bytes a bitmap row takes, at eight screen pixels each.</summary>
  internal const int BytesPerRow = PixelWidth / 8;

  /// <summary>Where the bitmap starts: past the mode byte and the nine registers.</summary>
  internal const int BitmapOffset = 10;

  /// <summary>How many colour registers precede the bitmap.</summary>
  internal const int RegisterCount = 9;

  /// <summary>The one length an AGP file has.</summary>
  internal const int FileSize = BitmapOffset + BytesPerRow * PixelHeight;

  static string IImageFormatMetadata<AtariAgpFile>.PrimaryExtension => ".agp";
  static string[] IImageFormatMetadata<AtariAgpFile>.FileExtensions => [".agp"];
  static AtariAgpFile IImageFormatReader<AtariAgpFile>.FromSpan(ReadOnlySpan<byte> data) => AtariAgpReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<AtariAgpFile>.VideoModes => [
    new("Default", [(PixelWidth, PixelHeight)], [new IntegerRange(2, 16)])
  ];
  static byte[] IImageFormatWriter<AtariAgpFile>.ToBytes(AtariAgpFile file) => AtariAgpWriter.ToBytes(file);

  /// <summary>Width in pixels, always the full screen.</summary>
  public int Width => PixelWidth;

  /// <summary>Height in pixels, always the full screen.</summary>
  public int Height => PixelHeight;

  /// <summary>The ANTIC mode the first byte names.</summary>
  public AtariAgpMode Mode { get; init; }

  /// <summary>
  /// The nine GTIA registers: PM0 to PM3, then PF0 to PF3, then the background.
  /// </summary>
  public byte[] Registers { get; init; }

  /// <summary>The bitmap, 40 bytes a row for 192 rows.</summary>
  public byte[] Bitmap { get; init; }

  /// <summary>The three playfield registers and the background, in the order Graphics 15 wants.</summary>
  private byte[] _Gr15Registers => [
    Registers[8], Registers[4], Registers[5], Registers[6],
  ];

  public static RawImage ToRawImage(AtariAgpFile file) {
    var frame = new byte[PixelWidth * PixelHeight];

    switch (file.Mode) {
      case AtariAgpMode.Graphics8:
        // Two colours drawn from one register pair: the background keeps its hue throughout and
        // only the luminance of the set bits comes from the other one.
        var background = file.Registers[6];
        var foreground = (byte)((background & 240) | (file.Registers[5] & 14));
        for (var y = 0; y < PixelHeight; ++y)
        for (var x = 0; x < PixelWidth; ++x) {
          var b = file.Bitmap[y * BytesPerRow + (x >> 3)];
          frame[y * PixelWidth + x] = ((b >> (~x & 7)) & 1) == 0 ? background : foreground;
        }

        break;

      case AtariAgpMode.Graphics9:
        Atari8BitGraphics.DecodeGr9Into(
          file.Bitmap, 0, BytesPerRow, frame, 0, PixelWidth, PixelWidth, PixelHeight, file.Registers[8]);
        break;

      case AtariAgpMode.Graphics10:
        Atari8BitGraphics.DecodeGr10Into(
          file.Bitmap, 0, frame, 0, PixelWidth, PixelWidth, PixelHeight,
          Atari8BitGraphics.ExpandGr10Registers(file.Registers), leftSkip: 2);
        break;

      case AtariAgpMode.Graphics11: {
        // A hue field with no luminance field under it: the background register supplies the one
        // luminance the whole picture is drawn at, and hue zero means the background outright.
        var bak = file.Registers[8];
        for (var y = 0; y < PixelHeight; ++y)
        for (var x = 0; x < PixelWidth; ++x) {
          var hue = (file.Bitmap[y * BytesPerRow + (x >> 3)] << (x & 4)) & 240;
          frame[y * PixelWidth + x] = (byte)(hue == 0 ? bak & 240 : bak | hue);
        }

        break;
      }

      case AtariAgpMode.Graphics15:
        Atari8BitGraphics.DecodeGr15Into(
          file.Bitmap, 0, BytesPerRow, frame, 0, PixelWidth, PixelWidth, PixelHeight, file._Gr15Registers);
        break;

      default:
        throw new InvalidDataException($"AGP does not have a mode {(int)file.Mode}.");
    }

    return new() {
      Width = PixelWidth,
      Height = PixelHeight,
      Format = PixelFormat.Rgb24,
      PixelData = Atari8BitGraphics.ApplyPalette(frame),
    };
  }

  /// <summary>Writes a Graphics 15 screen: four registers, which is the most colours AGP holds.</summary>
  /// <remarks>
  /// Of the five modes, only this one and Graphics 10 hold arbitrary colours, and Graphics 10 pays
  /// four bits a pixel for its nine of them where this pays two for four. At 160 logical pixels
  /// across, four freely chosen colours is the better trade for a picture that came from elsewhere.
  /// </remarks>
  public static AtariAgpFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(PixelWidth, PixelHeight).EnsureFormat(PixelFormat.Rgb24);
    var bgra = PixelConverter.Convert(rgb, PixelFormat.Bgra32);
    var chosen = Atari8BitGraphics.ChooseGr15Registers(
      bgra.PixelData, PixelWidth * PixelHeight, Atari8BitGraphics.Gr15RegisterCount);

    var registers = new byte[RegisterCount];
    registers[8] = chosen[0];
    registers[4] = chosen[1];
    registers[5] = chosen[2];
    registers[6] = chosen[3];

    return new() {
      Mode = AtariAgpMode.Graphics15,
      Registers = registers,
      Bitmap = Atari8BitGraphics.PackGr15Frame(rgb.PixelData, BytesPerRow, PixelWidth, PixelHeight, chosen),
    };
  }
}
