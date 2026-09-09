# Unified Image / Video Pipeline Architecture

> Target architecture for converging PNGCrushCS image, multi-image, animation, texture, and video handling on one capability-driven media graph that supports both packet-preserving light conversion and full decoded processing.

## Status and implementation checkpoint

This document is both the architecture target and the handoff document for incremental adoption. API sketches are illustrative unless a section explicitly says that it is implemented.

### Current implementation checkpoint — 2026-09-09

Phase 1 is on `main`, merged in #361. It was written on a branch as #350, which was superseded: that
version had Bob's untyped `FromRawImage` stop adapting and require an already-quantised `Indexed8`
picture, on the reasoning that a typed API should not convert behind the caller's back. That is a
defensible position for the typed overloads and the wrong one for the untyped entry point, because
the untyped one is what the registry calls and the registry's writer contract promises that any
`RawImage` can be written and read back — so it turned the write-coverage sweep red for one format
of 878 while every other format still adapted. Phase 2 should decide that question for every format
at once rather than one at a time.

Implemented in commit `613174dc2b1c6b0daf41a40e200b5d25e5884612`:

- `IRawPixelFormat<TSelf>` compile-time raw pixel-format descriptors using static abstract interface members.
- `RawPixelFormatTraits`, including storage family, storage bits, byte width, nominal component precision, alpha representation, plane count, indexed bit depth, and YUV subsampling.
- `RawPixelFormats` as the compatibility mapping between typed representations and the current runtime `PixelFormat` enum.
- marker types for every current raw representation in `FileFormat.Core.PixelFormats`.
- logical `Indexed1` through `Indexed16` marker types without extending or renumbering the legacy enum.
- `RawImage<TPixel>` as a zero-copy typed wrapper around the current `RawImage`.
- validation for logical indexed types narrower than their compatibility storage. For example, `RawImage<Indexed6>` currently stores one byte per index through legacy `PixelFormat.Indexed8`, but rejects index values above 63 and palettes above 64 entries.
- NUnit coverage for legacy trait parity, typed construction, zero-copy legacy adaptation, and narrowed indexed validation.

This first implementation deliberately does **not** replace the existing `RawImage`, `PixelFormat`, readers, converters, or writers. That is the compatibility boundary that lets the architecture be adopted format by format.

### Deliberate transitional compromises

`RawImage<TPixel>` currently wraps `RawImage` instead of inheriting from a new common raw-surface base. This avoids a flag-day change while writer/reader contracts are still non-generic. Once the typed contracts and dynamic registry descriptors exist, the wrapper can be collapsed into the final shared-root representation without changing format-facing semantics.

Logical indexed widths that have no current enum member use a lossless compatibility storage:

| Typed representation | Current compatibility storage |
| --- | --- |
| `Indexed1` | packed `Indexed1` |
| `Indexed2`, `Indexed3` | byte-per-index `Indexed8` |
| `Indexed4` | packed `Indexed4` |
| `Indexed5`, `Indexed6`, `Indexed7` | byte-per-index `Indexed8` |
| `Indexed8` | `Indexed8` |
| `Indexed9` … `Indexed15` | little-endian `Indexed16` |
| `Indexed16` | `Indexed16` |

This distinction is intentional: the generic type describes the **logical index domain** while the compatibility layer describes current in-memory storage. It proves that `TPixel` must not mean “one CLR struct physically stored per logical pixel”.

### Next implementation work

Future work should continue in this order unless a concrete migration exposes a better dependency:

1. make legacy `RawImage` helpers (`IsIndexed`, bit/byte width, floating/planar classification, YUV layout, alpha capability) consume `RawPixelFormatTraits` instead of maintaining duplicate switches;
2. introduce a first-class palette representation while keeping adapters for current RGB-triplet `Palette` + `AlphaTable` storage;
3. add direct exact indexed-to-indexed conversion paths, especially `Indexed8 -> IndexedN` when the used index domain fits, without expanding through BGRA;
4. define typed writer input capabilities / write modes and teach the source generator to expose them dynamically without reflection;
5. migrate a representative set before broad rollout: PNG, GIF, PCX, ICO/CUR, and one constrained fixed-palette/retro format;
6. only after those contracts prove out, move quantization/dithering/resampling out of writer-side `Ensure*` paths and into explicit pipeline stages.

Do **not** start by rewriting every reader/writer. The purpose of Phase 1 is to establish the contracts and conversion graph so each format can migrate independently.

## Goals

The architecture has six primary goals:

1. make unsupported operations difficult or impossible to express;
2. make every lossy or semantic transformation explicit;
3. permit arbitrary cross-format conversion when a meaningful conversion policy exists;
4. preserve light paths that never decode/re-encode when packet/subresource passthrough is sufficient;
5. represent multi-image/video relationships semantically instead of flattening them to anonymous image arrays;
6. keep common pixel and packet paths extremely fast with direct kernels, pooling, SIMD, vectors, and hardware intrinsics.

