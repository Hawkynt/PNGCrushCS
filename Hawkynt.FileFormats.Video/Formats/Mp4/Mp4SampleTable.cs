using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Mp4;

/// <summary>One sample of a track as the sample tables describe it.</summary>
/// <param name="Offset">Where the bytes are, counted from the start of the file.</param>
/// <param name="Size">How many bytes they are.</param>
/// <param name="DecodeTimestamp">When the sample is due for decoding, in the media time scale.</param>
/// <param name="PresentationTimestamp">When it is due for display, in the media time scale.</param>
/// <param name="Duration">How long it occupies, in the media time scale.</param>
/// <param name="IsSync">Whether decoding may begin here without anything before it.</param>
internal readonly record struct Mp4Sample(
  long Offset,
  int Size,
  long DecodeTimestamp,
  long PresentationTimestamp,
  long Duration,
  bool IsSync);

/// <summary>
/// The sample tables of one track, and the walk that turns them back into samples.
/// </summary>
/// <remarks>
/// An ISO base media file does not store its packets as packets. <c>mdat</c> is an undifferentiated
/// heap of bytes with no lengths and no boundaries in it at all, and every question about where one
/// sample stops and the next begins is answered by the tables in <c>stbl</c> — which is why this type
/// exists and why <c>mdat</c> is never parsed. Five tables have to agree before a single packet can
/// be named:
/// <list type="bullet">
///   <item><c>stco</c>/<c>co64</c> say where each chunk starts, absolutely, in the file.</item>
///   <item><c>stsc</c> says how many samples each chunk holds, run-length coded by first chunk.</item>
///   <item><c>stsz</c>/<c>stz2</c> say how long each sample is, so the ones inside a chunk can be
///     walked off its start one after another.</item>
///   <item><c>stts</c> says how long each sample lasts, run-length coded, which accumulated is its
///     decode timestamp.</item>
///   <item><c>ctts</c>, where present, says how far display runs ahead of decoding for each sample,
///     which is what a codec that reorders frames needs.</item>
/// </list>
/// <para/>
/// The run-length tables are expanded into arrays here because they are short — one entry per change,
/// five for the audio track of a half-second recording. The per-sample tables are not: sizes, chunk
/// offsets and sync flags are read out of the file one entry at a time as the walk reaches them, so
/// opening a two-hour film costs the runs and nothing per frame.
/// </remarks>
internal sealed class Mp4SampleTable {

  private const int _FULL_BOX_PREFIX = 4;

  /// <summary>A run of samples that share a duration, from <c>stts</c>.</summary>
  private readonly (long Count, long Delta)[] _durations;

  /// <summary>A run of samples that share a display offset, from <c>ctts</c>.</summary>
  private readonly (long Count, long Offset)[] _compositionOffsets;

  /// <summary>A run of chunks that hold the same number of samples, from <c>stsc</c>.</summary>
  private readonly (long FirstChunk, long SamplesPerChunk)[] _chunkRuns;

  /// <summary>The per-sample size table, or empty when every sample is <see cref="_fixedSampleSize"/>.</summary>
  private readonly ReadOnlyMemory<byte> _sizes;

  /// <summary>The bits one entry of <see cref="_sizes"/> occupies — 32 for <c>stsz</c>, 4, 8 or 16 for <c>stz2</c>.</summary>
  private readonly int _sizeFieldBits;

  /// <summary>The size every sample shares, or zero when <see cref="_sizes"/> states them one by one.</summary>
  private readonly long _fixedSampleSize;

  /// <summary>The chunk offset table, either 32 or 64 bits an entry.</summary>
  private readonly ReadOnlyMemory<byte> _chunkOffsets;
  private readonly int _chunkOffsetBytes;
  private readonly int _chunkCount;

  /// <summary>The sync sample numbers, one-based and ascending, or empty when every sample is one.</summary>
  private readonly ReadOnlyMemory<byte> _syncSamples;
  private readonly int _syncSampleCount;
  private readonly bool _hasSyncTable;

  /// <summary>The file the offsets point into.</summary>
  private readonly ReadOnlyMemory<byte> _file;

  /// <summary>What the edit list shifts every timestamp of this track by.</summary>
  private readonly long _editShift;

