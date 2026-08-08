using System;
using FileFormat.Core;

namespace FileFormat.AndrewToolkit;

/// <summary>In-memory representation of an Andrew Toolkit (ATK) raster image.</summary>
public readonly record struct AndrewToolkitFile : IImageFormatReader<AndrewToolkitFile>, IImageToRawImage<AndrewToolkitFile>, IImageFromRawImage<AndrewToolkitFile>, IImageFormatWriter<AndrewToolkitFile> {

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

  /// <summary>Creates an Andrew Toolkit raster from a <see cref="RawImage"/> of any size.</summary>
  /// <remarks>
  /// The body is one byte of grey per pixel and the dimensions live in the text header, so nothing
  /// about the picture's size has to be negotiated — only its colour, which collapses to luminance.
  /// The header lines are the ones <see cref="AndrewToolkitWriter"/> emits, so the model here and
  /// the bytes it turns into stay in step.
  /// </remarks>
  public static AndrewToolkitFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var gray = image.EnsureFormat(PixelFormat.Gray8);

    return new() {
      Width = gray.Width,
      Height = gray.Height,
      RawData = gray.PixelData[..],
      HeaderLines = [$"width = {gray.Width}", $"height = {gray.Height}", string.Empty],
    };
  }

}
