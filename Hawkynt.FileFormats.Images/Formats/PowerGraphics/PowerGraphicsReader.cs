using System;
using System.IO;
using System.Text;

namespace FileFormat.PowerGraphics;

/// <summary>Reads PowerGraphics pictures from bytes, streams, or file paths.</summary>
public static class PowerGraphicsReader {

  public static PowerGraphicsFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PowerGraphicsFile FromStream(Stream stream) {
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

  public static PowerGraphicsFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 1776 || data[0] != 255 || data[1] != 255 || data[2] != 6 || data[3] != 130
        || Encoding.ASCII.GetString(data.Slice(8, PowerGraphicsFile.Signature.Length)) != PowerGraphicsFile.Signature)
      throw new InvalidDataException("Not a PowerGraphics picture.");

    // The executable header's declared block must account for the file exactly.
    var block = (data[4] | (data[5] << 8)) - (data[2] | (data[3] << 8)) + 1;
    if (6 + block != data.Length)
      throw new InvalidDataException("A PowerGraphics picture's header does not account for the file.");

    var dmaControl = data[774];
    var columns = (dmaControl & 243) switch {
      49 => 32,
      50 => 40,
      _ => throw new InvalidDataException($"A PowerGraphics screen is 32 or 40 characters wide, not {dmaControl & 243}."),
    };

    if ((data[6] | (data[7] << 8)) - PowerGraphicsFile.LoadAddress < 1536)
      throw new InvalidDataException("A PowerGraphics picture's raster program starts inside its own header.");

    return new() { Data = data.ToArray(), Columns = columns, DmaControl = dmaControl };
  }

  public static PowerGraphicsFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
