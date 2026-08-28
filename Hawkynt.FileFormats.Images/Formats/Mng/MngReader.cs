using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Mng;

/// <summary>Reads MNG files from bytes, streams, or file paths.</summary>
public static class MngReader {

  private static readonly byte[] _MNG_SIGNATURE = { 0x8A, 0x4D, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
  private static readonly byte[] _PNG_SIGNATURE = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

  public static MngFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MNG file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MngFile FromStream(Stream stream) {
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

  public static MngFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 8)
      throw new InvalidDataException("Data too small for a valid MNG file.");
    if (!data[..8].SequenceEqual(_MNG_SIGNATURE))
      throw new InvalidDataException("Invalid MNG signature.");

    var offset = 8;
    MngHeader header = default;
    var frames = new List<byte[]>();
    var frameDelays = new List<int>();
    var termAction = MngTermAction.ShowLast;
    var actionAfterIterations = MngTermAction.ShowLast;
    var repeatDelay = 0;
    var numPlays = 0;
    var foundMhdr = false;
    var foundTerm = false;
    List<byte[]>? currentFrameChunks = null;

    var defaultDelay = 1;
    var nextDelay = defaultDelay;
    var oneShotDelay = false;

    while (offset + 12 <= data.Length) {
      var chunkLength = checked((int)_ReadUInt32BE(data[offset..]));
      var chunkType = _ReadChunkType(data[(offset + 4)..]);
      var chunkDataStart = offset + 8;
      var chunkEnd = checked(chunkDataStart + chunkLength + 4);
      if (chunkEnd > data.Length)
        throw new InvalidDataException($"Truncated MNG {chunkType} chunk.");

      switch (chunkType) {
        case "MHDR":
          if (foundMhdr || offset != 8)
            throw new InvalidDataException("MHDR must be the first and only MNG chunk.");
          if (chunkLength != MngHeader.StructSize)
            throw new InvalidDataException("MHDR must contain exactly 28 bytes.");
          header = MngHeader.ReadFrom(data[chunkDataStart..]);
          foundMhdr = true;
          break;

        case "TERM":
          if (foundTerm)
            throw new InvalidDataException("Only one TERM chunk is permitted.");
          if (chunkLength is not 1 and not 10)
            throw new InvalidDataException("TERM must contain either 1 or 10 bytes.");

          termAction = (MngTermAction)data[chunkDataStart];
          if (termAction is < MngTermAction.ShowLast or > MngTermAction.Repeat)
            throw new InvalidDataException($"Invalid TERM termination action {(byte)termAction}.");

          if (termAction == MngTermAction.Repeat) {
            if (chunkLength != 10)
              throw new InvalidDataException("TERM repeat action requires the 10-byte form.");
            actionAfterIterations = (MngTermAction)data[chunkDataStart + 1];
            if (actionAfterIterations is < MngTermAction.ShowLast or > MngTermAction.ShowFirst)
              throw new InvalidDataException("TERM action_after_iterations must be 0, 1, or 2.");
            repeatDelay = checked((int)_ReadUInt32BE(data[(chunkDataStart + 2)..]));
            numPlays = checked((int)_ReadUInt32BE(data[(chunkDataStart + 6)..]));
          } else if (chunkLength != 1) {
            throw new InvalidDataException("Only TERM repeat action may use the 10-byte form.");
          }
          foundTerm = true;
          break;

        case "FRAM":
          if (currentFrameChunks != null)
            throw new InvalidDataException("FRAM cannot occur inside an embedded PNG datastream.");
          _ApplyFram(data.Slice(chunkDataStart, chunkLength), ref defaultDelay, ref nextDelay, ref oneShotDelay);
          break;

        case "IHDR":
          if (!foundMhdr)
            throw new InvalidDataException("Embedded PNG encountered before MHDR.");
          if (currentFrameChunks != null)
            throw new InvalidDataException("Nested embedded PNG datastreams are not valid MNG.");
          currentFrameChunks = [];
          _AddChunkRaw(currentFrameChunks, data, offset, chunkLength);
          break;

        case "IEND":
          if (currentFrameChunks == null)
            throw new InvalidDataException("IEND encountered without an embedded PNG IHDR.");
          _AddChunkRaw(currentFrameChunks, data, offset, chunkLength);
          frames.Add(_AssemblePng(currentFrameChunks));
          frameDelays.Add(nextDelay);
          currentFrameChunks = null;
          if (oneShotDelay) {
            nextDelay = defaultDelay;
            oneShotDelay = false;
          }
          break;

        case "MEND":
          if (chunkLength != 0)
            throw new InvalidDataException("MEND must be empty.");
          if (currentFrameChunks != null)
            throw new InvalidDataException("MEND encountered inside an embedded PNG datastream.");
          break;

        default:
          if (currentFrameChunks != null)
            _AddChunkRaw(currentFrameChunks, data, offset, chunkLength);
          break;
      }

      offset = chunkEnd;
    }

    if (!foundMhdr)
      throw new InvalidDataException("Missing MHDR chunk in MNG file.");

    return new MngFile {
      Width = checked((int)header.Width),
      Height = checked((int)header.Height),
      TicksPerSecond = checked((int)header.TicksPerSecond),
      NumPlays = numPlays,
      TermAction = termAction,
      ActionAfterIterations = actionAfterIterations,
      RepeatDelay = repeatDelay,
      Frames = frames,
      FrameDelays = frameDelays,
    };
  }

