using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.CiscoIp;

/// <summary>In-memory representation of a Cisco IP Phone image image.</summary>
public sealed class CiscoIpFile : IImageFileFormat<CiscoIpFile> {

  internal const int HeaderSize = 80;


  static string IImageFileFormat<CiscoIpFile>.PrimaryExtension => ".cip";
  static string[] IImageFileFormat<CiscoIpFile>.FileExtensions => [".cip"];
  static CiscoIpFile IImageFileFormat<CiscoIpFile>.FromFile(FileInfo file) => CiscoIpReader.FromFile(file);
  static CiscoIpFile IImageFileFormat<CiscoIpFile>.FromBytes(byte[] data) => CiscoIpReader.FromBytes(data);
  static CiscoIpFile IImageFileFormat<CiscoIpFile>.FromStream(Stream stream) => CiscoIpReader.FromStream(stream);
  static byte[] IImageFileFormat<CiscoIpFile>.ToBytes(CiscoIpFile file) => CiscoIpWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(CiscoIpFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static CiscoIpFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgb24)
      throw new ArgumentException("RawImage must use PixelFormat.Rgb24.", nameof(image));
    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
