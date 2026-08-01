using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.PerfectPix;

/// <summary>In-memory representation of a Perfect Pix picture (.pph, with .odd and .eve beside it).</summary>
/// <remarks>
/// Three files rather than one, divided by what the television does rather than by what the picture
/// contains: the .odd and .eve hold the two fields the display alternates between, and the .pph
/// holds the size, the mode and the colours they share. Averaged, the pair shows shades neither
/// field can.
/// <para/>
/// The mode byte chooses between sixteen colours at half the horizontal resolution, sixteen at full
/// with the fields offset a pixel from each other, and four colours whose palette is rewritten down
/// the screen at intervals the file states.
/// </remarks>
public readonly record struct PerfectPixFile
  : IImageFormatReader<PerfectPixFile>, IImageToRawImage<PerfectPixFile> {

  /// <summary>The mode byte of the sixteen-colour form whose fields are offset.</summary>
  public const byte OffsetMode = 3;

  /// <summary>The mode byte of the sixteen-colour form at half resolution.</summary>
  public const byte WideMode = 4;

  /// <summary>The mode byte of the four-colour form with palettes down the screen.</summary>
  public const byte StripedMode = 5;

  /// <summary>Colours the striped form's palette holds at a time.</summary>
  public const int StripedColorCount = 4;

  /// <summary>Colours the other forms' palette holds.</summary>
  public const int WideColorCount = 16;

  static string IImageFormatMetadata<PerfectPixFile>.PrimaryExtension => ".pph";
  static string[] IImageFormatMetadata<PerfectPixFile>.FileExtensions => [".pph"];
  static PerfectPixFile IImageFormatReader<PerfectPixFile>.FromSpan(ReadOnlySpan<byte> data)
    => PerfectPixReader.FromSpan(data);

  /// <summary>Reads the file together with the companion it cannot be shown without.</summary>
  static PerfectPixFile IImageFormatReader<PerfectPixFile>.FromFile(FileInfo file)
    => PerfectPixReader.FromFile(file);
  static VideoMode[] IImageFormatMetadata<PerfectPixFile>.VideoModes => [
    new("Perfect Pix", [(new IntegerRange(4, 384), new IntegerRange(1, 272))], [WideColorCount])
  ];

  /// <summary>The head file, holding the size, the mode and the colours.</summary>
  public byte[] Head { get; init; }

  /// <summary>The odd field.</summary>
  public byte[] OddField { get; init; }

  /// <summary>The even field.</summary>
  public byte[] EvenField { get; init; }

  /// <summary>Pixels across.</summary>
  public int Width { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  /// <summary>Which of the three forms the picture takes.</summary>
  public byte Mode { get; init; }

  public static RawImage ToRawImage(PerfectPixFile file) {
    var first = _Render(file, file.OddField ?? [], false);
    var second = _Render(file, file.EvenField ?? [], true);

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = FrameBlend.Average(first, second),
    };
  }

  private static byte[] _Render(PerfectPixFile file, ReadOnlySpan<byte> bitmap, bool second) {
    var head = file.Head ?? [];
    var stride = file.Width >> 2;
    var rgb = new byte[file.Width * file.Height * 3];
    var palette = new byte[WideColorCount * 3];

    if (file.Mode != StripedMode)
      _ReadFirmwarePalette(head, 6, WideColorCount, palette);

    // Only the form at full resolution offsets its fields, which is what interlacing them buys.
    var skip = file.Mode == OffsetMode;

    var paletteAt = 6;
    var remaining = 0;

    for (var y = 0; y < file.Height; ++y) {
      if (file.Mode == StripedMode && remaining == 0) {
        _ReadFirmwarePalette(head, paletteAt, StripedColorCount, palette);
        paletteAt += StripedColorCount;

        // The last palette carries to the bottom rather than stating a count that would repeat it.
        remaining = paletteAt < (1 + head[5]) * 5 ? head[paletteAt++] : file.Height;
      }

      --remaining;

      for (var x = 0; x < file.Width; ++x) {
        int index;

        if (file.Mode == StripedMode)
          index = AmstradGraphics.Mode1Index(_At(bitmap, y * stride + (x >> 2)) >> (~x & 3));
        else {
          var source = x + (skip ? (y ^ (second ? 1 : 0)) & 1 : 0);
          var b = source >= file.Width ? 0 : _At(bitmap, y * stride + (source >> 2));
          index = AmstradGraphics.Mode0Index(b, (source & 2) != 0);
        }

        var entry = index * 3;
        var target = (y * file.Width + x) * 3;
        rgb[target] = palette[entry];
        rgb[target + 1] = palette[entry + 1];
        rgb[target + 2] = palette[entry + 2];
      }
    }

    return rgb;
  }

  private static void _ReadFirmwarePalette(ReadOnlySpan<byte> head, int offset, int count, Span<byte> palette) {
    for (var i = 0; i < count; ++i)
      AmstradGraphics.TryFirmwareColor(offset + i < head.Length ? head[offset + i] : 0, palette.Slice(i * 3, 3));
  }

  private static byte _At(ReadOnlySpan<byte> data, int offset)
    => offset >= 0 && offset < data.Length ? data[offset] : (byte)0;
}
