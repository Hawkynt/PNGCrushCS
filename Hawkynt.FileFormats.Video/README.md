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
| AVI (RIFF) | `.avi` | Y | — |
| Flash Video (FLV) | `.flv`, `.f4v` | Y | — |
| ISO base media (MP4, QuickTime, 3GP) | `.mp4`, `.m4v`, `.mov`, `.qt`, `.3gp`, `.3g2`, `.m4a` | Y | — |
| Matroska / WebM (EBML) | `.mkv`, `.mka`, `.mks`, `.mk3d`, `.webm` | Y | — |
| MPEG program stream (MPEG-1, MPEG-2, VOB) | `.mpg`, `.mpeg`, `.vob`, `.m2p`, `.m2ps` | Y | — |
| Motion JPEG stream | `.mjpg`, `.mjpeg` | Y | — |
| MPEG-1 video elementary stream | `.m1v`, `.mpv`, `.mpeg1video` | Y | — |

| Codec | Tag | Decode | Encode |
| --- | --- | --- | --- |
| Uncompressed (`BI_RGB`) | 0 | Y | — |
| Motion JPEG | `MJPG`, `mjpg`, `jpeg`, `V_MJPEG` | Y | — |
| MPEG-1 video (ISO/IEC 11172-2) | `MPG1`, `PIM1`, `mp1v` | Y | — |

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

FLV is the one container here that declares nothing. Its nine-byte header says whether sound and
pictures are present and stops, so the streams are discovered from the tags and each codec from the
first tag belonging to it — which also means the streams are numbered in the order their first tag
appears, as ffprobe numbers them. Two payload shapes are not frames and never become packets: an AVC
sequence header, whose configuration record becomes the stream's private data, and an AAC one, which
does the same.

A stream coded with anything else is refused by name — the code, or the container's own name for the
codec where it has one — rather than half decoded into noise.

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

What is not implemented refuses and says so: D pictures, an MPEG-2 sequence extension, and a picture
size that changes while pictures predicted from the old one are still held.

## 📜 License

LGPL-3.0-or-later.
