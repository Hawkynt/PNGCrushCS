using System;
using System.IO;

namespace FileFormat.FalconFuckpaint;

/// <summary>Reads Falcon Fuckpaint pictures from bytes, streams, or file paths.</summary>
public static class FalconFuckpaintReader {

  public static FalconFuckpaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Fuckpaint picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FalconFuckpaintFile FromStream(Stream stream) {
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

  public static FalconFuckpaintFile FromSpan(ReadOnlySpan<byte> data) {
    // With no header there is nothing to check but the length, and only three lengths are a picture.
    var (width, height) = data.Length switch {
      65024 => (320, 200),
      77824 => (320, 240),
      308224 => (640, 480),
      _ => throw new InvalidDataException($"Not a Fuckpaint picture: {data.Length} bytes."),
    };

    return new() { Data = data.ToArray(), Width = width, Height = height };
  }

  public static FalconFuckpaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
