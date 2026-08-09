using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.ElectricImage;

/// <summary>An ElectricImage rendered picture (.ei, .eidi).</summary>
/// <remarks>
/// What the ElectricImage Animation System wrote its renders as, on the Macintosh, from the late
/// eighties on. The file carries the Mac type <c>EIDI</c> and creator <c>EIAD</c>, and the name it
/// goes by here is the one XnView gives it.
/// <para/>
/// No specification of it has been published. The layout below is the one Kostya Shishkov's NihAV
/// carries in <c>na_eofdec</c>, and it was then checked against eighteen real files: all eighteen
/// walk from the header to the last byte of the file exactly — the offset the header leads to plus
/// the length it states is the file's own length in every one — and the run-length data in every one
/// unpacks to exactly the width times the height that the header states, consuming exactly the
/// bytes the header said it would.
/// <para/>
/// Everything is big-endian. Two bytes of version, four of a frame count, and then each frame is a
/// time, a zero, the height and the width as words, a depth byte and a flag byte, another zero, the
/// height and width again, a further word, the length of the frame's data and a mode. A frame at
/// eight bits then carries the first and last palette index it uses and three bytes for each entry
/// between them. A mode of 1 has five bytes of its own behind that; a mode of 0x0100 has none, which
/// is the correction the eighteen files forced — with five bytes skipped for both, the two
/// eight-bit files overran their own ends by exactly five.
/// <para/>
/// The data is run-length coded over elements the width of a pixel. A lead byte under 0x80 is a run
/// of that many plus one copies of the single element behind it; one of 0x80 or over is that many
/// less 0x80, plus one, elements standing as they are. A depth of 24 with the low byte of the
/// fifteenth word set to 8 is really four bytes a pixel, alpha first.
/// <para/>
/// Checked against XnView on all eighteen: every pixel of every one agrees — the palette entries for
/// the two eight-bit files, red, green and blue in that order for the one true 24-bit file, and
/// alpha, red, green, blue for the fifteen with a fourth channel.
/// <para/>
/// Depths of 1 and 16, and the depth frames that carry floating-point distances rather than colour,
/// are refused: none of the eighteen is one and there is nothing to check a reading of them against.
/// Nothing is written, for the same reason — a renderer's output file is not something this can
/// produce a true example of.
/// </remarks>
public sealed class ElectricImageFile
  : IImageFormatReader<ElectricImageFile>, IImageToRawImage<ElectricImageFile>,
    IMultiImageFileFormat<ElectricImageFile> {

  /// <summary>The only version any of the files carries.</summary>
  public const int Version = 5;

  /// <summary>Two bytes of version and four of frame count.</summary>
  public const int FileHeaderSize = 6;

  /// <summary>The fixed part of a frame's own header.</summary>
  public const int FrameHeaderSize = 30;

  static string IImageFormatMetadata<ElectricImageFile>.PrimaryExtension => ".ei";
  static string[] IImageFormatMetadata<ElectricImageFile>.FileExtensions => [".ei", ".eidi"];
  static ElectricImageFile IImageFormatReader<ElectricImageFile>.FromSpan(ReadOnlySpan<byte> data) => ElectricImageReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<ElectricImageFile>.Capabilities => FormatCapability.MultiImage;
  static VideoMode[] IImageFormatMetadata<ElectricImageFile>.VideoModes => [
    new("Palette", [(IntegerRange.Any, IntegerRange.Any)], [256]),
    new("Colour", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  /// <summary>One rendered frame.</summary>
  public sealed record Frame {

    /// <summary>How wide it is.</summary>
    public int Width { get; init; }

    /// <summary>How tall it is.</summary>
    public int Height { get; init; }

    /// <summary>How many bytes a pixel takes once unpacked: 1, 3 or 4.</summary>
    public int BytesPerPixel { get; init; }

    /// <summary>The unpacked pixels, one row after another.</summary>
    public byte[] PixelData { get; init; } = [];

    /// <summary>The colour table as red, green and blue triplets, when the frame has one.</summary>
    public byte[]? Palette { get; init; }
  }

  /// <summary>The frames the file holds, in order.</summary>
  public IReadOnlyList<Frame> Frames { get; init; } = [];

  public static int ImageCount(ElectricImageFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return file.Frames.Count;
  }

  public static RawImage ToRawImage(ElectricImageFile file, int index) {
    ArgumentNullException.ThrowIfNull(file);
    if ((uint)index >= (uint)file.Frames.Count)
      throw new ArgumentOutOfRangeException(nameof(index));

    var frame = file.Frames[index];
    return frame.BytesPerPixel switch {
      1 => new() {
        Width = frame.Width,
        Height = frame.Height,
        Format = PixelFormat.Indexed8,
        PixelData = frame.PixelData[..],
        Palette = frame.Palette,
        PaletteCount = frame.Palette == null ? 0 : frame.Palette.Length / 3,
      },
      3 => new() {
        Width = frame.Width,
        Height = frame.Height,
        Format = PixelFormat.Rgb24,
        PixelData = frame.PixelData[..],
      },
      _ => new() {
        Width = frame.Width,
        Height = frame.Height,
        Format = PixelFormat.Argb32,
        PixelData = frame.PixelData[..],
      },
    };
  }

  public static RawImage ToRawImage(ElectricImageFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Frames.Count == 0)
      throw new InvalidDataException("An ElectricImage file with no frame in it.");

    return ToRawImage(file, 0);
  }
}
