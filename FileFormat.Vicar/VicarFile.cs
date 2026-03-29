using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Vicar;

/// <summary>In-memory representation of a NASA JPL VICAR image.</summary>
public sealed class VicarFile : IImageFileFormat<VicarFile> {

  static string IImageFileFormat<VicarFile>.PrimaryExtension => ".vic";
  static string[] IImageFileFormat<VicarFile>.FileExtensions => [".vic", ".vicar"];
  static VicarFile IImageFileFormat<VicarFile>.FromFile(FileInfo file) => VicarReader.FromFile(file);
  static VicarFile IImageFileFormat<VicarFile>.FromBytes(byte[] data) => VicarReader.FromBytes(data);
  static VicarFile IImageFileFormat<VicarFile>.FromStream(Stream stream) => VicarReader.FromStream(stream);
  static byte[] IImageFileFormat<VicarFile>.ToBytes(VicarFile file) => VicarWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public int Bands { get; init; } = 1;
  public VicarPixelType PixelType { get; init; }
  public VicarOrganization Organization { get; init; }
  public string IntFormat { get; init; } = "LOW";
  public string RealFormat { get; init; } = "IEEE";

  /// <summary>Raw pixel data bytes.</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>All keyword=value labels from the VICAR header.</summary>
  public Dictionary<string, string> Labels { get; init; } = new();

  public static RawImage ToRawImage(VicarFile file) {
    ArgumentNullException.ThrowIfNull(file);

    if (file.PixelType != VicarPixelType.Byte)
      throw new ArgumentException($"Only Byte pixel type is supported for conversion, got {file.PixelType}.", nameof(file));

    if (file.Bands == 1)
      return new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Gray8,
        PixelData = file.PixelData[..],
      };

    if (file.Bands == 3) {
      var pixelCount = file.Width * file.Height;
      byte[] result;

      switch (file.Organization) {
        case VicarOrganization.Bip:
          result = file.PixelData[..];
          break;
        case VicarOrganization.Bsq:
          result = new byte[pixelCount * 3];
          for (var i = 0; i < pixelCount; ++i) {
            result[i * 3] = file.PixelData[i];
            result[i * 3 + 1] = file.PixelData[pixelCount + i];
            result[i * 3 + 2] = file.PixelData[pixelCount * 2 + i];
          }

          break;
        case VicarOrganization.Bil:
          result = new byte[pixelCount * 3];
          var w = file.Width;
          for (var y = 0; y < file.Height; ++y) {
            var lineOffset = y * w * 3;
            for (var x = 0; x < w; ++x) {
              result[(y * w + x) * 3] = file.PixelData[lineOffset + x];
              result[(y * w + x) * 3 + 1] = file.PixelData[lineOffset + w + x];
              result[(y * w + x) * 3 + 2] = file.PixelData[lineOffset + w * 2 + x];
            }
          }

          break;
        default:
          throw new ArgumentException($"Unsupported organization: {file.Organization}", nameof(file));
      }

      return new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Rgb24,
        PixelData = result,
      };
    }

    throw new ArgumentException($"Unsupported band count for conversion: {file.Bands}", nameof(file));
  }

  public static VicarFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    switch (image.Format) {
      case PixelFormat.Gray8:
        return new() {
          Width = image.Width,
          Height = image.Height,
          Bands = 1,
          PixelType = VicarPixelType.Byte,
          Organization = VicarOrganization.Bsq,
          PixelData = image.PixelData[..],
        };
      case PixelFormat.Rgb24:
        return new() {
          Width = image.Width,
          Height = image.Height,
          Bands = 3,
          PixelType = VicarPixelType.Byte,
          Organization = VicarOrganization.Bip,
          PixelData = image.PixelData[..],
        };
      default:
        throw new ArgumentException($"Unsupported pixel format for VICAR: {image.Format}", nameof(image));
    }
  }
}
