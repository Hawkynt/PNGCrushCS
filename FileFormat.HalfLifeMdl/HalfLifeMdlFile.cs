using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.HalfLifeMdl;

/// <summary>In-memory representation of a Half-Life Model texture image.</summary>
public sealed class HalfLifeMdlFile : IImageFileFormat<HalfLifeMdlFile> {

  internal const int HeaderSize = 16;


  static string IImageFileFormat<HalfLifeMdlFile>.PrimaryExtension => ".mdltex";
  static string[] IImageFileFormat<HalfLifeMdlFile>.FileExtensions => [".mdltex"];
  static FormatCapability IImageFileFormat<HalfLifeMdlFile>.Capabilities => FormatCapability.IndexedOnly;
  static HalfLifeMdlFile IImageFileFormat<HalfLifeMdlFile>.FromFile(FileInfo file) => HalfLifeMdlReader.FromFile(file);
  static HalfLifeMdlFile IImageFileFormat<HalfLifeMdlFile>.FromBytes(byte[] data) => HalfLifeMdlReader.FromBytes(data);
  static HalfLifeMdlFile IImageFileFormat<HalfLifeMdlFile>.FromStream(Stream stream) => HalfLifeMdlReader.FromStream(stream);
  static byte[] IImageFileFormat<HalfLifeMdlFile>.ToBytes(HalfLifeMdlFile file) => HalfLifeMdlWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(HalfLifeMdlFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
    };
  }

  public static HalfLifeMdlFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException("RawImage must use PixelFormat.Indexed8.", nameof(image));
    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
