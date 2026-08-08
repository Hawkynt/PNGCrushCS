using System;
using FileFormat.Core;

namespace FileFormat.AtariPlayerEditor;

/// <summary>In-memory representation of an Atari Player Editor sheet (.apl).</summary>
/// <remarks>
/// An animation's worth of sprites, laid out side by side. Each frame is two players overlapping
/// rather than one: the GTIA ORs the colours of players that share a pixel, so a pair drawn on top
/// of each other shows three colours where either alone shows one. The gap between the two is
/// stored, because sliding one against the other is what the editor was for.
/// <para/>
/// The file is a fixed 1677 bytes whether it holds one frame or sixteen — the editor wrote its
/// whole workspace out.
/// </remarks>
public readonly record struct AtariPlayerEditorFile
  : IImageFormatReader<AtariPlayerEditorFile>, IImageToRawImage<AtariPlayerEditorFile>,
    IImageFromRawImage<AtariPlayerEditorFile>, IImageFormatWriter<AtariPlayerEditorFile> {

  /// <summary>The four bytes every file starts with.</summary>
  public static ReadOnlySpan<byte> Signature => [154, 248, 57, 33];

  /// <summary>Total file size.</summary>
  public const int FileSize = 1677;

  /// <summary>Most frames the editor holds.</summary>
  public const int MaxFrames = 16;

  /// <summary>Tallest player the editor holds.</summary>
  public const int MaxHeight = 48;

  /// <summary>Widest gap the editor allows between the two players of a frame.</summary>
  public const int MaxGap = 8;

  /// <summary>Bytes one player's shape occupies, whatever its height.</summary>
  public const int ShapeStride = 48;

  /// <summary>Offset of the first player's colours.</summary>
  public const int FirstColorOffset = 7;

  /// <summary>Offset of the second player's colours.</summary>
  public const int SecondColorOffset = 24;

  /// <summary>Offset of the first player's shapes.</summary>
  public const int FirstShapeOffset = 42;

  /// <summary>Offset of the second player's shapes.</summary>
  public const int SecondShapeOffset = 858;

  static string IImageFormatMetadata<AtariPlayerEditorFile>.PrimaryExtension => ".apl";
  static string[] IImageFormatMetadata<AtariPlayerEditorFile>.FileExtensions => [".apl"];
  static AtariPlayerEditorFile IImageFormatReader<AtariPlayerEditorFile>.FromSpan(ReadOnlySpan<byte> data)
    => AtariPlayerEditorReader.FromSpan(data);
  static byte[] IImageFormatWriter<AtariPlayerEditorFile>.ToBytes(AtariPlayerEditorFile file)
    => AtariPlayerEditorWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<AtariPlayerEditorFile>.VideoModes => [
    new("Player sheet", [(IntegerRange.Any, new IntegerRange(1, MaxHeight))], [256])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  /// <summary>Frames the sheet holds.</summary>
  public int Frames { get; init; }

  /// <summary>Scanlines a player spans.</summary>
  public int Height { get; init; }

  /// <summary>Pixels the second player of a frame sits right of the first.</summary>
  public int Gap { get; init; }

  /// <summary>Screen pixels one frame occupies.</summary>
  public int FrameWidth => (8 + Gap + 2) * 2;

  public static RawImage ToRawImage(AtariPlayerEditorFile file) {
    var data = file.Data ?? [];
    var width = file.Frames * file.FrameWidth;
    var frame = new byte[width * file.Height];

    for (var f = 0; f < file.Frames; ++f) {
      var left = f * file.FrameWidth;
      Atari8BitGraphics.DrawPlayerInto(
        data, FirstShapeOffset + f * ShapeStride, data[FirstColorOffset + f], frame, left, width, file.Height, true);
      Atari8BitGraphics.DrawPlayerInto(
        data, SecondShapeOffset + f * ShapeStride, data[SecondColorOffset + f], frame, left + file.Gap * 2, width,
        file.Height, true);
    }

    return new() {
      Width = width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = Atari8BitGraphics.ApplyPalette(frame),
    };
  }

  /// <summary>Pixels one player covers, each drawn two screen pixels wide.</summary>
  public const int PlayerPixels = 8;

  /// <summary>Screen pixels a frame occupies when the two players sit on top of each other.</summary>
  public const int NarrowFrameWidth = (PlayerPixels + 2) * 2;

  /// <summary>
  /// Encodes a picture as a sheet of frames, the two players of each drawn on top of each other.
  /// </summary>
  /// <remarks>
  /// The gap is left at zero so that the two players overlap. That is what buys the third colour:
  /// the chip ORs the colours of players sharing a pixel, so a pair on top of each other shows
  /// black, either colour, or the two together, where sliding them apart shows only two colours over
  /// twice the width. Sliding them apart is what the editor was for, and it is not what makes the
  /// better picture out of one that was not drawn as sprites.
  /// <para/>
  /// A frame is wider than its players by two pixels, which are the spacing the editor drew between
  /// them and always show the border. The sheet is therefore a multiple of twenty across and at most
  /// sixteen frames, so a picture is sampled to the nearest such width and to at most forty-eight
  /// rows — the whole of what a player is.
  /// </remarks>
  public static AtariPlayerEditorFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var frames = Math.Clamp((image.Width + NarrowFrameWidth / 2) / NarrowFrameWidth, 1, MaxFrames);
    var height = Math.Clamp(image.Height, 1, MaxHeight);
    var width = frames * NarrowFrameWidth;
    var rgb = image.SampleTo(width, height).PixelData;
    var palette = Atari8BitGraphics.Palette;

    var data = new byte[FileSize];
    Signature.CopyTo(data);
    data[4] = (byte)frames;
    data[5] = (byte)height;
    data[6] = 0;

    for (var frame = 0; frame < frames; ++frame) {
      var left = frame * NarrowFrameWidth;
      var (first, second) = _ChooseColors(rgb, width, height, left, palette);
      data[FirstColorOffset + frame] = first;
      data[SecondColorOffset + frame] = second;

      for (var y = 0; y < height; ++y) {
        int firstBits = 0, secondBits = 0;

        for (var x = 0; x < PlayerPixels; ++x) {
          var at = (y * width + left + x * 2) * 3;
          var best = _Best(palette, first, second, rgb[at], rgb[at + 1], rgb[at + 2]);
          if ((best & 1) != 0)
            firstBits |= 1 << (7 - x);

          if ((best & 2) != 0)
            secondBits |= 1 << (7 - x);
        }

        data[FirstShapeOffset + frame * ShapeStride + y] = (byte)firstBits;
        data[SecondShapeOffset + frame * ShapeStride + y] = (byte)secondBits;
      }
    }

    return new() { Data = data, Frames = frames, Height = height, Gap = 0 };
  }

  /// <summary>
  /// The two colours a frame's players carry, chosen from the colours the frame actually shows.
  /// </summary>
  /// <remarks>
  /// The two are chosen together rather than one at a time, because what they show is not two
  /// colours but four: neither, either, and the two ORed. A pair that is poor on its own may be the
  /// pair whose combination lands on the colour most of the frame needs.
  /// </remarks>
  private static (byte First, byte Second) _ChooseColors(
    ReadOnlySpan<byte> rgb, int width, int height, int left, ReadOnlySpan<byte> palette) {
    Span<int> counts = stackalloc int[256];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < PlayerPixels; ++x) {
      var at = (y * width + left + x * 2) * 3;
      ++counts[Atari8BitGraphics.FindNearestColorByte(palette, rgb[at], rgb[at + 1], rgb[at + 2])];
    }

    const int candidateCount = 12;
    Span<byte> candidates = stackalloc byte[candidateCount];
    for (var slot = 0; slot < candidateCount; ++slot) {
      var best = 0;
      for (var value = 0; value < 256; value += 2)
        if (counts[value] > counts[best])
          best = value;

      candidates[slot] = (byte)best;
      counts[best] = 0;
    }

    byte bestFirst = 0, bestSecond = 0;
    var bestCost = long.MaxValue;

    foreach (var first in candidates)
    foreach (var second in candidates) {
      long cost = 0;
      for (var y = 0; y < height; ++y)
      for (var x = 0; x < PlayerPixels; ++x) {
        var at = (y * width + left + x * 2) * 3;
        cost += _Cost(palette, first, second, rgb[at], rgb[at + 1], rgb[at + 2]);
      }

      if (cost >= bestCost)
        continue;

      bestCost = cost;
      bestFirst = first;
      bestSecond = second;
    }

    return (bestFirst, bestSecond);
  }

  /// <summary>Which of the four things a pixel can show describes a colour best, as a pair of bits.</summary>
  private static int _Best(ReadOnlySpan<byte> palette, byte first, byte second, byte red, byte green, byte blue) {
    var best = 0;
    var bestCost = int.MaxValue;

    for (var value = 0; value < 4; ++value) {
      var cost = _Distance(palette, _Shown(first, second, value), red, green, blue);
      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = value;
    }

    return best;
  }

  private static int _Cost(ReadOnlySpan<byte> palette, byte first, byte second, byte red, byte green, byte blue) {
    var best = int.MaxValue;
    for (var value = 0; value < 4; ++value)
      best = Math.Min(best, _Distance(palette, _Shown(first, second, value), red, green, blue));

    return best;
  }

  /// <summary>The colour byte a pair of player bits puts on screen, the chip ORing what overlaps.</summary>
  private static int _Shown(byte first, byte second, int bits)
    => (((bits & 1) != 0 ? first & 254 : 0) | ((bits & 2) != 0 ? second & 254 : 0)) & 254;

  private static int _Distance(ReadOnlySpan<byte> palette, int entry, byte red, byte green, byte blue) {
    var at = entry * 3;
    int dr = palette[at] - red, dg = palette[at + 1] - green, db = palette[at + 2] - blue;

    return dr * dr + dg * dg + db * db;
  }
}
