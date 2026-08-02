using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.AtariPaintworks;

/// <summary>Reads Atari ST Paintworks/GFA/DeskPic files from bytes, streams, or file paths.</summary>
public static class AtariPaintworksReader {

  /// <summary>Standard Atari ST screen pixel data size: 32000 bytes.</summary>
  private const int _PIXEL_DATA_SIZE = 32000;

  /// <summary>Expected file size for a full screen file: 32-byte palette + 32000 bytes pixel data.</summary>
  private const int _EXPECTED_FILE_SIZE = AtariPaintworksFile.FileSize;

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

  /// <summary>
  /// Says why a file shorter than a whole screen was refused.
  /// </summary>
  /// <remarks>
  /// Only the uncompressed form is read here. The compressed one carries the same signature and the
  /// same palette, and is simply shorter — so calling it "too small" pointed at the wrong thing
  /// entirely, as though the file were damaged rather than in a form this does not decode.
  /// </remarks>
  private static string _ShortFileReason(ReadOnlySpan<byte> data)
    => data.Length > AtariPaintworksFile.SignatureOffset + AtariPaintworksFile.Signature.Length
       && data.Slice(AtariPaintworksFile.SignatureOffset, AtariPaintworksFile.Signature.Length).SequenceEqual(AtariPaintworksFile.Signature)
      ? $"This is a compressed Atari Paintworks picture ({data.Length} bytes against the {_EXPECTED_FILE_SIZE} an uncompressed one takes), which is not decoded here."
      : $"Data too small: expected at least {_EXPECTED_FILE_SIZE} bytes for palette + screen data, got {data.Length}.";

  public static AtariPaintworksFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < AtariPaintworksFile.BitmapOffset)
      throw new InvalidDataException("Data too small for a valid Atari Paintworks file.");

    if (data.Length < _EXPECTED_FILE_SIZE)
      throw new InvalidDataException(_ShortFileReason(data));

    var span = data;
    var header = AtariPaintworksHeader.ReadFrom(span[AtariPaintworksFile.PaletteOffset..]);

    var resolution = _DetectResolution(data);
    var (width, height) = _GetDimensions(resolution);

    var pixelData = new byte[_PIXEL_DATA_SIZE];
    data.Slice(AtariPaintworksFile.BitmapOffset, _PIXEL_DATA_SIZE).CopyTo(pixelData.AsSpan(0));

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
    if (data.Length < AtariPaintworksFile.BitmapOffset)
      throw new InvalidDataException("Data too small for a valid Atari Paintworks file.");

    if (data.Length < _EXPECTED_FILE_SIZE)
      throw new InvalidDataException(_ShortFileReason(data));

    var span = data.AsSpan();
    var header = AtariPaintworksHeader.ReadFrom(span[AtariPaintworksFile.PaletteOffset..]);

    var resolution = _DetectResolution(data);
    var (width, height) = _GetDimensions(resolution);

    var pixelData = new byte[_PIXEL_DATA_SIZE];
    data.AsSpan(AtariPaintworksFile.BitmapOffset, _PIXEL_DATA_SIZE).CopyTo(pixelData.AsSpan(0));

    return new AtariPaintworksFile {
      Width = width,
      Height = height,
      Resolution = resolution,
      Palette = header.Palette,
      PixelData = pixelData
    };
  }

  /// <summary>
  /// Reads the resolution the file states.
  /// </summary>
  /// <remarks>
  /// This used to be answered from the file's length, which cannot say: the screen is the same 32000
  /// bytes in all three resolutions, so every file measured the same and every one was called low.
  /// A 640 by 400 picture therefore came back 320 by 200 and drew from the wrong half of its own
  /// data. The resolution is the long word the file opens with.
  /// </remarks>
  private static AtariPaintworksResolution _DetectResolution(ReadOnlySpan<byte> data)
    => BinaryPrimitives.ReadInt32BigEndian(data) switch {
      1 => AtariPaintworksResolution.Medium,
      2 => AtariPaintworksResolution.High,
      _ => AtariPaintworksResolution.Low,
    };

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
    if (data.Length < AtariPaintworksFile.BitmapOffset)
      throw new InvalidDataException("Data too small for a valid Atari Paintworks file.");

    if (data.Length < _EXPECTED_FILE_SIZE)
      throw new InvalidDataException(_ShortFileReason(data));

    var span = data.AsSpan();
    var header = AtariPaintworksHeader.ReadFrom(span[AtariPaintworksFile.PaletteOffset..]);
    var (width, height) = _GetDimensions(resolution);

    var pixelData = new byte[_PIXEL_DATA_SIZE];
    data.AsSpan(AtariPaintworksFile.BitmapOffset, _PIXEL_DATA_SIZE).CopyTo(pixelData.AsSpan(0));

    return new AtariPaintworksFile {
      Width = width,
      Height = height,
      Resolution = resolution,
      Palette = header.Palette,
      PixelData = pixelData
    };
  }
}
