using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Core.BlockDecoders;
using FileFormat.Pkm;

namespace FileFormat.Pvr;

/// <summary>In-memory representation of a PVR (PowerVR Texture v3) file.</summary>
[FormatMagicBytes([0x50, 0x56, 0x52, 0x03])]
public readonly record struct PvrFile : IImageFormatReader<PvrFile>, IImageToRawImage<PvrFile>, IImageFromRawImage<PvrFile>, IImageFormatWriter<PvrFile> {

  static string IImageFormatMetadata<PvrFile>.PrimaryExtension => ".pvr";
  static string[] IImageFormatMetadata<PvrFile>.FileExtensions => [".pvr"];
  static PvrFile IImageFormatReader<PvrFile>.FromSpan(ReadOnlySpan<byte> data) => PvrReader.FromSpan(data);
  static byte[] IImageFormatWriter<PvrFile>.ToBytes(PvrFile file) => PvrWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public int Depth { get; init; }
  public PvrPixelFormat PixelFormat { get; init; }
  public PvrColorSpace ColorSpace { get; init; }
  public uint ChannelType { get; init; }
  public uint Flags { get; init; }
  public int Surfaces { get; init; }
  public int Faces { get; init; }
  public int MipmapCount { get; init; }
  public int MetadataSize { get; init; }
  public byte[] Metadata { get; init; }
  public byte[] CompressedData { get; init; }

  /// <summary>Decodes the PVR compressed data into a <see cref="RawImage"/> with RGBA32 pixels.</summary>
  public static RawImage ToRawImage(PvrFile file) {

    var width = file.Width;
    var height = file.Height;
    var output = new byte[width * height * 4];

    switch (file.PixelFormat) {
      case Pvr.PvrPixelFormat.ETC1:
        Etc1Decoder.DecodeImage(file.CompressedData, width, height, output);
        break;
      case Pvr.PvrPixelFormat.ETC2_RGB:
        Etc2Decoder.DecodeEtc2RgbImage(file.CompressedData, width, height, output);
        break;
      case Pvr.PvrPixelFormat.ETC2_RGBA:
        Etc2Decoder.DecodeEtc2RgbaImage(file.CompressedData, width, height, output);
        break;
      case Pvr.PvrPixelFormat.ASTC_4x4:
        AstcBlockDecoder.DecodeImage(file.CompressedData, width, height, 4, 4, output);
        break;
      case Pvr.PvrPixelFormat.PVRTC_2BPP_RGB:
      case Pvr.PvrPixelFormat.PVRTC_2BPP_RGBA:
        PvrtcDecoder.Decode2Bpp(file.CompressedData, width, height, output);
        break;
      case Pvr.PvrPixelFormat.PVRTC_4BPP_RGB:
      case Pvr.PvrPixelFormat.PVRTC_4BPP_RGBA:
        PvrtcDecoder.Decode4Bpp(file.CompressedData, width, height, output);
        break;
      default:
        throw new NotSupportedException($"Unsupported PVR pixel format: {file.PixelFormat}");
    }

    return new RawImage {
      Width = width,
      Height = height,
      Format = Core.PixelFormat.Rgba32,
      PixelData = output,
    };
  }

  /// <summary>Creates a PVR v3 file with ETC1 encoding from a <see cref="RawImage"/>. Uses naive individual-mode ETC1 blocks.</summary>
  public static PvrFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    // Reuse PKM's ETC1 encoding
    var pkm = Pkm.PkmFile.FromRawImage(image);

    return new() {
      Width = pkm.Width,
      Height = pkm.Height,
      Depth = 1,
      PixelFormat = PvrPixelFormat.ETC1,
      ColorSpace = PvrColorSpace.Linear,
      ChannelType = 0,
      Flags = 0,
      Surfaces = 1,
      Faces = 1,
      MipmapCount = 1,
      MetadataSize = 0,
      Metadata = [],
      CompressedData = pkm.CompressedData,
    };
  }
}
