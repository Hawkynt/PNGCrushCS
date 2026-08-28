using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;

namespace FileFormat.Mng;

/// <summary>Assembles MNG file bytes from an MNG data model.</summary>
public static class MngWriter {

  // Profile-validity + critical transparency + validity of the extended transparency bits +
  // semitransparency + background transparency. Bit 1 is added when FRAM is emitted.
  private const uint _VLC_PROFILE_WITH_TRANSPARENCY = 457;
  private const uint _SIMPLE_MNG_FEATURES = 1u << 1;
  private static readonly byte[] _MNG_SIGNATURE = { 0x8A, 0x4D, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
  private static readonly byte[] _PNG_SIGNATURE = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

  public static byte[] ToBytes(MngFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Width <= 0) throw new ArgumentOutOfRangeException(nameof(file), "MNG width must be positive.");
    if (file.Height <= 0) throw new ArgumentOutOfRangeException(nameof(file), "MNG height must be positive.");
    if (file.TicksPerSecond < 0) throw new ArgumentOutOfRangeException(nameof(file), "MNG tick rate cannot be negative.");

    var hasExplicitTiming = file.FrameDelays.Count > 0;
    if (hasExplicitTiming && file.FrameDelays.Count != file.Frames.Count)
      throw new InvalidDataException("MNG FrameDelays must be empty or contain one value per frame.");
    if (hasExplicitTiming && file.Frames.Count > 1 && file.TicksPerSecond == 0)
      throw new InvalidDataException("MNG animations with explicit frame delays require a nonzero ticks_per_second.");
    foreach (var delay in file.FrameDelays)
      if (delay is < 0 or > 0x7fffffff)
        throw new InvalidDataException("MNG interframe delays must be in [0, 0x7fffffff] ticks.");

    using var ms = new MemoryStream();
    ms.Write(_MNG_SIGNATURE);

    var frameCount = (uint)file.Frames.Count;
    var nominalPlayTime = _NominalPlayTime(file, hasExplicitTiming);
    var simplicity = _VLC_PROFILE_WITH_TRANSPARENCY | (hasExplicitTiming ? _SIMPLE_MNG_FEATURES : 0u);
    var mhdrData = new byte[MngHeader.StructSize];
    var mhdr = new MngHeader(
      (uint)file.Width,
      (uint)file.Height,
      (uint)file.TicksPerSecond,
      frameCount + 1, // mandatory initial background layer plus one image layer per PNG
      frameCount,
      nominalPlayTime,
      simplicity
    );
    mhdr.WriteTo(mhdrData);
    _WriteChunk(ms, "MHDR", mhdrData);

    _WriteTerm(ms, file);

    for (var frameIndex = 0; frameIndex < file.Frames.Count; ++frameIndex) {
      if (hasExplicitTiming)
        _WriteFrameDelay(ms, file.FrameDelays[frameIndex]);

      var frame = file.Frames[frameIndex];
      if (frame.Length < _PNG_SIGNATURE.Length || !frame.AsSpan(0, _PNG_SIGNATURE.Length).SequenceEqual(_PNG_SIGNATURE))
        throw new InvalidDataException("MNG frames must be complete PNG datastreams.");

      var offset = _PNG_SIGNATURE.Length;
      var sawIhdr = false;
      var sawIend = false;
      while (offset + 12 <= frame.Length) {
        var chunkLength = checked((int)_ReadUInt32BE(frame.AsSpan(offset)));
        var totalChunkSize = checked(12 + chunkLength);
        if (offset + totalChunkSize > frame.Length)
          throw new InvalidDataException("Embedded PNG contains a truncated chunk.");

        var type = frame.AsSpan(offset + 4, 4);
        sawIhdr |= type.SequenceEqual("IHDR"u8);
        sawIend |= type.SequenceEqual("IEND"u8);
        ms.Write(frame, offset, totalChunkSize);
        offset += totalChunkSize;

        if (sawIend)
          break;
      }

      if (!sawIhdr || !sawIend)
        throw new InvalidDataException("Embedded PNG frame must contain IHDR and IEND chunks.");
    }

    _WriteChunk(ms, "MEND", []);
    return ms.ToArray();
  }

