using System;
using FileFormat.Core;

namespace FileFormat.InterlaceStudio;

/// <summary>In-memory representation of an Interlace Studio picture for the Atari 8-bit.</summary>
/// <remarks>
/// This was modelled on the Commodore 64 — bitmap, video matrix and colour memory twice over, 19003
/// bytes — and Interlace Studio is an Atari program. Every sample is 17184 bytes and all were
/// refused. The giveaway was not the length but the colours: of the seven RECOIL draws, two are in
/// the Commodore's sixteen and all seven are in the Atari's, so no arrangement of a C64 screen could
/// ever have matched.
/// <para/>
/// A file is a sixteen-byte header and two Atari four-colour screens, the first taking a whole
/// eight-kilobyte page for the 8000 bytes it uses. They are shown one after the other fast enough
/// that the eye adds them, so a pixel is the average of what the two frames give it — four levels
/// each, blending to the seven that appear.
/// <para/>
/// The four levels are a grey ramp of nought, 68, 136 and 204, which is the Atari's first hue at
/// four luminances. The header does hold four bytes that look like colour registers and they differ
/// between samples, but the reference tool draws all three in that same ramp regardless, so they are
/// not what it colours by. Nothing else here reads this format, so the ramp is what is matched and
/// this is said rather than dressed up as a choice.
/// </remarks>
public readonly record struct InterlaceStudioFile : IImageFormatReader<InterlaceStudioFile>, IImageToRawImage<InterlaceStudioFile>, IImageFormatWriter<InterlaceStudioFile> {

  static string IImageFormatMetadata<InterlaceStudioFile>.PrimaryExtension => ".ist";
  static string[] IImageFormatMetadata<InterlaceStudioFile>.FileExtensions => [".ist"];
  static InterlaceStudioFile IImageFormatReader<InterlaceStudioFile>.FromSpan(ReadOnlySpan<byte> data) => InterlaceStudioReader.FromSpan(data);
  static byte[] IImageFormatWriter<InterlaceStudioFile>.ToBytes(InterlaceStudioFile file) => InterlaceStudioWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<InterlaceStudioFile>.VideoModes => [
    new("Interlace Studio", [(ImageWidth, ImageHeight)], [7])
  ];

  /// <summary>Pixels across as stored; the reference tool shows each twice.</summary>
  public const int ImageWidth = 160;

  /// <summary>Rows.</summary>
  public const int ImageHeight = 200;

  /// <summary>Bytes a row takes at two bits a pixel.</summary>
  internal const int BytesPerRow = ImageWidth / 4;

  /// <summary>The bytes one frame uses.</summary>
  internal const int FrameSize = BytesPerRow * ImageHeight;

  /// <summary>The address space the first frame occupies, being a whole page.</summary>
  internal const int FrameStride = 8192;

  /// <summary>The header before the first frame.</summary>
  internal const int HeaderSize = 16;

  /// <summary>Where the first frame starts.</summary>
  internal const int FirstFrameOffset = HeaderSize;

  /// <summary>Where the second starts: a page after the first, not 8000 bytes after it.</summary>
  internal const int SecondFrameOffset = HeaderSize + FrameStride;

  /// <summary>The least a file takes, the tail past the second frame being the same in every sample.</summary>
  public const int MinimumFileSize = SecondFrameOffset + FrameSize;

  /// <summary>
  /// The four levels a frame can show, as grey.
  /// </summary>
  /// <remarks>
  /// The Atari's first hue at four luminances. Two frames of these average to the seven levels the
  /// picture actually shows.
  /// </remarks>
  internal const int LevelStep = 68;

  /// <summary>Always 160.</summary>
  public int Width => ImageWidth;

  /// <summary>Always 200.</summary>
  public int Height => ImageHeight;

  /// <summary>The sixteen bytes before the picture, four of which look like colour registers.</summary>
  public byte[] Header { get; init; }

  /// <summary>The first frame, two bits a pixel.</summary>
  public byte[] FirstFrame { get; init; }

  /// <summary>The second frame.</summary>
  public byte[] SecondFrame { get; init; }

  /// <summary>Converts this picture to a platform-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(InterlaceStudioFile file) {
    var first = file.FirstFrame ?? [];
    var second = file.SecondFrame ?? [];
    var rgb = new byte[ImageWidth * ImageHeight * 3];

    for (var y = 0; y < ImageHeight; ++y)
      for (var x = 0; x < ImageWidth; ++x) {
        var shift = (3 - x % 4) * 2;
        var a = (first[y * BytesPerRow + x / 4] >> shift) & 3;
        var b = (second[y * BytesPerRow + x / 4] >> shift) & 3;

        // The eye averages the two frames, which is what turns four levels into seven.
        var level = (byte)((a * LevelStep + b * LevelStep) / 2);
        var at = (y * ImageWidth + x) * 3;
        rgb[at] = level;
        rgb[at + 1] = level;
        rgb[at + 2] = level;
      }

    return new() {
      Width = ImageWidth,
      Height = ImageHeight,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }
}
