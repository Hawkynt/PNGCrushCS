using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using FileFormat.Ico;
using FileFormat.Riff;

namespace FileFormat.Ani;

/// <summary>Assembles ANI animated cursor file bytes from an <see cref="AniFile"/>.</summary>
public static class AniWriter {

  public static byte[] ToBytes(AniFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var chunks = new List<RiffChunk>();
    var lists = new List<RiffList>();

    // anih chunk
    chunks.Add(new RiffChunk { Id = "anih", Data = _BuildAnihData(file) });

    // rate chunk (optional)
    if (file.Rates != null)
      chunks.Add(new RiffChunk { Id = "rate", Data = _BuildIntArrayData(file.Rates) });

    // seq  chunk (optional)
    if (file.Sequence != null)
      chunks.Add(new RiffChunk { Id = "seq ", Data = _BuildIntArrayData(file.Sequence) });

    // LIST "fram" with "icon" sub-chunks
    var iconChunks = new List<RiffChunk>();
    foreach (var frame in file.Frames)
      iconChunks.Add(new RiffChunk { Id = "icon", Data = IcoWriter.ToBytes(frame) });

    lists.Add(new RiffList { ListType = "fram", Chunks = iconChunks });

    var riffFile = new RiffFile {
      FormType = "ACON",
      Chunks = chunks,
      Lists = lists
    };

    return RiffWriter.ToBytes(riffFile);
  }

  private static byte[] _BuildAnihData(AniFile file) {
    var data = new byte[AniHeader.StructSize];
    var flags = (file.Header.HasSequence ? 1 : 0) | 2;
    new AniHeader(AniHeader.StructSize, file.Header.NumFrames, file.Header.NumSteps, file.Header.Width, file.Header.Height, file.Header.BitCount, 1, file.Header.DisplayRate, flags).WriteTo(data);
    return data;
  }

  private static byte[] _BuildIntArrayData(int[] values) {
    var data = new byte[values.Length * 4];
    for (var i = 0; i < values.Length; ++i)
      BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(i * 4), values[i]);
    return data;
  }
}