“Convert between every format” does not mean inventing semantics. It means the graph can describe every meaningful route and exposes missing choices such as frame selection, page timing, target icon variants, palette policy, or resampler instead of silently making them inside a writer.

## Why change the current model

PNGCrushCS already has many of the right pieces: separate video demux/decode/encode/mux contracts, `RawImage`, source-generated registries, metadata models, `VideoMode`, quantizers/ditherers, multi-image support, and optimized pixel converters.

The weak point is the boundary between those pieces.

Today a writer can receive a runtime-tagged `RawImage` and call helpers such as `EnsureFormat`, `EnsureIndexedAtMost`, or `EnsureIndexed`. That can make a writer responsible for serialization **and** pixel conversion, palette generation, quantization, or palette mapping. A caller cannot know from the contract whether the input is supported exactly or will be mutated until it happens to fit.

Likewise, `IMultiImageFileFormat` can expose ICO, APNG, DCX, TIFF, and other collections as “N images”, even though an icon alternative, animation frame, document page, and mip level have different semantics and different transformation rules.

The target architecture separates:

- encoded files/streams and containers;
- coded packets/subresources;
- semantic media topology;
- typed raw raster surfaces;
- explicit transformations;
- capability negotiation and mode selection;
- serialization;
- metadata and structural annotations;
- quality/performance observation.

The planner then selects the least invasive valid path.

A Matroska H.264 stream copied to a compatible MP4 should not be decoded because a decoder exists. Extracting one frame from that same stream, resizing it, choosing a palette, quantizing/dithering it, and turning it into several ICO variants should descend into the decoded raster domain and use the same graph.

## Core rules

### Writers serialize; they do not repair input

A writer or selected write mode receives an asset that already satisfies the representation it explicitly declares.

A writer may:

- validate runtime invariants;
- pack indices/samples/planes into the file’s required bit layout;
- perform format-defined reversible transforms;
- choose compression parameters;
- choose PNG row filters, LZW dictionary behavior, entropy coding, packetization, frame delta representation, and similar representation-preserving decisions;
- serialize supported metadata;
- write required companion files.

A writer must not silently:

- resize or resample;
- quantize;
- dither;
- invent or select an arbitrary palette;
- tone-map;
- alter frame timing;
- reinterpret color without explicit color information;
- discard metadata contrary to policy.

### Compile-time representation constraints, runtime value constraints

A writer accepting `RawImage<Indexed6>` cannot accidentally receive `RawImage<Rgba32>`.

The type system should handle representation identity where practical. Runtime validation handles values that should not become type parameters:

- width/height;
- palette count;
- actual maximum index used;
- fixed palette identity;
- frame count;
- topology;
- color profile/range constraints;
- timing constraints.

Do not create types such as `RawImage<Indexed6, Width320, Height200>`. Geometry is mode data, not generic-type identity.

### Destructive operations require policy

Deterministic representation changes may be automatic when they are exact.

Subjective or destructive operations require a named stage/policy:

- resampling;
- palette generation;
- quantization;
- dithering;
- tone mapping;
- frame-rate conversion;
- temporal filtering;
- metadata loss.

A low-level `Convert<TTarget>()` must never mean “do whatever is necessary until it fits”.

### Encoded passthrough is first class

Demux/mux remain separate from decode/encode.

If a destination container accepts the coded stream, the planner should prefer packet copy. If container conventions require only framing/extradata/timestamp adaptation, use an explicit bitstream/packet transform rather than decoding pixels.

This is the same practical distinction behind FFmpeg stream copy versus transcoding, but the implementation remains native to PNGCrushCS.

### Multi-image relationships are topology

Pages, icon/cursor alternatives, animation frames, mip levels, texture-array layers, cube faces, and video frames are not interchangeable entries in `RawImage[]`.

The semantic asset layer must preserve those relationships.

### Metadata has scope

Color interpretation belongs with samples. Cursor hotspots belong to cursor variants. Animation timing/placement belongs to frames. Camera data belongs to the captured image/page/frame or enclosing asset. Container metadata belongs to the container/stream.

Transformations update only metadata/structure that they semantically affect.

### Performance is architectural

Do not force conversions through BGRA32 simply because it is convenient.

The graph should prefer direct exact kernels and avoid unnecessary allocations. Common conversion paths should be optimized using the highest useful managed SIMD/vector/intrinsic implementation available, with scalar reference fallbacks.

## Media graph layers

A conversion graph may cross several abstraction levels:

