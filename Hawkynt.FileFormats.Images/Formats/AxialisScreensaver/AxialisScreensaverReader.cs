using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FileFormat.EmbeddedPicture;

namespace FileFormat.AxialisScreensaver;

/// <summary>Finds the pictures an Axialis screensaver project embeds, by the lengths it states.</summary>
public static class AxialisScreensaverReader {

  public static AxialisScreensaverFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Axialis screensaver project not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AxialisScreensaverFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var memory = new MemoryStream();
    stream.CopyTo(memory);
    return FromBytes(memory.ToArray());
  }

  public static AxialisScreensaverFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static AxialisScreensaverFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < AxialisScreensaverFile.SignatureSize)
      throw new InvalidDataException("Data too small to be an Axialis screensaver project.");
    if (!data[..AxialisScreensaverFile.Magic.Length].SequenceEqual(AxialisScreensaverFile.Magic))
      throw new InvalidDataException("Not an Axialis screensaver project: the file does not open with AXSSP.");

    var version = data.Slice(AxialisScreensaverFile.Magic.Length, 4);
    foreach (var digit in version)
      if (digit is < (byte)'0' or > (byte)'9')
        throw new InvalidDataException("An Axialis screensaver project states no version after its signature.");

    var embedded = new List<byte[]>();

    // The document is written in sequence and has no directory, so what is looked for is the shape
    // of a stored file rather than a position: a picture's signature with the length of that picture
    // written immediately in front of it. Both record shapes the format uses put the length there,
    // and the length has to be the one the picture's own framing gives, which is what makes this a
    // reading of the container rather than a search for signatures.
    var at = AxialisScreensaverFile.MinimumPayloadOffset;
    while (at < data.Length) {
      var measured = EmbeddedPictureExtent.Measure(data, at);
      if (measured <= 0) {
        ++at;
        continue;
      }

      var stated = (uint)(data[at - 4] | (data[at - 3] << 8) | (data[at - 2] << 16) | (data[at - 1] << 24));
      if (stated != (uint)measured) {
        ++at;
        continue;
      }

      embedded.Add(data.Slice(at, measured).ToArray());
      at += measured;
    }

    if (embedded.Count == 0)
      throw new InvalidDataException("An Axialis screensaver project embeds no picture whose stated length its own framing agrees with.");

    return new() { Version = Encoding.ASCII.GetString(version), Embedded = embedded };
  }
}
