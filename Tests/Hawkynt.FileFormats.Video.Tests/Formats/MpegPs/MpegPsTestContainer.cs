using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.MpegPs.Tests;

/// <summary>One PES packet of a built program stream.</summary>
/// <param name="StreamId">The stream id that introduces it.</param>
/// <param name="Payload">Its payload, which for a video stream is elementary stream bytes and for
/// a private stream includes the substream header.</param>
/// <param name="Pts">The presentation timestamp to write into its header, if any.</param>
/// <param name="Dts">The decoding timestamp to write into its header, if any. A packet with a
/// decoding timestamp and no presentation timestamp is not a shape the format allows.</param>
internal readonly record struct MpegPsTestPacket(byte StreamId, byte[] Payload, long? Pts = null, long? Dts = null);

/// <summary>
/// Builds program streams by hand, so that the shapes a reader has to survive can be stated exactly
/// rather than hoped for out of an encoder.
/// </summary>
/// <remarks>
/// Every container shape this produces was checked against ffprobe before it was written here. The
/// two pack header layouts, the pack stuffing, the system header, the padding stream, the end code
/// and both PES header layouts are as ffmpeg's <c>mpeg</c> and <c>vob</c> muxers write them, and
/// ffprobe reads files this builder produced as the same streams with the same payload boundaries and
/// the same timestamps. A builder that agreed only with its own reader would prove nothing.
/// <para/>
/// What a synthetic stream cannot prove is where a picture stops. The elementary stream fragments
/// below are start codes with filler behind them rather than real headers, so ffmpeg identifies the
/// codec as unknown, installs no parser and hands the PES payloads back unsplit — which says nothing
/// about whether the split is right. That was measured on eleven files ffmpeg muxed instead, where
/// every video packet agrees with <c>ffprobe -fflags +nofillin</c> in count, order, size and
/// timestamp and the concatenated packets reproduce ffmpeg's own extracted elementary stream byte for
/// byte. The fragments here exist to state the shapes those files cannot be made to contain on
/// demand — a start code falling on each of the three ways it can straddle a PES boundary, a slice
/// code at the same place, a group header with nothing behind it.
/// </remarks>
internal static class MpegPsTestContainer {

  internal const byte VIDEO_STREAM = 0xE0;
  internal const byte AUDIO_STREAM = 0xC0;
  internal const byte PRIVATE_STREAM_1 = 0xBD;

  internal const byte AC3_SUBSTREAM = 0x80;
  internal const byte SUBPICTURE_SUBSTREAM = 0x20;

  /// <summary>The 90 kHz tick every timestamp in a program stream is counted in.</summary>
  internal const int SYSTEM_CLOCK_HZ = 90_000;

  /// <summary>
  /// Writes a program stream, one pack per packet, the way ffmpeg's muxers lay one out.
  /// </summary>
  /// <param name="packets">The packets, in the order they are to be stored.</param>
  /// <param name="systemsVersion">1 for ISO/IEC 11172-1 pack and PES headers, 2 for ISO/IEC 13818-1.</param>
  /// <param name="packStuffing">Bytes of stuffing to put in each ISO/IEC 13818-1 pack header.</param>
  /// <param name="systemHeader">Whether to write a system header after the first pack header.</param>
  /// <param name="streamMap">A program stream map to write after the first pack, as
  /// (stream type, stream id) pairs.</param>
  /// <param name="padding">Bytes of padding stream to write before the end code.</param>
  /// <param name="endCode">Whether to finish with the program end code.</param>
  internal static byte[] Build(
    IReadOnlyList<MpegPsTestPacket> packets,
    int systemsVersion = 2,
    int packStuffing = 0,
    bool systemHeader = false,
    IReadOnlyList<(byte Type, byte StreamId)>? streamMap = null,
    int padding = 0,
    bool endCode = true) {
    ArgumentNullException.ThrowIfNull(packets);

    using var output = new MemoryStream();

    for (var i = 0; i < packets.Count; ++i) {
      _WritePack(output, systemsVersion, packStuffing, i);

      if (i == 0) {
        if (systemHeader)
          _WriteSystemHeader(output, packets);

        if (streamMap != null)
          _WriteStreamMap(output, streamMap);
      }

      _WritePes(output, packets[i], systemsVersion);
    }

    if (padding > 0)
      _WritePadding(output, padding);

    if (endCode)
      _StartCode(output, 0xB9);

    return output.ToArray();
  }

  // ------------------------------------------------------------------------------------------
  // Elementary stream fragments
  // ------------------------------------------------------------------------------------------

  /// <summary>A sequence header, which is what says decoding may begin here.</summary>
  internal static byte[] SequenceHeader(int length = 12) => _Unit(0xB3, 0xB3, length);

  /// <summary>A group of pictures header.</summary>
  internal static byte[] GroupHeader(int length = 8) => _Unit(0xB8, 0xB8, length);

  /// <summary>A picture header and its slices, as one run of bytes no two of which are alike.</summary>
  internal static byte[] Picture(int seed, int length = 32) => _Unit(0x00, (byte)seed, length);

