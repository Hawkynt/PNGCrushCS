using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.TmSat;

/// <summary>A TMSAT-1 narrow-angle camera image (.imi).</summary>
/// <remarks>
/// TMSAT-1, also called Thai-Paht and TO-31 by the amateur satellite service, was built by Surrey
/// Satellite Technology with Mahanakorn University of Technology and flew a pair of cameras. The
/// files its ground stations wrote have no header at all: they are the camera's samples, one byte
/// each, one row after another from the top, and what says which camera and which band a file came
/// from is its name. The reference for that is the help text of Colin Hurst's CCD Display, the
/// ground-station program that decoded the downlink, which records that the file structures were
/// specified by Surrey.
/// <para/>
/// A file with no header is identified by its length and nothing else, so only one length is taken:
/// 1,040,400 bytes, which is the narrow-angle camera's 1020 by 1020. That is also the only one
/// XnView takes — handed the wide-angle camera's 352,192 bytes it refuses the file, and handed one
/// byte less than 1,040,400 it refuses that too. What it converts out of the right length is the
/// file's own bytes, unchanged, which is what settles that there is no header and no palette.
/// <para/>
/// Three narrow-angle frames in different bands make one colour picture between them, and the
/// compressed <c>.imc</c> variant is described nowhere. Neither is read: one file is one grey frame.
/// </remarks>
public readonly record struct TmSatFile
  : IImageFormatReader<TmSatFile>, IImageToRawImage<TmSatFile>,
    IImageFromRawImage<TmSatFile>, IImageFormatWriter<TmSatFile> {

  /// <summary>The narrow-angle camera's frame, which is square.</summary>
  public const int Side = 1020;

  /// <summary>The only length a file of this format has.</summary>
  public const int FileSize = Side * Side;

  static string IImageFormatMetadata<TmSatFile>.PrimaryExtension => ".imi";
  static string[] IImageFormatMetadata<TmSatFile>.FileExtensions => [".imi"];
  static TmSatFile IImageFormatReader<TmSatFile>.FromSpan(ReadOnlySpan<byte> data) => TmSatReader.FromSpan(data);
  static byte[] IImageFormatWriter<TmSatFile>.ToBytes(TmSatFile file) => TmSatWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<TmSatFile>.VideoModes => [
    new("Narrow angle", [(new IntegerRange(Side, Side), new IntegerRange(Side, Side))], [256])
  ];

  /// <summary>Always 1020.</summary>
  public int Width => Side;

  /// <summary>Always 1020.</summary>
  public int Height => Side;

  /// <summary>The samples, one byte a pixel.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(TmSatFile file) {
    if (file.PixelData == null)
      throw new InvalidOperationException("No frame was read.");

    return new() {
      Width = Side,
      Height = Side,
      Format = PixelFormat.Gray8,
      PixelData = file.PixelData[..],
    };
  }

  /// <summary>Builds the frame, which only a picture of exactly 1020 by 1020 can be.</summary>
  public static TmSatFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != Side || image.Height != Side)
      throw new ArgumentException($"A TMSat frame is {Side} by {Side} and this picture is {image.Width} by {image.Height}.", nameof(image));

    return new() { PixelData = image.EnsureFormat(PixelFormat.Gray8).PixelData[..] };
  }
}
