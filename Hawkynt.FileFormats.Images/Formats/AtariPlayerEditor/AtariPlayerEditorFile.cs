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
  : IImageFormatReader<AtariPlayerEditorFile>, IImageToRawImage<AtariPlayerEditorFile> {

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
}
