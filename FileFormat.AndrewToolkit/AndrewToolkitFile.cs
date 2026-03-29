using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.AndrewToolkit;

/// <summary>In-memory representation of an Andrew Toolkit (ATK) raster image.</summary>
public sealed class AndrewToolkitFile : IImageFileFormat<AndrewToolkitFile> {

  static string IImageFileFormat<AndrewToolkitFile>.PrimaryExtension => ".atk";
  static string[] IImageFileFormat<AndrewToolkitFile>.FileExtensions => [".atk"];
  static AndrewToolkitFile IImageFileFormat<AndrewToolkitFile>.FromFile(FileInfo file) => AndrewToolkitReader.FromFile(file);
  static AndrewToolkitFile IImageFileFormat<AndrewToolkitFile>.FromBytes(byte[] data) => AndrewToolkitReader.FromBytes(data);
  static AndrewToolkitFile IImageFileFormat<AndrewToolkitFile>.FromStream(Stream stream) => AndrewToolkitReader.FromStream(stream);
  static RawImage IImageFileFormat<AndrewToolkitFile>.ToRawImage(AndrewToolkitFile file) => ToRawImage(file);
  static byte[] IImageFileFormat<AndrewToolkitFile>.ToBytes(AndrewToolkitFile file) => AndrewToolkitWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>Raw pixel data stored after the text header.</summary>
  public byte[] RawData { get; init; } = [];

  /// <summary>The original text header lines.</summary>
  public string[] HeaderLines { get; init; } = [];

  public static RawImage ToRawImage(AndrewToolkitFile file) {
    ArgumentNullException.ThrowIfNull(file);
    var pixelCount = file.Width * file.Height;
    var rgb = new byte[pixelCount * 3];
    for (var i = 0; i < pixelCount; ++i) {
      var value = i < file.RawData.Length ? file.RawData[i] : (byte)0;
      rgb[i * 3] = value;
      rgb[i * 3 + 1] = value;
      rgb[i * 3 + 2] = value;
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  public static AndrewToolkitFile FromRawImage(RawImage image) => throw new NotSupportedException("ATK writing from raw image is not supported.");
}
