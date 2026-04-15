using System;
using System.IO;

namespace FileFormat.AtariPaintworks;

/// <summary>Reads Atari ST Paintworks/GFA/DeskPic files from bytes, streams, or file paths.</summary>
public static class AtariPaintworksReader {

  /// <summary>Standard Atari ST screen pixel data size: 32000 bytes.</summary>
  private const int _PIXEL_DATA_SIZE = 32000;

  /// <summary>Expected file size for a full screen file: 32-byte palette + 32000 bytes pixel data.</summary>
  private const int _EXPECTED_FILE_SIZE = AtariPaintworksHeader.StructSize + _PIXEL_DATA_SIZE;

  public static AtariPaintworksFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Atari Paintworks file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariPaintworksFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static AtariPaintworksFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < AtariPaintworksHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid Atari Paintworks file.");

    if (data.Length < _EXPECTED_FILE_SIZE)
      throw new InvalidDataException($"Data too small: expected at least {_EXPECTED_FILE_SIZE} bytes for palette + screen data, got {data.Length}.");

    var span = data;
    var header = AtariPaintworksHeader.ReadFrom(span);

    // Determine resolution from file size context; default to low res for standard 32032-byte files
    var resolution = _DetectResolution(data.Length);
    var (width, height) = _GetDimensions(resolution);

    var pixelData = new byte[_PIXEL_DATA_SIZE];
    data.Slice(AtariPaintworksHeader.StructSize, _PIXEL_DATA_SIZE).CopyTo(pixelData.AsSpan(0));

    return new AtariPaintworksFile {
      Width = width,
      Height = height,
      Resolution = resolution,
      Palette = header.Palette,
      PixelData = pixelData
    };
    }

  public static AtariPaintworksFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < AtariPaintworksHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid Atari Paintworks file.");

    if (data.Length < _EXPECTED_FILE_SIZE)
      throw new InvalidDataException($"Data too small: expected at least {_EXPECTED_FILE_SIZE} bytes for palette + screen data, got {data.Length}.");

    var span = data.AsSpan();
    var header = AtariPaintworksHeader.ReadFrom(span);

    // Determine resolution from file size context; default to low res for standard 32032-byte files
    var resolution = _DetectResolution(data.Length);
    var (width, height) = _GetDimensions(resolution);

    var pixelData = new byte[_PIXEL_DATA_SIZE];
    data.AsSpan(AtariPaintworksHeader.StructSize, _PIXEL_DATA_SIZE).CopyTo(pixelData.AsSpan(0));

    return new AtariPaintworksFile {
      Width = width,
      Height = height,
      Resolution = resolution,
      Palette = header.Palette,
      PixelData = pixelData
    };
  }

  /// <summary>
  ///   Detects resolution from the file size. All variants store 32 bytes palette + 32000 bytes pixel data = 32032 bytes.
  ///   Without additional metadata, resolution defaults to low (most common for Paintworks).
  ///   Subclasses or callers may override based on the file extension.
  /// </summary>
  private static AtariPaintworksResolution _DetectResolution(int fileSize) =>
    // Standard files are exactly 32032 bytes; default to low resolution
    AtariPaintworksResolution.Low;

  private static (int Width, int Height) _GetDimensions(AtariPaintworksResolution resolution) => resolution switch {
    AtariPaintworksResolution.Low => (320, 200),
    AtariPaintworksResolution.Medium => (640, 200),
    AtariPaintworksResolution.High => (640, 400),
    _ => throw new InvalidDataException($"Unknown resolution: {resolution}.")
  };

  /// <summary>
  ///   Reads a file with an explicit resolution hint (useful when resolution is inferred from file extension).
  /// </summary>
  public static AtariPaintworksFile FromBytes(byte[] data, AtariPaintworksResolution resolution) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < AtariPaintworksHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid Atari Paintworks file.");

    if (data.Length < _EXPECTED_FILE_SIZE)
      throw new InvalidDataException($"Data too small: expected at least {_EXPECTED_FILE_SIZE} bytes for palette + screen data, got {data.Length}.");

    var span = data.AsSpan();
    var header = AtariPaintworksHeader.ReadFrom(span);
    var (width, height) = _GetDimensions(resolution);

    var pixelData = new byte[_PIXEL_DATA_SIZE];
    data.AsSpan(AtariPaintworksHeader.StructSize, _PIXEL_DATA_SIZE).CopyTo(pixelData.AsSpan(0));

    return new AtariPaintworksFile {
      Width = width,
      Height = height,
      Resolution = resolution,
      Palette = header.Palette,
      PixelData = pixelData
    };
  }
}
