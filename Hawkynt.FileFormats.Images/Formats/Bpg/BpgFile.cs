using System;
using System.Collections.Generic;
using FileFormat.Bpg.Codec;
using FileFormat.Core;

namespace FileFormat.Bpg;

/// <summary>In-memory representation of a BPG (Better Portable Graphics) image container.</summary>
[FormatMagicBytes([0x42, 0x50, 0x47, 0xFB])]
public sealed class BpgFile :
  IImageFormatReader<BpgFile>,
  IImageToRawImage<BpgFile>,
  IImageFromRawImage<BpgFile>,
  IImageFormatWriter<BpgFile>,
  IMultiImageFileFormat<BpgFile> {

  /// <summary>BPG magic bytes: "BPG" + 0xFB.</summary>
  internal static readonly byte[] Magic = [0x42, 0x50, 0x47, 0xFB];

  /// <summary>Minimum header size (magic + byte4 + byte5 + at least 2 ue7 bytes for width/height).</summary>
  internal const int MinHeaderSize = 6;

  static string IImageFormatMetadata<BpgFile>.PrimaryExtension => ".bpg";
  static string[] IImageFormatMetadata<BpgFile>.FileExtensions => [".bpg"];
  static BpgFile IImageFormatReader<BpgFile>.FromSpan(ReadOnlySpan<byte> data) => BpgReader.FromSpan(data);
  static byte[] IImageFormatWriter<BpgFile>.ToBytes(BpgFile file) => BpgWriter.ToBytes(file);

  /// <summary>Encodes a picture as an HEVC intra picture of PCM coding units — see <see cref="BpgWriter.FromRawImage"/>.</summary>
  public static BpgFile FromRawImage(RawImage image) => BpgWriter.FromRawImage(image);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Pixel format (chroma subsampling type).</summary>
  public BpgPixelFormat PixelFormat { get; init; }

  /// <summary>Bit depth (8-14).</summary>
  public int BitDepth { get; init; } = 8;

  /// <summary>Color space.</summary>
  public BpgColorSpace ColorSpace { get; init; }

  /// <summary>Whether an alpha plane is present.</summary>
  public bool HasAlpha { get; init; }

  /// <summary>Whether an additional alpha plane (premultiplied) is present.</summary>
  public bool HasAlpha2 { get; init; }

  /// <summary>Whether the range is limited (studio swing).</summary>
  public bool LimitedRange { get; init; }

  /// <summary>Whether this is an animated BPG.</summary>
  public bool IsAnimation { get; init; }

  /// <summary>Whether extension data is present.</summary>
  public bool ExtensionPresent { get; init; }

  /// <summary>Extension data bytes (empty if no extension).</summary>
  public byte[] ExtensionData { get; init; } = [];

  /// <summary>Raw picture data (HEVC or other codec data).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Decoded frames, populated lazily by the HEVC decoder.</summary>
  private IReadOnlyList<BpgHevcDecoder.DecodedFrame>? _frames;

  /// <summary>How many times an animation plays; zero means for ever. One for a still picture.</summary>
  public int LoopCount => this.IsAnimation ? this._AnimationControl().LoopCount : 1;

  /// <summary>How long each frame is shown: its repeat count from the stream times the file's frame period.</summary>
  public static IReadOnlyList<TimeSpan> FrameDurations(BpgFile file) {
    ArgumentNullException.ThrowIfNull(file);
    var frames = _Frames(file);
    var result = new TimeSpan[frames.Count];
    if (!file.IsAnimation)
      return result;

    var (_, numerator, denominator) = file._AnimationControl();
    for (var i = 0; i < result.Length; ++i)
      result[i] = TimeSpan.FromSeconds((double)frames[i].Duration * numerator / denominator);
    return result;
  }

  public static int ImageCount(BpgFile file) => _Frames(file).Count;

  public static RawImage ToRawImage(BpgFile file, int index) {
    ArgumentNullException.ThrowIfNull(file);
    var frames = _Frames(file);
    if (index < 0 || index >= frames.Count)
      throw new ArgumentOutOfRangeException(nameof(index), $"A BPG file of {frames.Count} frame(s) has no frame {index}.");

    var deep = file.BitDepth > 8 && !_IsUniformPcm(file, frames[index]);
    var format = (file.PixelFormat == BpgPixelFormat.Grayscale && !file.HasAlpha, file.HasAlpha, deep) switch {
      (true, _, false) => Core.PixelFormat.Gray8,
      (true, _, true) => Core.PixelFormat.Gray16,
      (false, true, false) => Core.PixelFormat.Rgba32,
      (false, true, true) => Core.PixelFormat.Rgba64,
      (false, false, false) => Core.PixelFormat.Rgb24,
      _ => Core.PixelFormat.Rgb48,
    };

    return new() { Width = file.Width, Height = file.Height, Format = format, PixelData = frames[index].Pixels[..] };
  }

  /// <summary>Converts the first frame of a BPG file to a <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(BpgFile file) => ToRawImage(file, 0);

  /// <summary>The package's own PCM writer output decodes straight to eight bits; everything deeper decodes to sixteen.</summary>
  private static bool _IsUniformPcm(BpgFile file, BpgHevcDecoder.DecodedFrame frame)
    => frame.Pixels.Length == file.Width * file.Height * 3 && file.BitDepth == 8;

  private static IReadOnlyList<BpgHevcDecoder.DecodedFrame> _Frames(BpgFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file._frames != null)
      return file._frames;
    if (file.PixelData.Length == 0)
      throw new InvalidOperationException("BPG file contains no HEVC picture data.");
    return file._frames = BpgHevcDecoder.Decode(file);
  }

  /// <summary>The animation control extension (tag 5): loop count and the frame period as a fraction of a second.</summary>
  private (int LoopCount, int Numerator, int Denominator) _AnimationControl() {
    var data = this.ExtensionData;
    var at = 0;
    while (at < data.Length) {
      var tag = BpgUe7.Read(data, ref at);
      var length = BpgUe7.Read(data, ref at);
      if (length < 0 || at + length > data.Length)
        break;
      if (tag == 5) {
        var inner = at;
        var loops = BpgUe7.Read(data, ref inner);
        var numerator = BpgUe7.Read(data, ref inner);
        var denominator = BpgUe7.Read(data, ref inner);
        if (numerator == 0 || denominator == 0)
          throw new System.IO.InvalidDataException("A BPG animation states a frame period of zero.");
        return (loops, numerator, denominator);
      }

      at += length;
    }

    throw new System.IO.InvalidDataException("A BPG animation carries no animation control extension, which the specification requires.");
  }
}
