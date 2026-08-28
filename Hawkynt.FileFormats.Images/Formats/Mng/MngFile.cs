using System;
using System.Collections.Generic;
using FileFormat.Core;
using FileFormat.Png;

namespace FileFormat.Mng;

/// <summary>In-memory representation of an MNG animation.</summary>
[FormatMagicBytes([0x8A, 0x4D, 0x4E, 0x47])]
[FormatMimeType("video/x-mng", "image/x-mng")]
public sealed class MngFile : IImageFormatReader<MngFile>, IImageToRawImage<MngFile>, IImageFromRawImage<MngFile>, IImageFormatWriter<MngFile>, IMultiImageFileFormat<MngFile> {

  static string IImageFormatMetadata<MngFile>.PrimaryExtension => ".mng";
  static string[] IImageFormatMetadata<MngFile>.FileExtensions => [".mng"];
  static MngFile IImageFormatReader<MngFile>.FromSpan(ReadOnlySpan<byte> data) => MngReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<MngFile>.Capabilities => FormatCapability.MultiImage;
  static byte[] IImageFormatWriter<MngFile>.ToBytes(MngFile file) => MngWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public int TicksPerSecond { get; init; }

  /// <summary>Maximum number of TERM repeat iterations. A value of <c>0x7fffffff</c> means infinity.</summary>
  public int NumPlays { get; init; }

  public MngTermAction TermAction { get; init; }

  /// <summary>Action after the requested repeat iterations. Used only when <see cref="TermAction"/> is <see cref="MngTermAction.Repeat"/>.</summary>
  public MngTermAction ActionAfterIterations { get; init; } = MngTermAction.ShowLast;

  /// <summary>Delay in MNG ticks before a TERM repeat. Used only for <see cref="TermAction"/> is <see cref="MngTermAction.Repeat"/>.</summary>
  public int RepeatDelay { get; init; }

  /// <summary>Embedded PNG frames (each is a complete PNG file).</summary>
  public IReadOnlyList<byte[]> Frames { get; init; } = [];

  /// <summary>
  /// Interframe delay in MNG ticks for each visible frame. Empty means the format default of one tick
  /// per image when <see cref="TicksPerSecond"/> is nonzero. A populated list must match <see cref="Frames"/>.
  /// </summary>
  public IReadOnlyList<int> FrameDelays { get; init; } = [];

  /// <summary>Returns the number of frames in this MNG file.</summary>
  public static int ImageCount(MngFile file) => file.Frames.Count;

  /// <summary>Converts the frame at the given index to a <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(MngFile file, int index) {
    ArgumentNullException.ThrowIfNull(file);
    if ((uint)index >= (uint)file.Frames.Count)
      throw new ArgumentOutOfRangeException(nameof(index));

    return PngFile.ToRawImage(PngReader.FromBytes(file.Frames[index]));
  }

  /// <summary>Converts the first frame of an MNG file to a <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(MngFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Frames.Count == 0)
      throw new ArgumentException("MNG file contains no frames.", nameof(file));

    return PngFile.ToRawImage(PngReader.FromBytes(file.Frames[0]));
  }

  /// <summary>Creates a single-frame MNG from a <see cref="RawImage"/> of any size.</summary>
  /// <remarks>
  /// An MNG frame is a whole PNG, so this defers to the PNG codec rather than growing a second one:
  /// whatever PNG can hold losslessly, a one-frame MNG holds too, at any size. The result needs no
  /// timing clock because the MNG specification defines ticks as irrelevant for a one-frame stream.
  /// </remarks>
  public static MngFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    return new() {
      Width = image.Width,
      Height = image.Height,
      TicksPerSecond = 0,
      NumPlays = 1,
      TermAction = MngTermAction.ShowLast,
      Frames = [PngWriter.ToBytes(PngFile.FromRawImage(image))],
      FrameDelays = [],
    };
  }
}
