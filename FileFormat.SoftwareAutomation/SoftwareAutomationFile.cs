using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.SoftwareAutomation;

/// <summary>In-memory representation of a Software Automation Graphics (.sag) file.</summary>
public sealed class SoftwareAutomationFile : IImageFileFormat<SoftwareAutomationFile> {

  /// <summary>Exact file size: 40 bytes/row x 192 rows.</summary>
  public const int ExpectedFileSize = 7680;

  /// <summary>Width in pixels.</summary>
  internal const int PixelWidth = 320;

  /// <summary>Height in pixels.</summary>
  internal const int PixelHeight = 192;

  /// <summary>Bytes per row in the raw screen dump.</summary>
  internal const int BytesPerRow = 40;

  static string IImageFileFormat<SoftwareAutomationFile>.PrimaryExtension => ".sag";
  static string[] IImageFileFormat<SoftwareAutomationFile>.FileExtensions => [".sag", ".swa"];
  static FormatCapability IImageFileFormat<SoftwareAutomationFile>.Capabilities => FormatCapability.IndexedOnly;
  static SoftwareAutomationFile IImageFileFormat<SoftwareAutomationFile>.FromFile(FileInfo file) => SoftwareAutomationReader.FromFile(file);
  static SoftwareAutomationFile IImageFileFormat<SoftwareAutomationFile>.FromBytes(byte[] data) => SoftwareAutomationReader.FromBytes(data);
  static SoftwareAutomationFile IImageFileFormat<SoftwareAutomationFile>.FromStream(Stream stream) => SoftwareAutomationReader.FromStream(stream);
  static byte[] IImageFileFormat<SoftwareAutomationFile>.ToBytes(SoftwareAutomationFile file) => SoftwareAutomationWriter.ToBytes(file);

  /// <summary>Always 320.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 192.</summary>
  public int Height => PixelHeight;

  /// <summary>Raw 1bpp MSB-first screen data (7680 bytes).</summary>
  public byte[] PixelData { get; init; } = [];

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  /// <summary>Converts this Software Automation Graphics file to an Indexed1 raw image (320x192, B&amp;W palette).</summary>
  public static RawImage ToRawImage(SoftwareAutomationFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelData = new byte[ExpectedFileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, ExpectedFileSize)).CopyTo(pixelData);

    return new() {
      Width = PixelWidth,
      Height = PixelHeight,
      Format = PixelFormat.Indexed1,
      PixelData = pixelData,
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  /// <summary>Creates a Software Automation Graphics file from an Indexed1 raw image (320x192).</summary>
  public static SoftwareAutomationFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException($"Expected {PixelFormat.Indexed1} but got {image.Format}.", nameof(image));
    if (image.Width != PixelWidth || image.Height != PixelHeight)
      throw new ArgumentException($"Expected {PixelWidth}x{PixelHeight} but got {image.Width}x{image.Height}.", nameof(image));

    var pixelData = new byte[ExpectedFileSize];
    image.PixelData.AsSpan(0, Math.Min(image.PixelData.Length, ExpectedFileSize)).CopyTo(pixelData);

    return new() { PixelData = pixelData };
  }
}