  /// <summary>A slice start code, which is inside a picture and must never end one.</summary>
  internal static byte[] Slice(int seed, int length = 16) => _Unit(0x01, (byte)seed, length);

  /// <summary>
  /// Bytes for a payload that is not elementary stream data — sound, or a private stream's contents.
  /// </summary>
  /// <remarks>
  /// Never zero, so that nothing this builder produces can spell a start code where a test did not
  /// ask for one.
  /// </remarks>
  internal static byte[] Bytes(int seed, int length) {
    var result = new byte[length];
    for (var i = 0; i < length; ++i)
      result[i] = (byte)(1 + (seed * 37 + i * 11) % 254);

    return result;
  }

  internal static byte[] Concat(params byte[][] parts) {
    var total = 0;
    foreach (var part in parts)
      total += part.Length;

    var result = new byte[total];
    var at = 0;
    foreach (var part in parts) {
      part.CopyTo(result, at);
      at += part.Length;
    }

    return result;
  }

  /// <summary>Splits a run of bytes into consecutive pieces of the given lengths, the rest last.</summary>
  internal static byte[][] Split(byte[] data, params int[] lengths) {
    var pieces = new List<byte[]>();
    var at = 0;
    foreach (var length in lengths) {
      pieces.Add(data[at..(at + length)]);
      at += length;
    }

    if (at < data.Length)
      pieces.Add(data[at..]);

    return pieces.ToArray();
  }

  private static byte[] _Unit(byte code, byte seed, int length) {
    var body = Bytes(seed, Math.Max(0, length - 4));
    var result = new byte[4 + body.Length];
    result[0] = 0x00;
    result[1] = 0x00;
    result[2] = 0x01;
    result[3] = code;
    body.CopyTo(result, 4);
    return result;
  }

  // ------------------------------------------------------------------------------------------
  // Container elements
  // ------------------------------------------------------------------------------------------

  private static void _StartCode(Stream output, byte streamId) {
    output.WriteByte(0x00);
    output.WriteByte(0x00);
    output.WriteByte(0x01);
    output.WriteByte(streamId);
  }

  /// <summary>
  /// Writes a pack header in whichever of the two layouts was asked for.
  /// </summary>
  /// <remarks>
  /// They share a start code and nothing else. ISO/IEC 11172-1 opens with the four bits <c>0010</c>
  /// and is twelve bytes; ISO/IEC 13818-1 opens with the two bits <c>01</c>, is fourteen, and ends
  /// with a byte whose low three bits count the stuffing after it.
  /// </remarks>
  private static void _WritePack(Stream output, int systemsVersion, int stuffing, int ordinal) {
    const long _MUX_RATE = 3528;

    _StartCode(output, 0xBA);

    // A plausible system clock reference that advances pack by pack; nothing reads it, and a value
    // that stood still would look like a file whose clock had stopped.
    var scr = 45_000L + ordinal * 3600L;

    if (systemsVersion == 1) {
      output.WriteByte((byte)(0x21 | ((scr >> 30) & 0x07) << 1));
      _Write15(output, (scr >> 15) & 0x7FFF);
      _Write15(output, scr & 0x7FFF);

      // '1' then twenty-two bits of mux rate then '1'.
      output.WriteByte((byte)(0x80 | (_MUX_RATE >> 15)));
      output.WriteByte((byte)((_MUX_RATE >> 7) & 0xFF));
      output.WriteByte((byte)(((_MUX_RATE << 1) & 0xFE) | 0x01));
      return;
    }

    const long _EXTENSION = 0;
    output.WriteByte((byte)(0x40 | ((scr >> 30) & 0x07) << 3 | 0x04 | ((scr >> 28) & 0x03)));
    output.WriteByte((byte)((scr >> 20) & 0xFF));
    output.WriteByte((byte)(((scr >> 15) & 0x1F) << 3 | 0x04 | ((scr >> 13) & 0x03)));
    output.WriteByte((byte)((scr >> 5) & 0xFF));
    output.WriteByte((byte)((scr & 0x1F) << 3 | 0x04 | ((_EXTENSION >> 7) & 0x03)));
    output.WriteByte((byte)((_EXTENSION & 0x7F) << 1 | 0x01));

    output.WriteByte((byte)((_MUX_RATE >> 14) & 0xFF));
    output.WriteByte((byte)((_MUX_RATE >> 6) & 0xFF));
    output.WriteByte((byte)((_MUX_RATE & 0x3F) << 2 | 0x03));
    output.WriteByte((byte)(0xF8 | (stuffing & 0x07)));

    for (var i = 0; i < (stuffing & 0x07); ++i)
      output.WriteByte(0xFF);
  }

  private static void _Write15(Stream output, long value) {
    output.WriteByte((byte)((value >> 7) & 0xFF));
    output.WriteByte((byte)(((value << 1) & 0xFE) | 0x01));
  }

