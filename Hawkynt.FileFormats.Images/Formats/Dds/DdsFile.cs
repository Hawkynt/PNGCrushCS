using System;
using System.Collections.Generic;
using FileFormat.Core;
using FileFormat.Core.BlockDecoders;

namespace FileFormat.Dds;

/// <summary>In-memory representation of a DDS (DirectDraw Surface) file.</summary>
[FormatMagicBytes([0x44, 0x44, 0x53, 0x20])]
[FormatMimeType("image/vnd.ms-dds", "image/x-dds")]
public readonly record struct DdsFile : IImageFormatReader<DdsFile>, IImageToRawImage<DdsFile>, IImageFromRawImage<DdsFile>, IImageFormatWriter<DdsFile> {

  static string IImageFormatMetadata<DdsFile>.PrimaryExtension => ".dds";
  static string[] IImageFormatMetadata<DdsFile>.FileExtensions => [".dds"];
  static DdsFile IImageFormatReader<DdsFile>.FromSpan(ReadOnlySpan<byte> data) => DdsReader.FromSpan(data);
  static byte[] IImageFormatWriter<DdsFile>.ToBytes(DdsFile file) => DdsWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public int Depth { get; init; }
  public int MipMapCount { get; init; }
  public DdsFormat Format { get; init; }
  public bool HasDx10Header { get; init; }
  public IReadOnlyList<DdsSurface> Surfaces { get; init; }

  /// <summary>
  /// Which byte of an uncompressed pixel is which colour. Meaningless for the block-compressed
  /// formats, whose layout is fixed by the compression rather than stated in the header.
  /// </summary>
  public DdsChannelOrder ChannelOrder { get; init; }

  public static RawImage ToRawImage(DdsFile file) {
    if (file.Surfaces.Count == 0)
      throw new InvalidOperationException("DDS file contains no surfaces.");

    var surface = file.Surfaces[0];
    var width = surface.Width > 0 ? surface.Width : file.Width;
    var height = surface.Height > 0 ? surface.Height : file.Height;
    var data = surface.Data;

    return file.Format switch {
      DdsFormat.Dxt1 => _DecodeBc(data, width, height, Bc1Decoder.DecodeImage),
      DdsFormat.Dxt3 => _DecodeBc(data, width, height, Bc2Decoder.DecodeImage),
      DdsFormat.Dxt5 => _DecodeBc(data, width, height, Bc3Decoder.DecodeImage),
      DdsFormat.Bc4 => _DecodeBc(data, width, height, Bc4Decoder.DecodeImage),
      DdsFormat.Bc5 => _DecodeBc(data, width, height, Bc5Decoder.DecodeImage),
      DdsFormat.Bc6HUnsigned => _DecodeBc(data, width, height, (d, w, h, o) => Bc6HDecoder.DecodeImage(d, w, h, o, false)),
      DdsFormat.Bc6HSigned => _DecodeBc(data, width, height, (d, w, h, o) => Bc6HDecoder.DecodeImage(d, w, h, o, true)),
      DdsFormat.Bc7 => _DecodeBc(data, width, height, Bc7Decoder.DecodeImage),
      DdsFormat.Rgb => _DecodeUncompressed(data, width, height, file.ChannelOrder.ToPixelFormat(3), 3),
      DdsFormat.Rgba => _DecodeUncompressed(data, width, height, file.ChannelOrder.ToPixelFormat(4), 4),
      DdsFormat.Single8 => _DecodeUncompressed(data, width, height, PixelFormat.Gray8, 1),
      _ => throw new NotSupportedException($"DDS format {file.Format} is not supported for conversion to RawImage.")
    };
  }

  /// <summary>
  /// Writes a picture as one uncompressed surface, blue channel first.
  /// </summary>
  /// <remarks>
  /// The bytes are laid out to match the masks the header states, which is A8R8G8B8 — the
  /// arrangement everything writes and everything expects. They used to be written red first under
  /// those same masks, so a file this project produced was read back by ImageMagick and XnView with
  /// red and blue exchanged; only this project's own reader, wrong in the same direction, agreed
  /// with it.
  /// <para/>
  /// Any picture is accepted rather than the five layouts that happened to be listed. A writer
  /// reachable from the registry is handed whatever a caller has, and refusing an indexed or
  /// greyscale picture made the format unwritable for most of what it might be given; converting is
  /// what every other writer here does with a picture that does not already match.
  /// </remarks>
  public static DdsFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var hasAlpha = image.HasAlpha;
    var data = hasAlpha ? image.ToBgra32() : _ToBgr24(image);

    return new DdsFile {
      Width = image.Width,
      Height = image.Height,
      Depth = 1,
      MipMapCount = 1,
      Format = hasAlpha ? DdsFormat.Rgba : DdsFormat.Rgb,
      ChannelOrder = hasAlpha ? DdsChannelOrder.Bgra : DdsChannelOrder.Bgr,
      Surfaces = [new DdsSurface { Width = image.Width, Height = image.Height, MipLevel = 0, Data = data }]
    };
  }

  /// <summary>The picture as three bytes a pixel, blue first.</summary>
  private static byte[] _ToBgr24(RawImage image) {
    if (image.Format == PixelFormat.Bgr24)
      return image.PixelData;

    var rgb = image.ToRgb24();
    var bgr = new byte[rgb.Length];
    for (var i = 0; i + 2 < rgb.Length; i += 3) {
      bgr[i] = rgb[i + 2];
      bgr[i + 1] = rgb[i + 1];
      bgr[i + 2] = rgb[i];
    }

    return bgr;
  }

  private delegate void _BcDecoder(ReadOnlySpan<byte> data, int width, int height, Span<byte> output);

  private static RawImage _DecodeBc(byte[] data, int width, int height, _BcDecoder decoder) {
    var pixels = new byte[width * height * 4];
    decoder(data, width, height, pixels);
    return new RawImage { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = pixels };
  }

  private static RawImage _DecodeUncompressed(byte[] data, int width, int height, PixelFormat format, int bytesPerPixel) {
    var expected = width * height * bytesPerPixel;
    var pixels = new byte[expected];
    data.AsSpan(0, Math.Min(data.Length, expected)).CopyTo(pixels);
    return new RawImage { Width = width, Height = height, Format = format, PixelData = pixels };
  }

}
