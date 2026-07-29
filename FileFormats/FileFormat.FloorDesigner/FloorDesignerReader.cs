using System;
using System.IO;

namespace FileFormat.FloorDesigner;

/// <summary>Reads Atari 8-bit Floor Designer (.fge) screens. from bytes, streams, or file paths.</summary>
public static class FloorDesignerReader {

  public static FloorDesignerFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("File not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FloorDesignerFile FromStream(Stream stream) {
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

  public static FloorDesignerFile FromSpan(ReadOnlySpan<byte> data) {
    // The format carries no signature; its fixed size is the only thing identifying it.
    if (data.Length != FloorDesignerFile.FileSize)
      throw new InvalidDataException($"This format is exactly {FloorDesignerFile.FileSize} bytes, got {data.Length}.");

    var header = new byte[FloorDesignerFile.HeaderSize];
    data[..FloorDesignerFile.HeaderSize].CopyTo(header);

    var screen = new byte[FloorDesignerFile.ScreenDataSize];
    data.Slice(FloorDesignerFile.HeaderSize, FloorDesignerFile.ScreenDataSize).CopyTo(screen);

    return new() { Header = header, ScreenData = screen };
  }

  public static FloorDesignerFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
