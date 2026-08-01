using System;
using System.IO;
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
  : IImageFormatReader<PerfectPixFile>, IImageToRawImage<PerfectPixFile>,
    IImageFromRawImage<PerfectPixFile>, IImageFormatWriter<PerfectPixFile> {

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

  /// <summary>Bytes the head of a sixteen-colour picture takes.</summary>
  public const int HeadSize = 6 + WideColorCount;

  static string IImageFormatMetadata<PerfectPixFile>.PrimaryExtension => ".pph";
  static string[] IImageFormatMetadata<PerfectPixFile>.FileExtensions => [".pph"];
  static PerfectPixFile IImageFormatReader<PerfectPixFile>.FromSpan(ReadOnlySpan<byte> data)
    => PerfectPixReader.FromSpan(data);

  /// <summary>Reads the file together with the companion it cannot be shown without.</summary>
  static PerfectPixFile IImageFormatReader<PerfectPixFile>.FromFile(FileInfo file)
    => PerfectPixReader.FromFile(file);
  static byte[] IImageFormatWriter<PerfectPixFile>.ToBytes(PerfectPixFile file) => PerfectPixWriter.ToBytes(file);

  /// <summary>Writes the two fields, without which the head describes nothing.</summary>
  static void IImageFormatWriter<PerfectPixFile>.WriteCompanions(PerfectPixFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(Path.ChangeExtension(target.FullName, ".odd"), file.OddField ?? []);
    File.WriteAllBytes(Path.ChangeExtension(target.FullName, ".eve"), file.EvenField ?? []);
  }

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

  /// <summary>Fits a picture into two fields of sixteen firmware colours each.</summary>
  /// <remarks>
  /// The mode that keeps both fields in register, so a pixel's two halves land on each other and
  /// their average is what shows. Sixteen colours chosen from the firmware's 27, and a pixel that
  /// falls between two of them takes one in each field — which is what the second field is for.
  /// </remarks>
  public static PerfectPixFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    // Four pixels a byte, so the width is a multiple of four; the machine shows 272 rows at most.
    var width = Math.Clamp(image.Width & ~3, 4, 384);
    var height = Math.Clamp(image.Height, 1, 272);

    var rgb = image.SampleTo(width, height).EnsureFormat(PixelFormat.Rgb24);
    var bgra = PixelConverter.Convert(rgb, PixelFormat.Bgra32);
    var quantized = ColorQuantizer.Quantize(bgra.PixelData, width * height, WideColorCount);

    var head = new byte[HeadSize];
    head[0] = WideMode;
    head[1] = (byte)width;
    head[2] = (byte)(width >> 8);
    head[3] = (byte)height;
    head[4] = (byte)(height >> 8);
    head[5] = 1;

    var palette = new byte[WideColorCount * 3];
    for (var i = 0; i < WideColorCount; ++i) {
      var entry = i * 3;
      var firmware = entry + 2 < quantized.Palette.Length
        ? AmstradGraphics.NearestFirmwareColor(
          quantized.Palette[entry], quantized.Palette[entry + 1], quantized.Palette[entry + 2])
        : (byte)0;

      head[6 + i] = firmware;
      AmstradGraphics.TryFirmwareColor(firmware, palette.AsSpan(entry, 3));
    }

    var stride = width >> 2;
    var odd = new byte[stride * height];
    var even = new byte[stride * height];

    // A byte holds two pixels and each is drawn two screen positions wide, so a byte covers four
    // columns. The bytes are walked rather than the columns, and each field gets its own half of
    // the pair chosen for the pixel.
    for (var y = 0; y < height; ++y)
    for (var at = 0; at < stride; ++at) {
      Span<int> first = stackalloc int[2];
      Span<int> second = stackalloc int[2];

      for (var half = 0; half < 2; ++half) {
        var x = Math.Min(at * 4 + half * 2, width - 1);
        var source = (y * width + x) * 3;
        _ChoosePair(
          palette, rgb.PixelData[source], rgb.PixelData[source + 1], rgb.PixelData[source + 2],
          out first[half], out second[half]);
      }

      odd[y * stride + at] = AmstradGraphics.Mode0Byte(first[0], first[1]);
      even[y * stride + at] = AmstradGraphics.Mode0Byte(second[0], second[1]);
    }

    return new() {
      Head = head,
      OddField = odd,
      EvenField = even,
      Width = width,
      Height = height,
      Mode = WideMode,
    };
  }

  /// <summary>The two palette entries whose average is nearest a colour.</summary>
  private static void _ChoosePair(
    ReadOnlySpan<byte> palette, int red, int green, int blue, out int first, out int second) {
    first = 0;
    second = 0;
    var bestCost = int.MaxValue;

    for (var a = 0; a < WideColorCount; ++a)
    for (var b = a; b < WideColorCount; ++b) {
      var dr = red - ((palette[a * 3] + palette[b * 3]) >> 1);
      var dg = green - ((palette[a * 3 + 1] + palette[b * 3 + 1]) >> 1);
      var db = blue - ((palette[a * 3 + 2] + palette[b * 3 + 2]) >> 1);
      var cost = dr * dr + dg * dg + db * db;
      if (cost >= bestCost)
        continue;

      bestCost = cost;
      first = a;
      second = b;
    }
  }
}
