using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Ico;
using FileFormat.Riff;

namespace FileFormat.Ani;

/// <summary>Reads ANI animated cursor files from bytes, streams, or file paths.</summary>
public static class AniReader {

  private const string _FORM_TYPE = "ACON";
  private const string _ANIH_ID = "anih";
  private const string _RATE_ID = "rate";
  private const string _SEQ_ID = "seq ";
  private const string _FRAM_LIST = "fram";
  private const string _ICON_ID = "icon";

  public static AniFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ANI file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AniFile FromStream(Stream stream) {
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

  public static AniFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static AniFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < 12)
      throw new InvalidDataException("Data is too small to be a valid ANI file.");

    var riff = RiffReader.FromBytes(data);
    if (riff.FormType.ToString() != _FORM_TYPE)
      throw new InvalidDataException($"Invalid ANI form type: expected '{_FORM_TYPE}', got '{riff.FormType}'.");

    var anihChunk = riff.Chunks.FirstOrDefault(c => c.Id.ToString() == _ANIH_ID)
      ?? throw new InvalidDataException("Missing 'anih' chunk.");

    if (anihChunk.Data.Length < AniHeader.StructSize)
      throw new InvalidDataException($"Invalid 'anih' chunk size: expected at least {AniHeader.StructSize}, got {anihChunk.Data.Length}.");

    var header = AniHeader.ReadFrom(anihChunk.Data);

    var rateChunk = riff.Chunks.FirstOrDefault(c => c.Id.ToString() == _RATE_ID);
    int[]? rates = null;
    if (rateChunk != null)
      rates = _ParseIntArray(rateChunk.Data, header.NumSteps);

    var seqChunk = riff.Chunks.FirstOrDefault(c => c.Id.ToString() == _SEQ_ID);
    int[]? sequence = null;
    if (seqChunk != null)
      sequence = _ParseIntArray(seqChunk.Data, header.NumSteps);

    var framList = riff.Lists.FirstOrDefault(l => l.ListType.ToString() == _FRAM_LIST);
    var frames = new List<IcoFile>();
    if (framList != null)
      foreach (var iconChunk in framList.Chunks.Where(c => c.Id.ToString() == _ICON_ID))
        frames.Add(IcoReader.FromBytes(iconChunk.Data));

    return new AniFile {
      Header = header,
      Frames = frames,
      Rates = rates,
      Sequence = sequence
    };
  }

  private static int[] _ParseIntArray(byte[] data, int expectedCount) {
    var count = Math.Min(data.Length / 4, expectedCount);
    var result = new int[count];
    for (var i = 0; i < count; ++i)
      result[i] = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(i * 4));
    return result;
  }
}
