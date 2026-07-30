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
  : IImageFormatReader<BlazingPaddlesWindowFile>, IImageToRawImage<BlazingPaddlesWindowFile> {

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
}
