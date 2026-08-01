using System;
using FileFormat.Core;

namespace FileFormat.BlazingPaddlesWindow;

/// <summary>In-memory representation of a Blazing Paddles window (.wnd).</summary>
/// <remarks>
/// A clipping from a Graphics 15 screen, saved at a fixed three kilobytes whatever its size — the
/// program allocated one buffer for the purpose and wrote the whole thing out. Only the first two
/// bytes say how much of it means anything: a width and a height, with the bitmap following
/// immediately and the rest of the file left as whatever the buffer happened to hold.
/// <para/>
/// No colours are stored. A window was meant to be pasted back into a picture that has its own, so
/// what it carries are the four registers Blazing Paddles itself worked in.
/// </remarks>
public readonly record struct BlazingPaddlesWindowFile
  : IImageFormatReader<BlazingPaddlesWindowFile>, IImageToRawImage<BlazingPaddlesWindowFile>,
    IImageFromRawImage<BlazingPaddlesWindowFile>, IImageFormatWriter<BlazingPaddlesWindowFile> {

  /// <summary>The fixed file size, most of which is usually unused.</summary>
  public const int FileSize = 3072;

  /// <summary>Offset of the bitmap, after the width and height.</summary>
  public const int BitmapOffset = 2;

  /// <summary>Widest window the format can describe, in logical pixels.</summary>
  public const int MaxStride = 40;

  /// <summary>Tallest window the format can describe.</summary>
  public const int MaxHeight = 192;

  /// <summary>The registers Blazing Paddles worked in: background, PF0, PF1 and PF2.</summary>
  public static ReadOnlySpan<byte> Registers => [0, 70, 136, 14];

  static string IImageFormatMetadata<BlazingPaddlesWindowFile>.PrimaryExtension => ".wnd";
  static string[] IImageFormatMetadata<BlazingPaddlesWindowFile>.FileExtensions => [".wnd"];
  static BlazingPaddlesWindowFile IImageFormatReader<BlazingPaddlesWindowFile>.FromSpan(ReadOnlySpan<byte> data)
    => BlazingPaddlesWindowReader.FromSpan(data);
  static byte[] IImageFormatWriter<BlazingPaddlesWindowFile>.ToBytes(BlazingPaddlesWindowFile file)
    => BlazingPaddlesWindowWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<BlazingPaddlesWindowFile>.VideoModes => [
    new("Window", [(new IntegerRange(2, 320), new IntegerRange(1, MaxHeight))], [4])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  /// <summary>Logical pixels across; each is drawn two screen pixels wide.</summary>
  public int LogicalWidth { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  public static RawImage ToRawImage(BlazingPaddlesWindowFile file) {
    var width = file.LogicalWidth * 2;
    var stride = (file.LogicalWidth + 3) >> 2;

    return new() {
      Width = width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = Atari8BitGraphics.DecodeGr15Frame(
        file.Data ?? [], BitmapOffset, stride, width, file.Height, Registers),
    };
  }

  /// <summary>Builds a window in the four colours Blazing Paddles worked in.</summary>
  /// <remarks>
  /// No colours are stored: a window was meant to be pasted back into a picture that has its own,
  /// so what it carries are the program's four registers and nothing else. That leaves only which
  /// of the four each logical pixel takes.
  /// <para/>
  /// The window keeps its own size rather than being sampled to one, since the format's whole point
  /// is that it is a clipping. A logical pixel is two screen pixels wide, so it is read at the left
  /// of the pair; the buffer is written at its full three kilobytes whatever the size, because that
  /// is what the program always saved.
  /// </remarks>
  public static BlazingPaddlesWindowFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var logicalWidth = Math.Clamp(image.Width / 2, 1, MaxStride * 4);
    var height = Math.Clamp(image.Height, 1, MaxHeight);
    var stride = (logicalWidth + 3) >> 2;

    if (stride * height > FileSize - BitmapOffset)
      throw new ArgumentException(
        $"A {logicalWidth}x{height} window does not fit the format's buffer.", nameof(image));

    var rgb = image.SampleTo(logicalWidth * 2, height);
    var data = new byte[FileSize];
    data[0] = (byte)(logicalWidth - 1);
    data[1] = (byte)height;

    for (var y = 0; y < height; ++y)
    for (var pixel = 0; pixel < logicalWidth; ++pixel) {
      var at = (y * logicalWidth * 2 + pixel * 2) * 3;
      var choice = _Nearest(rgb.PixelData, at);

      data[BitmapOffset + y * stride + (pixel >> 2)] |= (byte)(choice << ((~pixel & 3) << 1));
    }

    return new() { Data = data, LogicalWidth = logicalWidth, Height = height };
  }

  /// <summary>Which of the four registers a pixel is closest to.</summary>
  private static int _Nearest(ReadOnlySpan<byte> rgb, int pixel) {
    var gtia = Atari8BitGraphics.Palette;
    var best = 0;
    var bestCost = long.MaxValue;

    for (var register = 0; register < Registers.Length; ++register) {
      // The low bit of a register is not a colour: the hardware ignores it in this mode.
      var entry = (Registers[register] & 254) * 3;
      long dr = rgb[pixel] - gtia[entry];
      long dg = rgb[pixel + 1] - gtia[entry + 1];
      long db = rgb[pixel + 2] - gtia[entry + 2];
      var cost = dr * dr * 77 + dg * dg * 150 + db * db * 29;

      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = register;
    }

    return best;
  }
}