```text
encoded bytes / file / stream
          |
          v
      demux / parse ---------------------------------------+
          |                                                |
          | coded packets / native subresources            | container metadata
          v                                                |
 packet / bitstream transforms                             |
          |                                                |
          +---------------------------+                    |
          |                           |                    |
          | light path                | decode             |
          |                           v                    |
          |                    semantic media asset        |
          |                           |                    |
          |                    RawImage<TPixel>            |
          |                           |                    |
          |                  processing pipeline           |
          |                           |                    |
          |                        encode                  |
          |                           |                    |
          +---------------------------+                    |
                        coded packets                     |
                              |                           |
                              v                           |
                             mux <-------------------------+
                              |
                              v
                         encoded output
```

Entering the decoded domain is optional. The planner crosses that boundary only when the requested operation requires decoded semantics.

## Typed raw raster surfaces

### `RawImage<TPixel>`

`RawImage<TPixel>` is one atomic decoded raster surface whose generic argument describes the semantic pixel/sample representation.

Examples:

```csharp
RawImage<Rgb24>
RawImage<Rgba64>
RawImage<Gray16>
RawImage<Indexed4>
RawImage<Indexed6>
RawImage<Indexed8>
RawImage<Yuv420P10>
RawImage<RgbaF16>
```

The generic argument is a **format descriptor**, not the physical element type of a `TPixel[]` array.

That matters for:

- sub-byte indices;
- planar YUV;
- packed bitfields;
- retro machine layouts;
- future compressed/raw GPU surfaces.

A six-bit logical index may sensibly occupy one byte in working memory. File writers are responsible for the target file’s actual bit/nibble/plane packing.

### Runtime compatibility

Dynamic registries, viewers, and graph planners cannot always name the closed generic type at compile time. Therefore the architecture needs both:

- closed generic identities for compile-time contracts and optimized kernels;
- non-generic runtime descriptors for discovery, graph search, and UI.

The current Phase 1 implementation uses a zero-copy typed wrapper over `RawImage`. That is transitional glue, not a requirement that the final representation remain a wrapper.

### Pixel-format traits

Pixel-format facts belong in one descriptor system rather than repeated switches in readers, planners, converters, writers, and UI.

Traits include at least:

- packed/indexed/floating/planar family;
- current storage width;
- logical indexed width;
- nominal component precision;
- alpha representation;
- plane count;
- chroma subsampling;
- compatibility mapping during migration.

Future traits may add:

- channel/color model;
- channel ordering;
- endianness;
- signed/unsigned/normalized semantics;
- premultiplication;
- canonical row/plane alignment where relevant.

Static abstract interface members let generic kernels access these facts without reflection.

### Palette representation

The current `byte[] Palette` RGB triplets plus `AlphaTable` are too narrow as the final architecture.

A palette should become a first-class object that can preserve sufficient precision and alpha independently of the target file’s palette storage.

The raster owns or references the palette necessary to interpret its indices. **Palette policy** belongs to the target write mode and planner.

### Surface color interpretation

Information necessary to interpret sample values stays with the surface:

- primaries;
- transfer function;
- YUV matrix;
- full/limited range;
- chroma location;
- ICC/profile information where it defines the meaning of samples.

GPS, camera make, title, cursor hotspot, animation duration, etc. are not pixel interpretation.

## Semantic media assets

`RawImage<TPixel>` is not synonymous with “image file”. An asset describes how raster surfaces relate.

### Still image

```text
StillImageAsset
  Surface
  Metadata
```

### Page collection

Examples: TIFF, DCX, scanned document pages.

```text
PageCollectionAsset
  DocumentMetadata
  Page[]
    Surface
    PageMetadata
```

Pages are ordered independent items unless the format imposes additional coupling.

### Variant set

Examples: ICO, CUR, ICNS, device-specific resources.

```text
VariantSetAsset
  Variant[]
    IntendedSize / scale / role
    Surface
    VariantMetadata / structure
```

Variants are alternative renderings of one semantic object, not pages.

When generating many target sizes, derive each from the best available source rather than cascading destructive resizes:

```text
256 -> 16
256 -> 24
256 -> 32
256 -> 48
```

not:

```text
256 -> 48 -> 32 -> 24 -> 16
```

Cursor hotspots are structural data on each cursor variant. Resize scales them; crop translates them; rotation rotates them. An operation that places the hotspot outside the target should produce a plan-validation problem rather than silently clamping it.

### Animation sequence

Examples: GIF, APNG, ANI, MNG.

```text
ImageSequenceAsset
  Canvas
  Looping
  SequenceMetadata
  Frame[]
    Surface or frame region
    Placement
    Duration
    Blend
    Disposal
    FrameMetadata
```

A sequence target may impose shared constraints across frames, such as common dimensions, pixel representation, global palette, or timing rules.

Arbitrary visual processing is safest on presentation semantics:

```text
encoded delta/subrect frames
  -> compose presentation state
  -> presentation frames
  -> visual processing
  -> target frame optimizer
  -> target rectangles/blend/disposal
  -> encode
```

When a lighter structural transformation can preserve native frame encoding, the planner may remain above the presentation-frame layer.

### Texture asset

