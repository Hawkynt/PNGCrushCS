using System;
using FileFormat.Core;

namespace FileFormat.Iwc;

/// <summary>A WaveL Image Wavelet Codec (IWC) picture, as WaveL CompressIt wrote it for its Java applet.</summary>
/// <remarks>
/// <para>
/// WaveL Software's format (Barnaul, 2000–2001): a JPEG-style YCbCr picture decomposed by a
/// reversible integer lifting wavelet, every subband quantised to byte values by its own step, and
/// the lot coded with Jones's adaptive splay-tree prefix codes plus run lengths. The header names the
/// steps, so the decoder needs nothing else.
/// </para>
/// <para>
/// The coding is lossy only where a band's range exceeds 253 steps of one; <see cref="PixelData"/>
/// is the decoded picture, and writing it again re-encodes it.
/// </para>
/// </remarks>
[FormatMagicBytes([0x49, 0x57, 0x43, 0x01])]
[FormatMimeType("image/x-iwc")]
public readonly record struct IwcFile :
  IImageFormatReader<IwcFile>, IImageToRawImage<IwcFile>,
  IImageFromRawImage<IwcFile>, IImageFormatWriter<IwcFile>,
  IImageInfoReader<IwcFile> {

  static string IImageFormatMetadata<IwcFile>.PrimaryExtension => ".iwc";
  static string[] IImageFormatMetadata<IwcFile>.FileExtensions => [".iwc"];
  static IwcFile IImageFormatReader<IwcFile>.FromSpan(ReadOnlySpan<byte> data) => IwcReader.FromSpan(data);
  static byte[] IImageFormatWriter<IwcFile>.ToBytes(IwcFile file) => IwcWriter.ToBytes(file);

  /// <summary>"IWC", version 1, and a size and level count the applet can decompose.</summary>
  static bool? IImageFormatMetadata<IwcFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (!IwcHeader.HasSignature(header) || header[3] != IwcHeader.KnownVersion)
      return null;

    if (header.Length < 10)
      return true;

    var width = header[4] | header[5] << 8;
    var height = header[6] | header[7] << 8;
    var levels = header[8] | header[9] << 8;
    return width < 0x8000 && height < 0x8000 && levels < 0x8000 && IwcHeader.IsDecomposable(width, height, levels);
  }

  /// <summary>Picture width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Picture height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Decomposition depth; 0 lets the writer choose the deepest the picture allows, up to 7.</summary>
  public int Levels { get; init; }

  /// <summary>The encoder's quality setting as the header records it; the decoder does not use it. WaveL's samples carry 30 and 40.</summary>
  public int Quality { get; init; } = DefaultQuality;

  /// <summary>What the writer records when nothing else is asked for.</summary>
  public const int DefaultQuality = 100;

  /// <summary>Decoded samples, Rgb24.</summary>
  public byte[] PixelData { get; init; }

  public IwcFile() => this.PixelData = [];

  /// <summary>Size and coding from the header alone.</summary>
  public static ImageInfo? ReadImageInfo(ReadOnlySpan<byte> header) {
    if (header.Length < 12 || !IwcHeader.HasSignature(header) || header[3] != IwcHeader.KnownVersion)
      return null;

    var width = (short)(header[4] | header[5] << 8);
    var height = (short)(header[6] | header[7] << 8);
    var levels = (short)(header[8] | header[9] << 8);
    return IwcHeader.IsDecomposable(width, height, levels)
      ? new(width, height, 24, "Rgb24", $"WaveL lifting wavelet, {levels} levels, splay-coded")
      : null;
  }

  public static RawImage ToRawImage(IwcFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Rgb24,
    PixelData = file.PixelData[..],
  };

  public static IwcFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Rgb24);
    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