  /// <summary>
  /// Writes a system header, which states buffer bounds for the streams and is not a packet.
  /// </summary>
  /// <remarks>
  /// Six bytes of bounds for the programme and three per stream. Present so that a reader is shown
  /// walking past it: read as a PES packet its first two bytes would be taken for a header and the
  /// rest of the file would be lost.
  /// </remarks>
  private static void _WriteSystemHeader(Stream output, IReadOnlyList<MpegPsTestPacket> packets) {
    var ids = new List<byte>();
    foreach (var packet in packets)
      if (!ids.Contains(packet.StreamId))
        ids.Add(packet.StreamId);

    var length = 6 + 3 * ids.Count;
    _StartCode(output, 0xBB);
    output.WriteByte((byte)(length >> 8));
    output.WriteByte((byte)(length & 0xFF));

    output.WriteByte(0x80); // marker and the top of the rate bound
    output.WriteByte(0x0D);
    output.WriteByte(0xC1);
    output.WriteByte(0x21); // audio bound, fixed and CSPS flags
    output.WriteByte(0xA1); // lock flags and the video bound
    output.WriteByte(0x7F); // packet rate restriction and reserved bits

    foreach (var id in ids) {
      output.WriteByte(id);
      output.WriteByte(0xE0); // '11', the buffer bound scale and the top of its size
      output.WriteByte(0xE8);
    }
  }

  /// <summary>Writes a program stream map, the one place a program stream names its codecs.</summary>
  private static void _WriteStreamMap(Stream output, IReadOnlyList<(byte Type, byte StreamId)> entries) {
    var mapLength = 4 * entries.Count;
    var length = 2 + 2 + 2 + mapLength + 4;

    _StartCode(output, 0xBC);
    output.WriteByte((byte)(length >> 8));
    output.WriteByte((byte)(length & 0xFF));

    output.WriteByte(0xE0); // current, and version zero
    output.WriteByte(0xFF); // reserved and marker
    output.WriteByte(0x00); // no descriptors for the programme
    output.WriteByte(0x00);
    output.WriteByte((byte)(mapLength >> 8));
    output.WriteByte((byte)(mapLength & 0xFF));

    foreach (var (type, streamId) in entries) {
      output.WriteByte(type);
      output.WriteByte(streamId);
      output.WriteByte(0x00); // no descriptors for the stream
      output.WriteByte(0x00);
    }

    // A CRC nothing here checks, present because the field is part of the structure and a map without
    // it would put four bytes of the next element inside this one.
    for (var i = 0; i < 4; ++i)
      output.WriteByte(0x00);
  }

  private static void _WritePadding(Stream output, int length) {
    _StartCode(output, 0xBE);
    output.WriteByte((byte)(length >> 8));
    output.WriteByte((byte)(length & 0xFF));
    for (var i = 0; i < length; ++i)
      output.WriteByte(0xFF);
  }

  /// <summary>Writes one PES packet, in whichever of the two header layouts was asked for.</summary>
  private static void _WritePes(Stream output, MpegPsTestPacket packet, int systemsVersion) {
    using var header = new MemoryStream();

    if (systemsVersion == 1) {
      if (packet.Pts != null && packet.Dts != null) {
        _WriteTimestamp(header, 0x30, packet.Pts.Value);
        _WriteTimestamp(header, 0x10, packet.Dts.Value);
      } else if (packet.Pts != null)
        _WriteTimestamp(header, 0x20, packet.Pts.Value);
      else
        header.WriteByte(0x0F);
    } else {
      var flags = packet.Pts == null ? 0x00 : packet.Dts == null ? 0x80 : 0xC0;
      var data = packet.Pts == null ? 0 : packet.Dts == null ? 5 : 10;

      header.WriteByte(0x81); // '10', and the original-and-copy bit ffmpeg sets
      header.WriteByte((byte)flags);
      header.WriteByte((byte)data);
      if (packet.Pts != null)
        _WriteTimestamp(header, (byte)(packet.Dts == null ? 0x20 : 0x30), packet.Pts.Value);
      if (packet.Pts != null && packet.Dts != null)
        _WriteTimestamp(header, 0x10, packet.Dts.Value);
    }

    var headerBytes = header.ToArray();
    var length = headerBytes.Length + packet.Payload.Length;

    _StartCode(output, packet.StreamId);
    output.WriteByte((byte)(length >> 8));
    output.WriteByte((byte)(length & 0xFF));
    output.Write(headerBytes, 0, headerBytes.Length);
    output.Write(packet.Payload, 0, packet.Payload.Length);
  }

  /// <summary>
  /// Writes one 33-bit timestamp across the five bytes it is spread over.
  /// </summary>
  /// <remarks>
  /// Four bits of code, then the value in pieces of 3, 15 and 15 bits, each followed by a marker bit
  /// set to one so that a run of timestamps can never spell a start code.
  /// </remarks>
  private static void _WriteTimestamp(Stream output, byte code, long value) {
    output.WriteByte((byte)(code | ((value >> 30) & 0x07) << 1 | 0x01));
    _Write15(output, (value >> 15) & 0x7FFF);
    _Write15(output, value & 0x7FFF);
  }
}