Examples: DDS, BLP, WAL, WAD textures.

```text
TextureAsset
  TextureMetadata
  Layer[]
    Face[]
      Mip[]
        Surface
```

The topology can represent mip chains, arrays, cube faces, and volume/depth slices where required.

A target may couple palettes or pixel formats across the whole texture. Mip generation and palette generation must respect that scope rather than treating every subresource independently.

### Video / streaming sequence

Video is the streaming temporal form. It should not require materializing all frames in memory.

```text
VideoFrame
  Surface
  PresentationTimestamp
  Duration
  optional decode/order flags
  FrameMetadata
```

Stateless raster stages can be reused between still-image and video paths. Stateful stages retain history/order as necessary.

Examples of stateful stages:

- deinterlacing;
- temporal denoise;
- frame-rate conversion;
- motion interpolation;
- codecs themselves.

## Metadata scopes

### Surface interpretation

Properties needed to interpret samples as colors belong to the raw surface.

### Structural annotations

These participate in geometry/time transformations:

- cursor hotspot;
- frame placement;
- frame duration/delay;
- blend/disposal;
- mip level;
- layer/face identity;
- page relationship;
- pixel aspect ratio where part of presentation geometry.

### Descriptive and capture metadata

Examples:

- EXIF;
- GPS;
- camera/lens make/model;
- exposure parameters;
- XMP;
- IPTC;
- title/author/comment;
- capture timestamp.

These live on the most appropriate asset/item scope rather than on every raw buffer indiscriminately.

### Container and stream metadata

Examples:

- title/author/album;
- creation time;
- stream language;
- time base;
- codec identifiers/configuration;
- annotations carried by the container.

These remain available on packet-copy paths where no raster exists.

### Opaque native preservation data

Unknown chunks, APP segments, maker notes, private boxes, or other native structures may be retained on compatible same-format/light paths.

The architecture distinguishes:

- semantic metadata that can be mapped across formats;
- opaque native metadata that is only valid while its carrier remains compatible.

### Metadata policy

The planner should support explicit policies such as:

```text
PreserveWherePossible
FailOnMetadataLoss
StripAll
StripSensitive
```

The plan reports metadata consequences before writing.

Transformations update or invalidate dependent information intentionally:

- resize updates pixel dimensions;
- crop/rotate transforms structural coordinates;
- orientation normalization updates/removes orientation tags;
- resampling may invalidate embedded thumbnails;
- color conversion changes output profile/description;
- DPI behavior follows an explicit physical-size-vs-DPI policy.

## Formats and write modes

A format is a family of encodings. A selected write mode is a concrete output contract.

```text
Format
  -> enumerate candidate write modes
  -> planner evaluates source -> mode paths
  -> choose/fixate mode
  -> prepare asset
  -> mode validates
  -> writer serializes
```

### Mode responsibilities

A mode may declare:

- semantic asset kind;
- accepted `RawImage<TPixel>` representation(s);
- dimension/range/aspect constraints;
- palette policy;
- alpha capability;
- color/profile constraints;
- page/frame/item count constraints;
- topology/coupling constraints;
- metadata capabilities;
- extension/filename/companion-file requirements;
- codec/profile/container constraints for video.

`VideoMode` can evolve toward this role; eventually `ImageWriteMode` / `MediaWriteMode` is a less misleading name when the same abstraction describes PNG, ICO, APNG, DDS, and actual video.

### Typed mode inputs

Conceptually:

```csharp
public interface IImageWriteMode<TPixel>
  where TPixel : IRawPixelFormat<TPixel> {
  ImageGeometryConstraints Geometry { get; }
  PaletteConstraint? Palette { get; }

  void Write(RawImage<TPixel> image, Stream target);
}
```

The actual implementation may separate mode descriptors and writer instances, but the invariant matters: an `IImageWriteMode<Indexed6>` cannot receive `RawImage<Rgba32>`.

Dynamic selection uses a non-generic descriptor and source-generated typed dispatch rather than reflection.

## Palette policy

Palette capability must distinguish hard restrictions from presets.

```text
NoPalette
FixedOnly
Arbitrary
FixedOrArbitrary
```

A palette contract may also specify:

- entry-count ranges;
- required fixed palettes;
- preferred/conventional palettes;
- alpha rules;
- palette precision;
- sharing scope.

### Fixed palettes

A fixed-only mode offers mapping candidates:

```text
transformed source
  -> map/dither to fixed palette A
  -> map/dither to fixed palette B
  -> ...
```

It does not invoke a palette generator to invent palette C.

### Arbitrary palettes

```text
source
  -> palette generator / quantizer
  -> palette mapper
  -> optional dither
  -> RawImage<IndexedN>
```

Palette generation, palette mapping, and dithering are distinct stages.

### Palette scope

A mode may declare palette ownership as:

- per surface;
- per page/variant/frame;
- global sequence;
- global texture/mip chain;
- another explicit scope.

