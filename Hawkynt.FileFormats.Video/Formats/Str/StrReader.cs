using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Str;

/// <summary>
/// Walks a Sony PlayStation STR file's CD sectors and turns them into video and audio packets,
/// without reading a single MDEC run-length code or a single ADPCM nibble.
/// </summary>
/// <remarks>
/// STR is a raw run of Mode 2 CD sectors — 2352 bytes apiece: a twelve-byte sync pattern, a
/// three-byte time code and a mode byte, an eight-byte CD-XA subheader stated twice, and 2328 bytes
/// of user data whose shape depends on <c>submode</c>'s <c>Form 2</c> bit. A Form 1 sector carrying
/// the <c>Data</c> bit holds 2048 bytes: a thirty-two-byte per-chunk header this reader does
/// understand, naming which chunk of which frame this is, followed by 2016 bytes of MDEC bitstream.
/// A Form 2 sector carrying the <c>Audio</c> bit holds 2324 bytes of XA-ADPCM this reader hands out
/// unread. Every other sector — a duplicate subheader whose fields disagree with the first, a Form 1
/// sector without the recognised per-chunk header, anything this format's own known shapes do not
/// cover — is stepped over the same way an unrecognised RealMedia or EA chunk is: it costs nothing to
/// skip and nothing here claims to know what it means.
/// <para/>
/// Some real files wrap that same run of sectors in a RIFF container stating the form type
/// <c>CDXA</c> — a shell PlayStation development tools wrote around the sectors without changing one
/// byte of them. Nothing about the sectors changes once the wrapper is found and stepped over, which
/// is why one reader below the point of finding the first sector serves both shapes.
/// <para/>
/// A frame is not one sector. A CD sector's own 2048 or 2324 bytes are rarely enough for a whole
/// picture, so a frame is spread across a run of chunks a per-chunk header numbers <c>0</c> to
/// <c>chunk_count - 1</c>, and it is not necessarily a run of *consecutive* sectors either — audio
/// sectors interleave with a video frame's own chunks on real discs, which is why this walk tracks
/// an open frame's chunks by chunk index rather than by counting sectors forward. A chunk's header
/// also states the whole frame's own compressed byte length, which is smaller than the chunk budget
/// spends: real encoders reserve a fixed run of sectors per frame for a constant bit rate and pad
/// what compression left unused, so a frame's packet is the stated length trimmed out of the
/// concatenated chunks and not the chunks' own full capacity.
/// </remarks>
internal static class StrReader {

  internal const int SectorSize = 2352;

  private const int _SyncLength = 12;
  private const int _SubheaderOffset = 16;
  private const int _PayloadOffset = 24;
  private const int _ChunkHeaderLength = 32;
  private const int _Form1PayloadLength = 2048;

  /// <summary>How many bytes of a Form 1 sector's own 2048-byte user data are real chunk payload once
  /// the thirty-two byte per-chunk header is taken off — and not one byte more. A sector's user data
  /// is followed by its own EDC and ECC, and a naive slice to the end of what this reader copied out
  /// of the sector would fold those checksum bytes into the middle of a multi-chunk frame's bitstream
  /// wherever a frame's stated byte length happened to fall inside an early chunk rather than exactly
  /// on its boundary.</summary>
  private const int _ChunkPayloadLength = _Form1PayloadLength - _ChunkHeaderLength;
  private const int _Form2PayloadLength = 2324;
  private const int _XaAdpcmDataLength = 2304;
  private const int _RiffPreambleSearchLimit = 2048;

  // submode bits (CD-ROM XA, ISO/IEC 10149 / Sony "System Description CD-ROM XA")
  private const byte _SubmodeVideo = 0x02;
  private const byte _SubmodeAudio = 0x04;
  private const byte _SubmodeData = 0x08;
  private const byte _SubmodeForm2 = 0x20;

  private const ushort _ChunkMarker0 = 0x0160;
  private const ushort _ChunkMarker1 = 0x8001;
  private const ushort _ChunkMagic = 0x3800;

