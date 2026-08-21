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
| MPEG program stream (MPEG-1, MPEG-2, VOB) | `.mpg`, `.mpeg`, `.vob`, `.m2p`, `.m2ps` | Y | — |
| Motion JPEG stream | `.mjpg`, `.mjpeg` | Y | — |
| MPEG video elementary stream | `.m1v`, `.m2v`, `.mpv`, `.mpeg1video`, `.mpeg2video` | Y | — |
| MPEG-2 transport stream (also Blu-ray, AVCHD) | `.ts`, `.m2ts`, `.mts`, `.m2t`, `.tsv` | Y | — |
| Ogg (Theora, Vorbis, Opus, FLAC) | `.ogg`, `.ogv`, `.oga`, `.ogx`, `.opus`, `.spx` | Y | — |
| RealMedia (RealVideo, RealAudio) | `.rm`, `.rmvb`, `.ra`, `.rmj`, `.rms` | Y | — |
| Autodesk FLIC | `.fli`, `.flc`, `.flx` | Y | — |

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
| H.263 (ITU-T H.263 baseline) | `H263`, `s263`, `U263` | Y | — |
| Sorenson Spark (Flash Video's H.263) | `FLV1` | Y | — |
| RealVideo 1 (revision 0 only) | `RV10`, `RV13` | Y | — |
| H.264 / AVC, Baseline I and P slices | `avc1`, `avc3`, `H264`, `X264`, `DAVC`, `VSSH`, `V_MPEG4/ISO/AVC` | Y | — |
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

| Ut Video | `ULRG`, `ULRA`, `ULY0`, `ULY2`, `ULY4`, `ULH0`, `ULH2`, `ULH4` | Y | — |

One reader for MP4, MOV, M4V and 3GP because they are one format under four names — the same box
structure with different brands in `ftyp`. Its packet boundaries are not in the data at all: `mdat`
is an undivided heap of bytes, and where each packet starts and stops is a computation over five
tables in `stbl`, which is why a file whose `moov` follows its `mdat` needs no second pass. A
fragmented file, whose sample tables live in `moof` boxes instead, is refused by name rather than
read as a film of no packets.

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

## 📜 License

LGPL-3.0-or-later.
