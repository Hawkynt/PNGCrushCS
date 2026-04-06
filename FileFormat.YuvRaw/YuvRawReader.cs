using System;
using System.IO;

namespace FileFormat.YuvRaw;

/// <summary>Reads raw YUV 4:2:0 planar images from bytes, streams, or file paths.</summary>
public static class YuvRawReader {

  public static YuvRawFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("YUV file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static YuvRawFile FromStream(Stream stream) {
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

  public static YuvRawFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static YuvRawFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < 6)
      throw new InvalidDataException($"YUV data too small: expected at least 6 bytes, got {data.Length}.");

    var (width, height) = _DetectResolution(data.Length);
    return FromBytes(data, width, height);
  }

  public static YuvRawFile FromBytes(byte[] data, int width, int height) {
    ArgumentNullException.ThrowIfNull(data);

    var ySize = width * height;
    var uvSize = (width / 2) * (height / 2);
    var expectedSize = ySize + uvSize * 2;

    if (data.Length < expectedSize)
      throw new InvalidDataException($"YUV data too small: expected {expectedSize} bytes for {width}x{height}, got {data.Length}.");

    var yPlane = new byte[ySize];
    var uPlane = new byte[uvSize];
    var vPlane = new byte[uvSize];

    data.AsSpan(0, ySize).CopyTo(yPlane.AsSpan(0));
    data.AsSpan(ySize, uvSize).CopyTo(uPlane.AsSpan(0));
    data.AsSpan(ySize + uvSize, uvSize).CopyTo(vPlane.AsSpan(0));

    return new() { Width = width, Height = height, YPlane = yPlane, UPlane = uPlane, VPlane = vPlane };
  }

  private static (int Width, int Height) _DetectResolution(int fileSize) {
    foreach (var (w, h) in YuvRawFile.KnownResolutions)
      if (w * h * 3 / 2 == fileSize)
        return (w, h);

    throw new InvalidDataException($"Cannot detect YUV resolution from file size {fileSize}. Use FromBytes(data, width, height) overload.");
  }
}
