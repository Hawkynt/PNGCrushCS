using System;
using FileFormat.Bpg.Codec;
using FileFormat.Core;

namespace FileFormat.Bpg;

/// <summary>In-memory representation of a BPG (Better Portable Graphics) image container.</summary>
[FormatMagicBytes([0x42, 0x50, 0x47, 0xFB])]
public sealed class BpgFile : IImageFormatReader<BpgFile>, IImageToRawImage<BpgFile>, IImageFormatWriter<BpgFile> {

  /// <summary>BPG magic bytes: "BPG" + 0xFB.</summary>
  internal static readonly byte[] Magic = [0x42, 0x50, 0x47, 0xFB];

  /// <summary>Minimum header size (magic + byte4 + byte5 + at least 2 ue7 bytes for width/height).</summary>
  internal const int MinHeaderSize = 6;

  static string IImageFormatMetadata<BpgFile>.PrimaryExtension => ".bpg";
  static string[] IImageFormatMetadata<BpgFile>.FileExtensions => [".bpg"];
  static BpgFile IImageFormatReader<BpgFile>.FromSpan(ReadOnlySpan<byte> data) => BpgReader.FromSpan(data);
  static byte[] IImageFormatWriter<BpgFile>.ToBytes(BpgFile file) => BpgWriter.ToBytes(file);

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

  /// <summary>Decoded pixel data cache (populated lazily by the HEVC decoder).</summary>
  internal byte[]? DecodedPixelData { get; set; }

  /// <summary>Whether the pixel data has been decoded from the HEVC bitstream.</summary>
  internal bool IsDecoded { get; set; }

  /// <summary>Converts a BPG file to a <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(BpgFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var decoded = _GetDecodedPixels(file);

    if (file.PixelFormat == BpgPixelFormat.Grayscale)
      return new() {
        Width = file.Width,
        Height = file.Height,
        Format = Core.PixelFormat.Gray8,
        PixelData = decoded,
      };

    if (file.HasAlpha)
      return new() {
        Width = file.Width,
        Height = file.Height,
        Format = Core.PixelFormat.Rgba32,
        PixelData = decoded,
      };

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = Core.PixelFormat.Rgb24,
      PixelData = decoded,
    };
  }

  /// <summary>Returns decoded pixel data, decoding the HEVC bitstream if necessary.</summary>
  private static byte[] _GetDecodedPixels(BpgFile file) {
    if (file.IsDecoded && file.DecodedPixelData != null)
      return file.DecodedPixelData[..];

    if (file.PixelData.Length == 0)
      throw new InvalidOperationException("BPG file contains no HEVC picture data.");

    var decoded = BpgHevcDecoder.Decode(file);
    file.DecodedPixelData = decoded;
    file.IsDecoded = true;
    return decoded[..];
  }
}