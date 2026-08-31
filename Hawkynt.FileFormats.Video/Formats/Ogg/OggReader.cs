using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.Ogg;

/// <summary>
/// Takes an Ogg file apart: which logical bitstreams it multiplexes, what each of their mappings says
/// about itself, and where its data packets begin.
/// </summary>
/// <remarks>
/// The header scan reads the beginning of the file and stops. Ogg requires every bitstream of a group
/// to put its first page — and only its first page — before any other page in the file (RFC 3533
/// section 2), so the run of pages carrying the begin-of-stream flag is the file's declaration of what
/// it holds; the header packets that complete each mapping follow immediately after. Once every
/// declared bitstream has its headers, there is nothing more a demuxer learns by reading on, and the
/// scan stops there. Opening a two-hour recording costs its first few pages.
/// <para/>
/// Nothing of a packet's contents is read except the mapping headers, and of those only the fields the
/// mapping defines for a demuxer: the picture size, the frame rate, the sample rate, the granule
/// shift. The header packets themselves cross to the caller whole, as
/// <see cref="MediaStreamInfo.CodecPrivateData"/>.
/// </remarks>
public static class OggReader {

  /// <summary>How many pages the scan will read looking for a bitstream's headers before giving up.</summary>
  /// <remarks>
  /// A bound rather than a limit anything real reaches. Three header packets fit on two or three
  /// pages, and a setup header large enough to span more is still a handful; a file that has not
  /// finished declaring itself after this many is one whose begin-of-stream pages promise bitstreams
  /// that are not in it, and reading to the end of a large file to discover that is worse than saying
  /// so.
  /// </remarks>
  private const int _HEADER_SCAN_PAGE_LIMIT = 4096;

