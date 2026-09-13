using System;
using System.IO;

namespace FileFormat.GfaRaytrace;

/// <summary>Reads GfA Raytrace image files from bytes, streams, or file paths.</summary>
public static class GfaRaytraceReader {

  public static GfaRaytraceFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("GfaRaytrace file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static GfaRaytraceFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static GfaRaytraceFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < GfaRaytraceFile.HeaderSize)
      throw new InvalidDataException("Data too small for a valid GfaRaytrace file.");

    var width = data[0] | (data[1] << 8);
    var height = data[2] | (data[3] << 8);
    if (width == 0) width = data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24);
    if (width <= 0 || width > 65535) width = 320;

    if (8 >= 8) {
      height = data[4] | (data[5] << 8);
      if (height <= 0 || height > 65535) height = 200;
    } else if (height <= 0 || height > 65535) {
      height = 200;
    }

    var pixelBytes = width * height * 3;
    var pixelData = new byte[pixelBytes];
    // Padding what the file does not contain turns a misread size into a picture: a header taken
    // from the wrong offset asked for millions of pixels, the few hundred bytes present were
    // copied in, and the rest was zeros reported as a successful read.
    var available = data.Length - GfaRaytraceFile.HeaderSize;
    if (available < pixelBytes)
      throw new InvalidDataException($"Expected {pixelBytes} bytes of pixel data, got {available}.");

    data.Slice(GfaRaytraceFile.HeaderSize, pixelBytes).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
    }

  public static GfaRaytraceFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