  /// <summary>How many samples the size table claims the track holds.</summary>
  internal long SampleCount { get; }

  /// <summary>The duration every sample shares, or zero when <c>stts</c> states more than one.</summary>
  /// <remarks>
  /// A single-entry <c>stts</c> is the writer stating one duration for every sample of the track,
  /// which for a video track is the frame rate said the other way up. More than one entry means the
  /// samples differ, and there is then no single rate to report.
  /// </remarks>
  internal long ConstantSampleDuration => this._durations.Length == 1 ? this._durations[0].Delta : 0;

  internal Mp4SampleTable(
    ReadOnlyMemory<byte> file,
    ReadOnlyMemory<byte> timeToSample,
    ReadOnlyMemory<byte> compositionOffsets,
    ReadOnlyMemory<byte> sampleToChunk,
    ReadOnlyMemory<byte> sampleSizes,
    ReadOnlyMemory<byte> compactSampleSizes,
    ReadOnlyMemory<byte> chunkOffsets32,
    ReadOnlyMemory<byte> chunkOffsets64,
    ReadOnlyMemory<byte> syncSamples,
    long editShift) {
    this._file = file;
    this._editShift = editShift;

    this._durations = _ReadPairs(timeToSample, signedSecond: false);
    this._compositionOffsets = _ReadPairs(compositionOffsets, signedSecond: true);
    this._chunkRuns = _ReadChunkRuns(sampleToChunk);

    var (sizes, fieldBits, fixedSize, sampleCount) = _ReadSizes(sampleSizes, compactSampleSizes);
    this._sizes = sizes;
    this._sizeFieldBits = fieldBits;
    this._fixedSampleSize = fixedSize;
    this.SampleCount = sampleCount;

    if (!chunkOffsets64.IsEmpty) {
      (this._chunkOffsets, this._chunkCount) = _ReadTable(chunkOffsets64, 8);
      this._chunkOffsetBytes = 8;
    } else {
      (this._chunkOffsets, this._chunkCount) = _ReadTable(chunkOffsets32, 4);
      this._chunkOffsetBytes = 4;
    }

    // An absent stss means every sample is a sync sample, which is not the same thing as a present
    // one with no entries — that is a track nothing may be started from. Both occur, so the presence
    // of the box is recorded rather than inferred from its length.
    this._hasSyncTable = !syncSamples.IsEmpty;
    (this._syncSamples, this._syncSampleCount) = _ReadTable(syncSamples, 4);
  }

  /// <summary>
  /// Walks every sample of the track, in the order the file stores them.
  /// </summary>
  /// <remarks>
  /// Chunk by chunk, because that is the only order in which the tables answer without searching: a
  /// sample's offset is its chunk's offset plus the sizes of the samples before it in that chunk, and
  /// nothing states it directly. Within a chunk the samples are consecutive, which is what makes the
  /// running total correct.
  /// </remarks>
  internal IEnumerable<Mp4Sample> Walk() {
    var sampleNumber = 0L;
    var decodeTime = 0L;

    var durationRun = 0;
    var durationLeft = this._durations.Length > 0 ? this._durations[0].Count : 0L;
    var offsetRun = 0;
    var offsetLeft = this._compositionOffsets.Length > 0 ? this._compositionOffsets[0].Count : 0L;
    var syncCursor = 0;

    // The chunk runs are ascending by first chunk and the walk visits the chunks in order, so one
    // cursor answers every chunk. Searching the table per chunk instead would cost the runs times the
    // chunks, which for a film is a table walked millions of times to answer questions it answers in
    // order.
    var chunkRun = 0;
    var samplesInChunk = this._chunkRuns.Length > 0 ? this._chunkRuns[0].SamplesPerChunk : 0L;

    for (var chunk = 0; chunk < this._chunkCount; ++chunk) {
      while (chunkRun + 1 < this._chunkRuns.Length && this._chunkRuns[chunkRun + 1].FirstChunk <= chunk + 1)
        samplesInChunk = this._chunkRuns[++chunkRun].SamplesPerChunk;

      var offset = this._ChunkOffset(chunk);

      for (var i = 0L; i < samplesInChunk; ++i) {
        // The size table is the authority on how many samples there are. A chunk table that claims
        // more is a file whose tables disagree, and inventing the extra samples out of whatever bytes
        // follow would hand back packets nothing wrote.
        if (sampleNumber >= this.SampleCount)
          yield break;

        var size = this._SampleSize(sampleNumber);
        if (offset < 0 || size < 0 || offset + size > this._file.Length)
          throw new InvalidDataException(
            $"Sample {sampleNumber} is stated as {size} bytes at offset {offset}, which is not inside a file of {this._file.Length} bytes.");

        var duration = 0L;
        if (durationRun < this._durations.Length) {
          duration = this._durations[durationRun].Delta;
          if (--durationLeft <= 0 && ++durationRun < this._durations.Length)
            durationLeft = this._durations[durationRun].Count;
        }

        var compositionOffset = 0L;
        if (offsetRun < this._compositionOffsets.Length) {
          compositionOffset = this._compositionOffsets[offsetRun].Offset;
          if (--offsetLeft <= 0 && ++offsetRun < this._compositionOffsets.Length)
            offsetLeft = this._compositionOffsets[offsetRun].Count;
        }

        var isSync = true;
        if (this._hasSyncTable) {
          // The table is ascending, so one cursor walking it alongside the samples answers every
          // sample without searching. Sample numbers in it are one-based.
          while (syncCursor < this._syncSampleCount && this._SyncSample(syncCursor) < sampleNumber + 1)
            ++syncCursor;

          isSync = syncCursor < this._syncSampleCount && this._SyncSample(syncCursor) == sampleNumber + 1;
        }

        yield return new(
          offset,
          (int)size,
          decodeTime + this._editShift,
          decodeTime + compositionOffset + this._editShift,
          duration,
          isSync);

        decodeTime += duration;
        offset += size;
        ++sampleNumber;
      }
    }
  }