This is essential for global animation palettes and texture formats whose mip levels share a palette.

## Capability negotiation and planning

Every node advertises input/output capabilities.

```text
RasterCaps
  pixel representations
  geometry
  color interpretation
  alpha
  palette constraints

PacketCaps
  media type
  codec/profile/level
  codec private data
  framing
  timing

AssetCaps
  still/pages/variants/sequence/texture
  topology
  metadata scope
```

An edge is valid only when source output and target input capabilities intersect.

### Planning sequence

Planning is constraint solving rather than a hard-coded sequence, but the useful default reasoning is:

1. identify source/container capabilities;
2. enumerate target modes;
3. reject unreachable modes;
4. strongly prefer passthrough/light paths;
5. choose/fixate a mode;
6. satisfy structural and geometry requirements;
7. satisfy color/pixel/palette requirements;
8. satisfy metadata policy;
9. validate prepared asset;
10. encode/mux/write.

Geometry normally precedes final palette reduction because resampling can create colors while downsampling/cropping can remove enough colors to make a previously lossy palette conversion exact.

However this is a graph dependency, not a universal fixed order. High-quality resizing may require conversion to a linear working color space before the resampler.

### Loss classes

Every conversion edge declares semantic cost.

Suggested classes:

```text
StructuralExact
SemanticExact
VisuallyExact
Lossy
PolicyRequired
```

Examples:

- `Indexed4 -> Indexed8`: structural exact;
- `Indexed8 -> Indexed6` with all indices <=63 and preserved palette prefix: structural exact;
- sparse `Indexed8 -> Indexed6` after palette compaction/remap: visually/semantically exact but not structurally exact;
- `Rgb24 -> Indexed6` with <=64 exact colors: potentially visually exact;
- `Rgb24 -> Indexed6` requiring reduction: lossy + quantizer policy;
- `Rgb48 -> Rgb24`: precision-reducing;
- RGB/YUV conversion: only well-defined with explicit color interpretation.

### Plan scoring

There is no universal scalar “best”. Candidate ordering may include:

- loss class;
- metadata loss;
- measured visual quality;
- packet-copy/re-encode avoidance;
- CPU cost;
- memory/latency;
- output size;
- caller preferences.

Default policy should prefer exact passthrough, then exact transformations, then visually exact transformations. Destructive operations should require explicit consent/policy.

## Geometry before final color reduction

Consider:

```text
Indexed8 / 1024x768 / 120 colors
  -> nearest resize to 320x200
Indexed8 / 320x200 / 54 used colors
  -> exact compact/remap
Indexed6 / 320x200
```

Quantizing before resize would have destroyed information unnecessarily.

For interpolating resize:

```text
Indexed8
  -> color working surface
  -> Lanczos/bicubic/etc.
  -> new colors
  -> target palette generation or fixed-palette mapping
  -> dither
  -> IndexedN
```

Palette indices themselves must never be interpolated as if they were colors.

Nearest-neighbor can preserve index values/palette identity directly.

General rule:

> Do not satisfy a downstream destructive constraint earlier than necessary.

## Light conversion / remux

### Packet-copy path

Example: compatible H.264/AAC in Matroska to MP4.

```text
Matroska
  -> demux
  -> H.264 packets -------------------+
  -> AAC packets ------------------+  |
                                   |  |
                    optional bitstream adaptation
                                   |  |
                                   v  v
                                  MP4 mux
                                   |
                                   v
                                  MP4
```

No video frame exists. No raster conversion runs. Quality is unchanged.

### Bitstream adaptation

Some container transitions need:

- framing conversion;
- codec extradata construction;
- timestamp rewrite;
- header/config packet changes.

These are explicit packet-level stages and still avoid decode/re-encode.

### Mixed copy/transcode

A multi-stream plan can copy audio/subtitles while decoding and transforming only video. Planning is per stream, not “transcode whole container or nothing”.

## Full cross-domain examples

### Video frame to icon

```text
video file
  -> demux
  -> select stream
  -> decode to requested time/frame
  -> VideoFrame<Yuv...>
  -> color conversion if needed
  -> branch to requested icon sizes
  -> resize
  -> choose target mode/palette strategy
  -> quantize/map/dither if required
  -> icon variants
  -> ICO/CUR writer
```

### Indexed PNG to indexed target

```text
PNG decode
  -> RawImage<Indexed8>
  -> exact index-domain check / palette compact-remap if allowed
  -> RawImage<Indexed6>
  -> target mode
```

There is no reason to expand through ARGB/BGRA and quantize again when the source already fits.

### APNG to GIF

```text
APNG
  -> ImageSequenceAsset
  -> geometry/timing processing
  -> select GIF palette strategy
       global: analyze all transformed frames -> one palette -> map/dither
       local: per-frame palette generation/map/dither
  -> GIF write mode
```

