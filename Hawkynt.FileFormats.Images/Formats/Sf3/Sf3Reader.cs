using System;
using System.IO;

namespace FileFormat.Sf3;

/// <summary>Reads SF3 images from bytes, streams, or file paths.</summary>
public static class Sf3Reader {

  public static Sf3File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Sf3File FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromSpan(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromSpan(ms.ToArray());
  }

  public static Sf3File FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < Sf3File.HeaderSize
        || !data[..Sf3File.Signature.Length].SequenceEqual(Sf3File.Signature))
      throw new InvalidDataException("Not an SF3 file.");

    if (data[Sf3File.Signature.Length] != Sf3File.ImageFormatId)
      throw new InvalidDataException(
        $"An SF3 image is format {Sf3File.ImageFormatId}, not {data[Sf3File.Signature.Length]}.");

    var width = _ReadInt32(data, Sf3File.WidthOffset);
    var height = _ReadInt32(data, Sf3File.WidthOffset + 4);
    var depth = _ReadInt32(data, Sf3File.WidthOffset + 8);
    int channels = data[Sf3File.ChannelsOffset];

    // The low nibble of the sample format is how many bytes one sample takes.
    var bytesPerSample = data[Sf3File.SampleFormatOffset] & 15;

    if (width < 1 || height < 1 || depth != 1)
      throw new InvalidDataException($"An SF3 image is not {width}x{height}x{depth}.");
    if (channels is < 1 or > 4)
      throw new InvalidDataException($"An SF3 image has one to four channels, not {channels}.");
    if (bytesPerSample is not (1 or 2))
      throw new InvalidDataException($"An SF3 sample is one or two bytes, not {bytesPerSample}.");

    var count = width * height * channels;
    if (Sf3File.HeaderSize + count * bytesPerSample > data.Length)
      throw new InvalidDataException("An SF3 image is shorter than its header says.");

    var samples = new byte[count];
    for (var i = 0; i < count; ++i) {
      var at = Sf3File.HeaderSize + i * bytesPerSample;

      // A wide sample is narrowed by its top byte, which is where its magnitude lives.
      samples[i] = bytesPerSample == 1 ? data[at] : data[at + 1];
    }

    return new() { Width = width, Height = height, Channels = channels, Samples = samples };
  }

  private static int _ReadInt32(ReadOnlySpan<byte> data, int offset)
    => data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);

  public static Sf3File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