  private static readonly byte[] _Sync = [0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00];
  private static readonly byte[] _Riff = "RIFF"u8.ToArray();
  private static readonly byte[] _Cdxa = "CDXA"u8.ToArray();

  private static readonly CodecTag _VideoCodec = CodecTag.FromCharacters("MDEC");
  private static readonly CodecTag _AudioCodec = CodecTag.FromCharacters("XAAD");

  internal static CodecTag VideoCodec => _VideoCodec;
  internal static CodecTag AudioCodec => _AudioCodec;

  /// <summary>
  /// Finds where the run of CD sectors starts: byte zero of a raw file, or the first sync pattern
  /// found within a bounded search of a file wrapped in a RIFF/CDXA shell. Returns -1 for neither.
  /// </summary>
  /// <remarks>
  /// The RIFF wrapper's own chunk structure is not walked to find a <c>data</c> chunk, because real
  /// files do not reliably have one to find: every RIFF/CDXA sample this reader was measured against
  /// states a RIFF size of nought — a streamed file whose length was not known when its header was
  /// written — and the thirty-two bytes between the CDXA form type and the first sector are, on every
  /// one of them, either zero or a size nothing downstream needs, never a <c>fmt </c> or <c>data</c>
  /// fourCC a generic RIFF walk could key off. Searching for the sync pattern instead of a chunk name
  /// is what reads them regardless.
  /// </remarks>
  internal static int LocateSyncStart(ReadOnlySpan<byte> data) {
    if (data.Length >= _SyncLength && data[.._Sync.Length].SequenceEqual(_Sync))
      return 0;

    if (data.Length < 12 || !data[..4].SequenceEqual(_Riff) || !data.Slice(8, 4).SequenceEqual(_Cdxa))
      return -1;

    var searchable = Math.Min(data.Length - 12, _RiffPreambleSearchLimit);
    var found = data.Slice(12, searchable).IndexOf(_Sync);
    return found < 0 ? -1 : 12 + found;
  }

  /// <summary>
  /// Whether a header looks like a Sony STR file's: a sync pattern this reader can find, at a sector
  /// whose per-chunk header — Form 1, the two fixed marker words every real sample states, and the
  /// magic word at its own fixed offset — matches what a video chunk states. Checked on the first
  /// sector search finds rather than assumed to be the very first one, because a handful of real
  /// files open on an audio sector.
  /// </remarks>
  internal static bool? LooksPlausible(ReadOnlySpan<byte> header) {
    var syncStart = LocateSyncStart(header);
    if (syncStart < 0)
      return false;

    var sectorsAvailable = (header.Length - syncStart) / SectorSize;
    if (sectorsAvailable == 0)
      return null; // not enough of the header to tell

    for (var i = 0; i < sectorsAvailable; ++i) {
      var sector = header.Slice(syncStart + i * SectorSize, SectorSize);
      if (!sector[.._Sync.Length].SequenceEqual(_Sync))
        return false;

      var submode = sector[_SubheaderOffset + 2];
      if ((submode & _SubmodeForm2) != 0)
        continue; // an audio (or other Form 2) sector — keep looking for a video one to check

      var payload = sector[_PayloadOffset..];
      if (_LooksLikeChunkHeader(payload))
        return true;
    }

    return null; // every sector examined was Form 2; inconclusive from this much of the file
  }

  private static bool _LooksLikeChunkHeader(ReadOnlySpan<byte> payload)
    => payload.Length >= _ChunkHeaderLength
       && BinaryPrimitives.ReadUInt16LittleEndian(payload) == _ChunkMarker0
       && BinaryPrimitives.ReadUInt16LittleEndian(payload[2..]) == _ChunkMarker1
       && BinaryPrimitives.ReadUInt16LittleEndian(payload[22..]) == _ChunkMagic;

