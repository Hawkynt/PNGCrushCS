using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.DeluxePaintAnm;

/// <summary>
/// A Deluxe Paint Animation film, Electronic Arts' "large page file": a header with the palette, a table
/// of up to 256 pages, and the pages, 64 KiB apart, each listing the sizes of the records it holds.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>libavformat/anm.c</c>, LGPL-2.1-or-later. Records are numbered across the
/// film and a page states the first it holds, so pages need not lie in order: each page is found by the
/// number of the record that follows the last page's. A film whose header says it has a last delta — the
/// record that loops the end back to the start — counts one record fewer, which, as in FFmpeg, leaves that
/// record out only where it opens a page of its own. The header's
/// colour cycling and palette reach the decoder as codec private data.
/// </remarks>
public sealed class DeluxePaintAnmContainer : IVideoContainerReader<DeluxePaintAnmContainer> {

  internal const uint Tag = 0x2046504C; // "LPF "
  internal const uint AnimTag = 0x4D494E41; // "ANIM"
  internal const int MaximumPages = 256;
  internal const int PageSize = 0x10000;
  internal const int PageHeaderLength = 8;
  internal const int HeaderLength = 128;
  internal const int TableOffset = HeaderLength + PrivateDataLength;
  internal const int TableLength = MaximumPages * 6;
  /// <summary>The colour-cycling ranges and the palette that follow the header, handed to the decoder.</summary>
  internal const int PrivateDataLength = 16 * 8 + 4 * 256;

  internal static readonly CodecTag VideoCodec = CodecTag.FromCharacters("LPF ");

  public required ReadOnlyMemory<byte> Data { get; init; }
  public required int Width { get; init; }
  public required int Height { get; init; }
  public required int Records { get; init; }
  public required int FramesPerSecond { get; init; }
  public required int PageTableOffset { get; init; }
  internal (int BaseRecord, int Records)[] Pages { get; private init; } = [];

  public static string PrimaryExtension => ".anm";
  public static string[] FileExtensions => [".anm"];

  public static bool? MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 24 && BinaryPrimitives.ReadUInt32LittleEndian(header) == Tag
       && BinaryPrimitives.ReadUInt32LittleEndian(header[16..]) == AnimTag
       && BinaryPrimitives.ReadUInt16LittleEndian(header[20..]) != 0 && BinaryPrimitives.ReadUInt16LittleEndian(header[22..]) != 0
         ? true
         : null;

  public static DeluxePaintAnmContainer FromSpan(ReadOnlySpan<byte> data) => _Open(data.ToArray());

