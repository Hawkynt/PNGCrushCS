using System;
using System.Collections.Generic;

namespace FileFormat.Core;

/// <summary>
/// A picture a container carries about itself rather than as part of its film — a cover, a poster
/// frame, a thumbnail.
/// </summary>
/// <remarks>
/// The bytes are kept in the format they were embedded in rather than decoded, because that is what
/// a muxer writing another container has to hand over and decoding it first would only lose the
/// original. What it carries *about* itself is an <see cref="ImageMetadata"/>, which is the same
/// model every still in this library uses: cover art is an image, and an image's metadata already
/// has a home.
/// </remarks>
/// <param name="Data">The embedded picture, in whichever image format it was embedded as.</param>
/// <param name="MimeType">The media type the container declared for it, where it declared one.</param>
/// <param name="Description">The caption the container carried with it.</param>
/// <param name="Kind">What the picture is for — "cover", "poster", as the container names it.</param>
/// <param name="Metadata">The picture's own metadata, where it was read out.</param>
public sealed record CoverArt(
  byte[] Data,
  string? MimeType = null,
  string? Description = null,
  string? Kind = null,
  ImageMetadata? Metadata = null);

/// <summary>What a container says about one of its streams, beyond what decoding it needs.</summary>
/// <param name="Index">The stream's position in the container.</param>
/// <param name="Kind">What the stream carries.</param>
/// <param name="Codec">The code naming the codec its packets are coded with.</param>
/// <param name="Language">RFC 5646 language tag, where the container states one.</param>
/// <param name="Name">The name the writer gave the stream.</param>
public readonly record struct MediaStreamMetadata(
  int Index,
  MediaStreamKind Kind,
  CodecTag Codec,
  string? Language = null,
  string? Name = null);

/// <summary>
/// Container-independent metadata carried alongside a film, the counterpart of
/// <see cref="ImageMetadata"/> for moving pictures. Every field is optional — a container that
/// cannot hold a given facet simply never populates it, and a muxer that cannot emit one drops it
/// explicitly rather than inventing a substitute.
/// </summary>
/// <remarks>
/// This exists from the start rather than being bolted on later for the reason the image side proved:
/// a model added after the readers is a model the readers do not fill in, and a title that was in the
/// file but never read is indistinguishable from a file that never had one. Carrying it from the
/// first container means the second one has something to be written from.
/// <para/>
/// Like <see cref="ImageMetadata"/> this is an interchange model, not a byte-exact container.
/// Round-tripping one container through it may reorder or re-encode fields even though nothing was
/// lost; byte-exact preservation of a single container is a matter for that container's own
/// passthrough, beneath this model.
/// </remarks>
public sealed class VideoMetadata {

  /// <summary>The empty instance, which is what a container with no metadata reports.</summary>
  public static readonly VideoMetadata Empty = new();

  /// <summary>The title of the work. RIFF <c>INAM</c>, MP4 <c>©nam</c>, Matroska <c>Title</c>.</summary>
  public string? Title { get; init; }

  /// <summary>Who made it. RIFF <c>IART</c>, MP4 <c>©ART</c>.</summary>
  public string? Artist { get; init; }

  /// <summary>What it belongs to — a series, an album, a disc. RIFF <c>IPRD</c>, MP4 <c>©alb</c>.</summary>
  public string? Album { get; init; }

  /// <summary>The tool that wrote the file. RIFF <c>ISFT</c>, MP4 <c>©too</c>.</summary>
  public string? EncodedBy { get; init; }

  /// <summary>
  /// When the work was created, as the file states it.
  /// </summary>
  /// <remarks>
  /// An offset-carrying instant, because containers disagree about the clock: MP4 counts seconds
  /// from 1904 UTC where RIFF writes a local date as text. Normalising both to a local
  /// <see cref="DateTime"/> would throw away which of the two it was.
  /// </remarks>
  public DateTimeOffset? CreationTime { get; init; }

  /// <summary>How long the film runs, where the container states enough to say.</summary>
  /// <remarks>
  /// From the header's own declaration, which a file left unfinished may state wrongly. It is not a
  /// count of what is actually in the file — that costs a walk of every packet, which is the
  /// caller's to ask for.
  /// </remarks>
  public TimeSpan? Duration { get; init; }

  /// <summary>One entry per stream the container holds, in stream order.</summary>
  public IReadOnlyList<MediaStreamMetadata> Streams { get; init; } = [];

  /// <summary>Pictures the container carries about itself — cover, poster, thumbnail.</summary>
  public IReadOnlyList<CoverArt> CoverArt { get; init; } = [];

  /// <summary>
  /// Free-text annotations, keyed the way <see cref="ImageMetadata.TextEntries"/> keys them.
  /// </summary>
  /// <remarks>
  /// The same type as the image side deliberately: a comment is a comment, and the keyword-plus-text
  /// shape RIFF <c>INFO</c>, MP4 free-form atoms and Matroska tags all reduce to is the one PNG's
  /// text chunks already have. Sharing it means the optimiser that already decides what is worth
  /// keeping works on both.
  /// </remarks>
  public IReadOnlyList<TextMetadataEntry> TextEntries { get; init; } = [];

  /// <summary>True when every facet is absent.</summary>
  public bool IsEmpty
    => this.Title == null && this.Artist == null && this.Album == null && this.EncodedBy == null
       && this.CreationTime == null && this.Duration == null
       && this.Streams.Count == 0 && this.CoverArt.Count == 0 && this.TextEntries.Count == 0;
}
