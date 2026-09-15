using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>The container description and field layout shared by the AVUI reader and writer.</summary>
internal sealed class AvuiVideoFormat {

  internal const int Width = 720;
  internal const int NtscHeight = 486;
  internal const int PalHeight = 576;
  internal const int OpaqueDepth = 16;
  internal const int AlphaDepth = 32;

  private const int _VISUAL_SAMPLE_ENTRY_SIZE = 86;
  private const int _CODEC_PRIVATE_SIZE = 144;
  private const int _APRG_SIZE = 24;
  private const int _ARES_SIZE = 120;

  private readonly int _skip;

  private AvuiVideoFormat(int height, bool interlaced) {
    this.Height = height;
    this.Interlaced = interlaced;
    this._skip = height == NtscHeight ? 10 : 16;
  }

  internal int Height { get; }
  internal bool Interlaced { get; }
  internal int Fields => this.Interlaced ? 2 : 1;
  internal int RowStep => this.Interlaced ? 2 : 1;

  /// <summary>
  /// The length AVUI's decoder uses to find the optional alpha companion. Interlaced opaque packets
  /// carry four further trailing zero bytes, but those bytes are not part of this boundary.
  /// </summary>
  internal int OpaqueLength => checked(2 * Width * (this.Height + this._skip) + (this.Interlaced ? 4 : 0));

  internal int OpaquePacketLength => checked(this.OpaqueLength + (this.Interlaced ? 4 : 0));
  internal int AlphaPacketLength => checked(2 * this.OpaqueLength + 4);

  internal static AvuiVideoFormat For(MediaStreamInfo stream, bool defaultInterlaced) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException($"An Avid Meridien picture needs pixels; {stream.Width}x{stream.Height} was supplied.");
    if (stream.Width != Width || stream.Height is not (NtscHeight or PalHeight))
      throw new NotSupportedException(
        $"Avid Meridien Uncompressed is defined here only at its D1 sizes, 720x486 and 720x576; "
        + $"the stream is {stream.Width}x{stream.Height}.");

    return new(stream.Height, _Interlaced(stream.CodecPrivateData.Span, defaultInterlaced));
  }

  /// <summary>The first displayed row belonging to one coded field.</summary>
  internal int FirstRow(int field) {
    if (!this.Interlaced)
      return 0;

    // NTSC stores the odd field first; PAL stores the even field first. This is the observable AVUI
    // ordering used by Avid/FFmpeg rather than a generic interlace convention.
    return this.Height == NtscHeight ? 1 - field : field;
  }

  /// <summary>Byte offset of one field's first UYVY row inside the opaque part of a packet.</summary>
  internal int FieldOffset(int field) {
    if ((uint)field >= (uint)this.Fields)
      throw new ArgumentOutOfRangeException(nameof(field));

    var halfBlank = checked(Width * this._skip);
    if (!this.Interlaced)
      return checked(halfBlank * 2);

    var fieldPayload = checked(Width * this.Height);
    return field == 0
      ? halfBlank
      : checked(halfBlank + fieldPayload + 4 + halfBlank);
  }

  /// <summary>
  /// Byte offset of one field's first stored alpha byte. Alpha uses the same field-sized spacing as
  /// UYVY, but only every other byte is significant and the byte is inverted (zero means opaque).
  /// </summary>
  internal int AlphaFieldOffset(int field) => checked(this.OpaqueLength + 5 + this.FieldOffset(field));

  /// <summary>Builds the complete QuickTime visual sample entry an MP4/MOV muxer needs.</summary>
  internal byte[] SampleEntry(int depth) {
    if (depth is not (OpaqueDepth or AlphaDepth))
      throw new ArgumentOutOfRangeException(nameof(depth), depth, "AVUI sample descriptions are 16-bit opaque or 32-bit with alpha.");

    var result = new byte[_VISUAL_SAMPLE_ENTRY_SIZE + _CODEC_PRIVATE_SIZE];
    BinaryPrimitives.WriteUInt32BigEndian(result, checked((uint)result.Length));
    "AVUI"u8.CopyTo(result.AsSpan(4));
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(14), 1); // data-reference index
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(32), Width);
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(34), checked((ushort)this.Height));
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(36), 0x00480000); // 72 dpi
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(40), 0x00480000);
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(48), 1); // one frame per sample

    var compressor = "Avid Meridien Uncompressed"u8;
    result[50] = checked((byte)compressor.Length);
    compressor.CopyTo(result.AsSpan(51));
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(82), checked((ushort)depth));
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(84), 0xFFFF); // no colour table

    var codec = result.AsSpan(_VISUAL_SAMPLE_ENTRY_SIZE);
    BinaryPrimitives.WriteUInt32BigEndian(codec, _APRG_SIZE);
    "APRGAPRG0001"u8.CopyTo(codec[4..]);
    codec[19] = this.Interlaced ? (byte)2 : (byte)1;

    BinaryPrimitives.WriteUInt32BigEndian(codec[24..], _ARES_SIZE);
    "ARESARES0001"u8.CopyTo(codec[28..]);
    BinaryPrimitives.WriteUInt32BigEndian(codec[40..], 0x98);
    BinaryPrimitives.WriteUInt32BigEndian(codec[44..], Width);
    BinaryPrimitives.WriteUInt32BigEndian(codec[48..], checked((uint)this.Height));
    BinaryPrimitives.WriteUInt32BigEndian(codec[52..], 1);
    BinaryPrimitives.WriteUInt32BigEndian(codec[56..], 32);
    BinaryPrimitives.WriteUInt32BigEndian(codec[60..], 2);

    return result;
  }

  /// <summary>Takes the visual sample depth when <paramref name="data"/> is a whole AVUI sample entry.</summary>
  internal static int SampleDepth(ReadOnlySpan<byte> data) {
    if (!_TryVisualSampleEntry(data, out var entry))
      return 0;

    return BinaryPrimitives.ReadUInt16BigEndian(entry[82..]);
  }

  /// <summary>
  /// Finds APRG either in bare codec private data or behind the fixed visual sample-entry header.
  /// Missing APRG deliberately means the caller's fallback: the reference decoder historically
  /// assumes interlaced, while a newly created encoder chooses progressive.
  /// </summary>
  private static bool _Interlaced(ReadOnlySpan<byte> data, bool fallback) {
    if (_TryVisualSampleEntry(data, out var entry))
      data = entry[_VISUAL_SAMPLE_ENTRY_SIZE..];

    while (data.Length >= _APRG_SIZE) {
      var atomSize = BinaryPrimitives.ReadUInt32BigEndian(data);
      if (atomSize is < 8 or > int.MaxValue || atomSize > data.Length)
        break;

      if (atomSize >= _APRG_SIZE && data[4..16].SequenceEqual("APRGAPRG0001"u8))
        return data[19] != 1;

      data = data[checked((int)atomSize)..];
    }

    return fallback;
  }

  private static bool _TryVisualSampleEntry(ReadOnlySpan<byte> data, out ReadOnlySpan<byte> entry) {
    entry = default;
    if (data.Length < _VISUAL_SAMPLE_ENTRY_SIZE || !data[4..8].SequenceEqual("AVUI"u8))
      return false;

    var size = BinaryPrimitives.ReadUInt32BigEndian(data);
    if (size < _VISUAL_SAMPLE_ENTRY_SIZE || size > data.Length)
      return false;

    entry = data[..checked((int)size)];
    return true;
  }
}