### Texture to icon/image variants

```text
DDS/BLP/WAL texture
  -> preserve texture topology
  -> choose best source mip/subresource per requested output
  -> optional resample
  -> variant set / still image
  -> target writer
```

## Viewer as pipeline workbench

`Crush.Viewer` should become the visual front end to the same graph used by the library and CLI.

### Every display has an inspectable pipeline

Examples:

```text
PNG -> decode Indexed8 -> palette lookup -> display surface
JPEG -> decode YCbCr -> color conversion -> display surface
DDS -> select layer/face/mip -> decode/decompress -> display surface
APNG -> frame compose -> display color transform -> display surface
video -> demux -> decode -> selected time/frame -> display surface
```

The viewer should show:

- current source topology;
- current negotiated raw representation;
- color interpretation;
- every presentation-only conversion;
- active processing nodes;
- target mode constraints when converting.

### Visual pipeline builder

Users should be able to insert/connect nodes such as:

- stream/page/frame/variant/mip selection;
- crop/resize/rotate;
- color-space/transfer conversion;
- quantizer;
- fixed-palette selector;
- palette mapper;
- ditherer;
- metadata policy;
- encoder/write mode;
- muxer/container.

Invalid links are rejected by capability negotiation. Missing policy appears as an unresolved node, not an invisible default in a writer.

### Visually aided algorithm selection

Quantizers, ditherers, resamplers, and encoding choices can be compared as graph branches.

Example matrix:

```text
                no dither      Floyd-Steinberg      ordered ...
MedianCut       preview/KPI    preview/KPI          ...
Wu              preview/KPI    preview/KPI          ...
Octree          preview/KPI    preview/KPI          ...
```

The user can select by visible output, metric, time, memory, output size, or combined policy.

### Quality metrics

Metrics are observers/nodes operating on a controlled comparison representation.

Potential metrics:

- exact sample equality;
- MSE/RMSE;
- SNR/PSNR;
- Delta E variants;
- SSIM / future perceptual metrics;
- alpha error;
- temporal/flicker measures;
- metadata preservation warnings/count;
- runtime;
- allocation/peak working memory;
- output size.

Do not compare raw palette indices or samples in unrelated transfer/color spaces and call the number “image quality”.

### Reproducible recipes

The graph should be serializable as a recipe containing:

- node identities;
- parameters;
- selected mode;
- explicit policies;
- target format/container.

The CLI/library can execute the same recipe headlessly. UI layout coordinates are not conversion semantics.

## High-performance pixel conversion

Arbitrary conversion is only practical if pixel transformation is a specialized kernel problem rather than a generic per-pixel abstraction.

### Direct conversion graph

Register profitable direct edges for common pairs:

```text
Bgr24 <-> Rgb24
Rgba32 <-> Bgra32
Indexed4 -> Indexed8
Indexed8 -> Indexed6 when exact
Gray8 -> Gray16
Rgb48 -> Rgba64
planar YUV precision/subsampling conversions where semantics are known
```

Fallback graph search may chain edges when no direct kernel exists, but path cost must penalize:

- precision loss;
- palette destruction;
- color-space excursions;
- unnecessary allocations;
- unnecessary decode/re-encode.

### SIMD / vectors / intrinsics

Hot kernels should use the simplest implementation that wins in benchmarks:

1. fixed-width `Vector128<T>` / `Vector256<T>` / `Vector512<T>` where portable fixed-width SIMD is sufficient;
2. architecture-specific X86/Arm/Wasm intrinsics when higher-level vectors cannot express the useful operation efficiently;
3. `Vector<T>` where variable-width portable SIMD is convenient;
4. optimized scalar reference/fallback.

Useful kernels include:

- channel shuffle;
- widening/narrowing;
- planar/interleaved conversion;
- matrix color/YUV transforms;
- alpha fill/drop/premultiply when semantics permit;
- palette lookup batches;
- threshold/packing/unpacking;
- quality-metric accumulation.

Runtime dispatch chooses a kernel once per operation, never per pixel.

### Generic specialization

Closed generic source/target identities give the JIT/source generator static information for binding specialized kernels:

```text
RawImage<TSource> -> RawImage<TTarget>
```

Dynamic graph descriptors point to those generated typed delegates without reflection.

### Memory ownership

Avoid one `byte[]` allocation per stage.

Use as appropriate:

- `Span<T>` / `ReadOnlySpan<T>`;
- `Memory<T>` / `ReadOnlyMemory<T>`;
- `ArrayPool<T>`;
- `MemoryPool<T>` / `IMemoryOwner<T>`;
- explicit row/plane views;
- `IBufferWriter<byte>` for encoded output;
- planner/scheduler lifetime analysis so temporary buffers are reused after the final consumer.

Packet passthrough should slice/retain packet memory rather than copy payloads just to cross an API boundary.

### Parallelism

Safe independent work may run in parallel:

- pages;
- icon variants;
- mip branches;
- image tiles/row bands;
- algorithm/metric candidates;
- video stages where ordering permits.

Stateful stages declare ordering constraints. Parallelism must not make deterministic algorithms nondeterministic.

## Encoded output sinks

Common sinks should support:

```text
void Encode(..., Stream)
void Encode(..., IBufferWriter<byte>)
byte[] Encode(...)
bool TryEncode(..., Span<byte>, out int bytesWritten) // when practical
```

`IBufferWriter<byte>` is preferable to forcing compressed formats to know their final size up front.

File output remains a distinct context because some formats use filename/extension semantics or create companions.

## Source-generated registration

PNGCrushCS already uses source-generated image/video registries. The new graph should build on that rather than introducing reflection.

The generator should eventually discover/register:

- typed raw representations a reader may emit;
- typed raw representations a write mode accepts;
- supported semantic asset kinds;
- packet/container/codec capabilities;
- conversion kernels/stages;
- metadata capabilities;
- runtime descriptors for UI/planning.

Closed generic contracts provide compile-time safety; generated non-generic descriptors provide dynamic discovery.

## Reader architecture

Readers should preserve the most faithful practical native semantic representation:

- indexed input remains indexed;
- high-bit-depth input remains high-bit-depth;
- native YUV decode can remain YUV;
- pages/variants/sequences/textures retain topology;
- metadata remains scoped.

Do not force readers to RGB8 merely because the viewer needs a display surface. Display conversion is a consumer pipeline.

Readers may expose several access depths:

```text
metadata/header probe
native/container structure
coded packets/subresources
semantic asset
typed raster surface
```

Light operations stop at the shallowest level that satisfies the request.

## Writer architecture

A writer is reached after mode selection and asset preparation.

The selected mode validates all runtime invariants. The typed writer contract ensures the representation category is one it actually implements.

Eventually current APIs such as:

```text
IImageFromRawImage<TSelf>.FromRawImage(RawImage)
```

should evolve toward typed write inputs / modes rather than “accept anything and coerce”.

The source generator can discover multiple closed typed interfaces on one format so a writer may explicitly support, for example:

```text
Rgb24
Rgba32
Indexed8
```

without a runtime `switch` that secretly converts unsupported input.

## Conversion completeness

A route may be classified as:

```text
Reachable exactly
Reachable with explicit policy
Reachable with loss
Unreachable because required semantics have no bridge
```

Examples of required policy:

- video -> still: frame/time selection;
- video -> icon: frame/time + target variant sizes;
- still -> animation/video: timing/frame construction;
- pages -> animation: page-to-frame timing;
- texture -> still: subresource selection;
- animation -> still: frame/time/composited-state selection.

Missing semantics are choices, not defaults buried in format code.

## Migration plan

### Phase 1 — typed raw-image foundation **IN PROGRESS**

Implemented:

- typed descriptor interface;
- central typed trait model;
- marker types for current formats;
- logical Indexed1..Indexed16 descriptors;
- transitional zero-copy `RawImage<TPixel>` wrapper;
- narrowed indexed value validation;
- initial tests.

Still needed before Phase 1 is considered complete:

- route legacy `RawImage` classification/size helpers through the trait source of truth;
- first-class palette model + legacy adapters;
- direct exact indexed-to-indexed conversions;
- tests for palette precision/alpha and direct indexed conversion;
- performance baseline for representative direct conversions.

### Phase 2 — typed writer capabilities and modes

- define generic writer/mode input contracts;
- generate runtime descriptors from closed generic implementations;
- evolve `VideoMode`/write-mode metadata;
- model fixed/arbitrary/preferred palette policy and scope;
- stop adding new writer-side coercion.

Representative migrations first: PNG, GIF, PCX, ICO/CUR, one constrained retro/fixed-palette format.

### Phase 3 — conversion graph and quality classes

- register raster transformations as graph edges;
- mark exact/visual/lossy/policy-required semantics;
- make resampling/quantization/dithering explicit stages;
- add direct SIMD/intrinsic kernels and benchmark coverage;
- make Save As / CLI use the planner instead of format-specific coercion.

### Phase 4 — semantic asset topology

- add pages, variants, sequences, textures;
- keep `IMultiImageFileFormat` as compatibility/view facade during migration;
- move cursor hotspot and animation controls into structural asset data;
- migrate mipmap formats from ad-hoc `byte[][]` storage to texture subresources.

### Phase 5 — metadata scopes

- separate sample interpretation from descriptive metadata;
- add asset/item/container/stream scopes;
- make metadata preservation/loss part of planning;
- preserve opaque native data only on compatible paths.

### Phase 6 — video graph integration

- expose packet-copy and packet-adaptation edges;
- wrap decoded frames with timing semantics;
- reuse typed raster stages;
- negotiate codec/muxer contracts in the graph;
- support mixed stream copy/transcode plans.

