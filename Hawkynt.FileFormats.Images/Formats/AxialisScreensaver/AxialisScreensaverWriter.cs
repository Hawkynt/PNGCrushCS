using System;
using System.IO;
using System.Text;

namespace FileFormat.AxialisScreensaver;

/// <summary>Writes an Axialis screensaver project's media records round the pictures it embeds.</summary>
/// <remarks>
/// There is no directory in one of these and none is written: the signature, four digits of version,
/// and then each picture behind the length that picture has. That is the whole of what a reader can
/// find a picture by here — the length stated in front of a payload has to be the one the picture's
/// own framing gives — so the length goes out from the bytes rather than from anything else.
/// <para/>
/// What is not written is the rest of a project: the timing, the transitions, the paths the author's
/// pictures came from. Those are the greater part of one and nothing here models them, so what this
/// produces is a file whose pictures are found where a project's are and which the producer itself
/// would not run.
/// </remarks>
public static class AxialisScreensaverWriter {

  public static byte[] ToBytes(AxialisScreensaverFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Embedded.Count == 0)
      throw new ArgumentException("An Axialis screensaver project embeds pictures and this one has none.", nameof(file));

    var version = file.Version;
    if (version.Length != 4 || !_AllDigits(version))
      version = "0100";

    using var output = new MemoryStream();
    output.Write(AxialisScreensaverFile.Magic);
    output.Write(Encoding.ASCII.GetBytes(version));

    foreach (var picture in file.Embedded) {
      if (picture == null || picture.Length == 0)
        throw new ArgumentException("An Axialis screensaver record carries a whole picture file and this one is empty.", nameof(file));

      output.WriteByte((byte)picture.Length);
      output.WriteByte((byte)(picture.Length >> 8));
      output.WriteByte((byte)(picture.Length >> 16));
      output.WriteByte((byte)(picture.Length >> 24));
      output.Write(picture);
    }

    return output.ToArray();
  }

  private static bool _AllDigits(string value) {
    foreach (var c in value)
      if (c is < '0' or > '9')
        return false;

    return true;
  }
}
