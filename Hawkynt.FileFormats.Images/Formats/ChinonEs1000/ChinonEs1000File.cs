using System;
using FileFormat.Core;

namespace FileFormat.ChinonEs1000;

/// <summary>A picture off a Chinon ES-1000 digital camera (.cmt): a fixed-size COMET file holding
/// the camera's 512 by 243 complementary-colour CCD readout.</summary>
/// <remarks>
/// The reader follows the XnView-matched forward camera pipeline. Writing necessarily solves a
/// different problem: arbitrary RGB has no unique original sensor exposure because the forward path
/// interpolates neighbours, changes saturation, discards histogram tails and applies gamma. The
/// writable subset therefore synthesizes a legal CCD mosaic whose forward decode approximates the
/// requested 500 by 241 image, using an analytical complementary-filter projection followed by
/// bounded residual refinement through that same decoder.
/// </remarks>
[FormatMagicBytes([(byte)'C', (byte)'O', (byte)'M', (byte)'E', (byte)'T'])]
public readonly record struct ChinonEs1000File :
  IImageFormatReader<ChinonEs1000File>, IImageToRawImage<ChinonEs1000File>,
  IImageFromRawImage<ChinonEs1000File>, IImageFormatWriter<ChinonEs1000File> {

  /// <summary>The five bytes a file opens with; XnView compares only the first four of them.</summary>
  public static ReadOnlySpan<byte> Magic => [(byte)'C', (byte)'O', (byte)'M', (byte)'E', (byte)'T'];

  public const int FileHeaderSize = 128;
  public const int CameraHeaderSize = 512;
  public const int CcdColumns = 512;
  public const int CcdLines = 243;
  public const int FileSize = FileHeaderSize + CameraHeaderSize + CcdColumns * CcdLines;
  public const int LeftMargin = 2;
  public const int RightMargin = 10;
  public const int TopMargin = 1;
  public const int BottomMargin = 1;
  public const int Width = CcdColumns - LeftMargin - RightMargin;
  public const int Height = CcdLines - TopMargin - BottomMargin;

  static string IImageFormatMetadata<ChinonEs1000File>.PrimaryExtension => ".cmt";
  static string[] IImageFormatMetadata<ChinonEs1000File>.FileExtensions => [".cmt"];
  static ChinonEs1000File IImageFormatReader<ChinonEs1000File>.FromSpan(ReadOnlySpan<byte> data) => ChinonEs1000Reader.FromSpan(data);
  static byte[] IImageFormatWriter<ChinonEs1000File>.ToBytes(ChinonEs1000File file) => ChinonEs1000Writer.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<ChinonEs1000File>.VideoModes => [
    new("Chinon ES-1000", [(Width, Height)], [16777216])
  ];

  /// <summary>The raw CCD readout, 512 cells a line and 243 lines.</summary>
  public byte[] CcdData { get; init; }

  public static RawImage ToRawImage(ChinonEs1000File file) {
    if (file.CcdData == null)
      throw new InvalidOperationException("No picture was read.");

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = ChinonEs1000Demosaic.ToRgb24(file.CcdData),
    };
  }

  public static ChinonEs1000File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    return new() { CcdData = ChinonEs1000Inverse.FromRgb(image) };
  }
}