  internal static StrContainer Open(ReadOnlyMemory<byte> data) {
    var span = data.Span;
    var syncStart = LocateSyncStart(span);
    if (syncStart < 0)
      throw new NotSupportedException(
        "This file opens with neither the CD sector sync pattern a raw Sony STR file starts on, nor "
        + "a RIFF header stating the CDXA form type the wrapped shape uses. This is not a Sony STR "
        + "file this reader recognises.");

    var sectorCount = (data.Length - syncStart) / SectorSize;
    if (sectorCount == 0)
      throw new InvalidDataException(
        $"The sync pattern was found at byte {syncStart}, but fewer than {SectorSize} bytes follow it "
        + "— not even one whole CD sector.");

    var width = 0;
    var height = 0;
    var videoFrameCount = 0;
    var audioPacketCount = 0;
    var anyChunkSeen = false;
    var frameOpen = false;
    var expectedChunks = 0;
    var chunksSeen = 0;
    var videoChannel = -1;
    var audioChannel = -1;

    for (var i = 0; i < sectorCount; ++i) {
      var sector = span.Slice(syncStart + i * SectorSize, SectorSize);
      if (!sector[.._Sync.Length].SequenceEqual(_Sync))
        throw new InvalidDataException(
          $"Sector {i}, at byte {syncStart + i * SectorSize}, does not open with the CD sync pattern. "
          + "Either this is not a whole number of CD sectors or the file is corrupt.");

      var channel = sector[_SubheaderOffset + 1];
      var submode = sector[_SubheaderOffset + 2];
      if ((submode & _SubmodeForm2) != 0) {
        if ((submode & _SubmodeAudio) != 0) {
          if (audioChannel < 0)
            audioChannel = channel;
          else if (audioChannel != channel)
            throw new NotSupportedException(
              $"Sector {i} is an audio sector on CD-XA channel {channel}, where an earlier one was on "
              + $"channel {audioChannel}. This reader hands out one audio stream per file; a disc "
              + "interleaving several channels' worth of sound needs telling apart by channel, which "
              + "nothing measured here forced and which this reader does not guess at.");

          ++audioPacketCount;
        }

        continue;
      }

      var payload = sector[_PayloadOffset..];
      if (!_LooksLikeChunkHeader(payload))
        continue;

      if (videoChannel < 0)
        videoChannel = channel;
      else if (videoChannel != channel)
        throw new NotSupportedException(
          $"Sector {i} is a video chunk on CD-XA channel {channel}, where an earlier one was on channel "
          + $"{videoChannel}. This reader hands out one video stream per file; a disc interleaving "
          + "several channels' worth of picture needs telling apart by channel, which nothing measured "
          + "here forced and which this reader does not guess at.");

      anyChunkSeen = true;
      var chunkIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload[4..]);
      var chunkCount = BinaryPrimitives.ReadUInt16LittleEndian(payload[6..]);

      if (chunkIndex == 0) {
        if (width == 0 && chunkCount > 0) {
          width = BinaryPrimitives.ReadUInt16LittleEndian(payload[16..]);
          height = BinaryPrimitives.ReadUInt16LittleEndian(payload[18..]);
        }

        expectedChunks = chunkCount;
        chunksSeen = chunkCount > 0 ? 1 : 0;
        frameOpen = chunkCount > 0;
      } else if (frameOpen) {
        ++chunksSeen;
      } else
        continue; // a continuation chunk with no frame open — a truncated start, stepped over

      if (frameOpen && chunksSeen == expectedChunks) {
        ++videoFrameCount;
        frameOpen = false;
      }
    }

    if (!anyChunkSeen)
      throw new NotSupportedException(
        "Every sector in this file is either Form 2 or a Form 1 sector without the per-chunk header "
        + "this reader knows: the two fixed marker words and the magic word real Sony STR video "
        + "chunks all state. There is nothing here this reader recognises as MDEC video.");

