using System;
using System.IO;

namespace FileFormat.PlaybackBitmapSequence;

/// <summary>Reads playback bitmap sequences (.bms) from bytes, streams, or file paths.</summary>
public static class PlaybackBitmapSequenceReader {

  public static PlaybackBitmapSequenceFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Playback bitmap sequence not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PlaybackBitmapSequenceFile FromStream(Stream stream) {
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

  public static PlaybackBitmapSequenceFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static PlaybackBitmapSequenceFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < PlaybackBitmapSequenceFile.HeaderSize + 2)
      throw new InvalidDataException($"Data too small for a playback bitmap sequence (got {data.Length} bytes).");

    if (!data[..PlaybackBitmapSequenceFile.Magic.Length].SequenceEqual(PlaybackBitmapSequenceFile.Magic))
      throw new InvalidDataException("Not a playback bitmap sequence: it does not open with BMSWinPlay.");

    var bitmap = data[PlaybackBitmapSequenceFile.HeaderSize..];
    if (bitmap[0] != 'B' || bitmap[1] != 'M')
      throw new InvalidDataException("A playback bitmap sequence carries a Windows bitmap and there is none behind its header.");

    return new() { Bitmap = bitmap.ToArray() };
  }
}
