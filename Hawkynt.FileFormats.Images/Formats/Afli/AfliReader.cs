using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Afli;

/// <summary>Reads AFLI (Advanced FLI) files from bytes, streams, or file paths.</summary>
/// <remarks>
/// The length was pinned to 9218 bytes exactly, which is neither the size of an AFLI nor a size any
/// sample has. A file is a load address, eight video matrices of a page apiece, and the bitmap; it
/// may then run on to the end of the sixteen-kilobyte block it was saved from, and the only sample
/// does — 16385 bytes against the 16194 the picture needs.
/// </remarks>
public static class AfliReader {

  public static AfliFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("AFLI file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AfliFile FromStream(Stream stream) {
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

  public static AfliFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < AfliFile.MinimumFileSize)
      throw new InvalidDataException(
        $"An AFLI picture takes at least {AfliFile.MinimumFileSize} bytes; this file is {data.Length}.");

    return new() {
      LoadAddress = BinaryPrimitives.ReadUInt16LittleEndian(data),
      Screens = data.Slice(AfliFile.ScreensOffset, AfliFile.ScreenCount * AfliFile.ScreenStride).ToArray(),
      BitmapData = data.Slice(AfliFile.BitmapOffset, AfliFile.BitmapDataSize).ToArray(),
    };
  }

  public static AfliFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
