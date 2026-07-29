using System;
using FileFormat.Core;

namespace FileFormat.AndrewToolkit;

/// <summary>In-memory representation of an Andrew Toolkit (ATK) raster image.</summary>
public readonly record struct AndrewToolkitFile : IImageFormatReader<AndrewToolkitFile>, IImageToRawImage<AndrewToolkitFile>, IImageFormatWriter<AndrewToolkitFile> {

  static string IImageFormatMetadata<AndrewToolkitFile>.PrimaryExtension => ".atk";
  static string[] IImageFormatMetadata<AndrewToolkitFile>.FileExtensions => [".atk"];
  static AndrewToolkitFile IImageFormatReader<AndrewToolkitFile>.FromSpan(ReadOnlySpan<byte> data) => AndrewToolkitReader.FromSpan(data);
  static byte[] IImageFormatWriter<AndrewToolkitFile>.ToBytes(AndrewToolkitFile file) => AndrewToolkitWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>Raw pixel data stored after the text header.</summary>
  public byte[] RawData { get; init; }

  /// <summary>The original text header lines.</summary>
  public string[] HeaderLines { get; init; }

  public static RawImage ToRawImage(AndrewToolkitFile file) {
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

}
