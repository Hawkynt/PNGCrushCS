using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Rpl;

/// <summary>
/// Splits an ARMovie/RPL file into the header it opens with, the chunk catalogue naming where every
/// chunk's video and sound bytes sit, and the packets those chunks hold.
/// </summary>
/// <remarks>
/// ARMovie is not AVI and shares nothing with RIFF: a text header of twenty-one newline-terminated
/// fields (<see cref="RplHeader"/>), then a flat binary catalogue at the offset the header names,
/// naming every chunk's own file offset and the byte length of its video and sound payloads —
/// <c>FO,BS;OS</c>, video first, sound after, sitting contiguously at that offset. The header's own
/// "number of chunks" field states the highest chunk index rather than a count, so the catalogue holds
/// one more line than that field says; this was found against a real file whose header claims three
/// chunks and twenty-five frames a chunk — seventy-five frames read literally — while its catalogue
/// holds four lines and ffmpeg reports the true count, <c>4 x 25 = 100</c>.
/// <para/>
/// A chunk's video bytes are handed on to the codec whole, exactly as RoQ hands a codec its own chunk
/// header and Sierra VMD hands a codec its own frame information record: this reader does not know the
/// shape of Escape 130's sixteen-byte frame header, Escape 124's eight-byte one, or any other codec
/// this container might one day carry, and reading one to split a chunk into several pictures would
/// make the demuxer codec-specific. Only a chunk stating exactly one frame is read for exactly that
/// reason — a chunk holding several pictures needs a per-codec frame walk this reader does not have,
/// and every real Escape 130 clip found for this reader states one frame a chunk regardless.
/// </remarks>
internal static class RplReader {

  /// <summary>The one video format whose per-frame header this container knows how to walk.</summary>
  private const int _ESCAPE_124 = 124;

  /// <summary>Bytes of flags and length in front of every Escape 124 frame inside a chunk.</summary>
  private const int _Escape124FrameHeaderLength = 8;

  internal readonly record struct Summary(RplHeader Header, IReadOnlyList<RplChunkEntry> Chunks);

  internal static RplContainer Open(ReadOnlyMemory<byte> data) {
    var span = data.Span;
    var header = RplHeader.Parse(span);

    if (header.FramesPerChunk != 1 && header.VideoCompressionFormat != _ESCAPE_124)
      throw new NotSupportedException(
        $"This ARMovie/RPL file states {header.FramesPerChunk} frames a chunk for video format "
        + $"{header.VideoCompressionFormat}. Splitting a chunk needs a per-frame header, and the only one this "
        + "container knows is Escape 124's — every other format measured against this reader states "
        + "one frame a chunk.");

    var chunkCount = header.HighestChunkIndex + 1;
    if (chunkCount <= 0)
      throw new InvalidDataException($"This ARMovie/RPL file's header states a highest chunk index of {header.HighestChunkIndex}, leaving no chunks at all.");

    var chunks = _ReadCatalogue(span, header.ChunkCatalogueOffset, chunkCount, data.Length);

    return new() {
      Data = data,
      Header = header,
      Chunks = chunks,
    };
  }

  private static IReadOnlyList<RplChunkEntry> _ReadCatalogue(ReadOnlySpan<byte> data, long offset, int count, long fileLength) {
    if (offset < 0 || offset > fileLength)
      throw new InvalidDataException($"This ARMovie/RPL file's chunk catalogue offset, {offset}, lies outside the file.");

    var chunks = new RplChunkEntry[count];
    var at = checked((int)offset);
    for (var i = 0; i < count; ++i) {
      var newline = data[at..].IndexOf((byte)'\n');
      if (newline < 0)
        throw new InvalidDataException($"This ARMovie/RPL file's chunk catalogue runs out of data after {i} of its {count} lines.");

      var line = data[at..(at + newline)];
      chunks[i] = _ParseCatalogueLine(line, i);
      at += newline + 1;
    }

    for (var i = 0; i < count; ++i) {
      var chunk = chunks[i];
      var end = chunk.FileOffset + chunk.VideoByteSize + chunk.AudioByteSize;
      if (chunk.FileOffset < 0 || end > fileLength)
        throw new InvalidDataException($"Chunk {i}'s catalogue entry names bytes running past the end of the file.");

      if (i + 1 < count && end != chunks[i + 1].FileOffset)
        throw new InvalidDataException(
          $"Chunk {i}'s own offset and sizes end at byte {end}, which is not where chunk {i + 1} begins. "
          + "The chunk catalogue is not internally consistent.");
    }

    return chunks;
  }

