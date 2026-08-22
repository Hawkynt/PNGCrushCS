# Hawkynt.FileFormats.Video

> Pure-C# video containers and codecs, with demuxing, decoding, encoding and muxing kept as four
> separate things.

The video sibling of [`Hawkynt.FileFormats.Images`](https://www.nuget.org/packages/Hawkynt.FileFormats.Images).
A decoded frame is a `FileFormat.Core.RawImage` — the same type every image format in that package
reads and writes — so saving a frame, resampling it, quantising it or comparing it uses the code that
already does those things to a photograph.

## The four contracts

| Contract | Interface | Knows about |
| --- | --- | --- |
| Demux | `IVideoContainerReader<T>` | where the packets are; nothing about what is in them |
| Decode | `IVideoCodecDecoder<T>` | one codec; nothing about which container it came from |
| Encode | `IVideoCodecEncoder<T>` | one codec; nothing about where its packets will be written |
| Mux | `IVideoContainerWriter<T>` | where to put packets; nothing about how they were made |

A container reader hands out `CodedPacket`s and never a picture. That is what makes remuxing
possible at all: reading one container and writing another is a demuxer and a muxer with nothing in
between, and the pictures come out the far side bit for bit. A reader that decoded as it demuxed
could only ever produce pictures, which turns every remux into a re-encode.

Packets and frames are reached lazily. A film is not a list of pictures held in memory, and a caller
who wants one frame of a two-hour recording pays for one frame.

## Supported

| Container | Extensions | Read | Write |
| --- | --- | --- | --- |
| Advanced Systems Format (ASF, WMV, WMA) | `.asf`, `.wmv`, `.wma`, `.wm`, `.wmx`, `.asx` | Y | — |
| AVI (RIFF) | `.avi` | Y | — |
| Flash Video (FLV) | `.flv`, `.f4v` | Y | — |
| ISO base media (MP4, QuickTime, 3GP) | `.mp4`, `.m4v`, `.mov`, `.qt`, `.3gp`, `.3g2`, `.m4a` | Y | — |
| Matroska / WebM (EBML) | `.mkv`, `.mka`, `.mks`, `.mk3d`, `.webm` | Y | — |
| H.264 byte stream (Annex B) | `.264`, `.h264`, `.avc`, `.x264` | Y | — |
| H.265 byte stream (Annex B) | `.265`, `.h265`, `.hevc`, `.x265` | Y | — |
| MPEG program stream (MPEG-1, MPEG-2, VOB) | `.mpg`, `.mpeg`, `.vob`, `.m2p`, `.m2ps` | Y | — |
| Motion JPEG stream | `.mjpg`, `.mjpeg` | Y | — |
| MPEG video elementary stream | `.m1v`, `.m2v`, `.mpv`, `.mpeg1video`, `.mpeg2video` | Y | — |
| MPEG-2 transport stream (also Blu-ray, AVCHD) | `.ts`, `.m2ts`, `.mts`, `.m2t`, `.tsv` | Y | — |
| Ogg (Theora, Vorbis, Opus, FLAC) | `.ogg`, `.ogv`, `.oga`, `.ogx`, `.opus`, `.spx` | Y | — |
| RealMedia (RealVideo, RealAudio) | `.rm`, `.rmvb`, `.ra`, `.rmj`, `.rms` | Y | — |
| Autodesk FLIC | `.fli`, `.flc`, `.flx` | Y | — |
| id RoQ | `.roq` | Y | — |
| Interplay MVE | `.mve` | Y | — |
| id Cinematic | `.cin` | Y | — |
| Westwood VQA | `.vqa` | Y | — |
| Smacker | `.smk` | Y | — |
| Electronic Arts Multimedia | `.wve`, `.cmv`, `.tgv`, `.uv`, `.uv2` | Y | — |
| Commodore CDXL | `.cdxl` | Y | — |

| Codec | Tag | Decode | Encode |
| --- | --- | --- | --- |
| Uncompressed (`BI_RGB`) | 0 | Y | — |
| Motion JPEG | `MJPG`, `mjpg`, `jpeg`, `V_MJPEG` | Y | — |
| MPEG-1 video (ISO/IEC 11172-2) | `MPG1`, `PIM1`, `mp1v` | Y | — |
| MPEG-2 video (ISO/IEC 13818-2) | `MPG2`, `MPEG`, `mp2v`, `m2v1`, `hdv1`–`hdv3`, `V_MPEG2` | Y | — |
| Microsoft RLE | `MRLE`, `mrle`, `BI_RLE8` (1), `BI_RLE4` (2) | Y | — |
| Microsoft Video 1 | `CRAM`, `MSVC`, `WHAM` | Y | — |
| Cinepak | `cvid`, `CVID` | Y | — |
| QuickTime Animation (RLE) | `rle ` | Y | — |
| Apple Video (RPZA) | `rpza`, `azpr` | Y | — |
| Apple Graphics (SMC) | `smc ` | Y | — |
| H.261 (ITU-T H.261) | `H261` | Y | — |
| H.263 (ITU-T H.263 baseline) | `H263`, `s263`, `U263` | Y | — |
| Sorenson Spark (Flash Video's H.263) | `FLV1` | Y | — |
| RealVideo 1 (revision 0 only) | `RV10`, `RV13` | Y | — |
| H.264 / AVC, Baseline I and P slices | `avc1`, `avc3`, `H264`, `X264`, `DAVC`, `VSSH`, `V_MPEG4/ISO/AVC` | Y | — |
| H.265 / HEVC, Main profile intra pictures | `hvc1`, `hev1`, `hvc2`, `hev2`, `HEVC`, `H265`, `V_MPEGH/ISO/HEVC` | Y | — |
| On2 VP3.1 | `VP31`, `VP32` (and `VP30`, refused by name) | Y | — |
| VP8 (RFC 6386) | `VP80`, `vp08`, `V_VP8` | Y | — |
| VP9, profile 0 | `VP90`, `vp09`, `V_VP9` | Y | — |
| HuffYUV / FFVHUFF | `HFYU`, `FFVH` | Y | — |
| Avid DNxHD / DNxHR (SMPTE VC-3) | `AVdn`, `AVdh`, `AVd1`, `V_DNXHD` | Y | — |
| FFV1 (RFC 9043) | `FFV1`, `V_FFV1` | Y | — |
| MPEG-4 Part 2 (ISO/IEC 14496-2) | `mp4v`, `XVID`, `DIVX`, `DX50`, `FMP4`, `MP4S`, `M4S2`, `3IV2`, `FVFW`, `RMP4`, `V_MPEG4/ISO/*` | Y | — |
| Apple ProRes (SMPTE RDD 36) | `apco`, `apcs`, `apcn`, `apch`, `ap4h`, `ap4x` | Y | — |
| VC-1 / Windows Media Video 9, intra pictures | `WMV3`, `WMV9` | Y | — |
| Microsoft MPEG-4 version 2 | `MP42`, `DIV2` | Y | — |
| Theora (Xiph.Org Theora I) | `theora`, `V_THEORA`, `Theo` | Y | — |
| FLIC (Autodesk Animator / Animator Pro) | `FLIC` (synthetic — the format states no codec tag of its own) | Y | — |

| Zip Motion Blocks Video (ZMBV) | `ZMBV` | Y | — |
| MagicYUV | `M8RG`, `M8RA`, `M8Y0`, `M8Y2`, `M8Y4`, `M8YA`, `M8G0` | Y | — |

| Ut Video | `ULRG`, `ULRA`, `ULY0`, `ULY2`, `ULY4`, `ULH0`, `ULH2`, `ULH4` | Y | — |

| TechSmith Screen Capture (TSCC) | `tscc` | Y | — |

| CamStudio Screen Codec (CSCD) | `CSCD` | Y | — |

| Flash Screen Video (FSV1) | `FSV1` | Y | — |

| Uncompressed 4:2:2 10-bit (v210) | `v210` | Y | — |

| Uncompressed RGB 10-bit (r210) | `r210` | Y | — |

| AJA Kona 10-bit RGB (r10k) | `R10k` | Y | — |

| Uncompressed 4:1:1 (y41p) | `Y41P` | Y | — |

| Cirrus Logic AccuPak (CLJR) | `CLJR` | Y | — |

| id RoQ | `RoQV` (synthetic — the format states no codec tag of its own) | Y | — |

| Flash Screen Video 2 (FSV2) | `FSV2` | Y | — |

| ZeroCodec | `ZECO` | Y | — |

| Interplay Video | `IMVE` (synthetic — the format states no codec tag of its own) | Y | — |

| Lossless Codec Library, ZLIB variant | `ZLIB` | Y | — |

| Vidvox Hap | `Hap1`, `Hap5`, `HapY`, `HapM`, `HapA` (`Hap7`, `HapH` refused by name) | Y | — |
| id Cinematic Video | `IDCV` (synthetic — the format states no codec tag of its own) | Y | — |

| Westwood VQA Video | `WSVQ` (synthetic — the format states no codec tag of its own) | Y | — |

| Electronic Arts CMV | `cmv ` (synthetic — the format states no codec tag of its own) | Y | — |

| Apple Planar RGB (8BPS) | `8BPS` | Y | — |

| GoPro CineForm (SMPTE VC-5) | `CFHD` | Y | — |

One reader for MP4, MOV, M4V and 3GP because they are one format under four names — the same box
structure with different brands in `ftyp`. Its packet boundaries are not in the data at all: `mdat`
is an undivided heap of bytes, and where each packet starts and stops is a computation over five
tables in `stbl`, which is why a file whose `moov` follows its `mdat` needs no second pass. A
fragmented file, whose sample tables live in `moof` boxes instead, is refused by name rather than
read as a film of no packets.

Classic QuickTime allows the whole movie atom to be written deflated into a single `cmov` — what
"Save As" writes for a file meant to start playing before it finishes downloading — and a reader
that only ever looked for `mvhd` straight off `moov`'s own children refused every one of these,
naming the box that happened not to be there rather than the one that was. Inflated with the zlib
the file itself names in `dcom`, what `cmvd` holds is an ordinary uncompressed `moov`; nothing about
reading it afterwards changes, because a chunk offset inside it still counts from the start of the
real file, exactly as one in an uncompressed `moov` does — a compressed header moves where the atom
tree lives and not one byte of `mdat`. A method other than `zlib`, which is the only one any file
this was measured against names, is refused by name instead of guessed at.

Measured on thirty-eight files a sweep of samples.ffmpeg.org's QuickTime and MOV samples found
written this way, seven of them opened in full — Sorenson SMC, VP3, QuickTime RLE twice over, ALAC,
SVQ1 with QCELP sound, and ZyGo — every video track's packets agreeing with `ffprobe -fflags
+noparse` on count, size, timestamp and key-frame flag, all of it: 962, 622, 1440, 214, 1 and 3586
packets across the six with pictures, matched one for one.

An MPEG program stream states its packet boundaries even less than that. It chops each elementary
stream into PES packets sized to fill 2048-byte packs, so a picture routinely spans two of them and
one of them routinely holds seven pictures. The payloads are stitched back together and cut again
where the elementary stream itself says a picture starts, which makes a packet the coded picture a
decoder wants with nothing of the container left in it — measured against `ffprobe -fflags +nofillin`
on eleven files, agreeing on every packet's size and timestamp, and reproducing ffmpeg's own extracted
elementary stream byte for byte. Its two systems standards, ISO/IEC 11172-1 and ISO/IEC 13818-1, share
one reader and one start code and no layout at all; which of them a file is, is read off its first
pack header, because a program stream has no version field, no index and no header listing its
streams.

One reader for Matroska and WebM for the reason MP4 and MOV share one: WebM is a Matroska document
with a `DocType` that says so and a shorter list of codecs allowed inside it, and which codecs are
allowed is the business of whoever is asked for a decoder. Its packet boundaries are in neither a
table nor a chunk header but in the clusters themselves, and a single block may carry several frames
at once — all four lacings are unpacked, so a laced block comes out as the packets it holds rather
than as one. Elements the writer stated no length for, which is what a stream written live produces,
end where the next element that cannot be inside them begins.

Matroska names its codecs with strings rather than four-character codes, so a stream from it carries
a `CodecId` and no tag — the exception being `V_MS/VFW/FOURCC`, whose `BITMAPINFOHEADER` holds a real
code. A stream whose blocks were compressed, header-stripped or encrypted before being written is
refused by name rather than handed on as frames it is not.

ASF is one format under three extensions: `.asf` is its own name, `.wmv` one whose first stream
carries pictures and `.wma` one whose streams carry only sound. Nothing in the file distinguishes
them, so nothing in the reader does either. Its whole structure is objects keyed by sixteen-byte
GUIDs, each stating its own length, which means an object nobody has heard of costs a skip — a file
carrying rights management, a mutual exclusion or an index reads exactly as fast as one carrying
none.

Its packets are a fixed size and frames are not, so a frame larger than a packet is cut across
several and several small frames share one. Both are put back together, because a reader that handed
the pieces out would be reporting the shape of the wire rather than the shape of the film. Three
payload forms have to be handled or ordinary files come out wrong: the one ffmpeg writes, the
single-payload form that states no length at all, and the compressed form, whose one byte of
replicated data means the payload is a run of whole frames rather than a piece of one. Every
timestamp has the file's preroll taken off it — ffmpeg writes 3100 milliseconds of it, and a reader
that kept it would report every frame of every such file three seconds late.

Measured against `ffprobe -fflags +noparse` on fifteen files — Microsoft MPEG-4 v3, WMV1, WMV2, sound
alone, sound and pictures together, two video streams at different rates, and eight assembled by hand
for the forms ffmpeg will not write — agreeing on the count, order, size and presentation timestamp of
all 549 packets. The one difference is the key frame flag on sound: ffprobe reports every audio packet
as a key frame whatever the file says, because an audio frame is independently decodable, and that is
a fact about the codec rather than anything an ASF file contains.

FLV is the one container here that declares nothing. Its nine-byte header says whether sound and
pictures are present and stops, so the streams are discovered from the tags and each codec from the
first tag belonging to it — which also means the streams are numbered in the order their first tag
appears, as ffprobe numbers them. Two payload shapes are not frames and never become packets: an AVC
sequence header, whose configuration record becomes the stream's private data, and an AAC one, which
does the same.

A transport stream is the one container here that was not designed for a file. It is a broadcast, so
there is no index, no directory and no header: the streams are found by reading the tables the
multiplex repeats — the program association table at PID 0, then a program map per program — and a
coded unit is reassembled out of the 188-byte packets it was cut into, which is also why its packets
are copies rather than windows onto the file. Blu-ray and AVCHD put a four-byte arrival timecode in
front of every packet, so the stride is 192 rather than 188; which of the two a file uses is measured
from its sync bytes rather than taken from its name. A lost packet is caught by the continuity
counter and refused by name, because a unit assembled across one is a frame with a hole in it.

Ogg is the purest framing layer here: pages, a lacing that divides them into packets, a serial number
saying which of the multiplexed bitstreams a page belongs to, and a checksum of its own — a CRC-32
sharing only its polynomial with the usual one, computed over the whole page with the checksum field
read as zeroes. Packets routinely span pages, because a page holds at most 65 025 bytes and a keyframe
of a large picture does not; they are put back together, and only those packets are copied rather than
windowed onto the file. A page's last lacing value of exactly 255 is the only thing that says a packet
continues, which is also why a packet whose length divides by 255 ends on a zero-length segment.

Its granule position is not a timestamp and the format says so — it is a stream position whose meaning
each codec's mapping defines. Vorbis and FLAC count output samples; Opus counts them at 48 kHz
whatever the encoder was fed, and the first playable one sits at the granule less the header's
pre-skip; Theora packs the count of frames to the last keyframe and the count since it into two bit
fields, whose sum counts from one, so the frame index is one less. A position also sits at the *end*
of a page and belongs to the last packet finishing on it. For Theora, where one packet is one frame,
the packets before it are counted back from it exactly; for the audio mappings, where a packet is
worth a block whose length is in the codec's own setup data, they are not, and the reader reports the
one position the file states — the timestamp of the packet beginning at the page boundary — rather
than a reconstruction.

Measured against `ffprobe -fflags +noparse` on nine files: Theora alone at three sizes, with a long
group of pictures, at 30000/1001, with duplicate-frame packets, with keyframes large enough to span
three pages, with Vorbis, with Opus, and Opus and FLAC alone. Every packet's stream, order and size is
identical across all of them, and every video packet's presentation timestamp as well. The header
packets are not packets — they are the codec's private data, reported once, framed in the Xiph lacing
Matroska uses for the same codecs so that one decoder reads a stream out of either container.

RealMedia is a flat run of chunks, each naming itself and stating its own length: `PROP` for the
rates and the duration, one `MDPR` per stream carrying that stream's codec-specific description
verbatim, `CONT` for the title, author, copyright and comment, `DATA` for the packets, `INDX` for
seeking. A chunk nobody here has heard of costs nothing to step over, which is what makes the reader
complete for the format while it decodes none of its codecs.

Its packets are capped at a size the writer chose and its pictures are not, so a picture arrives in
pieces, each behind a small header of its own saying which piece of which picture it is. Two of that
header's fields are the trap: for every piece but the last they are the whole picture's length and
this piece's offset, and for the last they are the whole length and this piece's *own* length, the
offset following by subtraction. A picture is handed over when its bytes are all present rather than
when a piece is marked as the last, because plenty of pictures are never marked; a piece sent twice is
skipped rather than costing the picture; a piece that leaves a hole drops it. Only the element that
opens a packet takes the packet's timestamp — a second picture in the same packet is one the file gave
no time to, and it is reported as having none rather than being given an interpolated one.

Where it cut is reported and not thrown away. RealMedia cuts a picture at its slices, one slice to a
piece, and a RealVideo slice carries no start code and no fixed padding — so once the pieces are
joined the boundaries are gone, and a RealVideo decoder needs them. They go out on the packet as
`CodedPacket.FragmentOffsets`. ffmpeg carries the same fact by writing a table of those offsets in
front of the picture's bytes, one count byte and eight bytes a slice, which is why a packet from its
demuxer is `8n+1` bytes longer than the picture; the fact is identical and only the spelling differs,
and a byte layout invented by one demuxer for one decoder is the private arrangement the split
between demux and decode exists to prevent.

Sound comes out as it is stored. RealAudio's codecs interleave their sub-packets across the packets
carrying them, and the geometry that undoes it is in the RealAudio header this reader hands across as
codec-private data, which makes deinterleaving the codec's business; ffmpeg's demuxer does it there
and so reports five or six times as many audio packets for the same file.

Measured against `ffprobe -fflags +noparse` on twelve recordings — RealVideo 1, 2, 3 and 4, 50 KB to
18 MB, 360 330 coded pictures — every file yields the same picture count with the same timestamps,
key-frame flags and byte lengths, and the piece offsets are compared entry for entry against
ffmpeg's own table and are identical. Three of the twelve are damaged: two cut off mid-recording, one
whose data chunk length was never filled in and which re-sends a piece. All three are read. On the
last, one picture in 338 672 differs on purpose — the repeat is skipped and the picture recovered
whole, where ffmpeg, having lost the sequence there, hands back the 46 bytes it still had as though
they were a picture.

Electronic Arts' own family of game cinematics — CMV, TGV, TGQ, TQI, MAD and the rest, carried under
`.wve`, `.cmv`, `.tgv`, `.uv` and a handful of other extensions the studio never settled on one of —
share one container underneath every one of them: a flat run of chunks, each a four-character name, a
four-byte little-endian size counting its own eight-byte header as well as the payload behind it, and
nothing else. `EaReader` answers only where a chunk is and which of the handful this reads anything of
it is; a chunk it has no name for — every audio stream, every one of EA's own video codecs this package
does not decode — costs nothing to step over, the same as an unrecognised RealMedia chunk.

The format carries no signature of its own: which of EA's codecs a file holds is stated nowhere outside
the chunk names themselves, so what stands in for one here is the same shape of plausibility check id
Cinematic's reader already needs — the file's first chunk names one of the handful of kinds this reader
is built from, and the size behind it is at least the eight bytes that size is itself supposed to cover.

A file is not necessarily one video stream from start to end. The one real sample this reader was built
and measured against, EA Sports' own `TITLE.CMV`, closes its first forty-nine pictures with an `MVIe`
chunk and opens straight back into a fresh `MVIh` that restates the palette for a second run of
pictures — 194 pictures across the two runs in all, matching `ffprobe -count_frames`'s own count exactly
— walked as one stream by this reader rather than as two, because nothing in the format calls that
boundary anything more than another header restatement.

### MPEG-1 video

I, P and B pictures, with the full block layer: the Annex B variable-length codes, dequantisation
against the default and any loaded quantiser matrices, the inverse transform, and motion compensation
at half-pixel resolution in both directions. Frames come out in display order, so an anchor is held
until the next one arrives and `Flush` is not empty at the end of a stream.

Thirty-one encoded streams were compared with ffmpeg's decode of the same bitstream, plane by plane
and frame by frame: sixteen match byte for byte against ffmpeg's floating-point inverse transform and
the rest differ in at most thirty-two samples of one frame, by one level, without growing across a
group of pictures. That residual is the transform's, which ISO/IEC 11172-2 specifies as a formula
with an accuracy bound rather than as an algorithm; ffmpeg's own two transforms differ from each other
by more.

What is not implemented refuses and says so: D pictures, and a picture size that changes while
pictures predicted from the old one are still held.

### MPEG-2 video

One decoder reads both standards, because ISO/IEC 13818-2 is written that way — it requires a decoder
of itself to decode ISO/IEC 11172-2 as well, and the picture, slice, macroblock and block layers are
the same walk with more fields in them. Which standard a stream is decides itself, from the sequence
extension after the sequence header, and not from what a container called the codec. The two decoder
types exist to claim different four-character codes; behind them is one engine.

What MPEG-2 adds and this reads: the sequence and picture coding extensions; 4:2:0 with MPEG-2's own
chrominance siting, and 4:2:2; `intra_dc_precision`, so a picture may code its DC to nine, ten or
eleven bits; the non-linear quantiser scale; the alternate scan; the second intra coefficient table
(Table B.15); loadable chrominance quantiser matrices; concealment motion vectors; 13818-2's own
dequantisation, which corrects each block's parity once at the end rather than forcing every
coefficient odd; and interlaced coding within a frame picture — field DCT, and field-based motion
compensation where the two fields of a macroblock are predicted separately from either field of the
reference.

What it refuses, by name and with the clause: field pictures, dual-prime prediction, 4:4:4, and the
three scalability extensions.

Thirty-seven encoded streams, eleven hundred frames in all, were compared with ffmpeg's decode of the
same bitstreams — every frame, every sample. Progressive and interlaced; 4:2:0 and 4:2:2; 64×48 up to
704×480; sizes that are and are not whole macroblocks in either direction; greyscale, so that no
chrominance convention could mask a luminance error; every intra DC precision; the alternate scan, the
non-linear quantiser and the second intra table, separately and together; and the same video through
an elementary stream, a program stream and a transport stream. Every one produced the frame count
ffprobe counts.

Against ffmpeg's floating-point inverse transform, twenty-seven of the thirty-seven are identical
sample for sample on every frame. The other ten differ in at most thirteen samples of one frame — out
of a million — by at most three levels, flat across a group of pictures rather than growing, which is
what separates a rounding difference from a fault in prediction or dequantisation. For scale: on those
same streams ffmpeg's own two inverse transforms differ from each other by tens of thousands of samples
per frame. The residual is the transform's, which both standards specify as a formula with an accuracy
bound rather than as an algorithm, and not a disagreement about the bitstream.

### Microsoft RLE

Run-length coded palettised frames at four bits a pixel and at eight, with the end-of-line, delta and
end-of-bitmap escapes. There is no second copy of the coding here: a run-length Windows bitmap is the
same opcodes over the same kind of picture, so the walk lives with the bitmap reader and takes the
canvas it paints on as an argument. That argument is the whole difference between the two uses — a
still starts on an empty canvas, and a frame starts on the frame before it, which is what turns the
escapes from a way of leaving parts of a picture unstated into the entire inter-frame coding.

The coding is lossless, so there is nothing to round: every frame of every stream measured came out
identical to ffmpeg's decode of the same file, key frames and delta frames alike, with no differing
samples at all.

What is not implemented refuses and says so: a depth the coding is not defined at, a depth that
disagrees with the compression stated beside it, a stream carrying no palette, rows stored top-down,
and any opcode that runs off the picture or off the end of the data. QuickTime's `WRLE` — the same
coding with a QuickTime colour table in place of the bitmap header's palette — is not claimed, so a
`.mov` carrying it is refused by name rather than decoded against the wrong colours.
### Microsoft Video 1

Vector quantisation over 4x4 blocks: a block is one colour, two colours chosen per pixel by a
sixteen-bit mask, or eight — the block split into four 2x2 quads with two colours each, chosen by the
same mask. A fourth code skips a run of blocks, and that is the whole of the inter-frame coding: a
skipped block is one that did not change, so the frame before has to still be there to be left alone.
Blocks run bottom to top as a bitmap's rows do, and left to right within a row.

Both depths are one decoder because they are one algorithm. What differs is how wide a colour is and,
at sixteen bits, that the choice between two colours and eight is made by the spare top bit of the
first colour rather than by the second flag byte.

The quantisation is the encoder's, so a decoder reading the same bitstream has nothing to round.
Eleven streams were compared with ffmpeg frame by frame — 4x4 up to 320x240, fifty frames of moving
content, noise, colour bars, in AVI and in Matroska — and every frame of every one is identical,
sample for sample. The eight-bit variant is measured the same way against hand-built streams, since
ffmpeg's own encoder writes only the sixteen-bit one.

Colours are 5-5-5 with red in the high bits, which is what ffprobe calls the codec's pixel format and
what decoding a frame each way and comparing with ffmpeg settles; the format description on
multimedia.cx names the channels the other way round. Five bits are widened to eight by repeating
the pattern rather than shifting, the same rule this library's bitmap reader arrived at against the
same tool.

What is not implemented refuses and says so: a depth other than eight or sixteen, a picture whose
sides are not whole blocks, an eight-bit stream with no palette, a skip run reaching past the last
block, a frame that stops before every block is accounted for, and an opcode wanting more bytes than
the packet holds. A skip run of *no* blocks is refused too, and for a different reason: read as the
format describes it the run is a no-op, where ffmpeg abandons the rest of the frame at one. Both
readings produce a picture, they differ across everything after the run, and nothing in the file says
which was meant.
### Cinepak

Vector quantisation with two codebooks per strip. A codebook entry is four luminance samples and one
chrominance pair — a 4x4 block at 12 bits a pixel — and a block is coded either as one entry, whose
four samples are each stretched over a 2x2 square, or as four, one per quadrant. One byte a block or
four, and everything else is in the codebooks. Both codebook depths are read, the 12-bit one and the
8-bit grey one.

The inter-frame coding is in two places at once, which is what makes the format small: a vector list
may say a block is unchanged and code nothing for it, and a codebook chunk may restate a handful of
entries and leave the other two hundred. So both the picture and the codebooks carry over between
frames, and a strip that states nothing is not a strip of nothing.

Fifteen streams were compared with ffmpeg frame by frame — 4x4 up to 640x480, a hundred frames of
zooming fractal, noise, greyscale, one strip forced and eight, in AVI and in QuickTime — and every
one of 303 frames is identical, sample for sample. Nothing drifts because nothing differs.

Two things measurement decided rather than the documentation. The chrominance bytes are **signed**
and not biased by 128, which the technical note has the other way round; a stream whose codebook
sweeps every value of each byte gives 5120 samples of what the answer must be, and the signed reading
reproduces all 5120 where the biased one reproduces none. And the halving in the green row truncates
toward zero rather than shifting right, which is a different number for a negative odd difference and
wrong in 319 of those same samples.

Every strip after the first states a top of zero and a bottom that is really its height, so it is
placed under the one before it. Read literally, a three-strip frame draws all three strips across the
top third of the picture — a picture rather than an error, and so one that would never be noticed.

What is not implemented refuses and says so: a strip identifier that is neither intra nor inter, a
chunk type the format does not define, a strip reaching outside its frame or not made of whole
blocks, a picture size that changes part way through a stream, a vector list stopping before every
block is accounted for, and any chunk shorter than it says it is.
### QuickTime Animation (RLE)

Lossless, and line-based rather than block-based: a frame names the band of lines it touches and
writes them as runs, literal pixels and skips over a canvas the frames before it left behind. All of
1, 2, 4, 8, 16, 24 and 32 bits, and the greyscale depths 33, 34, 36 and 40 that are the same indices
into a ramp running from white. Thirty-two bits carries alpha and the alpha survives.

Every count in the bitstream is in coded units and not in pixels. Above eight bits a unit is a pixel;
at eight and below it is four bytes — four indices at eight bits, eight at four, sixteen at two — and
at one bit it is two bytes, which is sixteen pixels again. One bit is a different shape altogether:
each opcode carries its own skip, and the skip's top bit is what starts a line.

Twenty-two streams covering every depth ffmpeg's encoder can write were decoded here and by ffmpeg and
compared pixel for pixel on every frame, alpha included: 360 frames, all identical. The depths ffmpeg
cannot encode — one, two and four bits, eight bits through a colour table, and widths that are not a
whole number of coded units — were checked the other way round, by building streams that say a known
picture and confirming ffmpeg reads them as that picture and this reads them as ffmpeg does: another
sixty frames across fifteen streams, all identical.

What refuses: a depth the compressor does not code; an indexed depth carrying no colour table, since
the Macintosh default palettes cannot be checked against anything here and a picture drawn through a
guessed table cannot be told from one drawn through the right one; a stream that opens with a frame
touching only part of the picture; and any count that would write outside the line it is on.
### On2 VP3.1

The codec Theora was built from, and all of it: the run-length coded block flags, all eight macro
block coding modes with the eight schemes for coding the modes themselves, motion vectors from either
of two reference frames at half-pixel accuracy, the eighty built-in DCT token codebooks, DC prediction
from four weighted neighbours, the normative integer inverse DCT with its DC-only shortcut, and the
deblocking loop filter.

On2 donated VP3 to Xiph.Org, who built Theora on it, so the free and complete Theora specification is
also the specification for most of VP3 — the two share the frame layout, the transform, the
quantisation, the coding modes, the motion vector coding and the loop filter, and Appendix B of that
document writes down the tables VP3 has hard-coded where Theora carries them in a setup header. What
it does not write down is VP3's own frame header, which it says only is "substantially different".
That was derived from VP3 streams and the derivation is written out where the code parses it: the
number of bits before the coded-block flags was found by decoding whole frames at each candidate
length and keeping the one where every coded block's coefficients were accounted for, and which six
bits hold the quantisation index was settled by decoding both ways and comparing against a reference
decoder. The same goes for where the picture sits inside the coded frame when the size is not a
multiple of sixteen: the specification says the lower left, VP3 files say the upper left, and the
files win.

Seven streams and 3,182 frames — 640x480, 640x272, 480x256, 350x141, 320x240 and 280x200, the last two
of which are not a whole number of macro blocks — were decoded here and by ffmpeg and compared plane
by plane, sample by sample. Every plane of every frame is identical: not close, not on average, the
same bytes, on the 1,505th frame of a run as on the first. That is the only acceptable result, because
the loss happened in the encoder and both decoders are reading what came out of it — and because an
error of one anywhere in the transform or the loop filter is added to the next frame's error and the
one after that, growing until the next intra frame. Counting what two of the streams contain, between
them they use all eight coding modes — including the four-vector mode and both golden-frame modes —
both ways of coding a motion vector, quantisation indices between 17 and 63, and 645,877 half-pixel
predictions.

What is not implemented refuses and says so: a `VP30` stream, which is the earlier VP3.0 bitstream and
cannot be read with VP3.1's rules at any bit offset; a stream whose container states no picture size,
since VP3 carries none of its own; a stream that begins at an inter frame; a packet that ends in the
middle of a frame; a run of block flags longer than the frame has blocks; a coefficient token that
would write past the end of a block; and a frame whose tokens do not account for every coefficient of
every coded block. None of them hands back a picture. That matters more here than in most codecs,
because a frame in which nothing changed is a normal thing for a VP3 stream to contain — so a decoder
that repeated the previous frame on failure would be producing exactly what working looks like.
### Apple Video (RPZA)

A vector quantizer over 4x4 blocks of 15-bit RGB colour, also called Road Pizza, and QuickTime's own
alternative to Microsoft Video 1: the same one-colour, several-colour and skip shape of coding, over
blocks read left to right and top to bottom rather than Microsoft's bottom-up bitmap order. A block is
one colour; a quad of colours, two given by the stream and two built from them by a fixed blend,
chosen per pixel by a two-bit index; or, one block at a time only, that same quad built inline or
sixteen colours with nothing shared between them. A run of blocks under one opcode shares one set of
colours and reads its own index bytes per block, which is what makes a flat run cheap without
changing which opcode reads it.

Two things measurement decided rather than the format's own documentation, both in the "special"
opcode — the one whose first byte doubles as a colour rather than naming an operation. Which of its
two variants a block uses is not decided by that colour's own low byte, tempting as that reading is;
it is decided by the byte after it, which is also the first byte either variant goes on to read
regardless of which one it turns out to be. Reading the choice off the wrong byte still produces a
picture — every byte after it is still there to be read as something — so this was only caught by
comparing decoded pixels against ffmpeg's, where a real chunk's second block came back as nine
scattered colours in a block that should hold one. And a standard opcode names four code points; the
format's documentation describes three and calls the fourth, 0xE0, unused. A real chunk from Apple's
own QuickTime encoder uses it seven times in one keyframe, and every block it names decodes correctly
against ffmpeg when read as a second spelling of skip.

Eight streams — QuickTime and AVI, geometry that is and is not a whole number of blocks in either
direction, 60x64 up to 574x252 — were decoded here and by ffmpeg and compared pixel for pixel on every
frame: 924 frames, all identical, RGB-native so there is no chroma-siting convention to disagree about.
The coding is lossy at the encoder and exact at the decoder — every colour a chunk paints with is
either read from the stream or built from two others by an integer formula the format states in full
— so there is nothing here for a decoder to round, and none of the 924 frames differ by so much as one
level.

What refuses: a chunk shorter than its four-byte header, a standard opcode's run reaching past the
last block, and a chunk that stops before every block is accounted for. A skip opcode is not refused
on the very first frame — the canvas a freshly built decoder starts with is black, which is exactly
the picture a skip paints when nothing has been decoded yet, so an encoder using one there is stating
a black block rather than pointing at a frame that does not exist.
### Apple Graphics (SMC)

A vector quantizer over 4x4 blocks of eight-bit palettised pixels, named after its author Sean M.
Callahan and read from the description Mike Melanson published, which the MultimediaWiki page itself
is drawn from. Blocks run left to right, top to bottom. A block is skipped, so the frame before it is
left alone; the last block, or the last two blocks together, is repeated forward; one, two, four or
eight colours are chosen per pixel by packed indices; or sixteen raw palette indices arrive with
nothing shared between them at all. Two, four and eight colours each have two spellings — a set of
colours given in the stream, or a number naming one of three small circular caches the decoder keeps
of the most recently given sets — and the caches are not part of the picture: they are reset empty at
the start of every chunk, where the picture itself is not.

The eight-colour block's index bytes are not six bytes of four pixels apiece. They are permuted:
twelve nibbles come out of the six bytes and two 24-bit numbers are built by picking six of those
twelve for each, in an order that is not the format's usual left-to-right, top-to-bottom shape and
was recovered from a worked example in the source document rather than derived from anything else
about the format.

Most real Apple Graphics streams carry no colour table of their own — six of the eight downloaded end
their sample description exactly where a table would begin — and that is not the same thing as
carrying no colours. QuickTime defines a standard colour table for every indexed depth, the classic
Macintosh system palette, and a sample description naming that table's own identifier, or the generic
"no table" value, with nothing following it, is stating "use the standard table" rather than "there
is none". All six of those streams name one or the other, and one of the six — the one whose depth
states forty rather than eight, QuickTime's convention for an eight-bit greyscale capture — asks for
the standard table's greyscale counterpart, the linear ramp white to black this library's QuickTime
Animation decoder already reads the same way for its own greyscale depths. Both tables are generated
by formula rather than looked up: the colour one from the six-level red/green/blue count and the ten
supplementary shades the classic Macintosh 'clut' resource of ID 8 is itself built from, the
greyscale one as the plain 256-level ramp.

Eight real streams — 60x64 up to 640x480, one to 399 frames, six of them reaching only the standard
tables above and two carrying an explicit one — were decoded here and by ffmpeg and compared pixel
for pixel on every frame: 950 frames in all, identical. Between them the eight streams cover every
opcode the format defines, including thousands of individual eight-colour and cached-reference
blocks and, in one stream, the "repeat the last two blocks together" opcode's only occurrence
straddling a row of blocks — which is what settled that opcode's reading after earlier hand-built
chunks exercising it had disagreed with ffmpeg in ways nothing about the format explained; the
disagreement was in those hand-built chunks, and the real stream's own use of the opcode matches the
reading the format's documentation gives without exception.

What refuses: a depth other than the two this format's sample descriptions state — eight bits with a
colour table, or forty for the greyscale convention — a colour table identifier that is neither "no
table" nor the stream's own depth and has no table bytes to fall back on, naming a system colour
resource genuinely outside the file; a chunk shorter than its four-byte header; an opcode's run
reaching past the last block; a chunk that stops before every block is accounted for; a repeat opcode
with nothing before it to repeat; and the one opcode value the format leaves undefined, which ffmpeg
does not refuse either but answers with palette index zero — not a reading of anything the format
states, and not reproduced here. A skip opcode is not refused on the very first frame: the canvas a
freshly built decoder starts with is already what a skip states, so an encoder using one there is
stating that canvas rather than pointing at a frame that does not exist.
### VP8

The codec WebM was built around, and all of it: the boolean entropy decoder, segmentation, both loop
filters, up to eight token partitions, all fourteen intra prediction modes, prediction from any of
the three reference frames with the six-tap and bilinear sub-pixel filters, and the probability state
that carries from one frame to the next. A frame the stream asks not to be shown — an alternate
reference built from several frames at once — is decoded, kept as a reference, and not handed back.

Fifty-three encoded streams and six built by hand — 3,189 coded frames, 3,116 of them shown — were
compared with ffmpeg's decode of the same bitstreams plane by plane and sample by sample. Every plane
of every frame is identical: not close, not on average, the same bytes. That is the only acceptable
result, because the loss happened in the encoder and both decoders are reading what came out of it —
and because an error in prediction or in the loop filter shows up as a small difference everywhere
that grows with every frame until the next key frame.

The streams cover both filters, sharpness zero to seven, all four bitstream versions, one to eight
token partitions, segmentation, hidden reference frames, motion vector sign bias, every split
partitioning, every subblock mode, every range token, and picture sizes from 16x16 to 1280x720
including sizes that are not a whole number of macroblocks. The reference-buffer copies and the
segment-based filter levels, which no encoder here will emit, were reached with hand-written frame
headers — decoded by ffmpeg as well, so agreement is still the measurement.

What is not implemented refuses and says so: a bitstream version RFC 6386 reserves, a key frame that
sets the reserved colour space or clamping fields, a stream that begins at an interframe, a truncated
packet, and a partition table that does not fit in one.

### VP9, profile 0

Profile 0 — eight bits a sample at 4:2:0 — and all of it: superframes, the uncompressed and
compressed headers, the four frame contexts and the probability updates they carry, tiles in both
directions, the recursive superblock partition from 64x64 down to 4x4, the motion vector reference
scan, the coefficient tokens with a scan order per transform type, the cosine transform at four sizes
and the sine transform at three, the lossless Walsh-Hadamard transform, all ten intra prediction modes
at four block sizes, eight-tap inter prediction with reference frame scaling and compound prediction,
the loop filter, and the backward probability adaptation — which needs every syntax element the frame
contained counted, in the context it was read in.

Profiles 1, 2 and 3 are refused by name rather than half-decoded. They carry chrominance at 4:2:2,
4:4:0 or 4:4:4, or ten and twelve bits a sample, and the transforms, the prediction and the loop
filter all change shape for those. Profile 0 is what WebM overwhelmingly carries, and this decodes an
eight-bit 4:2:0 stream completely or not at all.

Ninety-two encoded streams and twenty-two built by hand — 6,196 decoded frames — were decoded here, by
ffmpeg and by libvpx, and compared plane by plane and sample by sample. Every plane of every frame is
identical in all three: not close, not on average, the same bytes. VP9's inverse transforms are
specified down to the rounding of every intermediate, so that is the only acceptable result — and it
is the measurement that matters, because a mistake in the loop filter, in prediction or in the
probability adaptation shows up as a small difference that grows with every frame until the next key
frame. The one thing the adaptation is measured by is the frames *after* the ones it ran on: a frame
whose counts were wrong still decodes perfectly, and its successor is noise.

The encoded streams cover picture sizes from 2x2 to 1920x1080 including sizes that are a whole number
of neither superblocks nor blocks, one to four tile columns and one to four tile rows, lossless
frames, every intra and inter prediction mode, every transform size and type, every coefficient token
including the largest category, alternate reference frames and the superframes and repeated-frame
headers that come with them, compound prediction, segmentation, error resilient and frame parallel
frames, all four frame contexts, and reference frame scaling — libvpx resampling mid-sequence under a
starved buffer, so that later frames predict from references of a different size. The syntax libvpx
has but never chooses — intra-only frames, the frame context resets, segmentation stating absolute
values, the per-segment filter level and the per-reference filter adjustments — was reached with
hand-written frames, decoded by ffmpeg and libvpx as well, so agreement is still the measurement.

Two paths are implemented from the specification and reached by neither: a frame stating that *every*
inter block is compound rather than letting each block choose, and the segment feature that names a
block's reference frame. No encoder emits either, and neither can be reached from an intra frame.

What is not implemented refuses and says so: a profile other than 0, an sRGB colour space a profile 0
stream cannot carry, a missing frame marker or sync code, a compressed header or tile that does not
fit in the packet, a superframe index stating more than the chunk holds, a frame that shows a
reference slot nothing has written, and a reference too far from the current frame's size to be
scaled. There is no `catch` anywhere that hands back a blank frame or repeats the last one — which
matters more here than for most codecs, because a repeated frame is exactly what a still passage of a
film looks like.

### H.263 and Sorenson Spark

Baseline ITU-T H.263: the picture, group of blocks, macroblock and block layers, intra and predicted
pictures, one motion vector per macroblock at half-pixel resolution with the median predictor of
clause 6.1.1, and the inverse quantisation of 6.2.1. Group headers are optional in the bitstream and
whether each one was present is remembered, because the prediction rules treat the macroblocks above
a group as unavailable only when that group opened with a header of its own.

Sorenson Spark — what Flash Video calls `FLV1` — is the same codec from the group of blocks layer
down and a different bitstream above it, so it shares everything here but the picture header. Its
three real differences are all in the decoder by name: it states its own picture size rather than
naming one of five formats, it has no group of blocks layer at all, and a stream of version 1 puts a
bit in front of the coefficient escape choosing between a seven-bit and an eleven-bit level. A
Sorenson picture may also be disposable — predicted, shown, and never predicted from — which is kept
rather than ignored, because keeping it as a reference would put every picture after it one
prediction out of step.

Thirty encoded streams, seven hundred and forty-three frames, were compared with ffmpeg's decode of
the same bitstreams **plane by plane** and sample by sample — sizes from 100x60 to 704x576, quantisers
1 to 31, groups of pictures from one frame to fifty, and streams with and without group headers.
Plane by plane and not in RGB, because turning 4:2:0 samples into RGB is a display convention rather
than part of the decode and the two conventions differ: this library interpolates the chrominance
planes back up and ffmpeg repeats each sample across its square, which on a picture of hard colour
edges puts nearly half the samples of every frame tens of levels apart while the decoded samples are
identical. Feeding ffmpeg's own decoded planes through the conversion here reproduces that difference
exactly, with no decoder of ours involved, and the same comparison does the same thing to the MPEG-1
decoder above.
Twenty-one of the thirty match sample for sample on every frame against ffmpeg's floating-point
inverse transform; the rest differ in at most about forty samples of a frame out of thirty-eight
thousand, always by exactly one level, and the difference stays at one level across fifty frames with
no intra picture to reset it. That residual is the transform's, which H.263 Annex A specifies as an
accuracy bound rather than as an algorithm; ffmpeg's own two transforms differ from each other by an
order of magnitude more on the same streams.

What is not implemented refuses and says so, naming the annex and the field: the extended picture
header of clause 5.1.4 and everything it signals, unrestricted motion vectors (Annex D), arithmetic
coding (Annex E), advanced prediction and its four vectors per macroblock (Annex F), PB-frames
(Annex G), continuous presence multipoint (Annex C), and the escape level Annex T reserves.

### RealVideo 1

RealVideo 1 is ITU-T H.263 from the macroblock layer down with a different picture header on top, so
it is the H.263 decoder above with its own header reader and its own idea of where a picture begins
and ends. Nothing of the block layer is written twice.

What the header replaces: H.263 states the picture size as one of five named formats, where RealVideo
carries none at all and takes it from the container — a stream whose container lost the size is one
nothing can decode. H.263 codes a picture as one run of macroblocks broken by optional group headers,
where RealVideo cuts a picture into independently coded runs, each restating the picture's type and
quantiser and naming the macroblock it begins at and the number it carries, and sends each run in its
own packet so that losing one costs part of a picture rather than all of it. And H.263 keeps its
vectors inside the picture unless Annex D is signalled, where RealVideo always lets them point
outside it and reads the edge sample — there is no bit to turn that off with.

The runs carry no start code and the padding between them is not fixed, so where each begins is taken
from `CodedPacket.FragmentOffsets` rather than searched for. That is the seam working: the container
knows where it cut because it did the cutting, the decoder needs it and cannot recover it, and neither
has to know anything else about the other.

**Measured.** Twenty-seven encoded streams, 238 frames, compared with ffmpeg plane by plane and frame
by frame — 96x64 to 352x288, quantisers 2 to 31, intra-only and groups of pictures up to fifty.
Against `-idct faani`, **235 of 238 frames are identical sample for sample**; the other three differ
in five samples between them, always by one level. Against ffmpeg's default integer transform, 87 197
samples of 24 million differ at a maximum of two levels — the same size as the difference between
ffmpeg's own two transforms, which is the residual H.263 Annex A exists to allow.

**What it refuses, by name.** RealVideo 2, 3 and 4 are not accepted at all rather than accepted and
then failed, so a caller asking whether anything reads an `RV40` stream is told no once. Within
RealVideo 1 only revision 0 of the bitstream — version word `0x10000000`, which is what ffmpeg's own
encoder writes — is implemented; the recordings on the sample servers state `0x10001000` and
`0x10003001`, and those are a different bitstream below the picture header rather than the same one
shifted, since no offset into one of their pictures decodes even three macroblocks with the H.263
tables. A first run that leaves its macroblock position out is refused for the same reason: no
measured stream does it, so the shape of such a header is unverified, and reading it wrongly would
produce noise shaped like a picture instead of an error. A PB-frame is refused where it is signalled.

### H.264 / AVC

I and P slices of the Baseline and Constrained Baseline profiles, and every Main or High profile
stream that happens to be coded without the tools those profiles add. That is: CAVLC, 4:2:0, 8-bit
samples, progressive frames, one slice group, the 4x4 transform and flat quantiser matrices. Within
it, everything — the nine Intra_4x4 modes, Intra_16x16 and chroma prediction, `I_PCM`, all four
macroblock partitionings and all four sub-macroblock partitionings, multiple reference frames with
list reordering, quarter-sample motion with the six-tap luma filter and bilinear chroma, constrained
intra prediction, and the deblocking filter with per-slice offsets and both disable modes.

Both delivery forms, because H.264 has two. A transport stream, a program stream and a bare `.264`
carry NAL units separated by start codes; MP4, Matroska and FLV carry each unit behind its length,
with the parameter sets in an `AVCDecoderConfigurationRecord` in the container's header. Which form a
stream is in is decided from whether that record is present rather than guessed at each packet, and
the same content in either form decodes to identical frames.

Forty-six encoded streams were compared with ffmpeg's decode of the same bitstream, plane by plane
and frame by frame, and **every sample of every frame is identical** — across quantisers 1 to 51,
picture sizes from 16x16 to 640x480 including one whose size is not a whole number of macroblocks and
is therefore cropped, one to eight reference frames, one to nine slices a picture, deblocking off and
at both offset extremes, constant quantiser and two rate-controlled modes, intra refresh, a High
profile stream that uses none of the High profile tools, and the same content through all five
containers. Four of them are a single intra picture followed by 125 to 200 predicted ones, which is
the shape in which a small error compounds: the difference stays at zero for the whole chain. H.264
specifies its inverse transform as exact integer arithmetic rather than as a formula with an accuracy
bound, so exact equality is the right bar here, unlike MPEG-1 above.

Three things no encoder in ordinary use emits are covered by built streams instead, because a
comparison cannot reach what nothing produces: `I_PCM` macroblocks, reference picture list
reordering, and marking a reference unused part way through a sequence.

What is not implemented refuses by name and cites the clause: CABAC, B slices, SP and SI slices, the
8x8 transform and scaling matrices, 4:2:2, 4:4:4 and monochrome, sample depths above eight, field
pictures and MBAFF, flexible macroblock ordering, weighted prediction, long-term references, slice
data partitioning, redundant coded pictures, and the scalable and multiview extensions.

### HuffYUV and FFVHUFF

Lossless and intra only: no transform, no quantiser. A sample is predicted from its neighbours — from
the left, by gradient, or by the median of the two and the plane through them — and the difference is
Huffman coded with one table a plane.

Two things about it are easy to get wrong and both are load-bearing. **The bits are in little-endian
words**, so every four bytes of a frame have to be turned round before any of it decodes; the raw
first pixel arriving as alpha, red, green, blue is that swap showing through. And **the Huffman codes
are handed out from the longest length down**, not the shortest up, so the canonical assignment a
reader reaches for first decodes nothing.

Three header forms, and which one a file uses is not its four-character code — `HFYU` and `FFVH` both
write all three. One states a bitstream depth and codes 4:2:2 groups along each row, or colour a
pixel at a time bottom row first; one states a sample depth with the chroma subsampling packed into
its low nibble and codes each plane through to its end; the third is the original codec, which states
nothing at all and is refused rather than guessed at.

Eighty streams were decoded here and by ffmpeg: every pixel format its two encoders will write, each
with all three predictors, progressive and interlaced, with the tables in the stream description and
in every frame, at sizes from 2x2 to 352x576. The formats that need no colour conversion — `gray`,
`gbrp`, `gbrap`, `rgb24`, `bgra` — are compared against ffmpeg's frames directly and every frame is
identical. The luminance-and-chrominance formats are compared plane by plane against ffmpeg's decoded
planes, and every sample of every plane is identical: 362 frames in all, none differing anywhere.

What refuses: the original codec; samples deeper than eight bits; a description that states neither
interlaced nor progressive and expects the height to be guessed from; a prediction method that is
none of the three; a Huffman table whose lengths do not describe a complete code; and interlaced
4:2:0 with median prediction, whose row order could not be established against any file — reading it
as the nearest arrangement that is known reproduces five rows and then diverges, which is the one
answer a decoder must not give.

### FFV1

Lossless, intra only, and the codec archives standardised on. No transform and no quantiser: each
sample is predicted from the median of three neighbours and the difference is entropy coded in a
context chosen by five more. What makes it unusual is how much of the coding the stream itself gets
to decide — the context quantisers, the states each context starts at, and even the range coder's
state transition table are all replaceable by a file that says so, and all three are read here.

Both entropy coders. The range coder spends thirty-two adaptive states on each context; Golomb-Rice
spends four running numbers and adds a run mode for the flat areas. Everything else — the
prediction, the contexts, the plane order — is the same either way.

Versions 0, 1 and 3. Where the header lives is the version: 0 and 1 put it inside every keyframe, 3
moves it into a configuration record the container carries, adds slices that can be found and decoded
independently of one another, and protects both with a checksum. Version 2 was never finished and is
refused by name. A frame that is not a keyframe still codes every sample — what it inherits is the
entropy coder's statistics and nothing of the picture.

Eighty-four streams and 379 frames were decoded here and by ffmpeg: every pixel format its encoder
writes at eight bits, both coders, all three versions, one slice and sixteen, with and without slice
checksums, with the coder's own state transition table and with the default one, at sizes from 4x4 to
320x240. The formats that need no colour conversion — `gray`, `ya8`, `bgr0`, `bgra` — are compared
against ffmpeg's frames directly and every frame is identical. The luminance-and-chrominance formats
are compared plane by plane against ffmpeg's decoded planes, and every sample of every plane is
identical. Four of the streams carry an alpha channel that is a gradient rather than the constant a
test pattern produces, so the transparency is measured rather than assumed.

What refuses: samples deeper than eight bits, version 2, a coder type or colour space the
specification does not describe, a configuration record or a slice whose checksum does not come out,
a slice that states a place outside the raster, and a version 0 or 1 stream that opens with a frame
that is not a keyframe.
### MPEG-4 Part 2

Intra, predicted and bidirectionally coded pictures at Advanced Simple Profile, less quarter-sample
motion: the block layer with both coefficient tables and all three escape forms, prediction of an
intra block's DC and first row or column from its neighbours with the scan that goes with the
direction chosen, both inverse quantisation methods with the default and any loaded weighting
matrices, one and four motion vectors per macroblock, vectors that point outside the picture, the
direct prediction mode of a bidirectionally coded macroblock, and video packets. Frames come out in
display order, so an anchor is held until the next one arrives and `Flush` is not empty at the end of
a stream.

A stream in an ISO base media file usually carries its headers in the sample entry rather than in its
packets, so the codec walks the `esds` descriptor to find them — which is the codec's business and
not the container's, and is why the container hands over the sample entry verbatim.

Twenty-seven encoded streams, one thousand and eighty-three frames, were compared with ffmpeg's
decode of the same bitstreams plane by plane and sample by sample — sizes from 64x48 to 352x288,
quantisers 1 to 25, both quantisation methods, one and four vectors per macroblock, up to four
bidirectionally coded pictures between anchors, video packets, and groups of pictures from a single
frame to a hundred. Seventeen of the twenty-seven match on every sample of every frame; the rest
differ in at most sixty samples of a frame out of thirty-eight thousand, by one level. That residual
is the transform's, which Annex A specifies as an accuracy bound rather than as an algorithm.

The long streams are the ones that matter. A group of pictures that starts afresh every few frames
displaces any error before it can be seen, so a wrong reference or a wrong time base looks like
rounding; the streams here that carry one intra picture and a hundred frames after it are what a
bidirectionally coded picture's time base is actually measured against.

Two decisions are worth reading the code for, because both are places where following the standard's
words literally produces a decode that disagrees with every encoder in existence: the mismatch
control of clause 7.4.4.5 is applied to non-intra blocks only, and the inverse transform rounds an
exact half to the even value. Each is measured in the remarks beside it.

What is not implemented refuses and says so, naming the clause: quarter-sample motion vectors,
sprites and global motion compensation, interlaced coding, overlapped block motion compensation, data
partitioning, scalability, non-rectangular shape, samples of any depth but eight, chroma formats
other than 4:2:0, newpred, reduced-resolution pictures and the complexity estimation header.
### Apple ProRes

Written from SMPTE RDD 36:2022, which is the published description of the bitstream and is cited by
clause throughout the source. Intra only, so there is no reference handling at all: every frame is a
whole picture, a seek needs nothing decoded before it, and a difference in one frame cannot become a
difference in the next.

One bitstream under six names. The profiles — Proxy, LT, Standard, HQ, 4444 and 4444 XQ — differ in
how hard an encoder quantises and whether it writes 4:2:2 or 4:4:4, both of which a decoder reads out
of the frame rather than off the tag. **There is no sample depth in a ProRes frame at all**: 7.5.1
gives the conversion from the transform's output to samples of any depth and leaves the choice to the
decoder, so the depth taken here is the one the profile is coded for — ten bits for the 4:2:2
profiles and twelve for the 4:4:4 ones.

Coefficients are coded with Golomb-Rice/exponential-Golomb combination codes whose codebook adapts to
the previous symbol, with three adaptations running at once and each reset per component per slice to
a stated non-zero value rather than to nothing. Two details are easy to get wrong and neither fails
loudly: **a DC difference is negated when the one before it was negative**, so the sign is state and
not just the magnitude; and **the four 4:4:4 chroma blocks of a macroblock run top to bottom then
left to right where the four luma blocks run left to right then top to bottom**, which the
specification prints a note of its own about.

Measured against ffmpeg on the planes, at the coded depth, before any reduction to eight bits —
against `-pix_fmt yuv422p10le` and `yuv444p12le` — because this library interpolates chroma where
ffmpeg replicates and a comparison on packed colour measures that disagreement instead of the decode.
All six profiles, both of ffmpeg's encoders, progressive and interlaced in both field orders, sizes
that are and are not a whole number of macroblocks, and 176x144 up to 1280x718: **every sample of
every plane is within one level**, and one is the only difference that ever occurs. That residue is
the inverse transform and nothing else — RDD 36 specifies no particular IDCT and requires only the
accuracy of its Annex A, so this evaluates the defining sum in double precision rather than
reproducing anyone's fixed-point approximation. **Alpha is exact**: 8- and 16-bit alpha both decode
to ffmpeg's values with no sample differing anywhere, which it should, since ProRes codes alpha
losslessly with no transform in the path.

The clamping bounds are the second of the two 7.5.1 offers — the permissible video levels, 4 to 1019
at ten bits and 16 to 4079 at twelve, rather than the full 0 to 2^b−1. Taking the wider pair puts a
scatter of samples exactly four levels apart at the extremes of a heavily quantised picture and
nowhere else, which is how the choice was found.

Reducing the samples to the eight bits a `RawImage` holds is folded into the colour conversion so
that a sample is rounded once. It is worth saying why this is not `ChannelScaling`'s reduction:
that one narrows a channel which fills its range, `v * 255 / max`, whereas 7.5.1 fixes black at
`16 * 2^(b-8)` and white at `235 * 2^(b-8)`, so moving a Y′CbCr sample between depths is an exact
power of two. Ten-bit white is 940, and `round(940 * 255 / 1023)` is 234 where the format says 235.
Alpha is the opposite case — 7.5.2 does define it as filling its range — so that one is
`ChannelScaling.Reduce16` exactly. Reducing the planes first and converting afterwards, rather than
folding the two together, moves up to three levels of RGB on a fifth to a third of the samples.

What refuses: a bitstream version later than the two RDD 36 describes; a reserved `chroma_format`,
`interlace_mode` or `alpha_channel_type`; a `quantization_index` outside the permitted 1 to 224; a
version 0 frame stating syntax its own version does not have; a packet that is not a compressed
frame; and any structure whose stated size does not fit inside the one containing it.

### Avid DNxHD and DNxHR

Written from SMPTE ST 2019-1:2016, *VC-3 Picture Compression and Data Stream Format*, cited by clause
throughout the source. Intra only, and independently decodable a macroblock scan line at a time: each
scan line starts at a byte offset the header states and resets the DC prediction, which is the
property an editing codec on shared storage exists for.

Two profiles, one block layer. Header versions 1 and 2 are the HD profile of Table C.1 — fixed
rasters, a 640-byte header, codec tag `AVdn`. Version 3 is the resolution-independent profile of
Table C.2, which Avid sells as **DNxHR** and which tags itself `AVdh`: the raster comes from the
header and the header grows with the picture. Both are read.

**The compression identifier is the one thing a decoder cannot infer.** It is not a bitrate and not a
raster — it names a row of Annex C, and that row picks one of eleven quantisation weighting tables
and one of six groups of code tables. Two frames of the same size and depth under different
identifiers decode to different pictures, so an identifier in neither Table C.1 nor Table C.2 is
refused rather than guessed at.

Three things are easy to get wrong and none fails loudly. **Every block ends with an end-of-block
codeword, including a block that fills all sixty-three AC coefficients** — Figure 29 shows it
unconditionally, while the informative pseudo-code of Figure 47 simply runs out at coefficient 64 and
stops reading. Following the pseudo-code leaves one codeword unread in exactly those blocks: 52 of a
1080-line frame's 68 scan lines then fail outright, and the 16 that survive are the ones that
happened to contain no such block. **Annex D's weights are indexed by raster position, not by
zig-zag position** — indexing by the zig-zag decodes every block and gets every one slightly wrong,
moving samples by up to 49 levels of 255. And **the inverse quantisation adds half the divisor only
where the weight and the divisor differ**, which reads like a typo and is not.

Measured against ffmpeg on the planes, at the coded depth, frame by frame and sample by sample —
against `-pix_fmt yuv422p`, `yuv422p10le` and `yuv444p10le` — because this library interpolates
chroma where ffmpeg replicates and a comparison of packed colour measures that instead. Every
compression identifier ffmpeg's encoder will write, 4:2:2 and 4:4:4, eight and ten bits, and a raster
that is not a whole number of macroblocks: **no sample differs by more than 5 of 255 at eight bits or
11 of 1023 at ten**, with 0.4% of samples differing at eight bits and by one level in the great
majority of those. The residue is the inverse transform and the last rounding of the quantiser; VC-3
specifies no particular IDCT and settles accuracy in its conformance document, SMPTE RP 2019-2, so
this evaluates the defining sum in double precision rather than reproducing a fixed-point
approximation.

One row of Annex C does not match the bitstreams and is worth recording. **Table C.2 sends
compression ID 1271 — DNxHR HQX — to Table D.1; every frame measured is quantised with Table D.4**
and decodes correctly only with that. This is not a coin toss between two readings: sweeping all
eleven weighting tables against the reference decode picks exactly one table per identifier, and for
every other identifier it picks the one Annex C names (1272 picks D.3, 1273 picks D.2, 1270 picks
D.11, and 1235 — which Annex C also sends to D.1 — picks D.1). Only 1271 picks otherwise, and by a
margin that is not arguable: worst sample difference 3 of 1023 with D.4 against 103 with D.1, at
every divisor the standard defines.

What refuses, by name: an unknown compression identifier, a header version outside the three defined,
an undefined sample depth code, an interlaced frame — field-encoded or the adaptive-macroblock mode
of compression ID 1260 — 4:2:0 sampling, a macroblock coded in RGB mode, an alpha channel, a
macroblock whose quantisation scale factor is zero, and any structure whose stated size does not fit
inside the one containing it.

### VC-1 / Windows Media Video 9

Intra pictures of the Simple and Main profiles, which is the first rung of SMPTE 421M and where this
stops. What it covers is the whole of clause 8.1: the picture layer, the predicted coded block
pattern, the differentially coded DC with both of its tables, the three-dimensional run-level AC
coding with all eight coding sets and all three escape modes, DC and AC prediction with the scan each
implies, both quantisers, the integer inverse transform of Annex A, and overlap smoothing.

The sequence header is not in the bitstream. Simple and Main profile state it as the thirty-two bit
`STRUCT_C` of Annex J, which the container carries as the stream's private data — so a Windows Media
Video stream cannot be decoded from its packets alone, and the demuxer's habit of handing that data
across untouched is what makes it decodable at all. Four bits of it are reserved and the standard
fixes all four, which is how thirty-two bits with no length, no signature and no checksum can be
recognised as a sequence header rather than something else.

Thirty-five intra pictures of seven files were decoded here and by ffmpeg and compared plane by
plane: Simple and Main profile, picture quantisers from 3 to 13, both the uniform and the nonuniform
quantiser, overlap smoothing on and off, both DC tables, and all four intra and all four inter coding
sets. **Every sample of every plane is identical** — 16.1 million samples, none differing anywhere.
Each picture also consumes its packet to within a byte, which is the cheapest evidence there is that
the bitstream was read the way it was written.

What refuses, by name: predicted, bidirectional and skipped pictures, each as what it is, because
every one of them needs motion compensation against a reference this builds no part of; the Advanced
profile, under its own code, since it carries a sequence header and entry point inside a byte stream
and shares only its block layer; and multi-resolution coding, range reduction and the in-loop
deblocking filter, where the sequence header signals them.

One note on the source. The freely circulating committee draft of SMPTE 421M prints its three intra
scan tables twenty-four columns wide on a page that fits twenty-three, so two cells of each fall past
the margin and are absent from the document. Each scan is a permutation of 0 to 63, so which two
values are missing is not in doubt; which position each belongs in follows from the scan's own
geometry, and all three scans are exercised by the frames measured above.

### Microsoft MPEG-4 version 2

The middle of the three variants Microsoft derived from MPEG-4 Part 2 before Windows Media Video, and
the one that turns out to be nearly all standard underneath. Intra and predicted pictures both, which
is the whole format — it has no bidirectionally coded pictures.

There is no start code, no sequence header and no video object layer header anywhere in the
bitstream. A packet is a picture, and the picture header is seven bits — two for its type, five for
the quantiser it uses throughout — plus five more for an intra picture's slice count or one for a
predicted picture's skip flag. Everything ISO/IEC 14496-2 states once per layer is fixed rather than
signalled, so the picture size comes from the container and there is nothing left to refuse.

**The tables are mostly the standard's**, which is the finding that decides the shape of the whole
decoder. The luminance coded block pattern is Table B-8 unaltered. The run-level codes are Table B-16
for an intra luminance block and Table B-17 for an intra chrominance block and for every block of a
predicted macroblock, both unaltered — the split is between the two tables rather than between intra
and predicted macroblocks, which is not something a reader of the standard would guess. The intra DC
size codes are Tables B-13 and B-14 with every bit inverted, so a differential of nought is `100`
where the standard writes `011`. The motion vector difference is Table B-12 with the sign taken out of
the code and read as a bit of its own, which works because the standard's codes for a difference and
its negation differ in nothing but their last bit. Only two small tables are Microsoft's own: the
chrominance pattern of an intra macroblock, and the eight macroblock types of a predicted picture.

What else differs is small and each piece is invisible until it is not. A macroblock has one motion
vector and never four. The quantiser is stated once per picture and the macroblock layer cannot
change it, which is why the alternating current prediction here needs no rescaling. Vectors reach
thirty-one and a half samples either way rather than a range the picture chooses. The intra DC step is
eight at every quantiser, where the standard varies it by a table. The DC gradient test uses `<=`
where the standard uses `<`, and the two disagree exactly where the gradients are equal — which is
everywhere in a flat region. A predicted macroblock states its luminance pattern inverted unless both
of its chrominance bits are set. And the second of the three escape forms adds nothing to the run it
recovers where the standard adds one, which is a single `+ 1` that lands every later coefficient of
the block one position out and still decodes.

Sixty-four encoded streams, four thousand four hundred frames, were decoded here and by ffmpeg and
compared plane by plane on every frame: four sources, sizes from 64x64 to 352x288, quantisers 3, 8, 16
and 25, and groups of pictures of one frame, twelve and a thousand. **Forty-nine of the sixty-four are
identical on every sample of every plane of every frame.** The other fifteen differ by exactly one
level and never by more — 1,910 samples out of some 450 million, at worst 64 samples of a frame of
152,064. That comparison is against ffmpeg's floating-point inverse transform; against its default
integer one the residual is larger and it is the transform's rather than the decode's, which is what
identifies it, since ISO/IEC 14496-2 Annex A specifies the inverse transform as an accuracy bound
rather than as an algorithm. The bit-level reading was checked separately over 2,960 pictures, every
one of which consumed exactly the bits it should, exercising all three escape forms.

What refuses, by name: version 1 and version 3, which are different bitstreams sharing this one's
name.

### Ut Video

Lossless and intra only, and the capture codec of the three here that is still in active use. A
sample is predicted from its neighbours, the difference is Huffman coded with one table a plane, and
the plane is cut into horizontal bands that share the table but nothing else — which is what lets a
decoder with four cores use them, and is the reason the codec exists.

Six colour spaces in eight codes. `ULRG` and `ULRA` are full-range colour, with and without
transparency; the digit in `ULY0`, `ULY2` and `ULY4` is the chroma subsampling and `ULH0`, `ULH2` and
`ULH4` are the same bits against BT.709's primaries rather than BT.601's. All four prediction methods
are read: none, left, the gradient, and the median of the two and the plane through them.

**Almost none of the coding is written down.** The author publishes the codes and their colour
spaces; the community write-up adds the sixteen bytes of stream description, the order of a plane's
parts, that a slice starts at `height * index / slices`, and that codes run from the longest length
down. Everything else was established here by measurement, and each piece is recorded in the code
beside what reading it the other way does to a picture:

  - **The bits are in little-endian words**, as HuffYUV's are, so every four bytes of a slice have to
    be turned round. Read in file order a plane decodes for a dozen samples and then wanders.
  - **Within one code length the symbols run from the highest down.** Ascending order — what every
    other Huffman format here uses — decodes a plane's short codes correctly and every long one
    wrongly.
  - **Prediction starts a slice at 128, not nought.** It is a running sum, so the starting value never
    leaves it: nought is wrong by exactly that on every sample of every plane.
  - **The median runs on past the end of a row** — the sample left of column zero is the last sample
    of the row above. The gradient does not: it starts every row from the sample above it. The two
    differ nowhere else, and the median's rule usually chooses the sample above anyway, so reading it
    the simple way gets four rows of a forty-eight-row frame wrong and the rest right.
  - **Blue and red are carried as their distance from green plus 128.** The write-up mentions the
    difference and not the 128; without it both planes are out by exactly that, which is a picture
    with its blues and reds inverted rather than one that looks broken. The same write-up gives the
    plane order as green, red, blue, where every file measured here has blue second and red third.
  - **A 4:2:0 frame is cut on whole chrominance rows.** Dividing each plane on its own height instead
    agrees on every frame whose boundaries already land on even rows, and puts the two planes on
    different bands of picture as soon as the slice count does not divide the height.
  - **A plane in which one symbol occurs gives it a length of nought and carries no bits at all.** A
    flat alpha channel produces one on every frame.

163 streams and 883 frames were decoded here and by ffmpeg and compared **plane by plane against
ffmpeg's own planes** — no colour conversion in the comparison, so the chroma siting of the
subsampled formats cannot hide anything — and every sample of every plane of every frame is
identical. That covers every pixel format ffmpeg's encoder writes, both colour-space spellings, slice
counts of one, two, three, four, five and eight, and sizes from 16x16 to 320x240 including several
where the slice count does not divide the height.

ffmpeg's encoder will not write the gradient predictor, so 36 of those streams were coded here and
handed to ffmpeg's decoder instead, which reproduces the picture that went in exactly. That is the
only way that predictor could be measured against anything, and it also puts the rest of the reading
— the code assignment, the word order, the slice division, the decorrelation — under ffmpeg's
judgement rather than this decoder's own.

What refuses, by name: the ten-bit Pro codes and the T2 codes, both different bitstreams sharing the
name and neither published; a frame coded with finite state entropy coding rather than Huffman, which
is the mode version 23 of the codec added and which the stream description flags; an interlaced
stream, since nothing states what the flag does to a frame's rows and no encoder reachable here
writes one to measure against; a code-length table that does not describe a complete code; and a
frame whose parts do not add up to its length.

One note on the source, because it is why this job is shaped the way it is. Microsoft published no
specification for any of the three. The Open Specifications programme documents Microsoft's protocols
and containers, not its codec bitstreams; SMPTE ST 421 standardised Windows Media Video 9 and says
nothing of the three before it; and the one Microsoft document that reaches a nearby codec specifies
motion compensation and deblocking for Windows Media Video 8 while leaving entropy decoding to the
host. The only public description of the bitstream is Michael Niedermayer's *DIVX3 / MS-MPEG4v1-v3 /
WMV7-8* (0.07, 2003, GNU Free Documentation Licence), which gives the syntax in full and then refers
the reader to a reverse-engineered decoder's source for every large table. The syntax here follows
that document; the tables were derived from the bitstream, by building pictures whose content was
known and reading back the codeword that had to stand for it, and — for the four macroblock types no
encoder emits — by writing streams that use a codeword and asking a reference decoder what it made of
them.

Version 3 is out of reach on the same evidence. Each of its pictures chooses which of six run-level
tables, which of two DC tables and which of two motion vector tables it was coded with, and all ten
are Microsoft's own with nothing published anywhere. The motion vector tables pair one code with a
whole vector across some eleven hundred entries, which no encoder can be driven to emit in full, so a
decoder derived from observation would be complete only where somebody happened to look. Version 1 has
no encoder in existence to derive its two macroblock tables from or to check a guess against.

### Theora

Xiph's free specification, and all of it: the three setup headers with their loop filter limits,
interpolated quantisation matrices and eighty Huffman codes; the run-length coded block flags; all
eight macro block coding modes under all eight mode-coding schemes; motion vectors in both of their
codings, with a vector per luma block and the chroma vectors averaged from them; block-level
quantisation indices; the 32-token coefficient alphabet; DC prediction across the four
reference-frame classes; the normative integer inverse transform; whole- and half-pixel prediction
from either reference frame; and the in-loop deblocking filter. All three pixel formats — 4:2:0,
4:2:2 and 4:4:4 — decode.

Theora is On2's VP3 with a specification written for it, and the descent shows in the parts a decoder
has to get exactly right. Its coordinate system is right-handed, so the origin is the bottom-left
corner and every position in the format counts upwards. Blocks are walked in a coded order that is a
Hilbert curve inside each 4x4 super block, with any block past the edge of a plane simply left out —
while DC prediction and the loop filter walk the same blocks in raster order, so both mappings have
to exist. The coefficients are grouped by frequency rather than by block: every block's DC token,
then every block's first AC token, and so on for all 64 positions, with a single end-of-block run
able to finish blocks scattered across the frame and then carry on into the next pass. And the DC
predictor extrapolates a gradient from three neighbours with weights of 29, −26 and 29 over 32, then
checks whether it has run away from any of them by more than 128 and falls back on that neighbour's
own value if it has.

Two rules are easy to read past and change every sample if missed. The inverse transform is normative
to the bit — its intermediate truncations to sixteen bits are part of the specification and not an
artefact of a narrow register, so a decoder with wider ones has to reproduce them deliberately. And a
block whose coefficient count is under two takes a direct-current shortcut that is *not* equivalent
to running the full transform, because it skips those truncations; whether it applies is decided by
the count the token layer kept, not by looking to see whether the other coefficients happen to be
zero.

Twenty-five encoded streams — 1,925 coded frames, 1,717 of them carrying coded blocks — were compared
with ffmpeg's decode of the same bitstreams plane by plane and sample by sample. Every plane of every
frame is identical: not close, not on average, the same bytes. The streams cover all three pixel
formats, quality settings from the lowest to the highest, rate-controlled encodes, a scene change,
heavy motion, still scenes that are almost entirely uncoded, per-pixel noise that is almost entirely
coded, a 250-frame group of pictures, picture sizes that are not a whole number of macro blocks in
either direction, and frames from 100x70 to 1920x1080.

Two notes on measuring it. ffmpeg's Theora decoder emits no picture at all for a zero-length packet,
where section 7.11 defines one as an inter frame with nothing coded — a duplicate frame — so those
208 packets are decoded here and produce the previous picture again; they are excluded from the
comparison, since by construction they are the frame before. And ffmpeg's frame-threaded decode of
this codec is not deterministic on large frames: on four of the streams it left bands of chroma at
zero, differently on each run, and every one of those differences went away under `-threads 1`.

What refuses, by name: a bitstream version other than 3.2, the pixel format Table 6.4 reserves, a
reserved bit that is set in either the identification header or a frame header, a stream that begins
at an inter frame or a duplicate one, a packet that ends part way through its coded data, a Huffman
table with more than 32 entries or a code longer than 32 bits, quant ranges that do not cover the
quantisation scale exactly, and a stream whose container carried no setup headers. There is no
`catch` anywhere that hands back a blank frame or repeats the last one.

### FLIC

Autodesk Animator's format, and the one container here with no demuxer underneath it at all: a file
is its own codec's bitstream, a 128-byte header and then a run of frame chunks with nothing else
between them. Splitting it into the same four contracts as every other format here still asks two
different questions of the same bytes. `FliContainer` answers the first — where a frame begins and
ends, and nothing about what is in it — from a single field, each frame chunk's own four-byte size;
`FlicVideoDecoder` answers the second, walking every palette packet and delta opcode the container
never touches. What a `CodedPacket` carries out of this container is a frame chunk's sub-chunks
verbatim, exactly what a JPEG carries out of a raw Motion JPEG stream: the codec's own bitstream
syntax, not container framing.

One piece of container-shaped judgement still belongs on the demux side: whether a frame carries a
whole-frame picture chunk (`BLACK`, `BRUN` or `COPY`) is what decides `IsKeyFrame`, and that is a
structural fact about which sub-chunk types are present — the same kind of fact an MP4 sample flag or
an ASF key-frame bit states outright — read by scanning six-byte sub-chunk headers rather than by
decoding any of them.

Palette updates (`FLI_COLOR64`, `FLI_COLOR256`), delta-coded frames (`FLI_LC`, the original Animator's
byte-oriented coding, and `FLI_SS2`, Animator Pro's word-oriented one), whole frames (`FLI_BRUN`
byte-run and `FLI_COPY` uncompressed), `FLI_BLACK`, and `PSTAMP` — a postage-stamp thumbnail for a file
requestor, skipped rather than decoded into the canvas. Both magic numbers are read, `0xAF11` (`.fli`)
and `0xAF12` (`.flc`, and an eight-bit `.flx`); every other FLIC-family magic — Huffman/BWT compression,
frame-shift compression, and DTA's non-eight-bit form — is a different, undocumented bitstream sharing
only a file extension, and is refused by name rather than guessed at.

Two places this is quietly wrong to get slightly wrong. A palette packet's skip and change counts are
in palette *entries*, not bytes — a two-byte packet header in front of up to 256 three-byte colours,
and a change count of zero means all 256 rather than none. And `FLI_COLOR64` packs each component in
six bits rather than eight, widened here by repeating the top two bits into the bottom
(`ChannelScaling.Expand6`) rather than by a plain shift, the same rule this library's other six-bit
channels use — measured against ffmpeg's decode of every `.fli` sample that carries the chunk, where a
plain `<< 2` disagrees on every colour but black and white.

The third: a `.fli`'s last frame chunk is not a picture of the film. It is the ring frame — a delta
back to frame one, written only so a player can loop without paying to re-decode the run-length-coded
first frame — and every clean file pulled from ffmpeg's own sample corpus carries exactly one more
frame chunk than its header's `frames` field states, with the file ending exactly there. `FliContainer`
stops after exactly `frames` packets, so the ring frame is never handed out as an ordinary extra frame;
ffmpeg's own frame count is one higher, which is not a difference to match; the header's own count is
what a caller asking for the pictures of the film is asking for.

`.flc`'s `oframe1` field is trusted over the assumption that frame one sits directly behind the header.
One sample — `2422.FLC`, from ffmpeg's own corpus — needs it: a 2778-byte `PREFIX_TYPE` chunk of
undocumented Animator Pro settings sits between the header and frame one, and `oframe1` is the only
thing in the file that says where frame one actually is. `.fli`'s header has no such field, and its
frame one always sits at byte 128 — the format states no other possibility.

**The coding is lossless**, so the target is exact agreement and there is no rounding excuse. Twelve
files pulled from `samples.ffmpeg.org/fli-flc`, including its `fli-bugs` subdirectory — ffmpeg has no
FLIC encoder, only a decoder, so ffmpeg served as a decoding oracle rather than as the source of a
built corpus — were compared against ffmpeg's own decode frame by frame: 320x200 up to 720x360, both
magic numbers, chains from 6 frames to 384 with no drift anywhere along the longest of them, and every
sample of every one of 1,418 frames identical. Chunk types exercised by those files: `FLI_COLOR64`,
`FLI_COLOR256`, `FLI_LC`, `FLI_SS2`, `FLI_BRUN`, and `PSTAMP` — `2422.FLC` carries a genuine 100x63
byte-run thumbnail on its first frame, behind the `oframe1` prefix chunk above. `FLI_COPY` and
`FLI_BLACK` are not reachable in any sample this was built against or could be fetched — ffmpeg's own
demuxer only decodes the format, and every sample found opens with a byte-run first frame — so both
are covered by hand-built fixtures instead, checked against the specification's stated behaviour
rather than against a third-party decode.

One sample in ffmpeg's `fli-flc/fli-bugs` directory, `malev2.fli`, is genuinely corrupted: its
twenty-first frame chunk states magic `0xF5FA` where every other frame in every file states `0xF1FA`,
a single corrupted nibble. This is refused by name, citing the frame index and the byte the magic was
found at, rather than resynchronised or skipped — and ffmpeg's own decode of the same file is why.
Decoded and compared frame by frame against itself, ffmpeg's output is correct for the first eighty-odd
frames and then, without erroring, repeats its very first frame byte-for-byte for the last sixteen of
its 102 — a `catch` somewhere silently handing back a blank canvas or the frame it already had, exactly
the failure mode this project's decoders are built not to produce. Matching that would mean matching a
masked failure rather than a decode.

What refuses, by name: a magic outside `{0xAF11, 0xAF12}`; a depth other than eight bits; a picture of
zero width or height; a frame chunk whose magic is not `0xF1FA`; a frame stating a picture-size
override, which no sample here uses and which nothing states how to apply mid-stream; a sub-chunk type
outside the eight this reads; a palette write reaching past the 256th entry; a delta chunk naming lines
past the picture's height; an opcode writing pixels past a row's width; an opcode wanting more bytes
than the packet holds; and a `FLI_BRUN` packet whose count is zero, which is a no-op under one sign
reading and a single ambiguous byte under the format's own reversed convention for `FLI_LC` — the two
disagree about everything that follows and no encoder has a reason to write one. There is no `catch`
anywhere that hands back a blank frame or repeats the last one.

### Zip Motion Blocks Video

Lossless, and built entirely out of zlib: DOSBox's screen-capture codec spends no bits of its own on
entropy coding, only on saying which rectangular block of the picture changed and how. A block is
either copied whole from wherever a motion vector in the frame before it points, or copied and then
corrected with an XOR'ed difference — the whole of the format is that choice, repeated once a block,
laid end to end and handed to DEFLATE.

**The trap is not the block arithmetic, it is the zlib stream.** ZMBV's own description says to reset
zlib for an intraframe and nothing about doing so for any other kind, which means every interframe's
compressed bytes are a continuation of the same stream the intraframe opened — meaningless read on
their own, since a block's copied bytes and an intraframe's raw picture both sit in the same 32
kilobyte window a decoder's dictionary is supposed to carry forward. A decoder that opened a fresh
zlib stream per packet would decode an intraframe correctly, since a lone one carries a complete
stream of its own, and then diverge on the interframe straight after it — silently, with no packet
ever failing to decompress, which is what makes it the trap rather than an ordinary bug. See
`ZmbvInflater`, which holds one zlib stream open for as long as intraframes let it and only ever
changes which bytes it is fed next.

Both of the format's other pieces of state live for exactly as long as the dictionary does: the
picture a block's motion vector is copied out of, and — in the one pixel layout that carries one — the
palette. An intraframe states a palette outright; an interframe with the palette-change bit set states
768 bytes to XOR into the one already held. There is no palette anywhere in the container for this
codec: `MediaStreamInfo.CodecPrivateData` is not read at all, because the stream carries its own.

**Measured against ffmpeg's own encoder**, one of the few codecs in this package that has one. Six
streams and 460 frames — 8-bit palettised, 15-, 16- and 32-bit pixel layouts, a picture that is not a
whole number of blocks in either direction so a block's copy and its XOR correction are both clipped
at the picture's edge rather than padded to a hidden grid, a stream carrying more than one intraframe,
and a 150-frame run long enough that a dictionary carried wrongly across even one packet would have
shown up in the frame right after it. **Every sample of every frame is identical** — these are RGB and
palette-index native pixel layouts, so a direct sample comparison is the right one and not the
convention this package otherwise has to work around for 4:2:0 codecs. The one piece of the format no
encoder here will write — a palette-change interframe — was checked the other way round, against a
hand-built stream ffmpeg decodes the same way this does.

What refuses, by name: a stream that opens on an interframe, which has no picture to predict from and
no zlib stream to continue; a version other than the only one the format defines, 0.1; a block width or
height of zero; a pixel layout the format defines but no encoder writes — 1, 2 and 4 bits a pixel
palettised, and 24 bits a pixel — since there is nothing to measure a guess at their byte packing
against; and a packet whose compressed data runs out before its frame does.
### MagicYUV

Lossless and intra only, and the codec of the three lossless ones here whose format is least
described. Huffman coding over a spatial prediction, with the frame cut into slices that decode
independently. Seven codes: `M8RG` and `M8RA` for colour with and without transparency, `M8Y0`,
`M8Y2`, `M8Y4` and `M8YA` for the subsamplings, and `M8G0` for grey.

**Almost nothing about the bitstream is published.** It is a commercial codec with no specification
and no format note. Public are: a list of the four-character codes and their pixel formats, from the
codec's author; that it is intra only and cut into slices, also from him; and the rule by which its
Huffman codes are built from the transmitted lengths, which is stated in ffmpeg's commit messages
rather than in its source. That is the whole of the record. Everything else here was established by
measuring frames against the pictures they were made from, and each piece is recorded in the code
beside what reading it the other way does:

  - **The frame carries its own header** — signature, size, slice height, tables and all — and the
    sixteen bytes an AVI holds behind the `BITMAPINFOHEADER` are a copy of it, so the container
    contributes nothing but the picture size.
  - **The offsets are a permutation, and it runs the other way from the obvious one.** The map's
    k-th entry names the piece the k-th offset belongs to, not the offset the k-th piece uses. On a
    frame of one slice the two readings are the same permutation, so a single-slice frame decodes
    perfectly either way and every other frame comes apart — which is how it was found.
  - **The bits are plain bytes, most significant first.** No little-endian word swapping, which both
    HuffYUV and Ut Video need and which decodes nothing here.
  - **Within one code length the symbols run ascending**, where Ut Video's otherwise identical
    construction runs them descending.
  - **Every row starts again from the sample above it**, not from the end of the row before. Reading
    it the other way — the way HuffYUV and Ut Video work — decodes the first row of a plane exactly
    and puts every row after it out, which is how it was found: a plane that agreed for exactly its
    first 64 samples and disagreed from the 65th.
  - **A slice may carry its differences as plain bytes** rather than coded, and says so in its first
    byte. The prediction still applies. Sixty-four streams of the corpus contain one, so it is not an
    edge case — a frame too small for a table to pay for itself produces them, and so does noise.
  - **The colour planes are blue, green, red**, with green carried plainly and the other two as their
    distance from it — with no offset, where Ut Video's otherwise identical decorrelation adds 128.

309 streams and 1,446 frames were decoded and compared plane by plane, every sample of every plane of
every frame identical. **The oracle here is not another decoder.** The ffmpeg built on this machine
has MagicYUV's encoder but not its decoder, so what the decode is measured against is the rawvideo
that went into the encoder — which for a lossless codec is the stronger of the two, being the ground
truth rather than a second opinion. The corpus covers all seven pixel formats its encoder writes, all
three predictors, slice counts of one to eight, and sizes from 1x1 to 320x240 including odd widths and
heights and slice counts larger than the number of rows.

One thing is assumed rather than measured, and is called out where it happens: the conversion from
luminance and chrominance to pixels uses BT.601. The codec's author has said publicly that which set
of primaries a file uses is carried inside the stream, but no field of the header changes when the
encoder here is asked for BT.709 — it simply never writes one — so there is no file against which a
reading of that field could be found. It affects only the pixels handed back and none of the samples
the codec codes, which is why the comparison above is made on the planes.

What refuses, by name: the codes for samples deeper than eight bits, `MAGY` from before each format
had a code of its own, grey with an alpha channel, a frame whose header size or version byte is not
the one measured, a frame without its signature or stating a size other than the stream's, a
code-length table that does not describe a complete code or holds a code longer than the frame says
it uses, a slice height that does not divide by the chrominance block, a slice map that names a piece
twice, and a slice whose first byte is neither of the two values that mean anything. There is no
`catch` anywhere that hands back a blank frame or repeats the last one.

### TechSmith Screen Capture

Lossless, and built from a document rather than a guess: "Description of the TechSmith Screen Capture
Codec (TSCC)" by Mike Melanson and Konstantin Shishkov gives the whole of the coding. It is zlib
wrapped around a run-length coding that is Microsoft's own in every particular but the width of a
pixel — a count and a colour repeated, or one of three escapes that move the pen instead of painting
with it — walked onto the picture from the bottom row up, at eight bits a pixel through a palette or
at sixteen, twenty-four or thirty-two directly.

**Not every packet carries a picture.** A screen-capture video spends most of its frames on content
that has not changed at all, and this codec's answer to a completely unchanged frame is not to
compress an empty delta — compressing nothing costs more bytes than it saves — but to write a few
bytes that are not zlib data at all and carry no picture. Measured directly: on one sample, 348 of 849
packets open with a valid zlib header, and ffprobe's own frame count for the same file is 348 exactly.
The other 501 are read off the bytes rather than a flag the format states nowhere — a valid zlib
stream's header carries a checksum property, `(CMF × 256 + FLG) mod 31 = 0`, that essentially never
holds by accident — and a packet that fails it produces no frame at all, the same "not yet" this
package's decoder interface already has a word for.

**Measured against ffmpeg**, at every depth a real sample was found at. Four files and 2,240 frames —
16-bit (555), 24-bit and 32-bit pixel layouts, one of them truncated to a fraction of its stated length
by the mirror it was fetched from and read for whatever whole frames that leaves — decoded here and
compared against ffmpeg's own decode of the same packets. **Every sample of every frame is identical.**
The palettised 8-bit path, which none of the four samples happens to use, was checked the other way
round: a hand-built stream exercising a run, an absolute copy, a position-change escape and an
unchanged-frame packet, decoded here and by ffmpeg, agreeing on every index and every palette entry.

What refuses: a depth the format does not define; a palettised stream with no palette to decode its
indices to, since the palette is the container's business — TSCC's own document says so — and never
carried in a frame; and any opcode that runs off the picture or off the end of the decompressed data.

### H.265 / HEVC

Intra pictures, decoded from ITU-T H.265 itself rather than from anybody's implementation: NAL and
Annex B parsing with emulation prevention, the video, sequence and picture parameter sets, the CABAC
engine with its contexts entered from the normative initialisation tables, the coding tree quadtree,
all thirty-five intra prediction modes with reference substitution and both smoothing filters, four
transform sizes including the 4x4 sine transform the smallest luma blocks use, dequantisation with
scaling lists, residual coding with sign data hiding, per-unit quantiser derivation, wavefront
entropy synchronisation, the deblocking filter, the sample adaptive offset, and Annex C output
bumping.

Forty-two intra streams were compared plane by plane against ffmpeg on every frame — sizes from 34x18
to 640x360, every x265 preset from ultrafast to placebo, coding tree units of 16, 32 and 64, quantiser
groups of 8 to 64, transform depths 1 and 4, the sample adaptive offset and deblocking and sign data
hiding each on and off, transform skip, lossless, quantisers 4, 32 and 48, and runs of 100 and 200
frames. **Every sample of every plane of every frame is identical**, against ffmpeg's default integer
transform and against `-idct faani` alike.

**Predicted and bidirectional slices are refused, and the reason is the interesting part.** The inter
path is written and mostly works: reference picture sets, list construction, merge and advanced motion
vector prediction, temporal candidates, the eight-tap luma and four-tap chroma interpolation, weighted
prediction. Forty-four of fifty measured predicted-slice streams are bit-exact, including long-GOP
runs. The other six build the motion candidate list differently from the reference for certain coding
structures, differing in a tenth of a percent to one percent of samples. A codec here is exact or it
is refused, so it refuses — naming `slice_type` and Table 7-7, and saying that intra pictures are
exact. The code stays in the tree, unreachable, with the reason recorded against it, because the
distance left to run is small and throwing it away would mean finding it again.

Also refused by name: tiles, dependent slice segments, coding units coded as raw samples, 4:2:2,
4:4:4, monochrome, more than eight bits a sample, separate colour planes, and the range, screen
content, multilayer and three-dimensional extensions. There is no `catch` anywhere that returns a
blank, a copied or a partial frame.

### CamStudio Screen Codec

Lossless, and the MultimediaWiki page names the whole of the coding in five lines: a header byte whose
top seven bits choose LZO or zlib and whose bottom bit says whether this is a key frame, a reserved
byte, and the compressed data. A key frame is a whole picture; a delta frame is the same compression
around a difference added onto the frame before it.

Two things the page does not say, and this reads off real files rather than guesses at. **Which
compression a header value actually means** — the page's two-case switch, 0 for LZO and 1 for zlib,
never once appears as literally 1 in a real file; every zlib stream measured states some other nonzero
value instead, so nonzero is the rule read off the bytes here rather than the documented case. And
**what "add deltas to previous frame" means at the byte level** — unsigned addition with the result
wrapped modulo 256, settled by reconstructing a real captured frame both ways and finding addition
matches ffmpeg's decode exactly where the same byte-wise XOR a natural first guess would reach for
differs in 67,144 of 1,572,864 bytes on the same frame.

**A coded row is a whole four-byte word**, the convention every Windows bitmap this format is built
around uses and which nothing in the wiki page states. A 239-pixel-wide, 24-bit picture is 717 bytes
of pixels and 720 bytes of picture, and a delta read against the packed width alone lands three bytes
out of step on every row after the first — silent corruption, not a refusal. It was found by a match
instruction inside a real file whose distance was consistently the padded stride and made no sense as
the packed one.

**There is no 8-bit palettised mode**, despite the format otherwise mirroring TSCC closely enough that
assuming one seemed reasonable. Building a stream that stated one and handing it to ffmpeg settled it:
ffmpeg's own decoder refuses a depth of 8 bits a pixel by name, "invalid depth 8 bpp", so this refuses
it the same way instead of inventing a palette layout nothing exercises.

The LZO compression the header can choose is CamStudio's, and the MultimediaWiki page for it says
outright that no specification exists. "LZO stream format as understood by Linux's LZO decompressor"
(Willy Tarreau, 2014; updated by Dave Rodgman, 2018) is the closest thing to one, and even it disagrees
with itself in one place: its bit diagram for the sixteen-bit word that carries a match's distance and
the literals to copy after it can be read two ways, and the two disagree about which bits are which.
The `lzop` command-line tool's own encoder, used to build streams of known content, settles it — the
state is the low two bits of the word and the distance is everything above them.

**Measured against ffmpeg**, at every depth and every compression a real file was found at. Four
streams and 6,309 frames — 16-bit (555), 24-bit and 32-bit pixel layouts, both compressions the header
names, and a picture whose row is not a whole number of four-byte words. **Every sample of every frame
is identical.**

What refuses: 8 bits a pixel, which ffmpeg's own decoder refuses as well, and any depth besides that
and 16, 24 and 32; an LZO stream whose instructions run off the end of the compressed data or whose
match reaches before the start of the picture; and a zlib stream that does not decompress to exactly
the bytes a frame of this picture needs.

### Flash Screen Video

Lossless, and simpler than either TSCC or CSCD: a grid of blocks, each block either unchanged since the
frame before it or its own independent zlib stream, and nothing else — no run-length layer, no delta
arithmetic, no palette. Read from the SWF File Format Specification's own appendix rather than from any
decoder's source, since Adobe published Screen Video's bitstream in full once the format's licensing
restrictions lifted.

**Nothing about the picture's size comes from the container.** A Flash Video file states no width or
height for this codec at all unless a script tag happens to carry one, and this format does not need
one: every packet opens with a four-byte header stating both the block grid's cell size and the
picture's own size, packed as four bit fields — `BlockWidth` and `BlockHeight` as `(actual / 16) - 1`,
so a cell is a multiple of sixteen up to 256, and `ImageWidth` and `ImageHeight` as twelve-bit pixel
counts — the whole thirty-two bits big-endian with no byte swap. The geometry a decoder needs is
therefore read fresh from the bitstream and held, the same shape this package already uses for Sorenson
Spark, which states its own picture size the same way.

**The grid, and which blocks are partial.** Cells are counted by dividing the picture's width and
height by the cell's own and rounding up, so a remainder becomes one partial column and one partial row
rather than being dropped — exactly the last column and the last row, the two edges furthest from where
counting starts. Blocks are ordered bottom row first, left to right within a row, upward to the top,
stated plainly in the specification; the pixels inside a block are ordered the same way, so a block's
first decompressed row is its own bottom row and copies straight across into a canvas built the same
way round.

**A block's two-byte length is the whole of the inter-frame coding.** There is no difference operation
here at all, unlike CSCD's byte-wise addition — a length of zero means this block is exactly what the
canvas already holds, and a nonzero length means a complete, self-terminating zlib stream for exactly
that cell's pixels, decompressing to precisely the width and height its grid position implies and never
a length the format states anywhere else. That makes the canvas the entire state a decoder carries
between packets: an unchanged block costs nothing to honour because there is nothing to do to it.

Pixels are three bytes, B, G, R, which the specification states directly and which is this package's
own `Bgr24` layout byte for byte, so no channel reordering happens anywhere in this decoder.

**Measured against ffmpeg**, built with its own flashsv encoder since no real-world corpus of this
format was needed to reach every path — five streams, 106 frames, sizes that are and are not a whole
number of the encoder's 64x64 blocks in either direction, down to a 20x14 picture that is one partial
block covering the whole thing, and a mostly static capture whose interframes leave most of the grid at
zero length. **Every sample of every frame is identical**, RGB-native so the comparison is a direct one
rather than a plane-by-plane approximation of anything subsampled.

What refuses: a packet shorter than its own four-byte grid header; a picture the header states as zero
pixels in either direction; a block whose two-byte length runs past the packet's own end; a zlib stream
that does not decompress to exactly the byte count its position in the grid implies; and a packet whose
header states a different picture size or cell size than the one this decoder has already built its
canvas against, since every unchanged block from that point on would be read against a canvas the new
geometry does not describe.

### v210

Not a codec in the sense any other entry in this file is — a packing rule, and the whole of it fits
in one paragraph. Ten-bit 4:2:2 YUV, three components a word: six luma samples and three chroma pairs
sit in four little-endian 32-bit words, ten bits a component in bits 0-9, 10-19 and 20-29, the top two
bits of every one left unused. A row is padded out to a whole 128 bytes — eight such groups, forty-eight
luma samples — and a picture whose width is not a multiple of six still codes a whole group for its
last few columns, with the samples that fall past the picture's own edge read and discarded rather
than assumed absent.

Verified on the planes and not on packed colour, because this is a lossless repacking of the ten-bit
samples themselves and the planes are what that claim is about. Three geometries and 120 frames of
ffmpeg's `testsrc2` — 22x18 and 98x60, both needing row padding, and 48x32, exactly eight groups and
needing none — carried through v210 and decoded here, compared sample for sample against ffmpeg's own
raw `yuv422p10le` output of the same content before it was packed: every sample of every plane of
every frame is identical, because nothing in a fixed packing rule with no prediction and no entropy
coding underneath it is capable of losing one.

The packed colour a caller gets back is a display convention on top of that and not part of the
claim above — ITU-R BT.601 with studio swing, ten bits reduced to eight by a plain shift of two, and
each chroma pair repeated across the two luma columns it covers rather than interpolated between
neighbours, the same choice this package's HuffYUV decoder made and for the same reason: it is what
the reference decoder's own conversion does.

What refuses: a picture with no pixels, and a packet shorter than its padded stride times its height.

### r210

Another packing rule rather than a codec proper: 10-bit RGB, one 32-bit big-endian word a pixel, a row
padded out to a whole 256 bytes. MultimediaWiki's page for the format writes the bit string with red
in the high ten bits after two unused ones, green in the middle and blue in the low ten — and measured
against a real encoder that is backwards. Red sits in the word's low ten bits, green in the middle and
blue in the high ten, found by sweeping every reading of which component owns which bit range against
ffmpeg's own r210 encoder fed a picture of known samples, where exactly one reading reproduces the
source for every pixel of every geometry tried.

Decoded straight into `PixelFormat.Rgb30` and nothing is lost doing it. That format's own layout — red
in bits 0-9, green in 10-19, blue in 20-29, little-endian — is exactly what falls out of reading r210's
big-endian word and writing the same bits back little-endian, so there is no eight-bit reduction and no
display convention between the coded samples and what a caller receives. The two bits this format
leaves unused become the alpha field `Rgb30` reserves in the same position, set to fully opaque because
that is what ffmpeg's own decoder writes there — carrying a stream through it and back out to
`x2rgb10le`, the same 30 bits in the same arrangement this format owns, reproduces every sample of
every frame exactly, alpha included.

Three geometries and 90 frames of ffmpeg's `rgbtestsrc` — 8x2 and 64x40, a whole number of 256-byte
rows, and 33x25, which needs the padding — carried through r210 and decoded here, compared word for
word against the `x2rgb10le` samples that went into the encoder: **every one identical.**

What refuses: a picture with no pixels, and a packet shorter than its padded stride times its height.

### r10k

AJA's Kona 10-bit RGB layout, and a close relative of r210 rather than the same format under a second
name — the two differ in where the ten-bit fields and the two unused bits sit inside the word, and in
whether a row carries any padding at all. Neither is written down anywhere this project found; both
were recovered the same way, by sweeping every reading of which component owns which bit range against
ffmpeg's own encoder fed a picture of known samples and keeping the one that reproduces the source for
every pixel.

Red sits in the high ten bits of the big-endian word, bits 22-31; green in the middle ten, bits 12-21;
blue in the next ten down, bits 2-11; and the two unused bits are the *low* two of the word rather than
the high two r210 leaves spare. **There is no row padding at all** — a row is exactly `width` times
four bytes, measured against three geometries including one whose unpadded row is not a multiple of
any alignment r210's family uses, and ffmpeg's own encoder never writes a byte beyond it.

Decoded straight into `Rgb30`, as r210 is — but unlike r210 this is a real repacking and not a plain
byte reversal, since r10k's own bit arrangement is not `Rgb30`'s: each component is pulled out of its
own position in the big-endian word and written back into the little-endian one. The two bits this
format leaves unused become that format's alpha field, set to fully opaque, which is what carrying a
stream through ffmpeg's own decoder and back out to `gbrp10le` shows those two bits are worth.

Three geometries and 90 frames of ffmpeg's `rgbtestsrc` — 8x2, 33x25 and 64x40 — carried through r10k
and decoded here, compared word for word against the `gbrp10le` planes that went into the encoder:
**every one identical**, with no row padding found at any of the three widths.

What refuses: a picture with no pixels, and a packet shorter than its stride times its height.

### y41p

4:1:1 YUV with nothing compressed at all, twelve bytes packing eight luma samples and the two chroma
pairs that cover them. There is no MultimediaWiki page for this one, so the layout was recovered rather
than read: synthetic pseudo-random frames were carried through ffmpeg's own y41p encoder and swept
against every placement of which byte holds which sample. One group of twelve bytes is

```
U(0,1,2,3)  Y(0)  V(0,1,2,3)  Y(1)  U(4,5,6,7)  Y(2)  V(4,5,6,7)  Y(3)  Y(4)  Y(5)  Y(6)  Y(7)
```

— the first chroma pair ahead of the first two luma samples, the second ahead of the next two, and the
last four luma samples running on with no chroma among them. A row is exactly `width` times one and a
half bytes and there is no padding: ffmpeg's own encoder refuses a width that is not a whole number of
eight-pixel groups outright, so this refuses the same width for the same reason.

**Rows are coded bottom row first** — the same convention every Windows bitmap this format was built
around uses, and the reason the first sweep against real content found no placement that fit at all:
every byte looked plausible and none of them were right, because the row being compared against was
the wrong row entirely. Comparing each coded row against the picture's rows in reverse turned a match
rate indistinguishable from noise into an exact one.

Verified on the planes and not on packed colour, because this is a lossless packing of the eight-bit
samples themselves. Three geometries and 90 frames of pseudo-random content — 64x8, 96x40 and 128x33,
all a whole number of eight-pixel groups since ffmpeg's encoder accepts no other — carried through
y41p and decoded here, compared sample for sample against ffmpeg's own raw `yuv411p` output of the
same content before it was packed: every sample of every plane of every frame is identical.

The packed colour a caller gets back is a display convention on top of that, as with v210 — ITU-R
BT.601 with studio swing and each chroma pair repeated across the four luma columns it covers.

What refuses: a picture with no pixels, a width that is not a multiple of eight, and a packet shorter
than its stride times its height.

### CLJR

Cirrus Logic AccuPak — the one lossy entry among these packing rules, four pixels of 4:1:1 YUV
quantised into one 32-bit word rather than repacked whole. The loss is the encoder's; a decoder reading
the coded bits back has nothing left to round, which is what makes exact equality the right bar for a
lossy format here.

The word, read big-endian: bits 27-31 are the fourth luma sample, bits 22-26 the third, bits 17-21 the
second and bits 12-16 the first — the four columns in *reverse* order — then bits 6-11 are the shared
chroma blue difference and bits 0-5 the shared red difference. Recovered by quantising pseudo-random
content through ffmpeg's own encoder with dithering held to one fixed algorithm and sweeping every
placement of five- and six-bit fields against ffmpeg's own decode of what it wrote — the oracle this
format needs, because dithering carries a sample's rounding error into the columns after it, so a coded
word is not a plain quantisation of the source and only another decoder's reading of the bits is a fact
the encoder can be checked against.

**Two different rules turn five and six bits back into eight, and they are not the same rule at two
widths.** Luma replicates its own top three bits into the three it does not carry, the usual way of
filling a narrower channel without landing short of white. Chroma does not — a coded value of 41 decodes
to 164, which is `41 << 2` exactly, and not the 166 the same replication would give. Rows run top to
bottom, unlike y41p's, checked the same way that format's row order was found and getting the opposite
answer.

Three geometries and sixty frames of pseudo-random `yuv411p` content, quantised through CLJR and decoded
both here and by ffmpeg: **every sample of every plane of every frame identical.**

What refuses: a picture with no pixels, a width that is not a multiple of four — ffmpeg's own encoder
refuses the same width — and a packet shorter than its stride times its height.

### H.261

The codec H.263 grew out of, and the ancestor that makes H.263's own section above worth reading first:
the two share the macroblock, block and quantisation shape closely enough that this decoder reuses
H.263's picture buffer, colour conversion, coefficient dequantisation, zig-zag scan and inverse
transform outright — the arithmetic was checked term for term against ITU-T Recommendation H.261
(03/93) rather than assumed, and it is the same formula in both Recommendations, which is recorded in
the doc comments of `H261BlockDecoder` alongside the clause numbers on both sides. Everything else is
H.261's own, because everything else differs.

Two picture sizes only, QCIF and CIF, chosen by one bit of PTYPE (clause 3.1) — no source-format field
and no extended header. **No picture-level intra/inter flag at all**: every macroblock states its own
prediction mode in MTYPE (Table 2), so one picture may freely mix intra- and inter-coded macroblocks,
and only the very first picture of a stream is constrained, by having nothing yet to predict from.
Motion vectors are **whole-pixel**, not half-pixel — clause 3.2.2 gives them integer components not
exceeding ±15 — so there is no bilinear interpolation, and the chrominance vector is derived by
truncating towards zero rather than H.263's Table 18 rounding. A macroblock's address is coded as the
**difference from the last transmitted one** (clause 4.2.3.1), and a gap greater than one means the
macroblocks in between carry no bits at all: not coded with a zero residual, simply never visited, which
this decoder implements by seeding every predicted picture's canvas with a copy of the reference before
a single macroblock of it is read, so an address nothing ever mentions comes out exactly as the
reference left it. Table 5's coefficient coding is not H.263's single self-terminating table: an
explicit end-of-block symbol exists and cannot be a block's first thing, so the first coefficient of a
coded block and every one after it are read from two different tables.

**The loop filter is part of prediction, not a post-decode step.** Clause 3.2.3's optional two-dimensional
spatial filter — nominally 1/4, 1/2, 1/4 in each direction, degenerating to 0, 1, 0 at a block edge
rather than reading past it, full precision kept between the horizontal and vertical passes and a
fractional half rounded up — runs on the motion-compensated prediction *before* the residual is added
to it, when a macroblock's MTYPE asks for it ("Inter + MC + FIL", which Table 2's own second note says
may be requested with a zero vector). That is a different place in the pipeline from every other filter
this package reads: H.263 baseline has none at all, and VP8's and VP9's both run on the finished,
reconstructed picture after the residual, so what a later picture predicts from is the filtered result.
Getting H.261's ordering backwards — add the residual, then filter — reads the wrong samples through
the filter and desyncs from the encoder by an amount that compounds every predicted picture after it,
which a single still frame cannot show; it is verified here by a hand-built stream whose filtered block
is column-invariant, so the two-dimensional filter reduces to one dimension and every value can be
worked out by hand, including the one column where clause 3.2.3's "round a fractional half up" rule is
the only thing standing between two adjacent integers.

**Measured against ffmpeg.** Two streams — a QCIF and a CIF clip, sixty frames each, one intra picture
anchoring the whole chain (`-g 1000`, which the encoder itself clips to six hundred) — were decoded
here and by ffmpeg and compared plane by plane, sample by sample, every frame. Against `-idct faani`,
the QCIF stream differs in 518 samples of 2,280,960 and the CIF stream in 1,022 of 9,123,840, both
capped at one level — 0.02% and 0.01% of the samples measured, the residual Annex A's accuracy bound
exists to allow and not a disagreement about the bitstream. Against ffmpeg's default integer transform
the difference is larger — up to four levels on a few hundred thousand samples a frame — which is the
same size as the gap between ffmpeg's own two transforms on the same streams. Across the two corpora,
1,883 and 1,642 macroblock addresses respectively are gaps the decoder never visits, matched exactly
against ffmpeg's own reference handling, and thousands more are motion-compensated with and without a
coded residual — real content that exercises the address-difference and skip machinery thoroughly.

What real content does not reach: ffmpeg's own H.261 encoder was measured never to emit the loop
filter, a quantiser change in the middle of a group, or the bit-stuffing codeword, so those three are
verified instead by hand-built streams under `Tests/Hawkynt.FileFormats.Video.Tests/Codecs/H261`,
worked out from the Recommendation's own arithmetic rather than recorded from a run.

What is not implemented refuses and says so, naming the clause: the still image transmission of Annex
D, which reassembles four ordinary pictures into one at four times the resolution and is signalled by a
bit this decoder reads and rejects rather than silently ignoring; a picture size that changes mid-stream
while a picture predicted from the old size is still held as the reference; and, as in every codec here,
there is no `catch` anywhere that hands back a blank, a copied or a zero-filled picture.

### id RoQ

The FMV format Graeme Devine wrote for The 11th Hour, carried into Quake III and Return to Castle
Wolfenstein's engine and named, according to Devine's own development diary, after his newborn
daughter Roqee. Like FLIC it is a container with nothing beneath it: a file is a flat run of
self-delimiting chunks — id, a length that is the chunk's own payload, an argument, and the payload —
and `RoqContainer` answers only where each chunk begins and ends and which of picture, sound or
housekeeping it is; `RoqVideoDecoder` is the only thing here that reads a codebook entry or a motion
byte. A picture is not one chunk the way a Cinepak frame is: `RoQ_INFO` states the picture size once,
wherever in the file it happens to sit rather than at a fixed offset, and only `RoQ_QUAD_VQ` ever
produces one.

The coding is vector quantisation with motion compensation over a quadtree: a picture is 16-pixel
macroblocks, each four 8x8 quadrants, each either skipped, motion-compensated from the picture before,
painted from one 4x4 codebook cell doubled to fill it, or subdivided into four 4x4 blocks that repeat
the same choice one level down — where a 4x4 cell is now used at its own size rather than doubled, and
subdividing again is the walk's one terminal case, four raw 2x2 cell indices with no code of their own.
A codebook chunk restates both tables outright whenever either changes; long runs of frames between
restatements carry none at all.

Two things about it are not written down anywhere published and were recovered by measurement.

**Skipping reaches back two pictures, not one.** Every description of RoQ agrees a skipped block costs
no argument byte and "leaves the block unchanged" — both true, and neither says unchanged *from what*.
Reading it as "the picture immediately before" reproduces two real files' first two pictures exactly
and then drifts, worse wherever a chunk states a nonzero mean motion vector, healing only when a later
picture happens to recode the same area from a fresh codebook cell. Bisecting one wrong block against
ffmpeg's own decode of the picture before it — the technique that settles a token tree gone one bit
sideways — found the true source of those sixteen samples sitting two pictures back, not one: a RoQ
encoder keeps two picture buffers and alternates which one it is currently building, and a skipped block
is a block the encoder wrote nothing for, so the decoder has to write nothing for it either and let the
buffer's own two-pictures-stale content show through. Motion compensation and every codebook paint
write into the buffer being built and read the *other* one, the most recently completed picture — the
ordinary reference every other block type here uses; skipping is the one code that reaches further back
because it is the one code that writes nothing at all. The first picture has no second buffer to have
been building into two pictures ago, so its result is copied into both buffer slots once it is painted,
the same way a freshly built decoder's canvas is already what a skip opcode states for the block-vector
codecs elsewhere in this package.

**Chroma ends up at full resolution, not the half its codebook cells state.** A 2x2 codebook cell holds
one Cb and one Cr for its whole area — 4:2:0 on paper — but motion compensation moves whatever a block
already holds, chroma included, at the same pixel precision as luma, and a picture a few frames past its
last codebook repaint routinely has chroma that lines up with no 2x2 grid at all. This is not a rounding
approximation on this decoder's part; it is what the format's own history of blocks moving at whole-pixel
precision leaves the picture holding, and it is confirmed independently by ffmpeg's own decoder, whose
native output for RoQ is `yuvj444p` — full-resolution chroma throughout — and not `yuvj420p`.

**Measured.** Three files from `samples.ffmpeg.org/game-formats/idroq/` — 512x256 to 512x512, 210 to
802 pictures, 1,338 in all, one of them (`jk02.roq`, from Jedi Knight II) shipped with the sample's own
accompanying note naming motion compensation with a nonzero mean vector as "the last problem in the
native roq decoder" for chrominance addressing — were decoded here and by ffmpeg and compared sample
for sample against ffmpeg's own `yuvj444p` output, plane by plane rather than through any RGB
conversion: **every plane of every picture in all three files is identical**, ffmpeg's decode included
on the file whose own author flags it as exercising the addressing bug. RGB output is verified too, and
almost everywhere agrees exactly — the exceptions, a few dozen pixels across two of the three files,
were run down to ffmpeg's own `swscale` disagreeing with a plain reading of its own decoded planes
rather than with anything this decoder reconstructed, by reproducing the identical handful of pixels at
the identical positions with no decoder of ours involved at all. This is genuinely lossless-at-decode
territory — the quantisation is the encoder's, and a decoder reading the same bitstream has nothing
left to round — so exact agreement on the planes is the only acceptable result, not a residual within
some accuracy bound the way a DCT-based codec's is.

What is not implemented refuses and says so: `RoQ_JPEG`, the 11th Hour and Clandestiny superset of the
format where a keyframe may be a plain JFIF file in place of a quadtree-coded one — no sample this was
measured against carries one; a picture size that is not a whole number of 16-pixel macroblocks, and
one that changes part way through a stream; a codebook cell named before any codebook chunk has stated
one; and a motion vector reaching outside the picture, which nothing measured this against exercises.
Sound (`RoQ_SOUND_MONO`, `RoQ_SOUND_STEREO`) is demuxed onto its own stream, DPCM-coded and unread past
that — decoding it is future work.

### Flash Screen Video 2

Lossless, and despite the name a genuinely different bitstream from FSV1 rather than a variant of it —
nothing below the four-byte grid header is shared. The grid gains one more flags byte, every block gains
a format byte of its own, a pixel is one byte or two depending on its own top bit, and "unchanged" is no
longer the only inter-frame trick a block can play.

**The hybrid colourspace is a per-pixel choice.** Behind the block format byte's two-bit `ColorDepth`
— 24-bit RGB, or, on every stream measured, the 15/7-bit hybrid — a byte with its high bit clear is a
seven-bit index into a 128-entry palette; a byte with it set is the first half of a fifteen-bit colour,
its own low seven bits over bits 14-8 and the next byte whole over bits 7-0, widened to 24 bits the same
way this package's other 5-5-5 formats already are. That makes a block's decompressed length unknowable
in advance, so it is decompressed whole and then walked pixel by pixel until the grid position's own
pixel count is reached. The 128-entry default table is transcribed from the specification's Appendix C,
"Screen Video v2 Palette" — a stream is free to carry a table of its own instead, a v1-shaped block of
384 bytes, three a colour, decompressed the same way FSV1 already reads one.

**A diff block's two extra header bytes name a run of rows**, not the whole cell: a row and a count,
both counted from the cell's own bottom exactly as every row in this family already is. The decompressed
pixel count follows the count and not the cell's height.

**"Priming" is a DEFLATE preset dictionary keyed to the container's own key frames, and nothing about
that sentence was obvious going in.** The first reading tried was this package's own ZMBV decoder's
trick — one zlib stream held open across packets — and it is wrong: a block that does not claim to be
primed decompresses alone as a complete, checksummed zlib stream, which a stream ZMBV-style continuation
would have to *not* be. Feeding a primed block's raw bytes to an ordinary DEFLATE decoder with no history
at all fails outright with a match reaching before the start of the data — the exact diagnostic a genuine
preset dictionary produces and nothing else does. What the dictionary actually is took longer: not the
previous frame's content for that cell, and not the immediately preceding block's own decode, but the
exact byte sequence — in this format's coded form, one or two bytes a pixel, not the colour it means —
that cell held the last time the *container* stated a key frame. Two consecutive full, unprimed blocks
sent on ordinary interframes decode correctly on their own, each checked against ffmpeg's decoded
picture, but the block after them primes against neither; it primes against the key frame twelve frames
earlier, found by testing every candidate dictionary a plausible reading suggested until the decompressed
byte count exactly consumed the compressed data with nothing left over and every resulting pixel matched
ffmpeg's. Since neither .NET's zlib wrapper nor its `DeflateStream` exposes a preset dictionary, decoding
this needed RFC 1951 read directly rather than asked of either — Huffman decoding, LZ77, and a sliding
window seeded from the dictionary before the first bit of the compressed data is read, checked
independently of anything Screen Video v2 needs by running it with an empty dictionary over an ordinary
zlib payload's own raw DEFLATE bytes and comparing byte for byte against `ZLibStream`'s decode of the
same input before it was trusted for the case `ZLibStream` cannot do at all.

**Every block composes onto that reference, not onto the frame before it.** Before a block's own rows are
written, the whole cell is repainted from the reference the last key frame established; only afterwards
does the block's own decoded rows go on top. A block whose row count is zero and carries no data at all
is not empty — it still repaints the cell from the reference, which is how three bytes put a cell that
drifted across several interframes back where the last key frame left it.

`ZlibPrimeCompressCurrent` — priming against a *different* cell's data, named by an explicit position the
header would carry in that case — never appears in anything measured and refuses by name, as does a grid
header setting `HasIFrameImage`, whose second list of blocks the specification describes only as
interblocks "that must be combined with the previous keyblocks", without saying how.

**Measured against ffmpeg**, built with its own flashsv2 encoder — five streams, 122 frames, sizes that
are and are not a whole number of 64x64 blocks in either direction, multiple key frames a stream, and
interframes mixing fresh, primed, whole-cell and partial-row blocks in every combination the encoder
produced. **Every sample of every frame is identical**, RGB-native so the comparison is a direct one and
not a plane-by-plane approximation of anything subsampled.

What refuses: everything FSV1 already refuses, at the same points; `HasIFrameImage`; a block format byte
naming a colour depth the specification does not define, or setting `ZlibPrimeCompressCurrent`; a diff
block whose row range reaches outside its own cell; a key frame block that does not cover its whole cell,
since nothing measured exercises what a partial reference would mean; a primed block whose cell has no
reference to prime against; and a decompressed pixel stream that runs out before the pixel count a
block's position in the grid calls for.

### ZeroCodec

Lossless, and built entirely out of zlib in the plainest way this package has seen: a packet is one
complete, independently checksummed zlib stream, decompressing to exactly one picture's worth of bytes,
and the whole of the coding is what a decompressed byte of zero means — the byte already held at that
position is unchanged — against anything else, which is the literal new byte.

**No specification exists.** The community write-up on MultimediaWiki states only that the codec performs
"difference processing" and reads and writes RGB, YUY2 and UYVY; it names no frame layout, no byte order
and no rule for what the difference actually is. Everything below was established here by decompressing
real packets and comparing the result against ffmpeg's own decode of the same file — samples.ffmpeg.org
carries exactly one ZeroCodec recording and ffmpeg has no encoder for it, so there is no corpus to build,
only the one sample to read.

**The delta rule needs no notion of a key frame, and none is read.** Every packet — the container's own
much larger, full-picture ones included — decompresses to the picture's full byte count and is merged
into the picture already held by the very same rule. The first packet a decoder ever sees comes out
identical to a literal copy under it, because the picture "already held" before anything has arrived is
an all-zero buffer, and a decompressed zero at a position nothing has written to yet leaves a zero exactly
where a literal reading would have put one; a packet partway through the stream that happens to carry a
picture unrelated to the one before it decompresses to bytes that are almost all nonzero and so is carried
by the same rule without anything having to say which packets those are. Measured directly: applying this
one rule, with no container flag consulted anywhere, reproduces every frame of the sample file byte for
byte, including three packets an order of magnitude larger than the ones around them. One consequence
follows from the rule itself rather than from anything chosen here: a sample whose true new value is
exactly zero where the previous value was not can only be written by this scheme in the one case that does
not matter, where the previous value already reads as unchanged — nothing in the format works around that,
and nothing in the one sample measured here needed it to.

The picture is coded bottom row first, the Windows DIB convention this package's other AVI codecs already
carry, found by decompressing the first packet — exactly the stream's picture size — and finding it a
mirror image of ffmpeg's own first frame until the rows are reversed. The one pixel layout measured is
sixteen bits a pixel, packed 4:2:2 with the byte order U, Y, V, Y, matching ffmpeg's own report of
`uyvy422` for the sample and the only `biBitCount` any file reaching this decoder has stated; the
community page's other two forms, full RGB and the reverse YUY2 packing, have no sample here to measure a
byte layout against and are refused rather than guessed at.

**Measured against ffmpeg** on the packed samples themselves and not through an RGB conversion — this is
a 4:2:2 format, so the same chroma-siting ambiguity this package's other subsampled codecs are compared
plane by plane to avoid applies here too. One file, 38 frames, 1280x720: every one of the 70,041,600 bytes
ffmpeg's own decode produces (`ffmpeg -threads 1 -i sample-zeco.avi -fps_mode passthrough -f rawvideo
-pix_fmt uyvy422`) is reproduced exactly, frame by frame, with no drift across the run. The RGB picture
this package hands back — `RawImage` has no packed 4:2:2 format of its own — converts with BT.601
coefficients, assumed rather than measured since there is nothing in the one sample available to read a
colour-space choice off, and repeats each chroma pair across both of its luma samples rather than
interpolating, a display convenience the measurement above does not depend on.

What refuses: a picture whose width is odd, since two luma samples share one chroma pair and an odd width
leaves the last sample with none; a depth other than sixteen bits, for want of a second sample to measure
any other packing against; and a packet whose zlib stream is truncated, corrupt, or does not inflate to
exactly the picture's own byte count.

### Interplay Video

Interplay's own FMV codec, behind Baldur's Gate and the rest of their DOS-and-Windows-era catalogue,
carried in the same kind of self-contained container as RoQ: a twenty-byte signature and then a flat
run of chunks, each wrapping a stream of opcodes rather than one packet per picture. `MveContainer`
answers where each opcode is and which stream it belongs to; `MveVideoDecoder` is the only thing here
that reads an 8x8 block encoding. A picture needs three opcodes read together — `INIT_VIDEO_BUFFERS`
for the size, `DECODING_MAP` for which of sixteen encodings each block uses, and only `VIDEO_DATA`
reads that map and produces one — the same seam RoQ's `INFO`/`QUAD_CODEBOOK`/`QUAD_VQ` opcodes use.

The coding is a fixed 8x8 grid, each block one of sixteen encodings: a plain copy, a true no-op, four
kinds of motion compensation, a two-colour or four-colour bit-packed pattern at several block-splitting
granularities, three flavours of raw pixels, a solid fill, and a checkerboard dither. Interplay's own
published description — Mike Melanson's `interplay-mve.txt`, which ffmpeg's own decoder is written
from and credits by name — is thorough by the standard of this family of formats, and two things in it
were measured against real files and found wrong.

**Every `VIDEO_DATA` opcode opens with a fourteen-byte header nothing published mentions.** It was
found by noticing that bytes 8–11 of the payload restate the picture's own size in macroblocks — the
same figures `INIT_VIDEO_BUFFERS` already gives — and confirmed because a decode starting at the wrong
(zero) offset put every block needing more than a plain copy fifty per cent wrong and every raw or
solid block correct, which is exactly the shape a constant offset error draws: the opcodes that read
their own colour values regardless of position land anywhere, and the opcodes that read a byte meant
to be a motion vector or a pattern's first colour instead read whatever the header happened to hold.

**Every bit-packed pattern reads low bit first, not high bit first.** The description states a rule
for one case — the plain eight-byte two-colour block, "the rightmost pixel is represented by the
low-order bit" — and that statement does not hold for any pattern-coded block measured, this one
included: reading high bit first there reproduces no sample from either file, and low bit first
reproduces both completely, checkerboard splits and the two- and four-colour quadrant and half-block
patterns alike.

**A skip reaches back two pictures, not one — the same finding as RoQ's, this time from a description
that states it outright rather than leaving it to be found.** Interplay's own text says a skipped block
(encoding `0x1`) "has the same value it had 2 frames ago", which only makes sense built on exactly two
alternating picture buffers: every other encoding writes into the buffer being built and reads the
*other* one — the most recently completed picture — while a skip writes nothing at all, so whichever
content that same buffer slot held the last time *it* was written, two pictures back, shows through.
Encoding `0x0`, a plain copy naming no offset, is the ordinary one-picture-back reference by contrast,
and the two are easy to conflate from the description alone since both read as "unchanged". The first
picture has no second buffer to have been built into two pictures ago, so its result is copied into
both buffer slots once it is painted — the same bootstrap RoQ's decoder needs and for the identical
reason.

**Measured.** Two files from `samples.ffmpeg.org/game-formats/interplay-mve/` — 432x320 and 640x272,
225 and 330 pictures, 555 in all, covering every block encoding this reads and confirming encoding
`0x6` (which the format's own description doubts) never appears — were decoded here and by ffmpeg and
compared sample for sample against ffmpeg's own `pal8` output, index and installed palette both: every
picture is identical. This is paletted throughout, so the comparison is a direct one on samples with no
RGB conversion and no chroma-siting convention to get wrong — worth stating plainly here because for
several codecs elsewhere in this package that comparison would not mean what it appears to.

Palette entries are six-bit VGA precision, widened to eight bits by repeating the top two bits into the
bottom rather than shifting — the same rule this project's other six-bit channels use, and the one that
reproduces ffmpeg's installed palette exactly where a plain multiply by four does not.

What is not implemented refuses and says so: a true-colour video buffer, which the format's own
sixteen-bit block encodings are documented as differing in ways not fully stated and which no sample
here carries; block encoding `0x6`, which the format's own description doubts its own reading of and
which no sample states; a compressed palette opcode; and a picture size that changes part way through
a stream.

### Lossless Codec Library (ZLIB)

Lossless, and the simplest coding this family has: a picture converted to a target colour space and
handed straight to zlib's DEFLATE, with the compressor reset fresh for every frame, so a packet decodes
on its own with nothing carried from the one before it. Built from a real specification, unusually for
this family — "Description of the LCL codecs (MSZH and ZLIB)" by Roberto Togni, published as
`multimedia.cx/lcl.txt` under the GNU FDL — though its own author calls it "random notes... while
building a decoder" and leaves several fields as unfilled placeholders. Codec identity, the eight-byte
trailer LCL appends to a standard `BITMAPINFOHEADER`, and the zlib compression itself are exactly as
that document states. Only the RGB24 colour space is read: every YUV form the format defines has its
byte order left as one of the document's unfilled placeholders, and neither ffmpeg's encoder nor any of
seven real recordings from samples.ffmpeg.org write anything else.

**A coded row is sometimes a whole four-byte word, and which is a property of the file rather than of
the format.** One real recording, 1246 pixels wide — the one width among any sample here not already a
multiple of four — decompresses two bytes longer a row than the picture packs to, confirmed against the
file's own `biSizeImage`. A stream built here with ffmpeg's own encoder at an equally unaligned width
decompresses to exactly the packed byte count instead, and ffmpeg's own decoder logs a size mismatch
against the padded figure it expects and proceeds regardless. The two encoders disagree about whether
the padding the document never mentions is written, so nothing here assumes either answer: the row
stride is read off however many bytes the zlib stream actually produced, taken as whichever of the
packed or the padded byte count that total equals.

The picture is stored bottom row first, matching every AVI codec in this package.

**Measured against ffmpeg** two ways at once, because ZLIB is one of the few codecs in this family with
a real encoder. Round-tripped through it — four streams built and encoded here, 2x2 to 322x240,
including widths that leave a row unaligned and ones that do not — every decoded frame is identical to
the source frame that was encoded, the stronger of the two comparisons this package usually has to
choose between since it is the ground truth itself rather than a second decoder's opinion. And measured
against seven real files from samples.ffmpeg.org, 282 frames from 64x48 to 1246x992 — every sample of
every frame is identical across all 307 frames measured either way, RGB-native so the comparison is a
direct one and not a plane-by-plane approximation of anything subsampled.

What refuses: an image type other than RGB24, for want of anything to measure a YUV byte layout
against; the multithread flag, whose split's own length and offset fields the specification never
states; the PNG filter flag, whose per-colour-space structure is another of the specification's unfilled
placeholders and whose own author states his RGB24 implementation of it does not work correctly; and a
packet whose zlib stream is truncated, corrupt, or inflates to neither the picture's packed nor its
padded byte count.

### Vidvox Hap

Hap frames are meant to be handed to a graphics card almost unchanged: a `Hap1` or `Hap5` frame's
payload, once its second-stage compressor is undone, is exactly the DXT1 or DXT5 texture a GPU would
be loaded with, block for block. That is what sets this codec apart from almost every other one in
this package — a block's decoded pixels are not an approximation converging on a source picture and
not the output of a transform with a stated accuracy bound; they are the one and only picture that
block's bits mean, defined completely by S3TC (DXT1/BC1, DXT5/BC3) and, for the "Q" pixel format, by
van Waveren and Castaño's Scaled YCoCg-DXT5 reconstruction. Decoding is held to this package's
lossless bar — max delta 0 on every sample of every frame — even though the picture an encoder started
from was compressed to get there.

The frame layout is published in full, in the Hap project's own repository on GitHub
(`documentation/HapVideoDRAFT.md`): a run of sections, each a type byte and a size in a header that is
four bytes or grows to eight when the size does not fit in three. A top-level section's type names a
pixel format and how its data reaches it — as-is, through one Snappy block (Google's own
`format_description.txt`, which Hap names as an external reference rather than restating), or, for the
"consult decode instructions" forms, cut into chunks that are each decompressed independently, with
their own second-stage compressors, sizes and — optionally — explicit byte offsets. One type byte,
`0x0D`, names no pixel format at all and instead holds one or two further top-level sections whose
textures are combined into the final picture, the only combination the format defines being Scaled
YCoCg DXT5 with a separate RGTC1/BC4 alpha image — what the `HapM` code names.

**The Scaled YCoCg pixel format is a DXT5 block read for different meaning.** The eight-sample alpha
channel, reproduced at full precision rather than through a three-bit index into four values, carries
luma; the DXT1-style colour part carries the two chroma channels signed around 128 in its red and
green samples and a per-block scale factor in blue, which widens them back out before the 5- and
6-bit quantisation that crushed them. The reconstruction — `scale = blue/8 + 1`, `Co`/`Cg` divided by
that scale, `R = Y + Co - Cg`, `G = Y + Cg`, `B = Y - Co - Cg` — is carried over unchanged from the
[0,1] texture-space pseudocode van Waveren and Castaño give in "Real-Time YCoCg-DXT Compression" (id
Software / NVIDIA, September 2007), the paper the Hap specification itself names as this pixel
format's definition; every term of that pseudocode is an 8-bit sample divided by 255, and 255 is a
common factor of every term on both sides.

**Two things about the DXT1/DXT5 block decode measured differently from what this package's own
DDS and KTX block decoders do**, and Hap keeps its own decode of them rather than reuse that code.
The interpolated third and fourth colours of a four-colour block, and the six interpolated steps of an
alpha ramp, are a plain integer division with no rounding term — `(2*color0+color1)/3`,
`(6*alpha0+alpha1)/7` — which is what the OpenGL S3TC and RGTC extension texts themselves give,
literally, where the shared decoders round both to the nearest whole value; measured against the
corpus below, the rounded reading disagreed at a maximum delta of 2 and the literal one came out
bit-exact. And the 5-bit and 6-bit colour-endpoint expansion — what S3TC states only as "unpacked ...
as though a 16-bit packed pixel with a type of `UNSIGNED_SHORT_5_6_5`", no arithmetic given — is
**not** bit replication (the exact linear scaling the shared decoders use, and wrong for four of the
thirty-two five-bit values) and not a single rounding or truncating division by 31, at any constant
added before the divide from 0 to 30: every one of those still misses at least one value. What this
decoder uses instead was read directly off ffmpeg's own decode: every one of the thirty-two five-bit
and sixty-four six-bit values, at a colour index the extension text defines as an endpoint outright —
`code(x,y)` 0 or 1, `RGB0` or `RGB1`, no interpolation involved — appearing hundreds to hundreds of
thousands of times across the corpus below and never once disagreeing with another occurrence of the
same input.

**Measured, and lossless with respect to its own coded blocks**, which is the standard stated above:
max delta 0, not a bound on how close. ffmpeg's Hap encoder writes exactly the three pixel formats
named `Hap1`, `Hap5` and `HapY` in the table row above, so the corpus was built here rather than
fetched from samples.ffmpeg.org: six streams, 64x64 to 96x64, one to eight chunks, both second-stage
compressors, one hundred frames each. Decoded here and by ffmpeg (`-threads 1 -fps_mode passthrough`,
frame count cross-checked against `ffprobe -count_frames`) and compared **on raw RGB or RGBA planes,
never through a format that composites alpha** — Hap is RGB/RGBA-native, carrying no chroma
subsampling of any kind, so a direct plane comparison is the correct one and not merely a convenient
one, which is worth stating explicitly because that is what makes the number mean something: one
codec elsewhere in this package once read a maximum delta of 179 when its alpha-bearing frames were
compared through a format with no alpha channel, and 0 once the comparison was moved to its raw
planes. Six hundred frames, every sample of every plane, identical. Between the six streams: every
top-level pixel format ffmpeg writes; both of DXT1's colour branches, wherever the encoder happened to
choose one; a whole-frame Snappy block and an uncompressed one; and, at eight chunks, ffmpeg's encoder
chose the "consult decode instructions" form on its own, exercising the chunked path end to end rather
than only by construction. ffmpeg's own top-level section headers are all eight bytes regardless of
size, so the four-byte form and an explicit chunk offset table are reached only by hand-built frames in
this codec's own tests.

**Hap R (BC7) and Hap HDR (BC6U/BC6S) refuse by name**, at `Create`, before a single frame is read —
not for want of a block decoder, since this package already has one for each, reused by the image
formats that carry them, but because BC6 is half-float HDR data and this package's `RawImage` has no
floating-point pixel format to receive it in, and because ffmpeg's own Hap encoder — what this decoder
is measured against — writes none of the four HDR or BC7 variants, so there would be no oracle to
check a decode against even if one were written.

What else refuses, by name: a section whose header does not fit, a size that runs past the data
holding it, a top-level type byte naming no pixel format and no multiple-image marker, a "consult
decode instructions" section missing its compressor table or its size table, a chunk naming a
compressor that is neither uncompressed nor Snappy, a Snappy block whose elements do not produce the
length its own preamble states, a Snappy back-reference pointing before the start of the output, and a
multiple-image section holding any combination other than Scaled YCoCg DXT5 with RGTC1/BC4 alpha.
There is no `catch` anywhere in this decoder that hands back a blank frame or repeats the one before
it.

### id Cinematic Video

Quake II's cutscene codec, `.cin`, a third self-contained container in the same family as RoQ and
Interplay MVE — a twenty-byte header and then a flat run of frame commands, video and (where the header
states a sample rate at all) raw PCM audio alternating one for one. Header layout, the Huffman table
size and the per-frame command values are all stated in Tim Ferguson's format description, mirrored at
`multimedia.cx/mirror/idcin.html` and the page MultimediaWiki's own CIN entry points to. Unlike either
sibling container, the format carries no signature of any kind: `IdcinContainer.MatchesSignature` runs a
plausibility heuristic of its own instead, checking the header states a plausible picture size and,
where it states audio, a plausible sample width and channel count. `IdcinReader` answers where each
frame command is and which stream it belongs to; `IdcinVideoDecoder` is the only thing here that reads a
Huffman code.

Two of the frame layout's own fields are named by Ferguson's page without their arithmetic being
stated: "Huffman count" and "Decode count", four bytes each, right before the coded picture. **Whether a
picture's own bytes number "Huffman count" or "Huffman count minus four" was settled by measurement**,
not read anywhere: the former runs both real files out of data after two pictures each, and the latter —
treating "Huffman count" as covering "Decode count" and the picture together — is the one reading, among
five combinations of bit order and tie-breaking tried against this question and the two below, that
reaches every picture of both real files, forty-eight and eighty-two. "Decode count" itself is never
read back: on every picture of both real files it equals width times height exactly, which this decoder
already knows before a picture is reached.

The coding itself is unrelated to either sibling: an order-1 static Huffman code straight over the
already-paletted index buffer, with no motion compensation and no block structure of any kind. The
header's own sixty-four kilobytes are 256 histograms of 256 byte counts, one histogram per value the
previous pixel might hold; a decoder builds 256 canonical Huffman trees from them once, using the
standard construction — repeatedly pair the two lowest-count nodes not yet paired — and then walks one
for every pixel, switching to the tree named by whichever pixel it just produced. Ferguson's page states
that a dictionary is built from the histogram and explicitly leaves the construction itself to "look
elsewhere for a more in depth discussion on Huffman coding" — general knowledge of the algorithm, not a
fact this format states — so **which of several equal-count nodes is paired first was also settled by
measurement**: breaking a tie toward the lowest index is the only rule, among every combination tried,
that reaches every picture of both real files: breaking toward the highest index fails a first picture
outright or manages two before running out of bits it should not have. **Bits are read least significant
bit first**, the same way: reading most significant bit first fails a first picture on both real files,
where least significant bit first reaches every picture of both.

**A histogram with at most one nonzero count builds no internal node at all, and decodes to node 255
regardless of which symbol (if any) actually held that count.** Nothing pairs with nothing, so the
construction's own loop stops before writing anything above the 256 leaves, and the sentinel value the
construction is left holding happens to be the top of that leaf range rather than the symbol that was
actually starved of company. This is not a separate fact to confirm — it falls straight out of the
construction above once that construction is fixed by measurement — and a context this starved of data
cannot arise from a real picture without every other byte in it being outside this tree's alphabet too.

Palette entries are six-bit VGA precision, widened the same way this project's other six-bit channels
are — by repeating the top two bits into the bottom — unless any of the 768 bytes in a given palette
command exceeds 63, in which case none of it is touched: some of the tools that built these files wrote
full eight-bit RGB instead, and nothing in a palette command states which convention a given file uses.
A frame command that states no palette at all means exactly "the previous one still applies", not "there
is none" — the same carry-forward RoQ's own codebooks need across a skip.

**Measured.** Two files from `samples.ffmpeg.org/game-formats/idcin/` — 320x200 and 320x240, 48 and 82
pictures, 130 in all — were decoded here and by ffmpeg and compared sample for sample against ffmpeg's
own `rgb24` output, index looked up through the installed palette both ways: every picture is identical,
maximum delta nought. This is paletted throughout, so the comparison is direct with no chroma-siting
convention to get wrong. `quake.cin` runs out of file mid-picture with no end-of-file command anywhere
in it; ffmpeg's own decode stops at the same forty-eighth picture this reader does, which is what
"measured against ffmpeg" means for a file that is not, itself, complete. A frame command or a chunk
that does not fully fit in what remains of the file is therefore read as far as it goes and no further,
not refused outright. The audio chunk size is Ferguson's page's own formula — sample width times channel
count times sample rate divided by fourteen — applied with no remainder redistribution of any kind:
neither real file exercises a sample rate that does not divide fourteen evenly, so nothing beyond the
documented formula is claimed.

### Westwood VQA Video

The FMV codec behind Command & Conquer, Red Alert and most of Westwood's DOS-and-Windows-era catalogue,
carried in `.vqa`'s own RIFF-style container — a `FORM` chunk naming its type `WVQA`, then a flat run of
four-character-ID-and-size chunks, the size big-endian where ordinary RIFF's own chunks are
little-endian. Published in full in Gordan Ugarkovic's VQA format description, mirrored at
`multimedia.cx/vqa_overview.htm`. `VqaReader` answers where each chunk is and which stream it belongs
to; `VqaVideoDecoder` is the only thing here that reads a codebook entry or an index byte.
**`FORM`'s own stated size is not trustworthy** — measured directly: one real file's covers only its
header chunks and the real file runs on for megabytes past it — so this reader walks chunks by their
own sizes to the end of the file rather than to where `FORM` says it ends.

The coding is vector quantisation over an 8-bit palettised picture: a codebook of small pixel blocks —
four by two pixels in every sample this was measured against — and, for every block of a picture, an
index table naming either a codebook entry to copy or a single colour to fill the block with outright.
Any of a picture's codebook, palette or index-table chunks may be compressed with Westwood's own
run-length scheme, "format80", published in the same document down to the bit pattern of each of its
five commands — see `VqaFormat80` for the construction, and its own remarks for the one real distinction
its tests draw out: a short back-reference may overlap what it is still writing, which is what lets two
bytes encode a run of one repeated byte.

**A codebook is rationed across eight pictures, not delivered whole.** The first picture's codebook
chunk is complete; every eighth picture after that is preceded by seven more, each carrying an eighth of
the *next* codebook, which only becomes a real codebook once all eight pieces are concatenated in
picture order and decompressed together — the format's own description states plainly that decompressing
one piece alone is not the same data. **That assembled codebook becomes current starting with the
picture after the one whose eighth piece completed it, not the picture that delivered that final
piece** — measured directly against a real 85-picture file: applying it to the delivering picture too
reads every eighth picture wrong, and holding it back one picture reads every one of the eighty-five
correctly.

**An index table is two byte-arrays end to end, not one array of pairs.** For a block at column `bx`,
row `by` in block units, the format's own description gives `topVal = table[by*blocksWide+bx]` and
`lowVal = table[blocksWide*blocksHigh + by*blocksWide+bx]` — a value from the first half of the table
and the corresponding one from the second, not two neighbouring bytes. `lowVal == 0x0f` means "fill this
block with colour `topVal`" outright; any other `lowVal` means "copy codebook entry `lowVal*256+topVal`".

Palette entries are six-bit VGA precision, widened the same way this project's other six-bit channels
are — by repeating the top two bits into the bottom. **A palette chunk is not always the full 768
bytes.** All four files from the original Command & Conquer demo carry a 753-byte palette chunk — 251
colours, not 256 — and nothing past what a chunk actually states is touched; whatever a colour already
held (black, on the first picture) stands for any index a chunk leaves unnamed. Assuming the full 768
bytes always arrive is what an early build of this decoder did, and it read as a bare, unnamed range
exception on exactly those four files — the one thing this project's own standard does not allow a
decoder to do, refusal or not.

**Measured.** Six files from `samples.ffmpeg.org/game-formats/vqa/` — two from the Red Alert set and
all four of the original Command & Conquer demo set, 320x156 and 320x200, 2,046 pictures in all — were
decoded here and by ffmpeg and compared sample for sample against ffmpeg's own `rgb24` output: every
picture of all six files is identical. Three of the four demo files run past a point where ffmpeg's own
demuxer logs a chunk-size or corruption warning near the very end; every picture either decoder actually
produces past that point still agrees with the other exactly, which is what "measured against ffmpeg"
means for a file whose last few bytes are not itself clean. This is paletted throughout, so the
comparison is direct with no chroma-siting convention to get wrong.

**Only version 2, standard colour, is decoded.** The header states a version — `1`, from the format's
original use in Legend of Kyrandia III, and `2`, the far more common form every sample above uses — and
a flag byte that separately marks a fifteen-bit-colour form. Version 1's own index table does not decode
under the reading above: measured against a real version-1 file, the two-half split every version-2 file
decodes exactly under instead produces implausible, structureless indices, and nothing in the format's
own published description states what version 1 uses in its place. Version 1 and the separate
fifteen-bit-colour form both refuse by name rather than guess.

### Electronic Arts CMV

The block-replacement codec behind NHL 95's own cinematics, over the chunked container the whole
Electronic Arts family shares — see the container prose above. A picture is a plain grid of 4x4 pixel
blocks: an intra picture states every one of them as a raw raster of palette indices, and an inter
picture states each block as a motion vector, a raw replacement, or an escape.

**Motion compensation reaches back either one picture or two, and which is which is stated by the
escape byte rather than by the block's own position.** A block whose motion byte is not `0xFF` copies
from the picture immediately before it. `0xFF` reads a second byte: if that byte is itself not `0xFF`,
the block copies from the picture *before that one* — "the second-last decoded frame," in the format's
own words — and only a doubled `0xFF` means the sixteen bytes that follow are raw pixels rather than
another motion byte. Reaching two pictures back needs a decoder that keeps the two most recently
completed pictures apart rather than one held frame and a copy, the same shape this package's RoQ and
Interplay Video decoders already need for their own skip opcodes — except CMV's second reference is
never a no-op the way a skip is: every block, all three ways, writes real pixels. The first intra
picture has no second-last frame to have completed before it, so its result is copied into both
reference slots once it is painted, the same bootstrap RoQ's and MVE's decoders use.

**The palette is plain eight-bit RGB, not the six-bit VGA precision or the component order the format's
own published description states.** The header's own text gives the three palette bytes as red, blue,
green; reading them that way, at either full precision or widened as this package's other six-bit
channels are, disagrees with tens of thousands of ffmpeg's own decoded samples. Reading them as red,
green, blue at full eight-bit precision — the plain reading the text does not give — reproduces the
intra picture exactly, all 40 000 samples of it.

**Measured.** The one sample known to exist for this codec, `TITLE.CMV` from
`samples.ffmpeg.org/game-formats/ea-cmv/` — 200x200, 194 pictures across the two runs the container
section above describes — was decoded here and by ffmpeg and compared sample for sample against
ffmpeg's own `rgb24` output: every picture is identical, including every one past the mid-file palette
restatement. This is paletted throughout, so a direct sample comparison — no RGB conversion beyond
looking a decoded index up in the picture's own palette, no chroma-siting convention — is exactly what
settles it.

What is not implemented refuses and says so: a picture whose size is not a whole number of 4-pixel
blocks in either direction, and a picture size that changes part way through a stream; nothing this was
measured against carries either. A motion vector reaching outside the picture reads as zero, as the
format's own description states, though no block in the one file this was measured against ever names
one.

### 8BPS

Apple's own Planar RGB, QuickTime's lossless codec for capturing true-colour frames whole — red,
green and blue as three complete planes, and a fourth of alpha where the picture carries one, each
run-length coded a line at a time rather than block by block. Read from "Description of the Planar
RGB (8BPS) Codec" by Roberto Togni, v1.0, October 2003, published at `multimedia.cx/8bps.txt` under
the GNU Free Documentation Licence and mirrored on MultimediaWiki's own `8BPS` page — a standalone
technical write-up citing XAnim as its own source, predating rather than following the codec's
inclusion in any tool this project treats as an oracle, and not a paraphrase of anybody's own
decoder.

A frame is two sections: for every plane in turn, one 16-bit big-endian compressed length a row, top
row first; then, in the same order, the compressed rows themselves. Which colourspace decides plane
count — one plane of palette indices at eight bits, three planes of red, green and blue at
twenty-four, four with alpha at thirty-two — and nothing about the line coding differs between them.

Line decompression is PackBits, and the one place the document's own prose does not match a real
file is the literal run's length. It states the control byte itself is the count; every file measured
disagrees. Decoding a real frame both ways and comparing against ffmpeg's own decode of the same file
settles it: reading the control byte as the count leaves the row short and every pixel after it
wrong, where reading it as **control plus one** — ordinary PackBits — reproduces the row exactly and
every row after it for the rest of the file.

The indexed depth's colour table sits exactly where QuickTime Animation's own indexed depths keep
one, in the sample description behind the depth field, and a table identifier of zero — the value
every real file measured here carries — states that a custom table follows rather than naming a
system colour resource. Each entry gives red, green and blue at sixteen bits; only the high byte of
each survives, which is not documented anywhere but is what a real file's embedded table and ffmpeg's
own decoded palette agree on entry for entry across all 256 of them.

No inter-frame coding is implemented. The format's own document is unsure whether a row shorter than
the picture — leaving the rest as whatever the previous frame drew — was ever used by a real encoder,
since none of its own samples used it either; none of the three real files measured here do, so a row
that cannot be filled to the picture's width from its own compressed bytes is refused rather than
patched in from a frame this decoder does not keep between packets.

**Measured against ffmpeg's own decode, exactly, on real files** — RGB-native throughout, so a direct
sample comparison is valid and there is no chroma-siting convention to disagree about. Three streams
from `samples.ffmpeg.org/V-codecs/8BPS-PlanarRGB/`, one at each depth this codec reads: 34 frames of
160x120 at twenty-four bits, 150 frames of 320x213 at thirty-two bits with a real alpha channel, and
169 frames of 360x240 at eight bits through an embedded colour table — 353 frames in all, every plane
of every one identical to ffmpeg's decode of the same file, alpha and palette entries included.

What refuses, by name: a depth that is none of eight, twenty-four or thirty-two; an indexed picture
whose colour table identifier is not the custom-table value every sample measured here uses, or whose
table names an index outside its own stated size; a packet too short for the line-length tables its
plane count and picture height require; a row whose control bytes run past the compressed length its
own table entry states, fall short of the picture's width without doing so, or overrun it; and a
plane's worth of rows that does not end exactly where its table entry said it would.

### GoPro CineForm

Written from SMPTE ST 2073-1:2017, the VC-5 elementary bitstream standard — free, fifty-two pages, and
GoPro's own SDK documentation calls VC-5 a "superset" of the original CineForm engine, standardised and
better defined. That description holds up: the tag-value and chunk framing (clause 8), the inverse
wavelet transform (Annex A, normative), the two-hundred-and-sixty-four-entry entropy codebook (Annex C,
transcribed in full and checked entry by entry against the standard's own printed table) and the
dequantisation formula (Annex F) are all stated completely, and nothing here reads GoPro's SDK source
or ffmpeg's `cfhd` decoder — both are used, where at all, only as a black-box oracle on their output.

**What the free standard does not state, and what a real file carries beyond it.** A frame from
ffmpeg's own `cfhd` encoder opens with none of the standard's own start marker and threads its
tag-value header through with dozens of tag numbers Table B.2 does not define — GoPro's own encoder
predates the standard it was later folded into. None of that matters to a decoder, because clause 8.3.1
makes every tag-value pair exactly one segment whatever its tag: an unrecognised tag costs four bytes
and nothing more. What is not skippable — where a channel's lowpass and each of its nine highpass
codeblocks begin, which physical channel carries which colour, and the prescale shifts a real encoder
actually applies — was recovered by building a corpus with that same encoder and measuring. A highpass
row whose stated width is not already a whole multiple of eight is coded padded out to the next one,
the padding always zero, on any channel at any wavelet level, not only the horizontally-subsampled
chroma channels where it happens most often; `yuv422p10le` codes channel 0 as luma, channel 1 as **V**
and channel 2 as **U**, and `gbrp12le`/`gbrap12le` code channel 0 as green, channel 1 as red and
channel 2 as blue, matching neither ffmpeg's own plane order nor a guess. And Annex E.1's own table of
prescale shifts — informative, something an encoder "can benefit from" rather than must state — turns
out to be wrong for the case that matters most: at ten bits it states shifts of (0,0,2) for wavelet
levels 1 through 3, and reconstructing with that leaves the middle level's highpass four times too
large while the other two already agree, discovered by forward-transforming ffmpeg's own decoded
reference through the same three levels and comparing every subband's coefficients against what this
decoder read back. Moving that one shift from level 3 to level 2 — (0,2,0) — is what the real encoder
actually does, and it is what the arithmetic in `CineFormPrescale` states along with the measurement
behind it. Twelve bits needed no such correction: Annex E.1 states the same shift at both of those
levels, so nothing distinguishes them either way.

**Scope.** ffmpeg's own `cfhd` encoder writes exactly three pixel formats — `yuv422p10le`, `gbrp12le`
and `gbrap12le` — and this decoder reads the two of them without alpha: ten-bit 4:2:2 and twelve-bit
RGB, three channels each. A frame stating any other channel count, the alpha-bearing `gbrap12le`
layout included, is refused by name: alpha's channel position was never measured against a real file,
and guessing at it risks exactly the wrong-picture-that-looks-right failure this library refuses to
ship. Intra only, like every other codec of this shape here — no reference handling, no state carried
between packets beyond the stream's declared dimensions.

**A reconstructed sample is clamped to the depth it is coded at, and the free standard never says so.**
The wavelet transform's ordinary overshoot near a hard edge — the same ringing every linear transform
codec has — puts a reconstructed channel sample a few levels below zero or above the coded maximum now
and again; nothing in Annex A forbids it, and nothing states a decoder must undo it. ffmpeg's own
decode cannot even show the alternative, because `yuv422p10le` and `gbrp12le` are unsigned formats:
whatever it reconstructs internally is clamped before it can be written out at all. Left unclamped,
that overshoot survives as a plausible small difference on the channels themselves — a handful of
levels, indistinguishable from ordinary quantisation noise — but explodes once it is narrowed to eight
bits: `ChannelScaling.Reduce16` narrows a value that fills its declared range, and a `byte` cast on one
that does not wraps rather than saturates. A sample of −15 shifts to −240 and reduces to 255 instead of
0; a sample of 4108 reduces to 0 instead of 255 — the coded picture's own edges turning into the exact
opposite extreme of the eight-bit range, at whichever pixels the overshoot happens to reach. A 256x192
`gbrp12le` frame at default quality carried twenty-three thousand seven hundred and one such pixels
across twenty frames, eighty of them in its own first row alone, every one wrong by up to the full
scale of the format; a heavier quantiser setting on the same frame carried ten times as many. Clamping
every reconstructed sample to its coded depth, once, at the end of channel reconstruction, removes it
entirely — the same corpus reads zero such pixels afterwards — and is also what makes the "compare the
channels themselves" measurement below mean what it says: an unclamped decode compared against
ffmpeg's own clamped one was reading two different things at exactly the pixels this overshoot reaches,
not two decodes of the same bitstream.

**Measured against ffmpeg, on the channels, before any narrowing or colour conversion** — this library
interpolates chroma where ffmpeg replicates, exactly the reason every other subsampled codec here is
compared on planes and not on packed colour. Thirteen streams built with ffmpeg's own encoder, a
hundred and forty-one frames in all: 4:2:2 at 64x48, 80x64, 96x50, 96x84 and 256x192 — sizes that are
and are not a whole multiple of sixty-four wide, exercising the row padding above on every wavelet
level rather than only the coarsest — a synthetic test pattern, colour bars and a Mandelbrot zoom, at
default and at a heavier quantiser setting; and 12-bit RGB at the same sizes and settings. Every frame
of every stream, sampled with `-threads 1 -fps_mode passthrough` and cross-checked against
`ffprobe -count_frames`: at ten bits the largest difference on any sample of any plane is 9 of 1023,
the mean under one level on every stream; at twelve bits the largest is 49 of 4095, reached only at the
heavier quantiser setting, the mean under three on every stream. CineForm is visually lossless and
mathematically lossy, and that is what these numbers say — a small, flat residual from the codebook's
own quantisation, not a defect that grows or resets with content.

What refuses, by name: a channel count other than three; a highpass codeblock that does not
entropy-decode cleanly to Annex C.2's band end marker at its stated row width or at the next multiple
of eight; and a channel that ends before its lowpass band was ever coded. There is no `catch` here
returning a blank, a copied or a repeated frame.

## 📜 License

LGPL-3.0-or-later.
