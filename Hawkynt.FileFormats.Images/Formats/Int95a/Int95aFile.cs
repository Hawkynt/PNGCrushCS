using System;
using FileFormat.Core;

namespace FileFormat.Int95a;

/// <summary>In-memory representation of an INT95a picture (.int).</summary>
/// <remarks>
/// Two Atari four-colour frames shown on alternate television fields, averaged by the eye and here.
/// Each frame is 160 pixels across at two bits a pixel — 40 bytes a row — and the four colour
/// registers they share sit after both of them rather than in front, which is why the file opens
/// with a run of noughts and gives nothing away.
/// <para/>
/// So the length is four registers plus two frames: <c>4 + 2 * 40 * height</c>, which is 16004 for
/// the 200-row sample. The height follows from the length rather than being fixed, since the format
/// runs to 239 rows.
/// <para/>
/// A stored pixel is drawn twice, so the picture comes out 320 across.
/// <para/>
/// <c>.int</c> was claimed only by the plain Atari screen dump, which takes 7680 bytes or 1920.
/// </remarks>
public readonly record struct Int95aFile
  : IImageFormatReader<Int95aFile>, IImageToRawImage<Int95aFile>,
    IImageFromRawImage<Int95aFile>, IImageFormatWriter<Int95aFile> {

  /// <summary>Stored pixels across; each is drawn twice.</summary>
  public const int StoredWidth = 160;

  /// <summary>Pixels across once drawn.</summary>
  public const int DisplayWidth = StoredWidth * 2;

  /// <summary>Bytes one row of one frame takes, at four pixels a byte.</summary>
  public const int BytesPerRow = StoredWidth / 4;

  /// <summary>Colour registers, which follow the two frames.</summary>
  public const int RegisterCount = 4;

  /// <summary>The tallest the format runs.</summary>
  public const int MaxHeight = 239;

  static string IImageFormatMetadata<Int95aFile>.PrimaryExtension => ".int";
  static string[] IImageFormatMetadata<Int95aFile>.FileExtensions => [".int"];
  static Int95aFile IImageFormatReader<Int95aFile>.FromSpan(ReadOnlySpan<byte> data) => Int95aReader.FromSpan(data);
  static byte[] IImageFormatWriter<Int95aFile>.ToBytes(Int95aFile file) => Int95aWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<Int95aFile>.VideoModes => [
    new("Interlaced", [(DisplayWidth, new IntegerRange(1, MaxHeight))], [Atari8BitGraphics.Gr15RegisterCount])
  ];

  public int Height { get; init; }

  /// <summary>The first field.</summary>
  public byte[] FirstFrame { get; init; }

  /// <summary>The second field, shown alternately with the first.</summary>
  public byte[] SecondFrame { get; init; }

  /// <summary>The four colours both frames draw from.</summary>
  public byte[] Registers { get; init; }

  /// <summary>What a file of a given height weighs.</summary>
  public static int FileSizeFor(int height) => RegisterCount + BytesPerRow * height * 2;

  public static RawImage ToRawImage(Int95aFile file) {
    var gtia = Atari8BitGraphics.CreatePalette();
    var regs = file.Registers ?? new byte[RegisterCount];
    var height = file.Height;
    var rgb = new byte[DisplayWidth * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < StoredWidth; ++x) {
      var at = y * BytesPerRow + (x >> 2);
      var shift = (3 - (x & 3)) * 2;
      var a = regs[(file.FirstFrame[at] >> shift) & 3] * 3;
      var b = regs[(file.SecondFrame[at] >> shift) & 3] * 3;

      // The eye averages what the two fields show, and so does this.
      for (var drawn = 0; drawn < 2; ++drawn) {
        var to = (y * DisplayWidth + x * 2 + drawn) * 3;
        rgb[to] = (byte)((gtia[a] + gtia[b]) >> 1);
        rgb[to + 1] = (byte)((gtia[a + 1] + gtia[b + 1]) >> 1);
        rgb[to + 2] = (byte)((gtia[a + 2] + gtia[b + 2]) >> 1);
      }
    }

    return new() { Width = DisplayWidth, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  public static Int95aFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    var height = Math.Clamp(image.Height, 1, MaxHeight);
    var rgb = image.SampleTo(DisplayWidth, height).EnsureFormat(PixelFormat.Rgb24);

    var registers = Atari8BitGraphics.ChooseGr15Registers(
      PixelConverter.Convert(rgb, PixelFormat.Bgra32).PixelData, DisplayWidth * height, Atari8BitGraphics.Gr15RegisterCount);

    var first = new byte[BytesPerRow * height];
    var second = new byte[BytesPerRow * height];
    var gtia = Atari8BitGraphics.CreatePalette();

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < StoredWidth; ++x) {
      var from = (y * DisplayWidth + x * 2) * 3;
      var best = 0;
      var bestCost = long.MaxValue;
      for (var i = 0; i < RegisterCount; ++i) {
        var e = registers[i] * 3;
        long dr = rgb.PixelData[from] - gtia[e], dg = rgb.PixelData[from + 1] - gtia[e + 1], db = rgb.PixelData[from + 2] - gtia[e + 2];
        var cost = dr * dr * 77 + dg * dg * 150 + db * db * 29;
        if (cost >= bestCost)
          continue;

        bestCost = cost;
        best = i;
      }

      var at = y * BytesPerRow + (x >> 2);
      var shift = (3 - (x & 3)) * 2;
      first[at] |= (byte)(best << shift);
      second[at] |= (byte)(best << shift);
    }

    return new() { Height = height, FirstFrame = first, SecondFrame = second, Registers = registers };
  }
}
