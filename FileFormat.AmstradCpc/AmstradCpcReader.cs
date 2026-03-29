using System;
using System.IO;

namespace FileFormat.AmstradCpc;

/// <summary>Reads Amstrad CPC screen memory dumps from bytes, streams, or file paths.</summary>
public static class AmstradCpcReader {

  /// <summary>Standard CPC screen memory size in bytes.</summary>
  private const int _SCREEN_SIZE = 16384;

  /// <summary>Bytes per scanline in CPC memory.</summary>
  private const int _BYTES_PER_LINE = 80;

  /// <summary>Number of scanlines.</summary>
  private const int _HEIGHT = 200;

  public static AmstradCpcFile FromFile(FileInfo file, AmstradCpcMode mode = AmstradCpcMode.Mode1) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Amstrad CPC screen file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName), mode);
  }

  public static AmstradCpcFile FromStream(Stream stream, AmstradCpcMode mode = AmstradCpcMode.Mode1) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data, mode);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray(), mode);
  }

  public static AmstradCpcFile FromBytes(byte[] data, AmstradCpcMode mode = AmstradCpcMode.Mode1) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _SCREEN_SIZE)
      throw new InvalidDataException($"Data too small for a valid CPC screen dump. Expected {_SCREEN_SIZE} bytes, got {data.Length}.");

    if (data.Length != _SCREEN_SIZE)
      throw new InvalidDataException($"Invalid CPC screen dump size. Expected exactly {_SCREEN_SIZE} bytes, got {data.Length}.");

    var width = mode switch {
      AmstradCpcMode.Mode0 => 160,
      AmstradCpcMode.Mode1 => 320,
      AmstradCpcMode.Mode2 => 640,
      _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown CPC mode.")
    };

    // Deinterleave: CPC memory layout is 8 groups of 25 character rows
    // Line Y address = ((Y / 8) * 80) + ((Y % 8) * 2048)
    // Visible screen is 200 lines * 80 bytes = 16000 bytes
    var linearData = new byte[_HEIGHT * _BYTES_PER_LINE];
    for (var y = 0; y < _HEIGHT; ++y) {
      var srcOffset = (y / 8) * _BYTES_PER_LINE + (y % 8) * 2048;
      var dstOffset = y * _BYTES_PER_LINE;
      data.AsSpan(srcOffset, _BYTES_PER_LINE).CopyTo(linearData.AsSpan(dstOffset));
    }

    return new AmstradCpcFile {
      Width = width,
      Height = _HEIGHT,
      Mode = mode,
      PixelData = linearData
    };
  }
}
