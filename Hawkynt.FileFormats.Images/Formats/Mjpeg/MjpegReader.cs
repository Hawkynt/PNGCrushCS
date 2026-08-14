using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Jpeg;

namespace FileFormat.Mjpeg;

/// <summary>Splits a raw Motion JPEG stream into the JPEGs it is a concatenation of.</summary>
/// <remarks>
/// There is no container here at all — a <c>.mjpg</c> is one complete JPEG after another, each
/// <c>FF D8</c> through <c>FF D9</c>, which is why cameras can write one a frame at a time and why
/// it is the payload of an MJPG AVI's frame chunks.
/// <para/>
/// The split walks the marker structure rather than searching for <c>FF D9</c>. Entropy-coded data
/// may contain those two bytes, and a JPEG carrying a thumbnail has a whole second JPEG inside its
/// APP1 with an <c>FF D9</c> of its own — a search would cut the first frame short at either. The
/// walk is <see cref="JpegChunkLayout.FirstImageLength"/>, which shares the entropy scanner with the
/// chunk enumeration the JPEG optimizer already relies on, so the delicate part has one home.
/// </remarks>
public static class MjpegReader {

  public static MjpegFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MJPEG file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MjpegFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var buffer = new MemoryStream();
    stream.CopyTo(buffer);
    return FromBytes(buffer.ToArray());
  }

  public static MjpegFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static MjpegFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
      throw new InvalidDataException("Data does not begin with a JPEG start-of-image marker.");

    var frames = new List<byte[]>();
    var offset = 0;
    while (offset + 4 <= data.Length) {
      // Skip whatever a writer left between frames. Some cameras separate them with a boundary line
      // and some pad to a block; neither is part of a picture, and the next one starts at the next
      // start-of-image marker either way.
      if (data[offset] != 0xFF || data[offset + 1] != 0xD8) {
        ++offset;
        continue;
      }

      var length = JpegChunkLayout.FirstImageLength(data[offset..]);

      // No end-of-image marker means the stream stops in the middle of this frame. What is there is
      // part of a picture, not a picture, so it is dropped rather than decoded into whatever the
      // truncation happens to produce.
      if (length <= 0)
        break;

      frames.Add(data.Slice(offset, length).ToArray());
      offset += length;
    }

    if (frames.Count == 0)
      throw new InvalidDataException("No complete JPEG frame found in the stream.");

    return new MjpegFile { Frames = frames };
  }

}