  public static DeluxePaintAnmContainer FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return _Open(data);
  }

  public static DeluxePaintAnmContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Deluxe Paint Animation file not found.", file.FullName);
    return _Open(File.ReadAllBytes(file.FullName));
  }

  private static DeluxePaintAnmContainer _Open(byte[] data) {
    var span = data.AsSpan();
    if (data.Length < TableOffset || BinaryPrimitives.ReadUInt32LittleEndian(span) != Tag || BinaryPrimitives.ReadUInt32LittleEndian(span[16..]) != AnimTag)
      throw new NotSupportedException("This file does not open with an LPF header naming ANIM; it is not a Deluxe Paint Animation.");
    if (BinaryPrimitives.ReadUInt16LittleEndian(span[4..]) != MaximumPages)
      throw new NotSupportedException("This Deluxe Paint Animation states a page limit other than 256, which no known film does.");
    // Variant, pixel type, compression and bitmap type: FFmpeg reads only the one combination ever written.
    if (span[24] != 0 || span[28] != 0 || span[29] != 1 || span[31] != 1)
      throw new NotSupportedException("This Deluxe Paint Animation states a variant, pixel type, compression or bitmap type other than Deluxe Paint's own.");
    var width = BinaryPrimitives.ReadUInt16LittleEndian(span[20..]);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(span[22..]);
    if (width == 0 || height == 0)
      throw new InvalidDataException("This Deluxe Paint Animation states a picture with no pixels.");

    var records = (long)BinaryPrimitives.ReadUInt32LittleEndian(span[8..]);
    if (span[26] != 0)
      records = Math.Max(records - 1, 0); // the last delta, which loops the film, is not a picture of its own
    var tableOffset = BinaryPrimitives.ReadUInt16LittleEndian(span[14..]);
    if (data.Length < tableOffset + TableLength)
      throw new InvalidDataException("This Deluxe Paint Animation ends inside its page table.");
    var pages = new (int, int)[MaximumPages];
    for (var i = 0; i < MaximumPages; ++i)
      pages[i] = (BinaryPrimitives.ReadUInt16LittleEndian(span[(tableOffset + i * 6)..]), BinaryPrimitives.ReadUInt16LittleEndian(span[(tableOffset + i * 6 + 2)..]));

    return new() {
      Data = data,
      Width = width,
      Height = height,
      Records = (int)Math.Min(records, int.MaxValue),
      FramesPerSecond = BinaryPrimitives.ReadUInt16LittleEndian(span[68..]),
      PageTableOffset = tableOffset,
      Pages = pages,
    };
  }

  public static IReadOnlyList<MediaStreamInfo> Streams(DeluxePaintAnmContainer container) {
    ArgumentNullException.ThrowIfNull(container);
    var fps = container.FramesPerSecond;
    return [
      new() {
        Index = 0,
        Kind = MediaStreamKind.Video,
        Codec = VideoCodec,
        Handler = VideoCodec,
        Width = container.Width,
        Height = container.Height,
        BitsPerPixel = 8,
        TimeBase = fps > 0 ? new(1, fps) : Rational.Unknown,
        FrameRate = fps > 0 ? new(fps, 1) : Rational.Unknown,
        DeclaredFrameCount = BinaryPrimitives.ReadUInt32LittleEndian(container.Data.Span[64..]),
        CodecPrivateData = container.Data.Slice(HeaderLength, PrivateDataLength).ToArray(),
      },
    ];
  }

  /// <summary>FFmpeg's <c>find_record</c>: the first page holding the record, or -1 past the film's last record or where no page holds it.</summary>
  private static int _FindPage(DeluxePaintAnmContainer container, int record) {
    if (record >= container.Records)
      return -1;
    for (var i = 0; i < MaximumPages; ++i) {
      var (first, count) = container.Pages[i];
      if (count > 0 && record >= first && record < first + count)
        return i;
    }
    return -1;
  }

  /// <summary>
  /// The records page by page, as FFmpeg's demuxer reads them: the page holding record 0, every record it
  /// lists from its first, then the page holding the record after its last, until none does.
  /// </summary>
  /// <remarks>
  /// The film's record count is consulted only when the next page is looked for, so a last delta that
  /// shares a page with the record before it is read with that page; only one that opens a page of its
  /// own is left out.
  /// </remarks>
  public static IEnumerable<CodedPacket> ReadPackets(DeluxePaintAnmContainer container) {
    ArgumentNullException.ThrowIfNull(container);
    var data = container.Data;
    for (var page = _FindPage(container, 0); page >= 0;) {
      var (first, count) = container.Pages[page];
      var start = container.PageTableOffset + TableLength + (long)page * PageSize;
      var at = start + PageHeaderLength + 2L * count;
      for (var k = 0; k < count; ++k) {
        var sizeAt = start + PageHeaderLength + 2L * k;
        if (sizeAt + 2 > data.Length || at >= data.Length)
          yield break;
        var size = BinaryPrimitives.ReadUInt16LittleEndian(data.Span[(int)sizeAt..]);
        var length = (int)Math.Min(data.Length - at, size);
        var record = first + k;
        yield return new(0, data.Slice((int)at, length), PresentationTimestamp: record, DecodeTimestamp: record, Duration: 1, IsKeyFrame: record == 0);
        at += size;
      }
      page = _FindPage(container, first + count);
    }
  }

  public static VideoMetadata Metadata(DeluxePaintAnmContainer container) {
    ArgumentNullException.ThrowIfNull(container);
    var streams = Streams(container);
    return new() { Streams = [new(streams[0].Index, streams[0].Kind, streams[0].Codec)] };
  }
}

