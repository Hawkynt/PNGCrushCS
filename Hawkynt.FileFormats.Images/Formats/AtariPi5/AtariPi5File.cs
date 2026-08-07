using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.AtariPi5;

/// <summary>In-memory representation of a 320 by 240 Atari picture in sixteen colours (.pi5).</summary>
/// <remarks>
/// Named after its extension, because that is what is known about it. The reference library's
/// catalogue does not list the name, so there is nothing here that says whose format it is; what
/// there is, is a sample and a decoder to check against, and the layout below reproduces that
/// decoder's rendering on every pixel.
/// <para/>
/// A word of mode, sixteen words of palette, then 320 by 240 in the Atari's four interleaved
/// bitplanes — 2 plus 32 plus 38400, which is the sample to the byte. The palette words are the
/// STE's: three bits a channel with a fourth carried in the bit below them, so a channel is
/// <c>((v &amp; 7) &lt;&lt; 1) | ((v &gt;&gt; 3) &amp; 1)</c> and not the plain nibble. Reading them as plain
/// nibbles gets every colour close and none of them right.
/// <para/>
/// <c>.pi5</c> was claimed only by the TT reader, which takes 153634 bytes — this is a quarter of
/// that and was refused for it.
/// </remarks>
public readonly record struct AtariPi5File
  : IImageFormatReader<AtariPi5File>, IImageToRawImage<AtariPi5File>,
    IImageFromRawImage<AtariPi5File>, IImageFormatWriter<AtariPi5File> {

  public const int Width = 320;
  public const int Height = 240;
  public const int ColorCount = 16;
  public const int Planes = 4;

  /// <summary>The mode word, then the palette, then the bitmap.</summary>
  public const int PaletteOffset = 2;
  public const int BitmapOffset = PaletteOffset + ColorCount * 2;
  public const int BitmapSize = Width * Height * Planes / 8;
  public const int FileSize = BitmapOffset + BitmapSize;

  static string IImageFormatMetadata<AtariPi5File>.PrimaryExtension => ".pi5";
  static string[] IImageFormatMetadata<AtariPi5File>.FileExtensions => [".pi5"];
  static AtariPi5File IImageFormatReader<AtariPi5File>.FromSpan(ReadOnlySpan<byte> data) => AtariPi5Reader.FromSpan(data);
  static byte[] IImageFormatWriter<AtariPi5File>.ToBytes(AtariPi5File file) => AtariPi5Writer.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<AtariPi5File>.VideoModes => [
    new("Default", [(Width, Height)], [ColorCount])
  ];

  /// <summary>The mode word the file opens with.</summary>
  public ushort Mode { get; init; }

  /// <summary>Sixteen packed STE colours.</summary>
  public ushort[] Palette { get; init; }

  /// <summary>The bitmap, four interleaved planes a word at a time.</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>One channel of an STE colour: three bits, with a fourth carried below them.</summary>
  internal static byte Channel(int value) => (byte)(((value & 7) << 1 | (value >> 3) & 1) * 17);

  public static RawImage ToRawImage(AtariPi5File file) {
    var data = file.BitmapData ?? [];
    var stored = file.Palette ?? [];
    var palette = new byte[ColorCount * 3];
    for (var i = 0; i < ColorCount; ++i) {
      var word = i < stored.Length ? stored[i] : (ushort)0;
      palette[i * 3] = Channel(word >> 8);
      palette[i * 3 + 1] = Channel(word >> 4);
      palette[i * 3 + 2] = Channel(word);
    }

    var pixels = new byte[Width * Height];
    for (var i = 0; i < Width * Height; ++i) {
      var group = i / 16;
      var bit = 15 - i % 16;
      var index = 0;
      for (var plane = 0; plane < Planes; ++plane) {
        var at = group * Planes * 2 + plane * 2;
        if (at + 1 >= data.Length)
          continue;

        index |= ((data[at] << 8 | data[at + 1]) >> bit & 1) << plane;
      }

      pixels[i] = (byte)index;
    }

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = palette,
      PaletteCount = ColorCount,
    };
  }

  public static AtariPi5File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    var indexed = image.SampleTo(Width, Height).EnsureIndexedAtMost(ColorCount);
    var source = indexed.Palette ?? [];

    var palette = new ushort[ColorCount];
    for (var i = 0; i < ColorCount && i * 3 + 2 < source.Length; ++i)
      palette[i] = (ushort)(_Pack(source[i * 3]) << 8 | _Pack(source[i * 3 + 1]) << 4 | _Pack(source[i * 3 + 2]));

    var data = new byte[BitmapSize];
    for (var i = 0; i < Width * Height; ++i) {
      var index = indexed.PixelData[i] & 0x0F;
      var group = i / 16;
      var bit = 15 - i % 16;
      for (var plane = 0; plane < Planes; ++plane) {
        if ((index >> plane & 1) == 0)
          continue;

        var at = group * Planes * 2 + plane * 2;
        if (bit >= 8)
          data[at] |= (byte)(1 << (bit - 8));
        else
          data[at + 1] |= (byte)(1 << bit);
      }
    }

    return new() { Mode = 4, Palette = palette, BitmapData = data };
  }

  /// <summary>A channel of 0..255 as the STE stores it: the top three bits, then the fourth below.</summary>
  private static int _Pack(byte value) {
    var level = (value * 15 + 127) / 255;
    return (level >> 1) | ((level & 1) << 3);
  }
}
