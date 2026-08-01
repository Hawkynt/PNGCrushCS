using System;
using FileFormat.Core;

namespace FileFormat.AtariHr2;

/// <summary>In-memory representation of an Atari 8-bit HR2 picture (.hci, .hr2).</summary>
/// <remarks>
/// A Graphics 8 screen and a Graphics 15 screen shown on alternate television fields. Graphics 8
/// gives full 320-pixel horizontal detail but only two colours that must share a hue; Graphics 15
/// gives four freely chosen colours at half that detail. Averaged, the pair reads as a picture with
/// both — the outlines come from one field and the colour from the other.
/// <para/>
/// The two-colour field is the reason Graphics 8 is described as monochrome: its foreground takes
/// the hue of the playfield register and only the luminance of the other, so the two colours are
/// always shades of one.
/// </remarks>
public readonly record struct AtariHr2File
  : IImageFormatReader<AtariHr2File>, IImageToRawImage<AtariHr2File>,
    IImageFromRawImage<AtariHr2File>, IImageFormatWriter<AtariHr2File> {

  static byte[] IImageFormatWriter<AtariHr2File>.ToBytes(AtariHr2File file) => AtariHr2Writer.ToBytes(file);

  /// <summary>Pixels across.</summary>
  public const int Width = 320;

  /// <summary>Rows.</summary>
  public const int Height = 200;

  /// <summary>Bytes one Graphics 8 row occupies: one bit per pixel.</summary>
  public const int HiresStride = Width / 8;

  /// <summary>Bytes one Graphics 15 row occupies: two bits per logical pixel, four to a byte.</summary>
  public const int ColorStride = Width / 8;

  /// <summary>Offset of the Graphics 8 field.</summary>
  public const int HiresOffset = 0;

  /// <summary>Offset of the Graphics 15 field.</summary>
  public const int ColorOffset = HiresStride * Height;

  /// <summary>Offset of the Graphics 8 field's two registers: PF2 then PF1.</summary>
  public const int HiresRegisterOffset = ColorOffset + ColorStride * Height;

  /// <summary>Offset of the Graphics 15 field's four registers: background, PF0, PF1, PF2.</summary>
  public const int ColorRegisterOffset = HiresRegisterOffset + 2;

  /// <summary>Total file size.</summary>
  public const int FileSize = ColorRegisterOffset + 4;

  static string IImageFormatMetadata<AtariHr2File>.PrimaryExtension => ".hr2";
  static string[] IImageFormatMetadata<AtariHr2File>.FileExtensions => [".hr2", ".hci"];
  static AtariHr2File IImageFormatReader<AtariHr2File>.FromSpan(ReadOnlySpan<byte> data)
    => AtariHr2Reader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<AtariHr2File>.VideoModes => [
    new("HR2", [(Width, Height)], [256])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(AtariHr2File file) {
    var data = file.Data ?? [];

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = FrameBlend.Average(_RenderHires(data), _RenderColor(data)),
    };
  }

  private static byte[] _RenderHires(ReadOnlySpan<byte> data) {
    var gtia = Atari8BitGraphics.Palette;
    var playfield = _At(data, HiresRegisterOffset) & 254;
    var luminance = _At(data, HiresRegisterOffset + 1);

    // The foreground keeps the playfield register's hue and takes only the other register's
    // luminance, which is what confines a Graphics 8 screen to two shades of one colour.
    ReadOnlySpan<byte> colors = [(byte)playfield, (byte)((playfield & 240) | (luminance & 14))];
    var rgb = new byte[Width * Height * 3];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      var index = HiresOffset + y * HiresStride + (x >> 3);
      var bit = (_At(data, index) >> (~x & 7)) & 1;

      var entry = colors[bit] * 3;
      var target = (y * Width + x) * 3;
      rgb[target] = gtia[entry];
      rgb[target + 1] = gtia[entry + 1];
      rgb[target + 2] = gtia[entry + 2];
    }

    return rgb;
  }

  private static byte[] _RenderColor(ReadOnlySpan<byte> data) {
    var registers = new byte[4];
    for (var i = 0; i < registers.Length; ++i)
      registers[i] = _At(data, ColorRegisterOffset + i);

    return Atari8BitGraphics.DecodeGr15Frame(data, ColorOffset, ColorStride, Width, Height, registers);
  }

  private static byte _At(ReadOnlySpan<byte> data, int offset)
    => offset >= 0 && offset < data.Length ? data[offset] : (byte)0;

  /// <summary>Builds a picture from the pair of screens the format shows alternately.</summary>
  /// <remarks>
  /// This is the one interlaced format here whose two fields are meant to differ: one is a hires
  /// screen confined to two shades of a single hue, the other four colours at half the horizontal
  /// resolution, and what the eye sees is their average. Putting the same picture in both would
  /// throw away the whole point of it.
  /// <para/>
  /// So the two are chosen together. A pixel's colour is the mean of one bit from the hires screen
  /// and one register from the colour screen — eight combinations, of which the nearest is kept.
  /// The colour screen's pixels are two wide, so each pair agrees on its register while the two
  /// hires bits stay free, and the register is picked by whichever costs the pair least.
  /// </remarks>
  public static AtariHr2File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(Width, Height).PixelData;
    var gtia = Atari8BitGraphics.Palette;

    var colorRegisters = Atari8BitGraphics.ChooseGr15Registers(
      PixelConverter.Convert(image.SampleTo(Width, Height), PixelFormat.Bgra32).PixelData, Width * Height, 4);

    var (playfield, luminance) = _ChooseHiresRegisters(rgb, gtia, colorRegisters);
    var hiresColors = new[] { playfield, (byte)((playfield & 240) | (luminance & 14)) };

    var data = new byte[FileSize];
    data[HiresRegisterOffset] = playfield;
    data[HiresRegisterOffset + 1] = luminance;
    colorRegisters.CopyTo(data.AsSpan(ColorRegisterOffset));

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; x += 2) {
      var (register, leftBit, rightBit) = _ChoosePair(rgb, gtia, hiresColors, colorRegisters, x, y);

      data[ColorOffset + y * ColorStride + (x >> 3)] |= (byte)(register << (~x & 6));

      if (leftBit != 0)
        data[HiresOffset + y * HiresStride + (x >> 3)] |= (byte)(1 << (~x & 7));
      if (rightBit != 0)
        data[HiresOffset + y * HiresStride + ((x + 1) >> 3)] |= (byte)(1 << (~(x + 1) & 7));
    }

    return new() { Data = data };
  }

  /// <summary>Picks the two shades the hires screen draws in, given the colours it will average with.</summary>
  private static (byte Playfield, byte Luminance) _ChooseHiresRegisters(
    ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> gtia, ReadOnlySpan<byte> colorRegisters) {
    // The hue is fixed by the playfield register and the foreground only varies its brightness, so
    // the hue is taken from the picture as a whole and only the second brightness is searched.
    long red = 0, green = 0, blue = 0;
    var pixels = rgb.Length / 3;
    for (var at = 0; at + 2 < rgb.Length; at += 3) {
      red += rgb[at];
      green += rgb[at + 1];
      blue += rgb[at + 2];
    }

    var playfield = Atari8BitGraphics.NearestRegister(
      (int)(red / pixels), (int)(green / pixels), (int)(blue / pixels));

    byte best = 0;
    var bestCost = long.MaxValue;

    for (byte candidate = 0; candidate < 16; candidate += 2) {
      var pair = new[] { playfield, (byte)((playfield & 240) | (candidate & 14)) };
      long cost = 0;

      for (var y = 0; y < Height; ++y)
      for (var x = 0; x < Width; x += 2)
        cost += _PairCost(rgb, gtia, pair, colorRegisters, x, y);

      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = candidate;
    }

    return (playfield, best);
  }

  /// <summary>Chooses one colour register and the two hires bits it is averaged with.</summary>
  private static (int Register, int LeftBit, int RightBit) _ChoosePair(
    ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> gtia, ReadOnlySpan<byte> hiresColors,
    ReadOnlySpan<byte> colorRegisters, int x, int y) {
    var best = (Register: 0, LeftBit: 0, RightBit: 0);
    var bestCost = long.MaxValue;

    for (var register = 0; register < 4; ++register) {
      var (leftCost, leftBit) = _BestBit(rgb, gtia, hiresColors, colorRegisters[register], x, y);
      var (rightCost, rightBit) = _BestBit(rgb, gtia, hiresColors, colorRegisters[register], x + 1, y);
      var cost = leftCost + rightCost;
      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = (register, leftBit, rightBit);
    }

    return best;
  }

  /// <summary>How much the best pairing costs a two-pixel run, without recording the choice.</summary>
  private static long _PairCost(
    ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> gtia, ReadOnlySpan<byte> hiresColors,
    ReadOnlySpan<byte> colorRegisters, int x, int y) {
    var bestCost = long.MaxValue;

    for (var register = 0; register < 4; ++register) {
      var cost = _BestBit(rgb, gtia, hiresColors, colorRegisters[register], x, y).Cost
                 + _BestBit(rgb, gtia, hiresColors, colorRegisters[register], x + 1, y).Cost;
      if (cost < bestCost)
        bestCost = cost;
    }

    return bestCost;
  }

  /// <summary>Which hires bit brings the average nearest one pixel, and how far off it still is.</summary>
  private static (long Cost, int Bit) _BestBit(
    ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> gtia, ReadOnlySpan<byte> hiresColors, byte register, int x, int y) {
    var at = (y * Width + x) * 3;
    var colorEntry = (register & 254) * 3;

    var best = 0;
    var bestCost = long.MaxValue;

    for (var bit = 0; bit < 2; ++bit) {
      var hiresEntry = hiresColors[bit] * 3;
      long cost = 0;

      for (var channel = 0; channel < 3; ++channel) {
        var mean = (gtia[hiresEntry + channel] + gtia[colorEntry + channel] + 1) >> 1;
        long delta = rgb[at + channel] - mean;
        cost += delta * delta;
      }

      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = bit;
    }

    return (bestCost, best);
  }
}