  private static uint _NominalPlayTime(MngFile file, bool hasExplicitTiming) {
    if (file.TicksPerSecond == 0 || file.Frames.Count == 0)
      return 0;

    ulong total = 0;
    if (hasExplicitTiming) {
      foreach (var delay in file.FrameDelays)
        total += (uint)delay;
    } else
      total = (uint)file.Frames.Count;

    return total > 0x7fffffff ? 0x7fffffffu : (uint)total;
  }

  /// <summary>
  /// Writes a FRAM that selects framing mode 1 and resets the default interframe delay. The optional
  /// name is empty, therefore a null separator precedes the four change bytes; only the delay field follows.
  /// </summary>
  private static void _WriteFrameDelay(Stream stream, int delay) {
    var data = new byte[10];
    data[0] = 1; // framing mode 1: one foreground layer per frame
    data[1] = 0; // empty subframe name separator
    data[2] = 2; // change_interframe_delay: yes, and reset default
    data[3] = 0; // no timeout/termination change
    data[4] = 0; // no clipping change
    data[5] = 0; // no sync-id change
    _WriteUInt32BE(data.AsSpan(6), (uint)delay);
    _WriteChunk(stream, "FRAM", data);
  }

  private static void _WriteTerm(Stream stream, MngFile file) {
    if (file.TermAction != MngTermAction.Repeat) {
      if (file.TermAction is < MngTermAction.ShowLast or > MngTermAction.ShowFirst)
        throw new ArgumentOutOfRangeException(nameof(file), "Invalid MNG TERM action.");

      _WriteChunk(stream, "TERM", [(byte)file.TermAction]);
      return;
    }

    if (file.ActionAfterIterations is < MngTermAction.ShowLast or > MngTermAction.ShowFirst)
      throw new ArgumentOutOfRangeException(nameof(file), "TERM action_after_iterations must be 0, 1, or 2.");
    if (file.RepeatDelay < 0)
      throw new ArgumentOutOfRangeException(nameof(file), "TERM repeat delay cannot be negative.");
    if (file.NumPlays < 0 || file.NumPlays > 0x7fffffff)
      throw new ArgumentOutOfRangeException(nameof(file), "TERM iteration maximum must be between 0 and 0x7fffffff.");

    var termData = new byte[10];
    termData[0] = (byte)MngTermAction.Repeat;
    termData[1] = (byte)file.ActionAfterIterations;
    _WriteUInt32BE(termData.AsSpan(2), (uint)file.RepeatDelay);
    _WriteUInt32BE(termData.AsSpan(6), (uint)file.NumPlays);
    _WriteChunk(stream, "TERM", termData);
  }

  private static void _WriteChunk(Stream stream, string type, byte[] data) {
    Span<byte> lengthBytes = stackalloc byte[4];
    _WriteUInt32BE(lengthBytes, (uint)data.Length);
    stream.Write(lengthBytes);

    Span<byte> typeBytes = stackalloc byte[4];
    for (var i = 0; i < 4; ++i)
      typeBytes[i] = (byte)type[i];
    stream.Write(typeBytes);

    if (data.Length > 0)
      stream.Write(data);

    var crc = new Crc32();
    crc.Append(typeBytes);
    if (data.Length > 0)
      crc.Append(data);

    Span<byte> crcBytes = stackalloc byte[4];
    _WriteUInt32BE(crcBytes, crc.GetCurrentHashAsUInt32());
    stream.Write(crcBytes);
  }

  private static uint _ReadUInt32BE(ReadOnlySpan<byte> data)
    => (uint)(data[0] << 24 | data[1] << 16 | data[2] << 8 | data[3]);

  private static void _WriteUInt32BE(Span<byte> target, uint value) {
    target[0] = (byte)(value >> 24);
    target[1] = (byte)(value >> 16);
    target[2] = (byte)(value >> 8);
    target[3] = (byte)value;
  }
}
