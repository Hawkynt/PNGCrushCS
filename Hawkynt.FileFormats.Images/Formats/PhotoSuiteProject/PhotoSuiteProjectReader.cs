using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Png;

namespace FileFormat.PhotoSuiteProject;

/// <summary>Reads the picture out of an MGI PhotoSuite project from bytes, streams, or file paths.</summary>
public static class PhotoSuiteProjectReader {

  public static PhotoSuiteProjectFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PhotoSuite project not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PhotoSuiteProjectFile FromStream(Stream stream) {
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

  public static PhotoSuiteProjectFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static PhotoSuiteProjectFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < PhotoSuiteProjectFile.ScanStart)
      throw new InvalidDataException(
        $"Data too small for a PhotoSuite project (minimum {PhotoSuiteProjectFile.ScanStart} bytes, got {data.Length}).");

    if (!data[..PhotoSuiteProjectFile.Signature.Length].SequenceEqual(PhotoSuiteProjectFile.Signature))
      throw new InvalidDataException("Not a PhotoSuite project: it is not a Microsoft compound document.");

    var found = -1;
    for (var at = PhotoSuiteProjectFile.ScanStart;
         at + PhotoSuiteProjectFile.PngSignature.Length <= data.Length;
         at += PhotoSuiteProjectFile.ScanStep)
      if (data.Slice(at, PhotoSuiteProjectFile.PngSignature.Length).SequenceEqual(PhotoSuiteProjectFile.PngSignature)) {
        found = at;
        break;
      }

    if (found < 0)
      throw new InvalidDataException("A PhotoSuite project holds no PNG behind its compound-document header.");

    var decoded = PixelConverter.Convert(
      PngFile.ToRawImage(PngReader.FromBytes(data[found..].ToArray())),
      PixelFormat.Rgb24);

    return new() { Width = decoded.Width, Height = decoded.Height, PixelData = decoded.PixelData };
  }
}