  public static MngFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  /// <summary>Reads the timing part of a general FRAM chunk while leaving unsupported presentation parameters intact.</summary>
  private static void _ApplyFram(ReadOnlySpan<byte> chunk, ref int defaultDelay, ref int nextDelay, ref bool oneShotDelay) {
    if (chunk.Length == 0)
      return; // delimiter only

    var framingMode = chunk[0];
    if (framingMode > 4)
      throw new InvalidDataException($"Invalid MNG FRAM framing mode {framingMode}.");
    if (chunk.Length == 1)
      return;

    // A nonempty name is followed by a null separator before the four change bytes. If no separator
    // occurs, this FRAM only changes framing mode/name and leaves all frame parameters unchanged.
    var separator = chunk[1..].IndexOf((byte)0);
    if (separator < 0)
      return;
    var changes = separator + 2;
    if (changes == chunk.Length)
      return;
    if (changes + 4 > chunk.Length)
      throw new InvalidDataException("MNG FRAM change-byte group is truncated.");

    var changeDelay = chunk[changes];
    var changeTimeout = chunk[changes + 1];
    var changeClipping = chunk[changes + 2];
    var changeSync = chunk[changes + 3];
    if (changeDelay > 2 || changeTimeout > 8 || changeClipping > 2 || changeSync > 2)
      throw new InvalidDataException("MNG FRAM contains an invalid change selector.");

    var at = changes + 4;
    if (changeDelay != 0) {
      if (at + 4 > chunk.Length)
        throw new InvalidDataException("MNG FRAM interframe delay is truncated.");
      var value = _ReadUInt32BE(chunk[at..]);
      if (value > 0x7fffffff)
        throw new InvalidDataException("MNG FRAM interframe delay exceeds 0x7fffffff ticks.");
      nextDelay = (int)value;
      oneShotDelay = changeDelay == 1;
      if (changeDelay == 2)
        defaultDelay = nextDelay;
      at += 4;
    } else {
      nextDelay = defaultDelay;
      oneShotDelay = false;
    }

    // Parse/validate the optional fields sufficiently to keep the cursor aligned. Their rendering
    // semantics are separate from timing, but malformed lengths must not be silently accepted.
    if (changeTimeout != 0) {
      if (at + 4 > chunk.Length)
        throw new InvalidDataException("MNG FRAM timeout is truncated.");
      at += 4;
    }
    if (changeClipping != 0) {
      if (at + 17 > chunk.Length)
        throw new InvalidDataException("MNG FRAM clipping boundary fields are truncated.");
      if (chunk[at] > 1)
        throw new InvalidDataException("MNG FRAM clipping delta type must be 0 or 1.");
      at += 17;
    }
    if (changeSync != 0 && (chunk.Length - at) % 4 != 0)
      throw new InvalidDataException("MNG FRAM sync-id list length is not a multiple of four.");
  }

  private static void _AddChunkRaw(List<byte[]> chunks, ReadOnlySpan<byte> data, int chunkStart, int chunkLength) {
    var totalChunkSize = checked(12 + chunkLength);
    var chunk = data.Slice(chunkStart, totalChunkSize).ToArray();
    chunks.Add(chunk);
  }

  private static byte[] _AssemblePng(List<byte[]> chunks) {
    var totalSize = _PNG_SIGNATURE.Length;
    foreach (var chunk in chunks)
      totalSize += chunk.Length;

    var result = new byte[totalSize];
    _PNG_SIGNATURE.CopyTo(result, 0);
    var offset = _PNG_SIGNATURE.Length;
    foreach (var chunk in chunks) {
      chunk.CopyTo(result, offset);
      offset += chunk.Length;
    }

    return result;
  }

  private static uint _ReadUInt32BE(ReadOnlySpan<byte> data)
    => (uint)(data[0] << 24 | data[1] << 16 | data[2] << 8 | data[3]);

  private static string _ReadChunkType(ReadOnlySpan<byte> data)
    => $"{(char)data[0]}{(char)data[1]}{(char)data[2]}{(char)data[3]}";
}
