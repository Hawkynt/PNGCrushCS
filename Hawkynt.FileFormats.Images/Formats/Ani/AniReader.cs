using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
  private const string _INFO_LIST = "INFO";
  private const string _ICON_ID = "icon";
  private const string _TITLE_ID = "INAM";
  private const string _ARTIST_ID = "IART";

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

  public static AniFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 12)
      throw new InvalidDataException("Data is too small to be a valid ANI file.");

    // RiffReader requires byte[] — allocate once here
    var riff = RiffReader.FromBytes(data.ToArray());
    if (riff.FormType.ToString() != _FORM_TYPE)
      throw new InvalidDataException($"Invalid ANI form type: expected '{_FORM_TYPE}', got '{riff.FormType}'.");

    var anihChunk = riff.Chunks.FirstOrDefault(c => c.Id.ToString() == _ANIH_ID)
      ?? throw new InvalidDataException("Missing 'anih' chunk.");

    if (anihChunk.Data.Length < AniHeader.StructSize)
      throw new InvalidDataException($"Invalid 'anih' chunk size: expected at least {AniHeader.StructSize}, got {anihChunk.Data.Length}.");

    var header = AniHeader.ReadFrom(anihChunk.Data);

    // Both chunks hold one four-byte number a step, and the whole chunk is read rather than as
    // many entries as the header's step count allows. A file whose count disagrees with its own
    // chunk is the case that matters, and the numbers that are there are the ones it carries.
    var rates = _ParseIntArray(riff.Chunks.FirstOrDefault(c => c.Id.ToString() == _RATE_ID)?.Data);
    var sequence = _ParseIntArray(riff.Chunks.FirstOrDefault(c => c.Id.ToString() == _SEQ_ID)?.Data);

    var framList = riff.Lists.FirstOrDefault(l => l.ListType.ToString() == _FRAM_LIST);
    var frameData = new List<byte[]>();
    var frames = new List<IcoFile>();
    if (framList != null)
      foreach (var iconChunk in framList.Chunks.Where(c => c.Id.ToString() == _ICON_ID)) {
        frameData.Add(iconChunk.Data);

        // A frame is a cursor far more often than an icon — that is what an animated cursor is
        // made of, and it is what the header's AF_ICON bit is describing. Parsing each frame as an
        // icon rejected the type field of every real-world file, so nothing but this library's own
        // output could be opened at all. The bundle reader takes either.
        var bundle = IcoReader.ReadBundle(iconChunk.Data);
        frames.Add(new IcoFile {
          Images = bundle.Entries.Select(e => new IcoImage {
            Width = e.Width,
            Height = e.Height,
            BitsPerPixel = e.BitsPerPixel,
            Format = e.Format,
            Data = e.Data
          }).ToArray()
        });
      }

    var infoList = riff.Lists.FirstOrDefault(l => l.ListType.ToString() == _INFO_LIST);

    return new AniFile {
      Header = header,
      Frames = frames,
      FrameData = frameData,
      Rates = rates,
      Sequence = sequence,
      Title = _ReadInfoText(infoList, _TITLE_ID),
      Artist = _ReadInfoText(infoList, _ARTIST_ID)
    };
  }

  public static AniFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  /// <summary>Reads one of the INFO list's text chunks, which are NUL-padded ASCII.</summary>
  private static string? _ReadInfoText(RiffList? infoList, string chunkId) {
    var chunk = infoList?.Chunks.FirstOrDefault(c => c.Id.ToString() == chunkId);
    if (chunk == null)
      return null;

    return Encoding.ASCII.GetString(chunk.Data).TrimEnd('\0', ' ');
  }

  private static int[]? _ParseIntArray(byte[]? data) {
    if (data == null)
      return null;

    var count = data.Length / 4;
    var result = new int[count];
    for (var i = 0; i < count; ++i)
      result[i] = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(i * 4));
    return result;
  }
}