    return new() {
      Data = data,
      SyncStart = syncStart,
      SectorCount = sectorCount,
      Width = width,
      Height = height,
      VideoFrameCount = videoFrameCount,
      HasAudio = audioPacketCount > 0,
      AudioPacketCount = audioPacketCount,
    };
  }

  /// <summary>Walks the sectors a second time, this time handing out the packets they describe. See
  /// <see cref="Open"/> for why a frame's chunks are tracked by index and not by sector count, and
  /// why its packet is trimmed rather than being the chunks' full reserved capacity.</summary>
  internal static IEnumerable<CodedPacket> ReadPackets(StrContainer container) {
    var data = container.Data;
    var span = data.Span;
    var syncStart = container.SyncStart;
    var sectorCount = container.SectorCount;

    List<byte[]>? chunks = null;
    var chunkHeader = ReadOnlyMemory<byte>.Empty;
    var expectedChunks = 0;
    var frameSize = 0u;
    long? firstFrameNumber = null;
    var frameOpen = false;

    for (var i = 0; i < sectorCount; ++i) {
      var sectorOffset = syncStart + i * SectorSize;
      var sector = data.Slice(sectorOffset, SectorSize);
      var sectorSpan = sector.Span;
      var submode = sectorSpan[_SubheaderOffset + 2];

      if ((submode & _SubmodeForm2) != 0) {
        if ((submode & _SubmodeAudio) != 0)
          yield return new(
            StreamIndex: 1,
            Data: sector.Slice(_PayloadOffset, _XaAdpcmDataLength),
            IsKeyFrame: true);

        continue;
      }

      var payload = sector.Slice(_PayloadOffset);
      var payloadSpan = payload.Span;
      if (!_LooksLikeChunkHeader(payloadSpan))
        continue;

      var chunkIndex = BinaryPrimitives.ReadUInt16LittleEndian(payloadSpan[4..]);
      var chunkCount = BinaryPrimitives.ReadUInt16LittleEndian(payloadSpan[6..]);

      if (chunkIndex == 0) {
        var frameNumber = (long)BinaryPrimitives.ReadUInt32LittleEndian(payloadSpan[8..]);
        firstFrameNumber ??= frameNumber;

        frameSize = BinaryPrimitives.ReadUInt32LittleEndian(payloadSpan[12..]);
        chunkHeader = payload[.._ChunkHeaderLength];
        expectedChunks = chunkCount;
        chunks = chunkCount > 0 ? [] : null;
        frameOpen = chunkCount > 0;

        if (frameOpen)
          chunks!.Add(payload.Slice(_ChunkHeaderLength, _ChunkPayloadLength).ToArray());

        if (frameOpen && chunks!.Count == expectedChunks) {
          yield return _CompleteFrame(chunkHeader, chunks, frameSize, frameNumber - firstFrameNumber.Value);
          frameOpen = false;
        }
      } else if (frameOpen) {
        chunks!.Add(payload.Slice(_ChunkHeaderLength, _ChunkPayloadLength).ToArray());
        if (chunks.Count == expectedChunks) {
          // The frame number is the one its opening chunk stated; every continuation chunk repeats it.
          var frameNumber = (long)BinaryPrimitives.ReadUInt32LittleEndian(payloadSpan[8..]);
          yield return _CompleteFrame(chunkHeader, chunks, frameSize, frameNumber - firstFrameNumber!.Value);
          frameOpen = false;
        }
      }
      // A continuation chunk with no frame open is a truncated start and is stepped over, the same
      // way Open's counting pass does.
    }

    // An open frame here is a truncated one at end of file — real samples are often cut mid-recording
    // — and it is dropped rather than handed out with fewer chunks than its own header promised.
  }

  private static CodedPacket _CompleteFrame(ReadOnlyMemory<byte> chunkHeader, List<byte[]> chunks, uint frameSize, long presentationTimestamp) {
    var totalCapacity = 0;
    foreach (var chunk in chunks)
      totalCapacity += chunk.Length;

    var used = (int)Math.Min(frameSize, (uint)totalCapacity);
    var combined = new byte[_ChunkHeaderLength + used];
    chunkHeader.Span.CopyTo(combined);

    var written = 0;
    var dest = combined.AsSpan(_ChunkHeaderLength);
    foreach (var chunk in chunks) {
      if (written >= used)
        break;

      var take = Math.Min(chunk.Length, used - written);
      chunk.AsSpan(0, take).CopyTo(dest[written..]);
      written += take;
    }

    return new(
      StreamIndex: 0,
      Data: combined,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: true);
  }
}
