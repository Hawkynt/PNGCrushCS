using System;
using FileFormat.Core;

namespace FileFormat.TobiasRichterSlideshow;

/// <summary>In-memory representation of a Tobias Richter Fullscreen Slideshow picture (.pci).</summary>
/// <remarks>
/// An overscanned ST picture: 352 by 278, wider and taller than the machine's nominal screen,
/// stored as two fields that alternate and with a fresh sixteen-colour palette for every one of the
/// 556 scanlines. The planes are not interleaved by word as an ST picture normally is but stored
/// one after another, each 12232 bytes, which is what a display list that reloads the palette every
/// line needs.
/// </remarks>
public readonly record struct TobiasRichterSlideshowFile
  : IImageFormatReader<TobiasRichterSlideshowFile>, IImageToRawImage<TobiasRichterSlideshowFile>,
    IImageFromRawImage<TobiasRichterSlideshowFile>, IImageFormatWriter<TobiasRichterSlideshowFile> {

  /// <summary>Pixels across.</summary>
  public const int Width = 352;

  /// <summary>Rows in one field.</summary>
  public const int Height = 278;

  /// <summary>Bitplanes a pixel is built from.</summary>
  public const int Bitplanes = 4;

  /// <summary>Bytes one row of one plane occupies.</summary>
  public const int BytesPerPlaneRow = Width / 8;

  /// <summary>Bytes one whole plane occupies.</summary>
  public const int BytesPerPlane = BytesPerPlaneRow * Height;

  /// <summary>Where the second field's planes start.</summary>
  public const int SecondFieldOffset = BytesPerPlane * Bitplanes;

  /// <summary>Where the per-scanline palettes start.</summary>
  public const int PaletteOffset = SecondFieldOffset * 2;

  /// <summary>Colours a scanline's palette holds.</summary>
  public const int ColorCount = 16;

  /// <summary>Scanlines with a palette of their own: both fields, one after the other.</summary>
  public const int PaletteLineCount = Height * 2;

  /// <summary>Total file size.</summary>
  public const int FileSize = PaletteOffset + PaletteLineCount * ColorCount * AtariStGraphics.PaletteEntrySize;

  static string IImageFormatMetadata<TobiasRichterSlideshowFile>.PrimaryExtension => ".pci";
  static string[] IImageFormatMetadata<TobiasRichterSlideshowFile>.FileExtensions => [".pci"];
  static TobiasRichterSlideshowFile IImageFormatReader<TobiasRichterSlideshowFile>.FromSpan(ReadOnlySpan<byte> data)
    => TobiasRichterSlideshowReader.FromSpan(data);
  static byte[] IImageFormatWriter<TobiasRichterSlideshowFile>.ToBytes(TobiasRichterSlideshowFile file)
    => TobiasRichterSlideshowWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<TobiasRichterSlideshowFile>.VideoModes => [
    new("Atari ST overscan", [(Width, Height)], [ColorCount])
  ];

  /// <summary>The whole file, every area of which is at an absolute offset.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(TobiasRichterSlideshowFile file) {
    var data = file.Data ?? [];

    // Which form the palettes are in is settled once from all of them, not line by line.
    var ste = AtariStGraphics.IsStePalette(data, PaletteOffset, PaletteLineCount * ColorCount);
    var fields = new byte[2][];

    for (var field = 0; field < 2; ++field) {
      var rgb = new byte[Width * Height * 3];
      var planeOffset = SecondFieldOffset * field;

      for (var y = 0; y < Height; ++y) {
        var line = field * Height + y;
        var palette = AtariStGraphics.ReadPalette(
          data, PaletteOffset + line * ColorCount * AtariStGraphics.PaletteEntrySize, ColorCount, ste);

        for (var x = 0; x < Width; ++x) {
          var entry = _PlanePixel(data, planeOffset + (x >> 3), x) * 3;
          var target = (y * Width + x) * 3;
          rgb[target] = palette[entry];
          rgb[target + 1] = palette[entry + 1];
          rgb[target + 2] = palette[entry + 2];
        }

        planeOffset += BytesPerPlaneRow;
      }

      fields[field] = rgb;
    }

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = FrameBlend.Average(fields[0], fields[1]),
    };
  }

  /// <summary>Builds a picture from any image, sampling it to the overscanned 352x278 screen.</summary>
  /// <remarks>
  /// Every scanline gets its own sixteen colours, chosen from that line alone — which is the whole
  /// of what the format buys over an ordinary ST picture and the reason a 352-pixel screen is worth
  /// 115648 bytes.
  /// <para/>
  /// Both fields are given the same picture and the same palettes. They alternate and the decoder
  /// averages them, so the only colours differing fields would add are the midpoints of two the
  /// palettes already hold — and a line already free to name any sixteen of the machine's 512 has
  /// no use for a midpoint it could simply have named.
  /// </remarks>
  public static TobiasRichterSlideshowFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var source = image.SampleTo(Width, Height).EnsureFormat(PixelFormat.Rgb24).PixelData;
    var data = new byte[FileSize];
    var line = new byte[Width * 3];
    var stored = new byte[ColorCount * AtariStGraphics.PaletteEntrySize];

    for (var y = 0; y < Height; ++y) {
      source.AsSpan(y * Width * 3, Width * 3).CopyTo(line);
      var reduced = new RawImage {
        Width = Width, Height = 1, Format = PixelFormat.Rgb24, PixelData = line,
      }.EnsureIndexedAtMost(ColorCount);

      // The palette is stored first and the colours it will actually come back as worked out from
      // what was stored, so the indices address those rather than the ones the reduction asked for
      // — three bits a channel is coarser than what it hands over.
      var snapped = _StorePalette(reduced.Palette ?? [], stored);
      var indices = new RawImage {
        Width = Width, Height = 1, Format = PixelFormat.Rgb24, PixelData = line,
      }.EnsureIndexed(PixelFormat.Indexed8, snapped).PixelData;

      for (var field = 0; field < 2; ++field) {
        stored.CopyTo(
          data.AsSpan(PaletteOffset + (field * Height + y) * ColorCount * AtariStGraphics.PaletteEntrySize));

        var row = SecondFieldOffset * field + y * BytesPerPlaneRow;
        for (var x = 0; x < Width; ++x) {
          var index = indices[x];
          for (var plane = 0; plane < Bitplanes; ++plane)
            if ((index & (1 << plane)) != 0)
              data[row + plane * BytesPerPlane + (x >> 3)] |= (byte)(1 << (~x & 7));
        }
      }
    }

    return new() { Data = data };
  }

  /// <summary>
  /// Stores one scanline's palette in the plain ST form and returns the colours it will be read back
  /// as.
  /// </summary>
  /// <remarks>
  /// Each channel keeps the three-bit value whose expansion is nearest, rather than the one scaling
  /// down and truncating would give. The expansion repeats the value's bits rather than scaling, so
  /// the two disagree on five of the eight intensities — three of which have no truncated value that
  /// maps back to them at all, and would be lost by an encoder that scaled.
  /// </remarks>
  private static byte[] _StorePalette(ReadOnlySpan<byte> palette, Span<byte> stored) {
    var snapped = new byte[ColorCount * 3];

    for (var i = 0; i < ColorCount; ++i) {
      var entry = i * 3;
      var red = _NearestChannel(entry < palette.Length ? palette[entry] : (byte)0);
      var green = _NearestChannel(entry + 1 < palette.Length ? palette[entry + 1] : (byte)0);
      var blue = _NearestChannel(entry + 2 < palette.Length ? palette[entry + 2] : (byte)0);

      stored[i * AtariStGraphics.PaletteEntrySize] = (byte)red;
      stored[i * AtariStGraphics.PaletteEntrySize + 1] = (byte)((green << 4) | blue);

      snapped[entry] = ChannelScaling.Expand3(red);
      snapped[entry + 1] = ChannelScaling.Expand3(green);
      snapped[entry + 2] = ChannelScaling.Expand3(blue);
    }

    return snapped;
  }

  /// <summary>The three-bit value whose expansion comes closest to a wanted intensity.</summary>
  private static int _NearestChannel(byte value) {
    var best = 0;
    var bestDistance = int.MaxValue;

    for (var candidate = 0; candidate < 8; ++candidate) {
      var distance = Math.Abs(ChannelScaling.Expand3(candidate) - value);
      if (distance >= bestDistance)
        continue;

      bestDistance = distance;
      best = candidate;
    }

    return best;
  }

  /// <summary>Reads one pixel from planes that are whole-picture blocks rather than interleaved.</summary>
  private static int _PlanePixel(ReadOnlySpan<byte> data, int offset, int x) {
    var bit = ~x & 7;
    var index = 0;
    for (var plane = Bitplanes; --plane >= 0;) {
      var at = offset + plane * BytesPerPlane;
      index = (index << 1) | (at < data.Length ? (data[at] >> bit) & 1 : 0);
    }

    return index;
  }
}
