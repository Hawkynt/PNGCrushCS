using System;
using System.IO;
using System.Text;

namespace FileFormat.BugbiterApac;

/// <summary>Reads Bugbiter APAC239i pictures from bytes, streams, or file paths.</summary>
public static class BugbiterApacReader {

  public static BugbiterApacFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static BugbiterApacFile FromStream(Stream stream) {
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

  public static BugbiterApacFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < BugbiterApacFile.BaseFileSize
        || Encoding.ASCII.GetString(data[..BugbiterApacFile.Signature.Length]) != BugbiterApacFile.Signature
        || data[30] != 255 || data[31] != 80 || data[32] != BugbiterApacFile.Height)
      throw new InvalidDataException("Not a Bugbiter APAC239i picture.");

    var textLength = data[BugbiterApacFile.TextLengthOffset] | (data[BugbiterApacFile.TextLengthOffset + 1] << 8);
    if (data.Length != BugbiterApacFile.BaseFileSize + textLength)
      throw new InvalidDataException($"Declared comment of {textLength} bytes does not match {data.Length} bytes.");

    // Each of the two halves opens with the same two bytes, which is the only check that the
    // comment length pointed at the picture rather than into it.
    var picture = BugbiterApacFile.TextOffset + textLength;
    if (data[picture] != 88 || data[picture + 1] != 37
        || data[picture + BugbiterApacFile.SecondHueOffset - 2] != 88
        || data[picture + BugbiterApacFile.SecondHueOffset - 1] != 37)
      throw new InvalidDataException("Not a Bugbiter APAC239i picture: the halves do not start where the comment ends.");

    return new() { Data = data.ToArray(), PictureOffset = picture };
  }

  public static BugbiterApacFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
