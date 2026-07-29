using System;
using FileFormat.Core;

namespace FileFormat.Spiff;

/// <summary>
/// ITU-T T.84 SPIFF (Still Picture Interchange File Format) container. Starts with the JPEG SOI
/// marker (FFD8), followed by an APP8 segment containing "SPIFF\0" + a 32-byte SPIFF directory
/// header (version, profile, num-components, height, width, colour-space, bps, compression-type,
/// resolution-units, vertical-res, horizontal-res). Embedded compressed payload (typically a JPEG
/// frame) is exposed as <see cref="CompressedPayload"/>; metadata interpretation is the consumer's
/// responsibility.
/// </summary>
[FormatMagicBytes([0xFF, 0xD8, 0xFF, 0xE8])]
public readonly record struct SpiffFile : IImageFormatReader<SpiffFile>, IImageFormatWriter<SpiffFile>, IImageToRawImage<SpiffFile>, IImageFromRawImage<SpiffFile> {

  static string IImageFormatMetadata<SpiffFile>.PrimaryExtension => ".spf";
  static string[] IImageFormatMetadata<SpiffFile>.FileExtensions => [".spf", ".spiff"];
  static SpiffFile IImageFormatReader<SpiffFile>.FromSpan(ReadOnlySpan<byte> data) => SpiffReader.FromSpan(data);
  static byte[] IImageFormatWriter<SpiffFile>.ToBytes(SpiffFile file) => SpiffWriter.ToBytes(file);

  public byte ProfileId { get; init; }
  public byte ComponentCount { get; init; }
  public int Width { get; init; }
  public int Height { get; init; }
  public byte ColorSpace { get; init; }     // T.84 ColorSpace code: 0=bilevel, 1=YCbCr, 2=NoSpec, 3=YCbCr-K, 4=CMYK, 5=CIELab, 8=Gray, 10=RGB ...
  public byte BitsPerSample { get; init; }
  public byte CompressionType { get; init; } // 0=uncompressed, 1=ModifiedHuffman, 5=JPEG, 8=ITU-T T.81 baseline-extension ...
  public byte[] CompressedPayload { get; init; }

  public static RawImage ToRawImage(SpiffFile file) {
    ArgumentNullException.ThrowIfNull(file.CompressedPayload);
    // SPIFF wraps a compressed payload — decoding it (e.g. via the embedded JPEG codec) is out
    // of scope here. Expose the payload as a 1xN grayscale strip so the platform-independent
    // surface remains non-empty; downstream consumers should route the bytes to FileFormat.Jpeg.
    return new() {
      Width = file.CompressedPayload.Length,
      Height = 1,
      Format = PixelFormat.Gray8,
      PixelData = (byte[])file.CompressedPayload.Clone(),
      Palette = null,
      PaletteCount = 0,
    };
  }

  public static SpiffFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    return new() {
      ProfileId = 0,
      ComponentCount = (byte)(image.Format == PixelFormat.Gray8 ? 1 : 3),
      Width = image.Width,
      Height = image.Height,
      ColorSpace = image.Format == PixelFormat.Gray8 ? (byte)8 : (byte)10,
      BitsPerSample = 8,
      CompressionType = 0, // uncompressed pass-through
      CompressedPayload = (byte[])image.PixelData.Clone(),
    };
  }
}
