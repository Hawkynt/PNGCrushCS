using System;
using System.IO;

namespace FileFormat.BkScreen;

/// <summary>Reads BK screen dumps from bytes, streams, or file paths.</summary>
public static class BkScreenReader {

  public static BkScreenFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Screen not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static BkScreenFile FromStream(Stream stream) {
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

  public static BkScreenFile FromSpan(ReadOnlySpan<byte> data) {
    // Nothing in a dump identifies it, so the length is the whole of what there is to go on.
    var (isColor, frames) = data.Length switch {
      BkScreenFile.ScreenSize => (false, 1),
      BkScreenFile.ScreenSize + 1 => (true, 1),
      BkScreenFile.ScreenSize * 2 => (false, 2),
      BkScreenFile.ScreenSize * 2 + 2 => (true, 2),
      _ => throw new InvalidDataException($"Not a BK screen: {data.Length} bytes."),
    };

    if (isColor)
      for (var frame = 0; frame < frames; ++frame)
        if (data[BkScreenFile.ScreenSize * frames + frame] >= BkScreenFile.PaletteCount)
          throw new InvalidDataException("Not a BK screen: a frame names a colour set the hardware does not have.");

    return new() { Data = data.ToArray(), IsColor = isColor, Frames = frames };
  }

  public static BkScreenFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
