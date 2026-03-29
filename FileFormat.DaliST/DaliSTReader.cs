using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.DaliST;

/// <summary>Reads Atari ST Dali (SD0/SD1/SD2) images from bytes, streams, or file paths.</summary>
public static class DaliSTReader {

  public static DaliSTFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Dali ST file not found.", file.FullName);

    var data = File.ReadAllBytes(file.FullName);
    var resolution = _DetectResolution(file.Extension);
    return _Parse(data, resolution);
  }

  public static DaliSTFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return _Parse(data, DaliSTResolution.Low);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return _Parse(ms.ToArray(), DaliSTResolution.Low);
  }

  public static DaliSTFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return _Parse(data, DaliSTResolution.Low);
  }

  public static DaliSTFile FromBytes(byte[] data, DaliSTResolution resolution) {
    ArgumentNullException.ThrowIfNull(data);
    return _Parse(data, resolution);
  }

  private static DaliSTFile _Parse(byte[] data, DaliSTResolution resolution) {
    if (data.Length < DaliSTFile.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid Dali ST file: expected {DaliSTFile.ExpectedFileSize} bytes, got {data.Length}.");

    var span = data.AsSpan();
    var palette = new short[16];
    for (var i = 0; i < 16; ++i)
      palette[i] = BinaryPrimitives.ReadInt16BigEndian(span[(i * 2)..]);

    var pixelData = new byte[DaliSTFile.PlanarDataSize];
    data.AsSpan(DaliSTFile.PaletteSize, DaliSTFile.PlanarDataSize).CopyTo(pixelData.AsSpan(0));

    var (width, height) = _GetDimensions(resolution);

    return new DaliSTFile {
      Width = width,
      Height = height,
      Resolution = resolution,
      Palette = palette,
      PixelData = pixelData
    };
  }

  private static DaliSTResolution _DetectResolution(string extension) => extension.ToLowerInvariant() switch {
    ".sd0" => DaliSTResolution.Low,
    ".sd1" => DaliSTResolution.Medium,
    ".sd2" => DaliSTResolution.High,
    _ => DaliSTResolution.Low
  };

  private static (int Width, int Height) _GetDimensions(DaliSTResolution resolution) => resolution switch {
    DaliSTResolution.Low => (320, 200),
    DaliSTResolution.Medium => (640, 200),
    DaliSTResolution.High => (640, 400),
    _ => (320, 200)
  };
}
