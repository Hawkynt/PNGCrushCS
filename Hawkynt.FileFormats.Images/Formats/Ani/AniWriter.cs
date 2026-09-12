using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using FileFormat.Ico;
using FileFormat.Riff;

namespace FileFormat.Ani;

/// <summary>Assembles ANI animated cursor file bytes from an <see cref="AniFile"/>.</summary>
public static class AniWriter {

  /// <summary>The frames are whole icon or cursor files, not bare bitmaps.</summary>
  private const int _AF_ICON = 0x0001;

  /// <summary>The animation states a sequence.</summary>
  private const int _AF_SEQUENCE = 0x0002;

  public static byte[] ToBytes(AniFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var chunks = new List<RiffChunk>();
    var lists = new List<RiffList>();

    // anih chunk
    chunks.Add(new RiffChunk { Id = "anih", Data = _BuildAnihData(file) });

    // LIST INFO, when there is anything to put in it
    var infoChunks = new List<RiffChunk>();
    if (!string.IsNullOrEmpty(file.Title))
      infoChunks.Add(new RiffChunk { Id = "INAM", Data = _AsciiWithTerminator(file.Title!) });
    if (!string.IsNullOrEmpty(file.Artist))
      infoChunks.Add(new RiffChunk { Id = "IART", Data = _AsciiWithTerminator(file.Artist!) });
    if (infoChunks.Count > 0)
      lists.Add(new RiffList { ListType = "INFO", Chunks = infoChunks });

    // rate chunk (optional)
    if (file.Rates != null)
      chunks.Add(new RiffChunk { Id = "rate", Data = _BuildIntArrayData(file.Rates) });

    // seq  chunk (optional)
    if (file.Sequence != null)
      chunks.Add(new RiffChunk { Id = "seq ", Data = _BuildIntArrayData(file.Sequence) });

    // LIST "fram" with "icon" sub-chunks
    var iconChunks = new List<RiffChunk>();
    foreach (var frame in _FrameBytes(file))
      iconChunks.Add(new RiffChunk { Id = "icon", Data = frame });

    lists.Add(new RiffList { ListType = "fram", Chunks = iconChunks });

    var riffFile = new RiffFile {
      FormType = "ACON",
      Chunks = chunks,
      Lists = lists
    };

    return RiffWriter.ToBytes(riffFile);
  }

  /// <summary>
  /// The bytes of each frame, preferring the ones the file was read from.
  /// </summary>
  /// <remarks>
  /// A frame is a whole icon or cursor file, and a cursor's hotspot lives in two directory bytes
  /// that the parsed view of a frame does not carry. Reassembling from that view therefore writes
  /// every frame back as an icon with its hotspot lost, which turns a working animated cursor into
  /// one that points at its own top left corner. When the original bytes are present they are the
  /// answer; reassembly is for a file that was built rather than read.
  /// </remarks>
  private static IEnumerable<byte[]> _FrameBytes(AniFile file) {
    if (file.FrameData.Count == file.Frames.Count && file.FrameData.Count > 0)
      return file.FrameData;

    var assembled = new List<byte[]>(file.Frames.Count);
    foreach (var frame in file.Frames)
      assembled.Add(IcoWriter.ToBytes(frame));
    return assembled;
  }

  private static byte[] _BuildAnihData(AniFile file) {
    var data = new byte[AniHeader.StructSize];

    // AF_ICON is set because this writer's frames are always whole icon or cursor files — there is
    // no path here that emits a bare bitmap. AF_SEQUENCE follows whether a sequence is actually
    // being written, which is not the same question as what the incoming header happened to claim.
    var flags = _AF_ICON | (file.Sequence is { Length: > 0 } ? _AF_SEQUENCE : 0);

    new AniHeader(
      AniHeader.StructSize,
      file.Header.NumFrames,
      file.Header.NumSteps,
      file.Header.Width,
      file.Header.Height,
      file.Header.BitCount,
      1,
      file.Header.DisplayRate,
      flags
    ).WriteTo(data);
    return data;
  }

  private static byte[] _BuildIntArrayData(int[] values) {
    var data = new byte[values.Length * 4];
    for (var i = 0; i < values.Length; ++i)
      BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(i * 4), values[i]);
    return data;
  }

  /// <summary>NUL-terminated ASCII, which is what a RIFF INFO chunk holds.</summary>
  private static byte[] _AsciiWithTerminator(string text) {
    var data = new byte[Encoding.ASCII.GetByteCount(text) + 1];
    Encoding.ASCII.GetBytes(text, data);
    return data;
  }
}
