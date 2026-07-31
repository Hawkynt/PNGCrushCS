using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.Ipl;

/// <summary>Reads IPL Image Sequence frame files from bytes, streams, or file paths.</summary>
/// <remarks>
/// <para>
/// The layout is a 44-byte header, then one plane per channel, then a "fini" trailer. The header
/// opens with a four-character tag that also states the byte order — "iiii" for a little-endian file
/// and "mmmm" for a big-endian one — followed by a version tag, the literal "data", and then size,
/// width, height, channel count, depth, time and sample type as 32-bit words.
/// </para>
/// <para>
/// What stood here before read a 16-bit width from offset 0 and a 16-bit height from offset 4, never
/// looked at the tag at all, and copied the planes out as though they were interleaved triples — so
/// an ordinary 37x23 frame came back as 26985x4 of noise, 26985 being half the "iiii" tag read as a
/// number. Both this and the writer carried a dead <c>if (16 &gt;= 8)</c> branch, which is the shape
/// of a stub nobody came back to.
/// </para>
/// </remarks>
public static class IplReader {

  /// <summary>Tag, size, version, "data", size, width, height, colours, z, time, sample type.</summary>
  internal const int HeaderSize = 44;

  private const int _WidthOffset = 20;
  private const int _HeightOffset = 24;
  private const int _ColoursOffset = 28;
  private const int _SampleTypeOffset = 40;

  public static IplFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Ipl file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static IplFile FromStream(Stream stream) {
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

  public static IplFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < HeaderSize)
      throw new InvalidDataException("Data too small for a valid IPL file.");

    var tag = Encoding.ASCII.GetString(data[..4]);
    var isBigEndian = tag switch {
      "iiii" => false,
      "mmmm" => true,
      _ => throw new InvalidDataException($"Invalid IPL tag '{tag}'; expected \"iiii\" or \"mmmm\"."),
    };

    var width = _Read(data, _WidthOffset, isBigEndian);
    var height = _Read(data, _HeightOffset, isBigEndian);
    var colours = _Read(data, _ColoursOffset, isBigEndian);
    var sampleType = _Read(data, _SampleTypeOffset, isBigEndian);

    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"Invalid IPL dimensions: {width}x{height}.");
    if (colours is not (1 or 3))
      throw new NotSupportedException($"IPL with {colours} channel(s) is not supported; expected 1 or 3.");
    if (sampleType != 0)
      throw new NotSupportedException($"Only 8-bit unsigned IPL samples are supported, got type {sampleType}.");

    // One plane per channel, each width*height bytes, rather than interleaved triples.
    var planeLength = width * height;
    var pixels = new byte[planeLength * 3];
    for (var i = 0; i < planeLength; ++i) {
      byte r, g, b;
      if (colours == 1) {
        r = g = b = _Sample(data, HeaderSize + i);
      } else {
        r = _Sample(data, HeaderSize + i);
        g = _Sample(data, HeaderSize + planeLength + i);
        b = _Sample(data, HeaderSize + (planeLength * 2) + i);
      }

      pixels[i * 3] = r;
      pixels[(i * 3) + 1] = g;
      pixels[(i * 3) + 2] = b;
    }

    return new() {
      Width = width,
      Height = height,
      PixelData = pixels,
    };
  }

  public static IplFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  /// <summary>One sample, or zero where the file is shorter than its header claims.</summary>
  private static byte _Sample(ReadOnlySpan<byte> data, int offset)
    => offset < data.Length ? data[offset] : (byte)0;

  private static int _Read(ReadOnlySpan<byte> data, int offset, bool isBigEndian)
    => isBigEndian
      ? BinaryPrimitives.ReadInt32BigEndian(data[offset..])
      : BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
}