/// <summary>
/// Writes Deluxe Paint Animation films: the header with the stream's palette, the page table, and the
/// records packed in number order into 64 KiB pages.
/// </summary>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class DeluxePaintAnmWriter : IVideoContainerWriter<DeluxePaintAnmWriter> {

  private const int _MAXIMUM_RECORDS_PER_PAGE = 256;

  private readonly MediaStreamInfo _video;
  private readonly List<byte[]> _records = [];
  private bool _finished;

  private DeluxePaintAnmWriter(IReadOnlyList<MediaStreamInfo> streams) {
    ArgumentNullException.ThrowIfNull(streams);
    if (streams.Count != 1 || streams[0].Kind != MediaStreamKind.Video || !streams[0].Codec.EqualsIgnoringCase(DeluxePaintAnmContainer.VideoCodec))
      throw new NotSupportedException("A Deluxe Paint Animation carries one LPF video stream and nothing else.");
    this._video = streams[0];
    if (this._video.Width is <= 0 or > 65535 || this._video.Height is <= 0 or > 65535)
      throw new NotSupportedException($"Deluxe Paint Animation states its picture size in sixteen bits; {this._video.Width}x{this._video.Height} does not fit.");
    if (this._video.CodecPrivateData.Length < DeluxePaintAnmContainer.PrivateDataLength)
      throw new NotSupportedException("A Deluxe Paint Animation stream must carry the header's colour cycling and palette as its codec private data.");
  }

  public static string PrimaryExtension => ".anm";
  public static string[] FileExtensions => [".anm"];

  public static DeluxePaintAnmWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) => new(streams);

  public void WritePacket(CodedPacket packet) {
    if (this._finished)
      throw new InvalidOperationException("The Deluxe Paint Animation writer has already been finished.");
    if (packet.StreamIndex != 0)
      throw new ArgumentOutOfRangeException(nameof(packet), packet.StreamIndex, "A Deluxe Paint Animation has only stream 0.");
    if (packet.Data.Length > DeluxePaintAnmContainer.PageSize - DeluxePaintAnmContainer.PageHeaderLength - 2)
      throw new InvalidDataException($"A Deluxe Paint Animation record must fit one 64 KiB page; this one is {packet.Data.Length} bytes.");
    this._records.Add(packet.Data.ToArray());
  }

  public byte[] Finish() {
    if (this._finished)
      throw new InvalidOperationException("The Deluxe Paint Animation writer has already been finished.");
    this._finished = true;

    // Records fill pages in order; a page closes when the next record and its size would not fit.
    var pages = new List<(int First, int Count, int Bytes)>();
    int first = 0, count = 0, bytes = 0;
    for (var i = 0; i < this._records.Count; ++i) {
      var size = this._records[i].Length;
      if (count > 0 && (count == _MAXIMUM_RECORDS_PER_PAGE
                        || DeluxePaintAnmContainer.PageHeaderLength + 2 * (count + 1) + bytes + size > DeluxePaintAnmContainer.PageSize)) {
        pages.Add((first, count, bytes));
        (first, count, bytes) = (i, 0, 0);
      }
      ++count;
      bytes += size;
    }
    if (count > 0)
      pages.Add((first, count, bytes));
    if (pages.Count > DeluxePaintAnmContainer.MaximumPages)
      throw new NotSupportedException($"A Deluxe Paint Animation holds at most 256 pages; these {this._records.Count} records need {pages.Count}.");

    using var output = new MemoryStream();
    var header = new byte[DeluxePaintAnmContainer.TableOffset];
    BinaryPrimitives.WriteUInt32LittleEndian(header, DeluxePaintAnmContainer.Tag);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), DeluxePaintAnmContainer.MaximumPages);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), (ushort)pages.Count);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), (uint)this._records.Count);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), _MAXIMUM_RECORDS_PER_PAGE);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(14), DeluxePaintAnmContainer.TableOffset);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), DeluxePaintAnmContainer.AnimTag);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(20), (ushort)this._video.Width);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(22), (ushort)this._video.Height);
    // Variant 0, frame-rate multiplier 0, no last delta, pixel type 0, compression 1 (RunSkipDump),
    // no other records a frame, bitmap type 1 (320x200 eight-bit), and no record types.
    header[29] = 1;
    header[31] = 1;
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(64), (uint)this._records.Count);
    var rate = this._video.FrameRate;
    var fps = rate.Numerator > 0 && rate.Denominator > 0 ? (int)Math.Clamp(Math.Round((double)rate.Numerator / rate.Denominator), 1, 65535) : 10;
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(68), (ushort)fps);
    this._video.CodecPrivateData.Span[..DeluxePaintAnmContainer.PrivateDataLength].CopyTo(header.AsSpan(DeluxePaintAnmContainer.HeaderLength));
    output.Write(header);

    var table = new byte[DeluxePaintAnmContainer.TableLength];
    for (var i = 0; i < pages.Count; ++i) {
      BinaryPrimitives.WriteUInt16LittleEndian(table.AsSpan(i * 6), (ushort)pages[i].First);
      BinaryPrimitives.WriteUInt16LittleEndian(table.AsSpan(i * 6 + 2), (ushort)pages[i].Count);
      BinaryPrimitives.WriteUInt16LittleEndian(table.AsSpan(i * 6 + 4), (ushort)pages[i].Bytes);
    }
    output.Write(table);

    for (var i = 0; i < pages.Count; ++i) {
      var (pageFirst, pageCount, pageBytes) = pages[i];
      var page = new byte[DeluxePaintAnmContainer.PageHeaderLength + 2 * pageCount + pageBytes];
      BinaryPrimitives.WriteUInt16LittleEndian(page, (ushort)pageFirst);
      BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(2), (ushort)pageCount);
      BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(4), (ushort)pageBytes);
      var at = DeluxePaintAnmContainer.PageHeaderLength + 2 * pageCount;
      for (var k = 0; k < pageCount; ++k) {
        var record = this._records[pageFirst + k];
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(DeluxePaintAnmContainer.PageHeaderLength + 2 * k), (ushort)record.Length);
        record.CopyTo(page, at);
        at += record.Length;
      }
      output.Write(page);
      // Every page but the last is padded to its 64 KiB.
      if (i < pages.Count - 1)
        output.Write(new byte[DeluxePaintAnmContainer.PageSize - page.Length]);
    }
    return output.ToArray();
  }
}
