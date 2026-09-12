using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.Ico;

namespace FileFormat.Ani;

/// <summary>In-memory representation of an ANI animated cursor file.</summary>
[FormatMimeType("application/x-navi-animation", "image/x-ani")]
[VerifiedBy(ConformanceOracle.XnView)]
public sealed class AniFile : IImageFormatReader<AniFile>, IImageToRawImage<AniFile>, IImageFromRawImage<AniFile>, IImageFormatWriter<AniFile>, IMultiImageFileFormat<AniFile> {

  static FormatCapability IImageFormatMetadata<AniFile>.Capabilities => FormatCapability.HasDedicatedOptimizer;

  public required AniHeader Header { get; init; }
  public IReadOnlyList<IcoFile> Frames { get; init; } = [];

  /// <summary>Each frame's bytes exactly as the file carried them.</summary>
  /// <remarks>
  /// A frame is a whole icon or cursor file. <see cref="Frames"/> is that file parsed, which is
  /// what anything wanting the pictures needs, but the parse drops what a cursor keeps in its
  /// directory — the hotspot — and so cannot be written back without changing the animation. This
  /// is the unparsed original, kept so a file that is read and written again comes out as it went
  /// in. It is empty for a file built rather than read, and then the frames are assembled from
  /// <see cref="Frames"/> instead.
  /// </remarks>
  public IReadOnlyList<byte[]> FrameData { get; init; } = [];

  public int[]? Rates { get; init; }
  public int[]? Sequence { get; init; }

  /// <summary>The title from the INFO list's INAM chunk, if the file carries one.</summary>
  public string? Title { get; init; }

  /// <summary>The author from the INFO list's IART chunk, if the file carries one.</summary>
  public string? Artist { get; init; }

  public static string PrimaryExtension => ".ani";
  public static string[] FileExtensions => [".ani"];
  static AniFile IImageFormatReader<AniFile>.FromSpan(ReadOnlySpan<byte> data) => AniReader.FromSpan(data);

  public static bool? MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 12
       && header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46
       && header[8] == 0x41 && header[9] == 0x43 && header[10] == 0x4F && header[11] == 0x4E
       ? true
       : null;

  public static AniFile FromFile(FileInfo file) => AniReader.FromFile(file);
  public static AniFile FromBytes(byte[] data) => AniReader.FromBytes(data);
  public static AniFile FromStream(Stream stream) => AniReader.FromStream(stream);
  public static byte[] ToBytes(AniFile file) => AniWriter.ToBytes(file);

  /// <summary>Returns the number of frames in this ANI file.</summary>
  /// <summary>
  /// Builds an animation of one frame holding the picture.
  /// </summary>
  /// <remarks>
  /// An animated cursor is a RIFF file whose frames are whole cursor files, so the picture goes
  /// through the same bitmap an icon carries and the container is the only part that is this
  /// format's own.
  /// <para/>
  /// One frame is what a single picture is. The rate is the sixty-hertz tick the format counts in,
  /// six of which is the tenth of a second Windows uses when a file states nothing, and the header
  /// states one frame and one step so that nothing is left to infer a sequence from.
  /// <para/>
  /// The flag set is AF_ICON, which says the frame is a whole icon file rather than a bare bitmap
  /// — and it is one. This used to set the value 2, which is AF_SEQUENCE, so a single-frame
  /// animation claimed a sequence it did not have and did not claim the one thing about it that
  /// was true.
  /// </remarks>
  public static AniFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var frame = IcoFile.FromRawImage(image);
    var entry = frame.Images[0];

    return new() {
      Header = new AniHeader(
        AniHeader.StructSize,
        NumFrames: 1,
        NumSteps: 1,
        Width: entry.Width,
        Height: entry.Height,
        BitCount: entry.BitsPerPixel,
        NumPlanes: 1,
        DisplayRate: 6,
        Flags: 1),
      Frames = [frame],
    };
  }

  public static int ImageCount(AniFile file) => file.Frames.Count;

  /// <summary>Converts the frame at the given index to a <see cref="RawImage"/> (uses largest image in each ICO frame).</summary>
  public static RawImage ToRawImage(AniFile file, int index) {
    ArgumentNullException.ThrowIfNull(file);
    if ((uint)index >= (uint)file.Frames.Count)
      throw new ArgumentOutOfRangeException(nameof(index));

    return IcoFile.ToRawImage(file.Frames[index]);
  }

  public static RawImage ToRawImage(AniFile file)
    => file.Frames.Count > 0
      ? ToRawImage(file, 0)
      : throw new NotSupportedException("ANI file contains no frames.");

}
