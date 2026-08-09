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
  /// <summary>Every name a JPEG is saved under, including the two shortest.</summary>
  /// <summary><c>.fsy</c> is what Photodex CompuPic wrote its JPEGs under; the bytes are a plain JFIF.</summary>
  /// <summary><c>.mph</c> is MonkeyPhoto's name for the same thing, and is likewise a plain JFIF.</summary>
  /// <summary>
  /// <c>.ncy</c> is a FlashCam frame. XnView reads it with the same function it reads a JPEG with —
  /// one entry in its format table names the other's loader — and its converter, told to read a
  /// plain JFIF as a FlashCam frame, returns the picture. So the extension is the camera's and the
  /// bytes are a JPEG.
  /// </summary>
  static string[] IImageFormatMetadata<JpegFile>.FileExtensions => [".jpg", ".jpeg", ".jpe", ".jfif", ".jps", ".thm", ".j", ".jif", ".fsy", ".mph", ".ncy"];
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

  /// <summary>Explicit metadata for the writer to embed. <c>null</c> means "use whatever
  /// <see cref="RawJpegBytes"/> already carries, unchanged" — see <see cref="JpegWriter"/> remarks for
  /// why this isn't auto-populated by <see cref="JpegReader"/>.</summary>
  public ImageMetadata? Metadata { get; init; }

  public static RawImage ToRawImage(JpegFile file) {
    if (file.RgbPixelData == null)
      throw new ArgumentException("RgbPixelData must not be null. Ensure the JPEG was decoded before conversion.", nameof(file));

    // Metadata is computed on demand from RawJpegBytes rather than eagerly stashed on JpegFile by the
    // reader: that keeps a plain decode -> ToBytes round trip byte-identical (see JpegWriter remarks)
    // while still exposing everything the source file carried to whoever reads RawImage.Metadata.
    var metadata = file.Metadata ?? (file.RawJpegBytes != null ? JpegMetadataCodec.Read(file.RawJpegBytes) : null);

    // The decoder spreads a grey picture to three equal channels, so the data is RGB whatever the
    // picture was. Declaring it Gray8 while handing over triplets made every grey JPEG come out
    // stretched three times across and cut to its left third — the values were right, so nothing
    // that looked only at pixel zero could see it.
    if (!file.IsGrayscale)
      return new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Rgb24,
        PixelData = file.RgbPixelData[..],
        Metadata = metadata,
      };

    var gray = new byte[file.Width * file.Height];
    for (var i = 0; i < gray.Length && i * 3 < file.RgbPixelData.Length; ++i)
      gray[i] = file.RgbPixelData[i * 3];

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Gray8,
      PixelData = gray,
      Metadata = metadata,
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
      Metadata = image.Metadata,
    };
  }
}
