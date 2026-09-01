using System;
using System.IO;

namespace FileFormat.NeoBookCartoon;

/// <summary>Reads NeoBook cartoons (.car) from bytes, streams, or file paths.</summary>
public static class NeoBookCartoonReader {

  /// <summary>The eight bytes a PNG opens with.</summary>
  private static ReadOnlySpan<byte> _PngSignature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

  public static NeoBookCartoonFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("NeoBook cartoon not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static NeoBookCartoonFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var buffer = new byte[stream.Length - stream.Position];
      stream.ReadExactly(buffer);
      return FromBytes(buffer);
    }

    using var memory = new MemoryStream();
    stream.CopyTo(memory);
    return FromBytes(memory.ToArray());
  }

  public static NeoBookCartoonFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static NeoBookCartoonFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < NeoBookCartoonFile.HeaderSize + _PngSignature.Length)
      throw new InvalidDataException($"Data too small for a NeoBook cartoon (got {data.Length} bytes).");

    if (!data[..NeoBookCartoonFile.Magic.Length].SequenceEqual(NeoBookCartoonFile.Magic))
      throw new InvalidDataException("Not a NeoBook cartoon: it does not open with SN.");

    var offset = (long)(uint)(data[2] | (data[3] << 8) | (data[4] << 16) | (data[5] << 24));
    if (offset < NeoBookCartoonFile.HeaderSize || offset + _PngSignature.Length > data.Length)
      throw new InvalidDataException($"The header states the picture stands at {offset}, which is not inside a file of {data.Length} bytes.");

    var picture = data[(int)offset..];
    if (!picture[.._PngSignature.Length].SequenceEqual(_PngSignature))
      throw new InvalidDataException($"There is no PNG at {offset}, which is where the header says the cartoon's picture stands.");

    var length = PngLength(picture);
    if (length <= 0)
      throw new InvalidDataException("The PNG the cartoon points at does not run to an IEND inside the file.");

    return new() {
      PictureOffset = (int)offset,
      Picture = picture[..length].ToArray(),
    };
  }

  /// <summary>How long the PNG starting here is, walking its chunks to the IEND, or zero when it does not reach one.</summary>
  internal static int PngLength(ReadOnlySpan<byte> data) {
    if (data.Length < _PngSignature.Length || !data[.._PngSignature.Length].SequenceEqual(_PngSignature))
      return 0;

    var at = _PngSignature.Length;
    while (at + 12 <= data.Length) {
      var length = (uint)((data[at] << 24) | (data[at + 1] << 16) | (data[at + 2] << 8) | data[at + 3]);
      if (length > int.MaxValue - 12)
        return 0;

      var next = at + 12 + (long)length;
      if (next > data.Length)
        return 0;

      var isEnd = data[at + 4] == 'I' && data[at + 5] == 'E' && data[at + 6] == 'N' && data[at + 7] == 'D';
      at = (int)next;
      if (isEnd)
        return at;
    }

    return 0;
  }
}
