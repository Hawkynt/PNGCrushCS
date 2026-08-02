using System;
using System.IO;

namespace FileFormat.ComputerEyes;

/// <summary>Reads ComputerEyes image files from bytes, streams, or file paths.</summary>
public static class ComputerEyesReader {

  public static ComputerEyesFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ComputerEyes file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ComputerEyesFile FromStream(Stream stream) {
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
  /// Says that a file bearing the EYES signature is not decoded here, and what is known about it.
  /// </summary>
  /// <remarks>
  /// Two samples carry it and RECOIL reads both — 320 by 200 for the version 0 one, 640 by 400 for
  /// version 1. What was established before the layout defeated the attempt, so the next one need
  /// not repeat it:
  /// <list type="bullet">
  ///   <item>The header is 22 bytes. What follows is 192000 bytes for the smaller and 256000 for the
  ///     larger, which is three bytes a pixel and one respectively.</item>
  ///   <item>The samples are six bits a channel, widened to eight by <c>(v &lt;&lt; 2) | (v &gt;&gt; 4)</c>.
  ///     That is certain rather than likely: the first pixel RECOIL draws is 199, 178, 154 and the
  ///     bytes at 22, 64022 and 128022 are 49, 44 and 38, each of which widens to exactly that.</item>
  ///   <item>The order those bytes come in is <b>not</b> settled. Three contiguous planes, rows
  ///     interleaved by plane, pixels interleaved, the two fields written apart, and bottom-up were
  ///     each tried against the whole picture and none reaches one per cent — though the first pixel
  ///     of the first of them is exact, which is what makes the plane idea tempting and wrong.</item>
  /// </list>
  /// </remarks>
  private static string _NotDecodedHere(int version)
    => $"This is a ComputerEyes version {version} picture, whose pixel order is not worked out here.";

  public static ComputerEyesFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < ComputerEyesFile.HeaderSize)
      throw new InvalidDataException($"Data too small for a valid ComputerEyes file: expected at least {ComputerEyesFile.HeaderSize} bytes, got {data.Length}.");

    // A real one begins with the four letters EYES and then a version word — 0 for the .ce1 samples
    // and 1 for .ce2. Reading those letters as a size gave 65313 by 246 and a demand for two
    // megabytes of pixels from a five kilobyte file.
    if (data.Length >= 6 && data[..4].SequenceEqual("EYES"u8))
      throw new InvalidDataException(_NotDecodedHere(data[4] << 8 | data[5]));

    var width = data[0] | (data[1] << 8);
    var height = data[2] | (data[3] << 8);

    if (width <= 0)
      throw new InvalidDataException($"Invalid ComputerEyes width: {width}.");
    if (height <= 0)
      throw new InvalidDataException($"Invalid ComputerEyes height: {height}.");

    var expectedPixelBytes = width * height;
    if (data.Length < ComputerEyesFile.HeaderSize + expectedPixelBytes)
      throw new InvalidDataException($"Data too small for pixel data: expected {ComputerEyesFile.HeaderSize + expectedPixelBytes} bytes, got {data.Length}.");

    var pixelData = new byte[expectedPixelBytes];
    data.Slice(ComputerEyesFile.HeaderSize, expectedPixelBytes).CopyTo(pixelData.AsSpan(0));

    return new ComputerEyesFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
    }

  public static ComputerEyesFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
