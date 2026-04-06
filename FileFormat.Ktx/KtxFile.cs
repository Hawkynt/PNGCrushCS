using System;
using System.Collections.Generic;
using FileFormat.Core;
using FileFormat.Core.BlockDecoders;

namespace FileFormat.Ktx;

/// <summary>In-memory representation of a KTX texture container.</summary>
[FormatMagicBytes([0xAB, 0x4B, 0x54, 0x58, 0x20, 0x31, 0x31, 0xBB])]
[FormatMagicBytes([0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB])]
public readonly record struct KtxFile : IImageFormatReader<KtxFile>, IImageToRawImage<KtxFile>, IImageFormatWriter<KtxFile> {

  static string IImageFormatMetadata<KtxFile>.PrimaryExtension => ".ktx";
  static string[] IImageFormatMetadata<KtxFile>.FileExtensions => [".ktx", ".ktx2"];
  static KtxFile IImageFormatReader<KtxFile>.FromSpan(ReadOnlySpan<byte> data) => KtxReader.FromSpan(data);
  static byte[] IImageFormatWriter<KtxFile>.ToBytes(KtxFile file) => KtxWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public int Depth { get; init; }
  public KtxVersion Version { get; init; }
  public int MipmapCount { get; init; }
  public int Faces { get; init; }
  public int ArrayElements { get; init; }
  public IReadOnlyList<KtxMipLevel> MipLevels { get; init; }
  public IReadOnlyList<KtxKeyValue>? KeyValues { get; init; }

  // KTX1 fields
  public int GlType { get; init; }
  public int GlTypeSize { get; init; }
  public int GlFormat { get; init; }
  public int GlInternalFormat { get; init; }
  public int GlBaseInternalFormat { get; init; }

  // KTX2 fields
  public int VkFormat { get; init; }
  public int TypeSize { get; init; }
  public int SupercompressionScheme { get; init; }

  /// <summary>Decodes the first mip level of a KTX file into a <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(KtxFile file) {

    if (file.MipLevels.Count == 0)
      throw new InvalidOperationException("KTX file contains no mip levels.");

    var mip = file.MipLevels[0];
    var width = mip.Width > 0 ? mip.Width : file.Width;
    var height = mip.Height > 0 ? mip.Height : file.Height;

    return file.Version == KtxVersion.Ktx2
      ? _DecodeKtx2(file, mip.Data, width, height)
      : _DecodeKtx1(file, mip.Data, width, height);
  }

  /// <summary>Creates an uncompressed KTX1 file from a <see cref="RawImage"/>. Only Rgba32 and Rgb24 are supported.</summary>
  public static KtxFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    int glFormat;
    int glInternalFormat;
    int glBaseInternalFormat;
    byte[] pixelData;

    switch (image.Format) {
      case PixelFormat.Rgba32:
        glFormat = 0x1908; // GL_RGBA
        glInternalFormat = 0x8058; // GL_RGBA8
        glBaseInternalFormat = 0x1908; // GL_RGBA
        pixelData = image.PixelData;
        break;
      case PixelFormat.Rgb24:
        glFormat = 0x1907; // GL_RGB
        glInternalFormat = 0x8051; // GL_RGB8
        glBaseInternalFormat = 0x1907; // GL_RGB
        pixelData = image.PixelData;
        break;
      default:
        throw new NotSupportedException($"Unsupported pixel format for KTX encoding: {image.Format}. Only Rgba32 and Rgb24 are supported.");
    }

    var mip = new KtxMipLevel {
      Width = image.Width,
      Height = image.Height,
      Data = pixelData,
    };

    return new KtxFile {
      Width = image.Width,
      Height = image.Height,
      Depth = 0,
      Version = KtxVersion.Ktx1,
      MipmapCount = 1,
      Faces = 1,
      ArrayElements = 0,
      MipLevels = [mip],
      GlType = 0x1401, // GL_UNSIGNED_BYTE
      GlTypeSize = 1,
      GlFormat = glFormat,
      GlInternalFormat = glInternalFormat,
      GlBaseInternalFormat = glBaseInternalFormat,
    };
  }

  private static RawImage _DecodeKtx1(KtxFile file, byte[] data, int width, int height) {
    // Uncompressed: GlType != 0 means uncompressed
    if (file.GlType != 0) {
      if (file.GlType == 0x1401) { // GL_UNSIGNED_BYTE
        if (file.GlFormat == 0x1908) // GL_RGBA
          return new RawImage {
            Width = width,
            Height = height,
            Format = PixelFormat.Rgba32,
            PixelData = data,
          };

        if (file.GlFormat == 0x1907) // GL_RGB
          return new RawImage {
            Width = width,
            Height = height,
            Format = PixelFormat.Rgb24,
            PixelData = data,
          };
      }

      throw new NotSupportedException($"Unsupported uncompressed KTX1 format: GlType=0x{file.GlType:X4}, GlFormat=0x{file.GlFormat:X4}");
    }

    // Compressed: dispatch on GlInternalFormat
    var output = new byte[width * height * 4];

    switch (file.GlInternalFormat) {
      // BC1 / DXT1
      case 0x83F0: // GL_COMPRESSED_RGB_S3TC_DXT1_EXT
      case 0x83F1: // GL_COMPRESSED_RGBA_S3TC_DXT1_EXT
      case 0x8C4C: // GL_COMPRESSED_SRGB_S3TC_DXT1_EXT
        Bc1Decoder.DecodeImage(data, width, height, output);
        break;

      // BC2 / DXT3
      case 0x83F2: // GL_COMPRESSED_RGBA_S3TC_DXT3_EXT
        Bc2Decoder.DecodeImage(data, width, height, output);
        break;

      // BC3 / DXT5
      case 0x83F3: // GL_COMPRESSED_RGBA_S3TC_DXT5_EXT
        Bc3Decoder.DecodeImage(data, width, height, output);
        break;

      // BC4
      case 0x8DBB: // GL_COMPRESSED_RED_RGTC1
        Bc4Decoder.DecodeImage(data, width, height, output);
        break;

      // BC5
      case 0x8DBD: // GL_COMPRESSED_RG_RGTC2
        Bc5Decoder.DecodeImage(data, width, height, output);
        break;

      // ETC1
      case 0x8D64: // GL_ETC1_RGB8_OES
        Etc1Decoder.DecodeImage(data, width, height, output);
        break;

      // ETC2 RGB
      case 0x9274: // GL_COMPRESSED_RGB8_ETC2
      case 0x9275: // GL_COMPRESSED_SRGB8_ETC2
        Etc2Decoder.DecodeEtc2RgbImage(data, width, height, output);
        break;

      // ETC2 punchthrough alpha
      case 0x9276: // GL_COMPRESSED_RGB8_PUNCHTHROUGH_ALPHA1_ETC2
        Etc2Decoder.DecodeEtc2RgbA1Image(data, width, height, output);
        break;

      // ETC2 RGBA
      case 0x9278: // GL_COMPRESSED_RGBA8_ETC2_EAC
        Etc2Decoder.DecodeEtc2RgbaImage(data, width, height, output);
        break;

      // EAC R11
      case 0x9270: // GL_COMPRESSED_R11_EAC
        Etc2Decoder.DecodeEacR11Image(data, width, height, output);
        break;

      // EAC RG11
      case 0x9272: // GL_COMPRESSED_RG11_EAC
        Etc2Decoder.DecodeEacRg11Image(data, width, height, output);
        break;

      // BC6H
      case 0x8E8E: // GL_COMPRESSED_RGB_BPTC_UNSIGNED_FLOAT
        Bc6HDecoder.DecodeImage(data, width, height, output, false);
        break;
      case 0x8E8F: // GL_COMPRESSED_RGB_BPTC_SIGNED_FLOAT
        Bc6HDecoder.DecodeImage(data, width, height, output, true);
        break;

      // BC7
      case 0x8E8C: // GL_COMPRESSED_RGBA_BPTC_UNORM
      case 0x8E8D: // GL_COMPRESSED_SRGB_ALPHA_BPTC_UNORM
        Bc7Decoder.DecodeImage(data, width, height, output);
        break;

      // ASTC (0x93B0..0x93BD covers 4x4 through 12x12)
      case >= 0x93B0 and <= 0x93BD:
        var (blockW, blockH) = _AstcBlockSizeFromGlFormat(file.GlInternalFormat);
        AstcBlockDecoder.DecodeImage(data, width, height, blockW, blockH, output);
        break;

      default:
        throw new NotSupportedException($"Unsupported KTX1 compressed format: GlInternalFormat=0x{file.GlInternalFormat:X4}");
    }

    return new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgba32,
      PixelData = output,
    };
  }

  private static RawImage _DecodeKtx2(KtxFile file, byte[] data, int width, int height) {
    // Uncompressed VkFormats
    switch (file.VkFormat) {
      case 37: // VK_FORMAT_R8G8B8A8_UNORM
        return new RawImage {
          Width = width,
          Height = height,
          Format = PixelFormat.Rgba32,
          PixelData = data,
        };
      case 23: // VK_FORMAT_R8G8B8_UNORM
        return new RawImage {
          Width = width,
          Height = height,
          Format = PixelFormat.Rgb24,
          PixelData = data,
        };
    }

    // Compressed VkFormats
    var output = new byte[width * height * 4];

    switch (file.VkFormat) {
      // BC1
      case 131: // VK_FORMAT_BC1_RGB_UNORM_BLOCK
      case 132: // VK_FORMAT_BC1_RGB_SRGB_BLOCK
      case 133: // VK_FORMAT_BC1_RGBA_UNORM_BLOCK
      case 134: // VK_FORMAT_BC1_RGBA_SRGB_BLOCK
        Bc1Decoder.DecodeImage(data, width, height, output);
        break;

      // BC2
      case 135: // VK_FORMAT_BC2_UNORM_BLOCK
      case 136: // VK_FORMAT_BC2_SRGB_BLOCK
        Bc2Decoder.DecodeImage(data, width, height, output);
        break;

      // BC3
      case 137: // VK_FORMAT_BC3_UNORM_BLOCK
      case 138: // VK_FORMAT_BC3_SRGB_BLOCK
        Bc3Decoder.DecodeImage(data, width, height, output);
        break;

      // BC4
      case 139: // VK_FORMAT_BC4_UNORM_BLOCK
      case 140: // VK_FORMAT_BC4_SNORM_BLOCK
        Bc4Decoder.DecodeImage(data, width, height, output);
        break;

      // BC5
      case 141: // VK_FORMAT_BC5_UNORM_BLOCK
      case 142: // VK_FORMAT_BC5_SNORM_BLOCK
        Bc5Decoder.DecodeImage(data, width, height, output);
        break;

      // ETC2 RGB
      case 149: // VK_FORMAT_ETC2_R8G8B8_UNORM_BLOCK
      case 150: // VK_FORMAT_ETC2_R8G8B8_SRGB_BLOCK
        Etc2Decoder.DecodeEtc2RgbImage(data, width, height, output);
        break;

      // ETC2 punchthrough alpha
      case 151: // VK_FORMAT_ETC2_R8G8B8A1_UNORM_BLOCK
      case 152: // VK_FORMAT_ETC2_R8G8B8A1_SRGB_BLOCK
        Etc2Decoder.DecodeEtc2RgbA1Image(data, width, height, output);
        break;

      // ETC2 RGBA
      case 153: // VK_FORMAT_ETC2_R8G8B8A8_UNORM_BLOCK
      case 154: // VK_FORMAT_ETC2_R8G8B8A8_SRGB_BLOCK
        Etc2Decoder.DecodeEtc2RgbaImage(data, width, height, output);
        break;

      // EAC R11
      case 155: // VK_FORMAT_EAC_R11_UNORM_BLOCK
      case 156: // VK_FORMAT_EAC_R11_SNORM_BLOCK
        Etc2Decoder.DecodeEacR11Image(data, width, height, output);
        break;

      // EAC RG11
      case 157: // VK_FORMAT_EAC_R11G11_UNORM_BLOCK
      case 158: // VK_FORMAT_EAC_R11G11_SNORM_BLOCK
        Etc2Decoder.DecodeEacRg11Image(data, width, height, output);
        break;

      // BC6H
      case 143: // VK_FORMAT_BC6H_UFLOAT_BLOCK
        Bc6HDecoder.DecodeImage(data, width, height, output, false);
        break;
      case 144: // VK_FORMAT_BC6H_SFLOAT_BLOCK
        Bc6HDecoder.DecodeImage(data, width, height, output, true);
        break;

      // BC7
      case 145: // VK_FORMAT_BC7_UNORM_BLOCK
      case 146: // VK_FORMAT_BC7_SRGB_BLOCK
        Bc7Decoder.DecodeImage(data, width, height, output);
        break;

      // ASTC (VkFormat 159..184 covers 4x4 through 12x12, UNORM and SRGB variants)
      case >= 159 and <= 184:
        var (blockW, blockH) = _AstcBlockSizeFromVkFormat(file.VkFormat);
        AstcBlockDecoder.DecodeImage(data, width, height, blockW, blockH, output);
        break;

      default:
        throw new NotSupportedException($"Unsupported KTX2 VkFormat: {file.VkFormat}");
    }

    return new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgba32,
      PixelData = output,
    };
  }

  /// <summary>Maps GL_COMPRESSED_RGBA_ASTC_*x*_KHR constants (0x93B0-0x93BD) to block dimensions.</summary>
  private static (int Width, int Height) _AstcBlockSizeFromGlFormat(int glInternalFormat) => glInternalFormat switch {
    0x93B0 => (4, 4),
    0x93B1 => (5, 4),
    0x93B2 => (5, 5),
    0x93B3 => (6, 5),
    0x93B4 => (6, 6),
    0x93B5 => (8, 5),
    0x93B6 => (8, 6),
    0x93B7 => (8, 8),
    0x93B8 => (10, 5),
    0x93B9 => (10, 6),
    0x93BA => (10, 8),
    0x93BB => (10, 10),
    0x93BC => (12, 10),
    0x93BD => (12, 12),
    _ => throw new NotSupportedException($"Unknown ASTC GL format: 0x{glInternalFormat:X4}")
  };

  /// <summary>Maps VK_FORMAT_ASTC_*x*_* constants (159-184) to block dimensions. Each block size has UNORM and SRGB variants.</summary>
  private static (int Width, int Height) _AstcBlockSizeFromVkFormat(int vkFormat) => vkFormat switch {
    159 or 160 => (4, 4),
    161 or 162 => (5, 4),
    163 or 164 => (5, 5),
    165 or 166 => (6, 5),
    167 or 168 => (6, 6),
    169 or 170 => (8, 5),
    171 or 172 => (8, 6),
    173 or 174 => (8, 8),
    175 or 176 => (10, 5),
    177 or 178 => (10, 6),
    179 or 180 => (10, 8),
    181 or 182 => (10, 10),
    183 or 184 => (12, 10),
    _ => throw new NotSupportedException($"Unknown ASTC VkFormat: {vkFormat}")
  };
}
