using System;
using System.Buffers.Binary;

namespace FileFormat.Asf;

/// <summary>
/// The File Properties Object (clause 3.2): what the file says about itself as a whole.
/// </summary>
/// <remarks>
/// Two of these fields are what make the Data Object readable at all. <see cref="MaximumPacketSize"/>
/// is how long a packet is when the packet itself states no length, which is the ordinary case; and
/// <see cref="Preroll"/> is the amount every stated presentation time is ahead of the time the frame
/// is actually due, because a file is written with its clock started early enough to fill a buffer
/// before playback begins. A reader that did not take the preroll off would report every timestamp in
/// the file several seconds late — for the files ffmpeg writes, exactly 3100 milliseconds late.
/// </remarks>
/// <param name="FileId">The identifier repeated in the Data Object, so the two can be paired.</param>
/// <param name="FileSize">How long the writer said the whole file is.</param>
/// <param name="CreationDate">When it was made, counted in 100-nanosecond units from 1601-01-01 UTC.</param>
/// <param name="DataPacketCount">How many packets the Data Object holds, as the writer claimed.</param>
/// <param name="PlayDuration">How long the file takes to play, in 100-nanosecond units, preroll included.</param>
/// <param name="SendDuration">How long it takes to send, in the same units.</param>
/// <param name="Preroll">How far ahead of real time every stated timestamp is, in milliseconds.</param>
/// <param name="Flags">Bit 0 says the file is a broadcast and its counts are meaningless; bit 1 says it is seekable.</param>
/// <param name="MinimumPacketSize">The shortest a data packet may be.</param>
/// <param name="MaximumPacketSize">The longest a data packet may be, and the length of every packet
/// that states none of its own.</param>
/// <param name="MaximumBitrate">The peak bandwidth the writer claimed, in bits a second.</param>
internal readonly record struct AsfFileProperties(
  ReadOnlyMemory<byte> FileId,
  ulong FileSize,
  ulong CreationDate,
  ulong DataPacketCount,
  ulong PlayDuration,
  ulong SendDuration,
  ulong Preroll,
  uint Flags,
  uint MinimumPacketSize,
  uint MaximumPacketSize,
  uint MaximumBitrate) {

  /// <summary>The length of the object's body — every field is fixed, so it is one number.</summary>
  internal const int STRUCT_SIZE = 80;

  /// <summary>Whether the file is a broadcast, whose packet count and duration were unknowable when
  /// it was written and are therefore not to be believed.</summary>
  internal bool IsBroadcast => (this.Flags & 0x01) != 0;

  /// <summary>Whether the writer claimed the file can be seeked in.</summary>
  internal bool IsSeekable => (this.Flags & 0x02) != 0;

  internal static AsfFileProperties ReadFrom(ReadOnlyMemory<byte> body) {
    var span = body.Span;

    return new(
      body[..AsfGuid.SIZE],
      BinaryPrimitives.ReadUInt64LittleEndian(span[16..]),
      BinaryPrimitives.ReadUInt64LittleEndian(span[24..]),
      BinaryPrimitives.ReadUInt64LittleEndian(span[32..]),
      BinaryPrimitives.ReadUInt64LittleEndian(span[40..]),
      BinaryPrimitives.ReadUInt64LittleEndian(span[48..]),
      BinaryPrimitives.ReadUInt64LittleEndian(span[56..]),
      BinaryPrimitives.ReadUInt32LittleEndian(span[64..]),
      BinaryPrimitives.ReadUInt32LittleEndian(span[68..]),
      BinaryPrimitives.ReadUInt32LittleEndian(span[72..]),
      BinaryPrimitives.ReadUInt32LittleEndian(span[76..]));
  }
}
