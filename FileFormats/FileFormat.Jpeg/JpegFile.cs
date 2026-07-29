using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Jpeg;

/// <summary>In-memory representation of a JPEG image.</summary>
[FormatMimeType("image/jpeg", "image/jpg", "image/pjpeg")]
public readonly record struct JpegFile :
  IImageFormatReader<JpegFile>, IImageToRawImage<JpegFile>, IImageFromRawImage<JpegFile>, IImageFormatWriter<JpegFile>,
  IFormatChunkLayout<JpegFile>, IFormatChunkRewriter<JpegFile>, IFormatChunkPlanRewriter<JpegFile> {

  static string IImageFormatMetadata<JpegFile>.PrimaryExtension => ".jpg";
  static string[] IImageFormatMetadata<JpegFile>.FileExtensions => [".jpg", ".jpeg", ".jpe", ".jfif", ".jps", ".thm"];
  static JpegFile IImageFormatReader<JpegFile>.FromSpan(ReadOnlySpan<byte> data) => JpegReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<JpegFile>.Capabilities => FormatCapability.HasDedicatedOptimizer;

  static bool? IImageFormatMetadata<JpegFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF
      ? true : null;

  static byte[] IImageFormatWriter<JpegFile>.ToBytes(JpegFile file) => JpegWriter.ToBytes(file);

  static IEnumerable<ChunkSpan> IFormatChunkLayout<JpegFile>.EnumerateChunks(ReadOnlySpan<byte> data)
    => JpegChunkLayout.Enumerate(data);

  static byte[] IFormatChunkRewriter<JpegFile>.Rewrite(ReadOnlySpan<byte> data, IReadOnlyList<ChunkRewriteRule> rules)
    => JpegChunkLayout.Rewrite(data, rules);

  static ChunkRewriteResult IFormatChunkPlanRewriter<JpegFile>.ApplyPlan(ReadOnlySpan<byte> data, ChunkRewritePlan plan)
    => JpegChunkLayout.ApplyPlan(data, plan);
  public int Width { get; init; }
  public int Height { get; init; }
  public bool IsGrayscale { get; init; }
  public byte[]? RgbPixelData { get; init; }
  public byte[]? RawJpegBytes { get; init; }

  public static RawImage ToRawImage(JpegFile file) {
    if (file.RgbPixelData == null)
      throw new ArgumentException("RgbPixelData must not be null. Ensure the JPEG was decoded before conversion.", nameof(file));

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = file.IsGrayscale ? PixelFormat.Gray8 : PixelFormat.Rgb24,
      PixelData = file.RgbPixelData[..],
    };
  }

  public static JpegFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    image = image.EnsureAnyFormat(PixelFormat.Rgb24, PixelFormat.Gray8);
    var isGrayscale = image.Format == PixelFormat.Gray8;

    return new() {
      Width = image.Width,
      Height = image.Height,
      IsGrayscale = isGrayscale,
      RgbPixelData = image.PixelData[..],
    };
  }
}