### Phase 7 — viewer pipeline workbench

- inspect every display pipeline;
- navigate topology (page/frame/variant/mip/stream);
- visually construct conversion graphs;
- compare quantizer/ditherer/resampler branches;
- show numeric quality/performance metrics;
- serialize recipes for CLI/library use.

### Phase 8 — remove legacy coercion

After all formats are migrated:

- remove writer-side implicit quantization/coercion paths;
- retire duplicated `PixelFormat` switches where typed traits replace them;
- reduce/remove flat `IMultiImageFileFormat` as appropriate;
- make the planner the only general cross-format conversion path;
- collapse transitional `RawImage<TPixel>` wrapper/base layering into the final common representation if still beneficial.

## Validation strategy

### Capability tests

- every declared stage/input/output capability is internally consistent;
- every write mode has a serializer;
- generic writer calls reject unsupported raw representations at compile time;
- runtime mode validation rejects invalid geometry/palette/topology.

### Conversion graph tests

- exact edges round-trip where mathematically possible;
- indexed-to-indexed conversions avoid hidden RGB expansion;
- high-bit-depth paths avoid 8-bit hubs;
- YUV conversion requires known color interpretation;
- graph search never chooses a more lossy path when a less-lossy one exists.

### Remux tests

- packet-copy routes preserve payload bytes where framing permits;
- timing/extradata/container metadata map correctly;
- pure passthrough plans instantiate no decoder/encoder.

### Semantic asset tests

- page ordering and metadata scopes survive;
- variants preserve alternative semantics/hotspots;
- geometry transforms update structural coordinates;
- animation composition preserves rendered semantics;
- texture mip/array/cube topology survives compatible conversion.

### Metadata tests

- supported metadata round-trips semantically;
- `FailOnMetadataLoss` rejects a lossy target;
- native opaque metadata is only promised on compatible carriers;
- transformations update/invalidate dependent metadata intentionally.

### Performance tests

- scalar reference and SIMD/intrinsic kernels are bit-identical where required;
- benchmark hot pixel-format pairs;
- record throughput/allocation/vector-width scaling;
- benchmark graph planning overhead separately from processing;
- keep wall-clock performance tests advisory, not correctness gates.

## Reference material and design precedent

This architecture is original to PNGCrushCS. The following material is used as specification/design precedent, not copied implementation code:

- FFmpeg stream copy/transcoding: <https://ffmpeg.org/ffmpeg.html>
- FFmpeg demuxer/muxer separation: <https://ffmpeg.org/ffmpeg-formats.html>
- GStreamer capability negotiation/caps: <https://gstreamer.freedesktop.org/documentation/additional/design/negotiation.html> and <https://gstreamer.freedesktop.org/documentation/additional/design/caps.html>
- W3C PNG Third Edition / APNG semantics: <https://www.w3.org/TR/png-3/>
- Microsoft DDS programming guide: <https://learn.microsoft.com/windows/win32/direct3ddds/dx-graphics-dds-pguide>
- Microsoft icon/cursor resource formats: <https://learn.microsoft.com/windows/win32/menurc/resource-file-formats>
- CIPA Exif standards: <https://www.cipa.jp/e/std/std-sec.html>
- Microsoft C# static abstract interface members: <https://learn.microsoft.com/dotnet/csharp/advanced-topics/interface-implementation/static-virtual-interface-members>
- Microsoft .NET SIMD/intrinsics guidance: <https://learn.microsoft.com/dotnet/standard/simd>
- SixLabors ImageSharp generic `Image<TPixel>` documentation as conceptual precedent for compile-time pixel identity: <https://docs.sixlabors.com/articles/imagesharp/gettingstarted.html>

No third-party implementation code is required or copied for this architecture or the current typed raw-image foundation.

## Target end state

One media graph should be able to express all of these without separate ad-hoc conversion frameworks:

```text
PNG Indexed8 -> GIF Indexed8
JPEG -> TIFF page
TIFF pages -> APNG frames
DDS mip -> PNG
video frame -> resized/dithered ICO variants
ANI cursor frames -> GIF/APNG
Matroska H.264 -> MP4 H.264 packet copy
Matroska H.264 -> resized/quantized GIF through full decode/process/encode
```

The same graph powers:

- library APIs;
- CLI conversion;
- optimizers;
- viewer display;
- visual pipeline building;
- quality comparison.

The central boundary remains:

```text
reader / demuxer
  -> faithful native / semantic representation
  -> explicit negotiated transformations
  -> exact target-mode representation
  -> writer / muxer
```

When no decoded transformation is necessary, the graph stays at the packet/native-resource level. When full processing is requested, it can descend to typed raw surfaces, perform geometry/color/palette/temporal processing, measure alternative results, and encode an entirely different kind of media asset.

That is how PNGCrushCS can support arbitrary conversion without making arbitrary decisions.
