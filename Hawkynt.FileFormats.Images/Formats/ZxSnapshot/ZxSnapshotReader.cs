using System;
using System.IO;

namespace FileFormat.ZxSnapshot;

/// <summary>Reads ZX Spectrum snapshots from bytes, streams, or file paths.</summary>
public static class ZxSnapshotReader {

  public static ZxSnapshotFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Snapshot not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ZxSnapshotFile FromStream(Stream stream) {
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

  public static ZxSnapshotFile FromSpan(ReadOnlySpan<byte> data) {
    // The three lengths a snapshot comes in. Nothing in the file says which it is, and nothing in
    // it is a signature either — the length is the only thing that distinguishes a snapshot from
    // any other block of memory, which is why anything else is refused rather than guessed at.
    if (data.Length is not (ZxSnapshotFile.ShortFileSize or ZxSnapshotFile.LongFileSize
        or ZxSnapshotFile.LongerFileSize))
      throw new InvalidDataException(
        $"A Spectrum snapshot is {ZxSnapshotFile.ShortFileSize}, {ZxSnapshotFile.LongFileSize} or "
        + $"{ZxSnapshotFile.LongerFileSize} bytes; this one is {data.Length}.");

    return new() {
      Screen = data.Slice(ZxSnapshotFile.HeaderSize, ZxSnapshotFile.ScreenSize).ToArray(),
      // Only the low three bits reach the border; the rest of the byte is other machine state.
      BorderColor = (byte)(data[ZxSnapshotFile.BorderOffset] & 7),
    };
  }

  public static ZxSnapshotFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
