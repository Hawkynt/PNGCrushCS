using System;
using System.IO;

namespace FileFormat.ApolloHdru;

/// <summary>Writes Apollo HDRU pages (.hdru, .gn), uncompressed.</summary>
public static class ApolloHdruWriter {

  public static byte[] ToBytes(ApolloHdruFile file) {
    if (file.PixelData == null)
      throw new InvalidOperationException("No page to write.");
    if (file.Width is < 1 or > ApolloHdruFile.MaximumSide || file.Height is < 1 or > ApolloHdruFile.MaximumSide)
      throw new InvalidOperationException($"An Apollo HDRU page of {file.Width}x{file.Height} cannot be written.");

    var stride = (file.Width + 7) / 8;
    var expected = (long)stride * file.Height;
    if (file.PixelData.Length < expected)
      throw new InvalidOperationException($"A {file.Width}x{file.Height} page needs {expected} bytes and {file.PixelData.Length} were given.");

    var output = new byte[ApolloHdruFile.HeaderSize + expected];
    ApolloHdruFile.Magic.CopyTo(output);
    _Write16(output, 2, ApolloHdruFile.Uncompressed);
    _Write16(output, 4, file.Resolution);
    _Write16(output, 6, file.Width);
    _Write16(output, 8, file.Height);
    Array.Copy(file.PixelData, 0, output, ApolloHdruFile.HeaderSize, expected);
    return output;
  }

  public static void ToStream(ApolloHdruFile file, Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    var bytes = ToBytes(file);
    stream.Write(bytes, 0, bytes.Length);
  }

  public static void ToFile(ApolloHdruFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }

  private static void _Write16(byte[] data, int at, int value) {
    data[at] = (byte)(value >> 8);
    data[at + 1] = (byte)value;
  }
}