  private long _ChunkOffset(int chunk) {
    var span = this._chunkOffsets.Span;
    var at = chunk * this._chunkOffsetBytes;
    return this._chunkOffsetBytes == 8
      ? (long)BinaryPrimitives.ReadUInt64BigEndian(span.Slice(at, 8))
      : BinaryPrimitives.ReadUInt32BigEndian(span.Slice(at, 4));
  }

  private long _SyncSample(int index) => BinaryPrimitives.ReadUInt32BigEndian(this._syncSamples.Span.Slice(index * 4, 4));

  /// <summary>How long one sample is, from whichever of the two size tables the file carries.</summary>
  private long _SampleSize(long sampleNumber) {
    if (this._fixedSampleSize > 0)
      return this._fixedSampleSize;

    var span = this._sizes.Span;
    switch (this._sizeFieldBits) {
      case 32: return BinaryPrimitives.ReadUInt32BigEndian(span.Slice((int)sampleNumber * 4, 4));
      case 16: return BinaryPrimitives.ReadUInt16BigEndian(span.Slice((int)sampleNumber * 2, 2));
      case 8: return span[(int)sampleNumber];
      // Four-bit fields are packed two to a byte, the earlier sample in the high nibble.
      case 4: {
        var packed = span[(int)(sampleNumber >> 1)];
        return (sampleNumber & 1) == 0 ? packed >> 4 : packed & 0x0F;
      }
      default: return 0;
    }
  }

  // ------------------------------------------------------------------------------------------
  // Table parsing
  // ------------------------------------------------------------------------------------------

  /// <summary>Reads a full box's entry count and hands back the entries as a window onto the file.</summary>
  private static (ReadOnlyMemory<byte> Entries, int Count) _ReadTable(ReadOnlyMemory<byte> box, int entrySize) {
    if (box.Length < _FULL_BOX_PREFIX + 4)
      return (ReadOnlyMemory<byte>.Empty, 0);

    var declared = BinaryPrimitives.ReadUInt32BigEndian(box.Span.Slice(_FULL_BOX_PREFIX, 4));
    var entries = box[(_FULL_BOX_PREFIX + 4)..];

    // The count is the writer's claim; the box is the fact. A file cut short keeps whatever was
    // written, and reading past the box would read the next one's bytes as entries.
    var available = entries.Length / entrySize;
    return (entries, declared > (uint)available ? available : (int)declared);
  }