  /// <summary>Reads an instance from the specified file.</summary>
  public static OggContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Ogg file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  /// <summary>Reads an instance from the specified stream.</summary>
  public static OggContainer FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var buffer = new MemoryStream();
    stream.CopyTo(buffer);
    return FromBytes(buffer.ToArray());
  }

  /// <summary>Reads an instance from the specified byte array.</summary>
  public static OggContainer FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    return _Parse(data);
  }

  /// <summary>Reads an Ogg file out of a span.</summary>
  /// <remarks>
  /// The bytes are copied once here, and only here. A container has to outlive the call that built it
  /// — its packets are windows onto the file and are walked long afterwards — and a span promises
  /// nothing about how long the memory behind it stays valid. Callers that already hold an array
  /// should use <see cref="FromBytes"/>, which keeps theirs.
  /// </remarks>
  public static OggContainer FromSpan(ReadOnlySpan<byte> data) => _Parse(data.ToArray());

  private static OggContainer _Parse(byte[] data) {
    var file = new ReadOnlyMemory<byte>(data);
    if (data.Length < OggPage.HEADER_SIZE || !data.AsSpan(0, 4).SequenceEqual(OggPage.CapturePattern))
      throw new InvalidDataException("The data does not begin with an Ogg page's 'OggS' capture pattern.");

    var scans = new List<_Scan>();
    var pages = 0;

    foreach (var page in OggPageScanner.Walk(file)) {
      if (++pages > _HEADER_SCAN_PAGE_LIMIT)
        throw new InvalidDataException(
          $"After {_HEADER_SCAN_PAGE_LIMIT} pages the file's logical bitstreams have still not finished declaring themselves, so it does not begin with the header pages Ogg requires.");

      if (page.IsBeginOfStream) {
        // A second begin-of-stream page for a serial already seen is a chained link rather than a
        // new bitstream of this group. It is left alone; see OggContainer's remarks.
        if (_IndexOf(scans, page.SerialNumber) < 0)
          scans.Add(new(page.SerialNumber));
      } else if (_AllComplete(scans))
        // Stopped here rather than the moment the last bitstream finished declaring itself, because
        // a bitstream whose mapping has no header packets at all — one nothing here recognises —
        // finishes on its own begin-of-stream page, and stopping there would be stopping before the
        // begin-of-stream pages of whatever is multiplexed with it. RFC 3533 section 2 puts all of
        // them ahead of every other page, so the first page that is not one is where they end.
        break;

      var index = _IndexOf(scans, page.SerialNumber);
      if (index < 0)
        continue;

      var scan = scans[index];
      if (scan.Complete)
        continue;

      // Only the pages this scan actually reads packets out of are checksummed. The rest of the file
      // is verified as it is walked, one page at a time, by whoever walks it.
      page.Verify(file);
      _Feed(scan, page);
    }

    if (scans.Count == 0)
      throw new InvalidDataException("The file declares no logical bitstream: no page in it carries the begin-of-stream flag.");

    var incomplete = scans.Find(s => !s.Complete);
    if (incomplete != null)
      throw new InvalidDataException(
        $"Logical bitstream 0x{incomplete.SerialNumber:X8} declares itself with a begin-of-stream page and the file ends before its header packets do — {incomplete.Packets.Count} of them are there.");

    var bitstreams = new OggBitstream[scans.Count];
    for (var i = 0; i < scans.Count; ++i)
      bitstreams[i] = _Describe(scans[i], i);

    return new() {
      File = file,
      Bitstreams = bitstreams,
      FileMetadata = _ReadMetadata(scans, bitstreams),
    };
  }

  private static int _IndexOf(List<_Scan> scans, uint serial) {
    for (var i = 0; i < scans.Count; ++i)
      if (scans[i].SerialNumber == serial)
        return i;

    return -1;
  }

  private static bool _AllComplete(List<_Scan> scans) {
    foreach (var scan in scans)
      if (!scan.Complete)
        return false;

    return scans.Count > 0;
  }

  /// <summary>Takes the header packets a page holds for one bitstream, and stops when it has them all.</summary>
  private static void _Feed(_Scan scan, OggPage page) {
    var packets = new List<OggAssembledPacket>();
    scan.Assembler.Split(page, packets);

    foreach (var packet in packets) {
      // The first packet is the identification header, and reading it is what says how many more
      // there are — which is why the count cannot simply be asked for up front.
      scan.Mapping ??= OggCodecMapping.Identify(packet.Data.Span);

      if (scan.Complete)
        return;

      var expected = scan.Mapping.HeaderPacketCount;

      // A mapping nothing here recognises has no headers to collect, because nothing here knows
      // which of its packets are headers. Every one of them is reported as data, which is the
      // reading that loses nothing.
      if (expected == 0) {
        scan.Complete = true;
        return;
      }

      scan.Packets.Add(packet.Data);

      if (expected > 0) {
        if (scan.Packets.Count >= expected)
          scan.Complete = true;

        continue;
      }

      // FLAC with an unstated header count: the metadata blocks run until one says it is the last.
      // The mapping packet itself carries STREAMINFO, whose last-block flag is normally clear.
      if (scan.Packets.Count > 1 && OggCodecMapping.IsLastFlacMetadataBlock(packet.Data.Span))
        scan.Complete = true;
    }
  }

  private static OggBitstream _Describe(_Scan scan, int index) {
    var mapping = scan.Mapping!;

    return new() {
      SerialNumber = scan.SerialNumber,
      Mapping = mapping,
      HeaderPacketCount = scan.Packets.Count,
      Info = new() {
        Index = index,
        Kind = mapping.Kind,
        // No four-character code, because there is none in the file. Ogg names its codecs with the
        // magic at the head of an identification header, which is text, so the name goes where
        // Matroska's CodecID goes and a decoder matches on that.
        Codec = CodecTag.None,
        CodecId = mapping.CodecId,
        TimeBase = mapping.TimeBase,
        FrameRate = mapping.FrameRate,
        Width = mapping.Width,
        Height = mapping.Height,
        CodecPrivateData = _PackHeaders(scan.Packets),
      },
    };
  }

  /// <summary>
  /// Packs a bitstream's header packets into the one block of bytes a decoder is handed.
  /// </summary>
  /// <remarks>
  /// The header packets are several and <see cref="MediaStreamInfo.CodecPrivateData"/> is one, so they
  /// need a framing, and the framing chosen is the one Matroska already uses for exactly these codecs:
  /// Xiph lacing — a count of packets less one, then the length of every packet but the last as a run
  /// of 255s and a remainder, then the packets end to end. The last packet's length is not stated
  /// because it is whatever is left.
  /// <para/>
  /// Chosen rather than invented so that a Theora or Vorbis decoder reads a stream out of an Ogg file
  /// and out of a Matroska file with the same code. A framing of this reader's own devising would
  /// have meant every codec learning which container it came out of, which is the one thing the split
  /// between demuxing and decoding exists to prevent.
  /// </remarks>
  private static ReadOnlyMemory<byte> _PackHeaders(List<ReadOnlyMemory<byte>> packets) {
    if (packets.Count == 0)
      return ReadOnlyMemory<byte>.Empty;

    var size = 1;
    for (var i = 0; i < packets.Count - 1; ++i)
      size += packets[i].Length / 255 + 1;

    foreach (var packet in packets)
      size += packet.Length;

    var result = new byte[size];
    var at = 0;
    result[at++] = (byte)(packets.Count - 1);

    for (var i = 0; i < packets.Count - 1; ++i) {
      var remaining = packets[i].Length;
      while (remaining >= 255) {
        result[at++] = 255;
        remaining -= 255;
      }

      result[at++] = (byte)remaining;
    }

    foreach (var packet in packets) {
      packet.CopyTo(result.AsMemory(at));
      at += packet.Length;
    }

    return result;
  }

  // ------------------------------------------------------------------------------------------
  // Metadata
  // ------------------------------------------------------------------------------------------

  /// <summary>
  /// Reads what the file says about itself out of the comment headers.
  /// </summary>
  /// <remarks>
  /// Every mapping here carries the same structure — a Vorbis comment: a vendor string and a list of
  /// <c>NAME=value</c> lines, all lengths little-endian and all text UTF-8. It sits in the second
  /// packet for Theora, Vorbis and Opus, and in a metadata block for FLAC; only the first three are
  /// read here, because a FLAC comment block is found by walking block types and that is work for a
  /// FLAC reader rather than for this one.
  /// </remarks>
  private static VideoMetadata _ReadMetadata(List<_Scan> scans, OggBitstream[] bitstreams) {
    var texts = new List<TextMetadataEntry>();
    string? title = null, artist = null, album = null, encoder = null;

    foreach (var scan in scans) {
      var comment = _CommentOf(scan);
      if (comment.IsEmpty)
        continue;

      _ReadComment(comment.Span, texts, ref title, ref artist, ref album, ref encoder);
    }

    var streams = new MediaStreamMetadata[bitstreams.Length];
    for (var i = 0; i < bitstreams.Length; ++i) {
      var info = bitstreams[i].Info;
      streams[i] = new(info.Index, info.Kind, info.Codec, info.Language, info.Name);
    }

    return new() {
      Title = title,
      Artist = artist,
      Album = album,
      EncodedBy = encoder,
      Streams = streams,
      TextEntries = texts,
    };
  }

  /// <summary>The bytes of a bitstream's comment header, past the magic that introduces it.</summary>
  private static ReadOnlyMemory<byte> _CommentOf(_Scan scan) {
    if (scan.Mapping == null || scan.Packets.Count < 2)
      return ReadOnlyMemory<byte>.Empty;

    var packet = scan.Packets[1];

    // Each mapping introduces its comment header with a magic of its own, and the comment begins
    // after it: seven bytes for Theora and Vorbis, eight for Opus's 'OpusTags'.
    var skip = scan.Mapping.Codec switch {
      OggCodec.Theora or OggCodec.Vorbis => 7,
      OggCodec.Opus => 8,
      _ => -1,
    };

    return skip >= 0 && packet.Length >= skip ? packet[skip..] : ReadOnlyMemory<byte>.Empty;
  }

  private static void _ReadComment(
    ReadOnlySpan<byte> comment, List<TextMetadataEntry> texts,
    ref string? title, ref string? artist, ref string? album, ref string? encoder) {
    if (!_TryTakeString(ref comment, out var vendor))
      return;

    if (vendor.Length > 0)
      encoder ??= vendor;

    if (comment.Length < 4)
      return;

    var count = (uint)(comment[0] | (comment[1] << 8) | (comment[2] << 16) | (comment[3] << 24));
    comment = comment[4..];

    for (var i = 0UL; i < count; ++i) {
      if (!_TryTakeString(ref comment, out var line))
        return;

      var separator = line.IndexOf('=');
      if (separator <= 0)
        continue;

      var name = line[..separator];
      var value = line[(separator + 1)..];

      switch (name.ToUpperInvariant()) {
        case "TITLE":
          title ??= value;
          break;
        case "ARTIST":
          artist ??= value;
          break;
        case "ALBUM":
          album ??= value;
          break;
      }

      texts.Add(new(name, value));
    }
  }

  private static bool _TryTakeString(ref ReadOnlySpan<byte> data, out string value) {
    value = string.Empty;
    if (data.Length < 4)
      return false;

    var length = (uint)(data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24));
    if (length > (uint)(data.Length - 4))
      return false;

    value = Encoding.UTF8.GetString(data.Slice(4, (int)length));
    data = data[(4 + (int)length)..];
    return true;
  }

  /// <summary>One bitstream part way through declaring itself.</summary>
  private sealed class _Scan(uint serialNumber) {

    internal uint SerialNumber { get; } = serialNumber;
    internal OggPacketAssembler Assembler { get; } = new();
    internal List<ReadOnlyMemory<byte>> Packets { get; } = [];
    internal OggCodecMapping? Mapping { get; set; }
    internal bool Complete { get; set; }
  }
}