  private static RplChunkEntry _ParseCatalogueLine(ReadOnlySpan<byte> line, int index) {
    var comma = line.IndexOf((byte)',');
    var semicolon = line.IndexOf((byte)';');
    if (comma < 0 || semicolon < 0 || semicolon < comma)
      throw new InvalidDataException($"Chunk {index}'s catalogue line is not of the form \"offset,videoSize;soundSize\".");

    if (!Utf8Parser.TryParseLong(line[..comma], out var fileOffset)
        || !Utf8Parser.TryParseLong(line[(comma + 1)..semicolon], out var videoSize)
        || !Utf8Parser.TryParseLong(line[(semicolon + 1)..], out var audioSize))
      throw new InvalidDataException($"Chunk {index}'s catalogue line does not hold three decimal integers.");

    return new(fileOffset, videoSize, audioSize);
  }

  internal static IEnumerable<CodedPacket> ReadPackets(RplContainer container) {
    var data = container.Data;
    var chunks = container.Chunks;
    var hasAudio = container.HasAudio;

    var splitFrames = container.Header.VideoCompressionFormat == _ESCAPE_124;
    long videoPosition = 0;
    long audioPosition = 0;
    for (var i = 0; i < chunks.Count; ++i) {
      var chunk = chunks[i];
      if (chunk.VideoByteSize > 0) {
        if (splitFrames)
          foreach (var frame in _SplitEscape124Chunk(data, chunk, i)) {
            yield return frame with {
              PresentationTimestamp = videoPosition,
              DecodeTimestamp = videoPosition,
              Duration = 1,
              IsKeyFrame = videoPosition == 0,
            };
            ++videoPosition;
          }
        else {
          yield return new(
            StreamIndex: 0,
            Data: data.Slice(checked((int)chunk.FileOffset), checked((int)chunk.VideoByteSize)),
            PresentationTimestamp: videoPosition,
            DecodeTimestamp: videoPosition,
            Duration: 1,
            IsKeyFrame: videoPosition == 0);
          ++videoPosition;
        }
      }

      if (hasAudio && chunk.AudioByteSize > 0) {
        yield return new(
          StreamIndex: 1,
          Data: data.Slice(checked((int)(chunk.FileOffset + chunk.VideoByteSize)), checked((int)chunk.AudioByteSize)),
          PresentationTimestamp: audioPosition,
          IsKeyFrame: true);
        audioPosition += chunk.AudioByteSize;
      }
    }
  }

  /// <summary>Walks the pictures an Escape 124 chunk holds, one packet apiece.</summary>
  /// <remarks>
  /// Every other format this reader carries states one frame a chunk, and for those the chunk is the
  /// packet. Escape 124 is the exception, and its own files are the reason: both real recordings at
  /// <c>samples.ffmpeg.org/game-formats/rpl/escape124/</c> state twenty-five and fifteen frames a
  /// chunk, so refusing the shape refuses every Escape 124 file there is.
  /// <para/>
  /// The walk is eight bytes of frame header — a flags word this reader does not interpret, then a
  /// little-endian length — where the length counts those eight bytes as well as the picture behind
  /// them. Measured on <c>ESCAPE.RPL</c>: the first chunk's twenty-five frames consume its 54,568
  /// video bytes exactly, with nothing left over, which is what says the length is inclusive rather
  /// than a payload size.
  /// </remarks>
  private static IEnumerable<CodedPacket> _SplitEscape124Chunk(
    ReadOnlyMemory<byte> data, RplChunkEntry chunk, int chunkIndex) {
    var at = checked((int)chunk.FileOffset);
    var end = checked((int)(chunk.FileOffset + chunk.VideoByteSize));

    while (at < end) {
      if (end - at < _Escape124FrameHeaderLength)
        throw new InvalidDataException(
          $"Chunk {chunkIndex} ends {end - at} bytes into an Escape 124 frame header, which is eight bytes long.");

      var length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.Span.Slice(at + 4, 4)));
      if (length < _Escape124FrameHeaderLength || at + length > end)
        throw new InvalidDataException(
          $"An Escape 124 frame in chunk {chunkIndex} states a length of {length} bytes, which does not "
          + $"fit the {end - at} bytes left in the chunk.");

      yield return new(StreamIndex: 0, Data: data.Slice(at, length));
      at += length;
    }
  }
}