  /// <summary>Reads a run-length table of two 32-bit fields — <c>stts</c> and <c>ctts</c> share the shape.</summary>
  private static (long First, long Second)[] _ReadPairs(ReadOnlyMemory<byte> box, bool signedSecond) {
    var (entries, count) = _ReadTable(box, 8);
    if (count == 0)
      return [];

    var span = entries.Span;
    var result = new (long, long)[count];
    for (var i = 0; i < count; ++i) {
      var first = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(i * 8, 4));
      // A ctts of version 0 states unsigned offsets and version 1 signed ones. Read as signed either
      // way: the values a version 0 file actually carries are frame counts of a few dozen, and a
      // reader that read a version 1 file's negatives as four billion would put every frame of it in
      // the wrong place.
      var second = signedSecond
        ? BinaryPrimitives.ReadInt32BigEndian(span.Slice(i * 8 + 4, 4))
        : (long)BinaryPrimitives.ReadUInt32BigEndian(span.Slice(i * 8 + 4, 4));

      result[i] = (first, second);
    }

    return result;
  }

  /// <summary>
  /// Reads <c>stsc</c>, whose entries are three fields rather than two.
  /// </summary>
  /// <remarks>
  /// The third field is the sample description index, which says which <c>stsd</c> entry the chunk's
  /// samples are coded with, and it is deliberately dropped. A track whose chunks changed codec part
  /// way through would need a second <see cref="FileFormat.Core.MediaStreamInfo"/> to describe it,
  /// and reporting one description for samples coded with another would be worse than not reporting
  /// the change at all.
  /// </remarks>
  private static (long FirstChunk, long SamplesPerChunk)[] _ReadChunkRuns(ReadOnlyMemory<byte> box) {
    var (entries, count) = _ReadTable(box, 12);
    if (count == 0)
      return [];

    var span = entries.Span;
    var result = new (long, long)[count];
    for (var i = 0; i < count; ++i)
      result[i] = (
        BinaryPrimitives.ReadUInt32BigEndian(span.Slice(i * 12, 4)),
        BinaryPrimitives.ReadUInt32BigEndian(span.Slice(i * 12 + 4, 4)));

    return result;
  }

  /// <summary>Reads whichever of <c>stsz</c> and <c>stz2</c> the file carries.</summary>
  private static (ReadOnlyMemory<byte> Sizes, int FieldBits, long FixedSize, long SampleCount) _ReadSizes(
    ReadOnlyMemory<byte> sampleSizes, ReadOnlyMemory<byte> compactSampleSizes) {
    if (!sampleSizes.IsEmpty && sampleSizes.Length >= _FULL_BOX_PREFIX + 8) {
      var span = sampleSizes.Span;
      var fixedSize = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(_FULL_BOX_PREFIX, 4));
      var count = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(_FULL_BOX_PREFIX + 4, 4));
      var entries = sampleSizes[(_FULL_BOX_PREFIX + 8)..];

      // A stated size of zero means the sizes follow one by one; anything else means every sample is
      // that long and no table follows at all.
      if (fixedSize > 0)
        return (ReadOnlyMemory<byte>.Empty, 0, fixedSize, count);

      var available = entries.Length / 4;
      return (entries, 32, 0, count > (uint)available ? available : count);
    }

    if (compactSampleSizes.IsEmpty || compactSampleSizes.Length < _FULL_BOX_PREFIX + 8)
      return (ReadOnlyMemory<byte>.Empty, 0, 0, 0);

    // stz2 spends its first three bytes on nothing so the field width lands on a byte of its own.
    var compact = compactSampleSizes.Span;
    var fieldBits = compact[_FULL_BOX_PREFIX + 3];
    var compactCount = BinaryPrimitives.ReadUInt32BigEndian(compact.Slice(_FULL_BOX_PREFIX + 4, 4));
    var compactEntries = compactSampleSizes[(_FULL_BOX_PREFIX + 8)..];

    if (fieldBits is not (4 or 8 or 16))
      throw new InvalidDataException($"A compact sample size table states a field width of {fieldBits} bits, which is not one of 4, 8 or 16.");

    var fits = fieldBits == 4 ? compactEntries.Length * 2 : compactEntries.Length / (fieldBits / 8);
    return (compactEntries, fieldBits, 0, compactCount > (uint)fits ? fits : compactCount);
  }
}
