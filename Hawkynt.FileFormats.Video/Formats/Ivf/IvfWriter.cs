using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Ivf;

/// <summary>Writes one coded video stream as a version-zero Duck IVF file.</summary>
public sealed class IvfWriter : IVideoContainerWriter<IvfWriter> {

  private const int _HEADER_SIZE = 32;
  private const int _FRAME_HEADER_SIZE = 12;

  private readonly MediaStreamInfo _stream;
  private readonly MemoryStream _output = new();
  private readonly uint _rate;
  private readonly uint _scale;
  private uint _frameCount;
  private bool _finished;

  private IvfWriter(IReadOnlyList<MediaStreamInfo> streams) {
    ArgumentNullException.ThrowIfNull(streams);
    if (streams.Count != 1)
      throw new NotSupportedException($"IVF carries exactly one video stream; {streams.Count} stream(s) were supplied.");

    var stream = streams[0];
    if (stream.Index != 0 || stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("IVF's only stream must be video stream index zero.");
    if (stream.Codec == CodecTag.None)
      throw new NotSupportedException("IVF needs the coded stream's four-byte codec tag.");
    if (stream.Width is <= 0 or > ushort.MaxValue || stream.Height is <= 0 or > ushort.MaxValue)
      throw new NotSupportedException(
        $"IVF stores width and height as unsigned 16-bit values; {stream.Width}x{stream.Height} does not fit.");

    var timeBase = stream.TimeBase.IsKnown
      ? stream.TimeBase
      : stream.FrameRate.IsKnown
        ? new Rational(stream.FrameRate.Denominator, stream.FrameRate.Numerator)
        : Rational.Unknown;
    if (!timeBase.IsKnown || timeBase.Numerator <= 0 || timeBase.Denominator <= 0
        || timeBase.Numerator > uint.MaxValue || timeBase.Denominator > uint.MaxValue)
      throw new NotSupportedException(
        "IVF needs a positive time base whose numerator and denominator each fit in 32 bits.");

    this._stream = stream;
    this._rate = (uint)timeBase.Denominator;
    this._scale = (uint)timeBase.Numerator;
    this._output.SetLength(_HEADER_SIZE);
    this._output.Position = _HEADER_SIZE;
  }

  /// <summary>Gets the primary file extension for this format.</summary>
  public static string PrimaryExtension => ".ivf";
  /// <summary>Gets the file extensions supported by this format.</summary>
  public static string[] FileExtensions => [".ivf"];

  /// <summary>Creates a writer for the specified stream descriptions and metadata.</summary>
  public static IvfWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) {
    ArgumentNullException.ThrowIfNull(metadata);
    return new(streams);
  }

  /// <summary>Writes the specified coded packet to the container.</summary>
  public void WritePacket(CodedPacket packet) {
    if (this._finished)
      throw new InvalidOperationException("The IVF writer has already been finished.");
    if (packet.StreamIndex != 0)
      throw new InvalidDataException($"IVF has only stream zero; packet stream {packet.StreamIndex} cannot be written.");
    if (packet.Data.Length > uint.MaxValue)
      throw new InvalidDataException($"An IVF frame may be at most {uint.MaxValue} bytes.");
    if (packet.PresentationTimestamp is not { } timestamp)
      throw new InvalidDataException("IVF stores an explicit 64-bit timestamp for every frame; the packet has none.");
    if (this._frameCount == uint.MaxValue)
      throw new InvalidDataException("IVF's 32-bit frame count cannot represent another frame.");

    Span<byte> header = stackalloc byte[_FRAME_HEADER_SIZE];
    BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)packet.Data.Length);
    BinaryPrimitives.WriteInt64LittleEndian(header[4..], timestamp);
    this._output.Write(header);
    this._output.Write(packet.Data.Span);
    ++this._frameCount;
  }

  /// <summary>Finishes writing the container and returns its encoded bytes.</summary>
  public byte[] Finish() {
    if (this._finished)
      throw new InvalidOperationException("The IVF writer has already been finished.");

    this._finished = true;
    var buffer = this._output.GetBuffer().AsSpan(0, _HEADER_SIZE);
    "DKIF"u8.CopyTo(buffer);
    BinaryPrimitives.WriteUInt16LittleEndian(buffer[4..], 0);
    BinaryPrimitives.WriteUInt16LittleEndian(buffer[6..], _HEADER_SIZE);
    BinaryPrimitives.WriteUInt32LittleEndian(buffer[8..], this._stream.Codec.Value);
    BinaryPrimitives.WriteUInt16LittleEndian(buffer[12..], (ushort)this._stream.Width);
    BinaryPrimitives.WriteUInt16LittleEndian(buffer[14..], (ushort)this._stream.Height);
    BinaryPrimitives.WriteUInt32LittleEndian(buffer[16..], this._rate);
    BinaryPrimitives.WriteUInt32LittleEndian(buffer[20..], this._scale);
    BinaryPrimitives.WriteUInt32LittleEndian(buffer[24..], this._frameCount);
    BinaryPrimitives.WriteUInt32LittleEndian(buffer[28..], 0);

    return this._output.ToArray();
  }
}
