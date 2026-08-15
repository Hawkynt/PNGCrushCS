using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Core;

/// <summary>
/// Takes a container apart into the streams it declares and the coded packets it holds. The first of
/// the four things a video pipeline is made of: demux.
/// </summary>
/// <remarks>
/// A container reader knows where the packets are and nothing about what is in them. It never
/// decodes, never returns a picture, and never refuses a file for holding a codec it has not heard
/// of — that refusal belongs to whoever is asked for a decoder, because a container full of H.264
/// is a perfectly good container and copying its packets into another one needs no decoder at all.
/// <para/>
/// This separation is the whole point of the type. A reader that decoded as it demuxed could only
/// ever hand back pictures, and pictures are the one thing a transcode does not want: reading one
/// container and writing another means moving packets, and a decoder in the middle would be a
/// generation of loss for no purpose.
/// <para/>
/// <see cref="ReadPackets(TSelf)"/> is lazily enumerated and may be enumerated more than once. A
/// film is not a list of pictures held in memory, and it is not a list of coded packets held in
/// memory either — a two-hour recording is tens of gigabytes and the caller usually wants one frame
/// of it.
/// </remarks>
public interface IVideoContainerReader<TSelf> : IVideoFormatMetadata<TSelf> where TSelf : IVideoContainerReader<TSelf> {

  /// <summary>Parses the container's structure from raw bytes, without reading any packet's contents.</summary>
  /// <remarks>
  /// A container outlives this call — its packets are windows onto the file and are walked long
  /// afterwards — while a span promises nothing about how long the memory behind it stays valid, so
  /// an implementation has to copy. Prefer <see cref="FromBytes"/> where the caller already holds an
  /// array; for a film, one avoided copy is the difference between reading it and doubling it.
  /// </remarks>
  static abstract TSelf FromSpan(ReadOnlySpan<byte> data);

  /// <summary>Parses the container from an array the caller is done with, without copying it.</summary>
  /// <remarks>
  /// The default copies, because the safe thing to do with a span is to own it. A container that can
  /// keep the caller's array instead should override this and say so — both of the ones here do.
  /// </remarks>
  static virtual TSelf FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    return TSelf.FromSpan(data);
  }

  /// <summary>Parses the container from a file.</summary>
  static virtual TSelf FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);

    return TSelf.FromBytes(File.ReadAllBytes(file.FullName));
  }

  /// <summary>The streams this container declares, in the order it declares them.</summary>
  /// <remarks>
  /// Every stream, including the ones nothing here decodes. A stream's <see cref="MediaStreamInfo.Index"/>
  /// is its position among all of them, and leaving the undecodable ones out would renumber the rest.
  /// </remarks>
  static abstract IReadOnlyList<MediaStreamInfo> Streams(TSelf container);

  /// <summary>Walks every packet of every stream, in the order the container stores them.</summary>
  static abstract IEnumerable<CodedPacket> ReadPackets(TSelf container);

  /// <summary>Walks the packets of one stream, in storage order.</summary>
  /// <remarks>
  /// The default filters the full walk. A container with an index of its own can do better by
  /// overriding this and seeking, which is why it is virtual rather than an extension method.
  /// </remarks>
  static virtual IEnumerable<CodedPacket> ReadPackets(TSelf container, int streamIndex) {
    foreach (var packet in TSelf.ReadPackets(container))
      if (packet.StreamIndex == streamIndex)
        yield return packet;
  }

  /// <summary>What the container says about itself: title, creation time, duration, per-stream
  /// language, cover art.</summary>
  static virtual VideoMetadata Metadata(TSelf container) => VideoMetadata.Empty;
}
