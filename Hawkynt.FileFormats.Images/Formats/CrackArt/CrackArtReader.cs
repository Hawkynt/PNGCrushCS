using System;
using System.IO;

namespace FileFormat.CrackArt;

/// <summary>Reads CrackArt files from bytes, streams, or file paths.</summary>
public static class CrackArtReader {

  private const int _DECOMPRESSED_PIXEL_DATA_SIZE = 32000;

  public static CrackArtFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("CrackArt file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CrackArtFile FromStream(Stream stream) {
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

  public static CrackArtFile FromSpan(ReadOnlySpan<byte> data) {

    if (!CrackArtHeader.TryRead(data, out var isCompressed, out var resolution))
      throw new InvalidDataException("Not a CrackArt file: missing the 'CA' tag.");

    var header = new { Palette = CrackArtHeader.ReadPalette(data, resolution) };
    var dataOffset = CrackArtHeader.GetDataOffset(resolution);
    var (width, height) = _GetDimensions(resolution);

    var stored = new byte[data.Length - dataOffset];
    data.Slice(dataOffset, stored.Length).CopyTo(stored.AsSpan(0));
    var pixelData = _ScreenFrom(stored, isCompressed);

    return new CrackArtFile {
      Width = width,
      Height = height,
      Resolution = resolution,
      Palette = header.Palette,
      PixelData = pixelData
    };
    }

  public static CrackArtFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

/// <summary>
  /// Returns the screen the file holds, unpacking it only if the file says it is packed.
  /// </summary>
  /// <remarks>
  /// The header carries a flag saying whether the picture is packed, and it was read and then
  /// thrown away: everything was run through the unpacker, so a file storing its screen plainly —
  /// which is what the flag being clear means — was unpacked as though it were not, and came out as
  /// noise. Half the pictures this format can hold could not be opened.
  /// </remarks>
  private static byte[] _ScreenFrom(byte[] stored, bool isCompressed) {
    if (isCompressed)
      return CrackArtCompressor.Decompress(stored, _DECOMPRESSED_PIXEL_DATA_SIZE);

    var screen = new byte[_DECOMPRESSED_PIXEL_DATA_SIZE];
    stored.AsSpan(0, Math.Min(stored.Length, screen.Length)).CopyTo(screen.AsSpan(0));
    return screen;
  }

    private static (int Width, int Height) _GetDimensions(CrackArtResolution resolution) => resolution switch {
    CrackArtResolution.Low => (320, 200),
    CrackArtResolution.Medium => (640, 200),
    CrackArtResolution.High => (640, 400),
    _ => throw new InvalidDataException($"Unknown CrackArt resolution: {resolution}.")
  };
}
