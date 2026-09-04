# Hawkynt.FileFormats.Images

[![NuGet](https://img.shields.io/nuget/v/Hawkynt.FileFormats.Images.svg)](https://www.nuget.org/packages/Hawkynt.FileFormats.Images/)
[![NuGet downloads](https://img.shields.io/nuget/dt/Hawkynt.FileFormats.Images.svg)](https://www.nuget.org/packages/Hawkynt.FileFormats.Images/)
[![CI](https://github.com/Hawkynt/PNGCrushCS/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/Hawkynt/PNGCrushCS/actions/workflows/ci.yml)
[![License](https://img.shields.io/github/license/Hawkynt/PNGCrushCS)](https://github.com/Hawkynt/PNGCrushCS/blob/main/LICENSE)
![Target](https://img.shields.io/badge/target-net8.0-blue)
![Formats](https://img.shields.io/badge/formats-850%2B-brightgreen)
![Reflection](https://img.shields.io/badge/runtime%20reflection-zero-success)

> One drop-in pure-C# package for detecting, reading, writing and converting image formats, through
> one source-generated registry and one platform-independent `RawImage` model. The package claims the
> WHOLE domain — every image format, not a selection of it. Where a format is missing or only partly
> supported that is a tracked gap, recorded row by row in [Format support](#-format-support) below.

## 📦 Installation

```bash
dotnet add package Hawkynt.FileFormats.Images
```

The package targets `net8.0`. Format implementations and the support libraries they need are bundled behind the public package API; consumers do not need one NuGet package per image format.

## ✨ Features

- 850+ registered formats spanning web, desktop, scientific, professional, fax, console, retro-computing, texture, icon/cursor, and document-preview formats.
- Source-generated `ImageFormat` enum and `FormatRegistry`; no runtime reflection is needed for registration or dispatch.
- Magic-byte, extension, MIME-type, file, byte-span, and stream detection.
- Common `RawImage` representation for cross-format conversion.
- Runtime read/write capability discovery instead of caller-maintained format lists.
- Multi-image access where a format exposes pages, frames, entries, or embedded images.
- Optional metadata-only `ImageInfo` paths when dimensions/properties can be read without decoding all pixels.
- Per-format `VideoMode` metadata for fixed dimensions, palette restrictions, pixel aspect ratios, and display filters used by historical formats.

## 🧩 Format support

This table is generated from `FormatRegistry.AllFormats`, which is the authoritative package inventory. Every registered image format has one row; extensions and read/write capability come directly from its `FormatEntry`.

`✅` means the corresponding registry operation is available. A registered operation can still have format-specific subset limitations described later in this README; the matrix records capability presence, not a claim that every producer-specific variant is implemented.

The columns are the four things a caller can ask the registry for: **Read** decodes to a `RawImage`; **Write** encodes an arbitrary `RawImage` (a format that can only re-serialise a file it parsed counts as read-only); **Info** answers `ReadImageInfo` — dimensions, depth, colour mode, compression and frame count from the header alone, without decoding pixels; **Multi** exposes pages, frames or entries through the multi-image contract; **Optimizer** means the `Crush.Image` optimizer below rewrites the format losslessly in place instead of converting it.

<!-- IMAGE-FORMATS:BEGIN generated from FormatRegistry -- do not edit this table by hand -->
| Format | Extensions | Read | Write | Info | Multi | Optimizer |
| --- | --- | :---: | :---: | :---: | :---: | :---: |
| Aai | `.aai` | ✅ | ✅ | — | — | — |
| AccessFax | `.g4`, `.acc` | ✅ | ✅ | — | — | — |
| Acorn | `.spr`, `.acorn` | ✅ | ✅ | — | — | — |
| AdexImage | `.adx` | ✅ | ✅ | — | — | — |
| AdTechFax | `.adt` | ✅ | ✅ | — | — | — |
| AdvancedArtStudio | `.ocp`, `.mpi`, `.mpic` | ✅ | ✅ | — | — | — |
| Afli | `.afl` | ✅ | ✅ | — | — | — |
| Ai | `.ai` | ✅ | ✅ | — | — | — |
| AimGreyScale | `.ima` | ✅ | ✅ | — | — | — |
| AirNav | `.anv` | ✅ | ✅ | — | — | — |
| AladdinPaint | `.alp` | ✅ | ✅ | — | — | — |
| AliasPix | `.pix`, `.als`, `.alias`, `.img`, `.lux` | ✅ | ✅ | — | — | — |
| AmicaPaint | `.ami` | ✅ | ✅ | — | — | — |
| AmigaIcon | `.info` | ✅ | ✅ | — | — | — |
| AmosBank | `.abk` | ✅ | ✅ | — | — | — |
| AmstradCpc | `.cpc` | ✅ | ✅ | — | — | — |
| AmstradMode5 | `.cm5` | ✅ | ✅ | — | — | — |
| Analyze | `.hdr`, `.img` | ✅ | ✅ | — | — | — |
| AndrewToolkit | `.atk` | ✅ | ✅ | — | — | — |
| Ani | `.ani` | ✅ | ✅ | — | ✅ | ✅ |
| AnimatorCompressor | `.kpr` | ✅ | ✅ | — | — | — |
| Anime4Ever | `.a4r` | ✅ | ✅ | — | — | — |
| AnimPainter | `.anp` | ✅ | ✅ | — | — | — |
| AnsiArt | `.ans`, `.ansi` | ✅ | ✅ | — | — | — |
| Apac3 | `.ap3`, `.apv`, `.dgi`, `.dgp`, `.esc`, `.ilc`, `.pzm`, `.app`, `.ils` | ✅ | ✅ | — | — | — |
| Apng | `.apng` | ✅ | ✅ | — | ✅ | — |
| ApolloHdru | `.hdru`, `.gn` | ✅ | ✅ | — | — | — |
| Apple3201 | `.3201` | ✅ | ✅ | — | — | — |
| AppleII | `.hgr`, `.dhgr` | ✅ | ✅ | — | — | — |
| AppleIIDhr | `.dhr`, `.a2d` | ✅ | ✅ | — | — | — |
| AppleIIgs | `.shr`, `.c1`, `.pic` | ✅ | ✅ | — | — | — |
| AppleIIHgr | `.hgr` | ✅ | ✅ | — | — | — |
| ApplePreferred | `.32k`, `.gs`, `.iigs`, `.shr` | ✅ | ✅ | — | — | — |
| AppleSh3 | `.sh3`, `.3200` | ✅ | ✅ | — | — | — |
| AppleShr | `.shr` | ✅ | ✅ | — | — | — |
| Apx | `.apx` | ✅ | ✅ | — | — | — |
| Arf | `.arf` | ✅ | ✅ | — | — | — |
| Arn | `.arn` | ✅ | ✅ | — | — | — |
| Art | `.art` | ✅ | ✅ | — | — | — |
| ArtDirector | `.art` | ✅ | ✅ | — | — | — |
| Artist64 | `.a64` | ✅ | ✅ | — | — | — |
| ArtMaster88 | `.arv`, `.img` | ✅ | ✅ | — | — | — |
| ArtStudio8 | `.as8` | ✅ | ✅ | — | — | — |
| ArtStudioWindow | `.mwi`, `.mwin` | ✅ | ✅ | — | — | — |
| AsciiMaker | `.asc`, `.gr0` | ✅ | ✅ | — | — | — |
| Astc | `.astc` | ✅ | ✅ | — | — | — |
| Atari16x16Font | `.sxs` | ✅ | ✅ | — | — | — |
| Atari2600 | `.a26`, `.tia` | ✅ | ✅ | — | — | — |
| Atari7800 | `.a78`, `.a7800` | ✅ | ✅ | — | — | — |
| Atari8Bit | `.gr7`, `.gr8`, `.gr9`, `.gr15`, `.hip`, `.mic`, `.int` | ✅ | ✅ | — | — | — |
| Atari8Missile | `.mis` | ✅ | ✅ | — | — | — |
| Atari8Player | `.pla` | ✅ | ✅ | — | — | — |
| AtariAgp | `.agp` | ✅ | ✅ | — | — | — |
| AtariAnimation | `.aan` | ✅ | ✅ | — | — | — |
| AtariAnticMode | `.ame`, `.anm` | ✅ | ✅ | — | — | — |
| AtariArtist | `.aat` | ✅ | ✅ | — | — | — |
| AtariCAD | `.drg`, `.acd` | ✅ | ✅ | — | — | — |
| AtariCel | `.cel` | ✅ | ✅ | — | — | — |
| AtariChampionsInterlace | `.cin`, `.cci` | ✅ | ✅ | — | — | — |
| AtariCompressed | `.acr`, `.acp` | ✅ | ✅ | — | — | — |
| AtariDoodle | `.doo` | ✅ | ✅ | — | — | — |
| AtariDump | `.asd`, `.adm` | ✅ | ✅ | — | — | — |
| AtariFalcon | `.ftc` | ✅ | ✅ | — | — | — |
| AtariFalconXga | `.xga` | ✅ | ✅ | — | — | — |
| AtariFont | `.fnt8` | ✅ | ✅ | — | — | — |
| AtariFontMaker | `.fn2` | ✅ | ✅ | — | — | — |
| AtariGfb | `.gfb` | ✅ | ✅ | — | — | — |
| AtariGr7 | `.gr7` | ✅ | ✅ | — | — | — |
| AtariGr8 | `.gr8` | ✅ | ✅ | — | — | — |
| AtariGrafik | `.pcp` | ✅ | ✅ | — | — | — |
| AtariGraphics10 | `.gr10`, `.g10` | ✅ | ✅ | — | — | — |
| AtariGraphics11 | `.gr11`, `.g11` | ✅ | ✅ | — | — | — |
| AtariGraphics3 | `.gr3`, `.sg3` | ✅ | ✅ | — | — | — |
| AtariGraphics9 | `.gr9`, `.g9`, `.g9s`, `.sfd` | ✅ | ✅ | — | — | — |
| AtariGraphicsStudio | `.ags` | ✅ | ✅ | — | — | — |
| AtariGrayscale9 | `.bg9`, `.g09` | ✅ | ✅ | — | — | — |
| AtariHardInterlace | `.hip`, `.hps` | ✅ | ✅ | — | — | — |
| AtariHighResPage | `.pg3` | ✅ | ✅ | — | — | — |
| AtariHr | `.hr` | ✅ | ✅ | — | — | — |
| AtariHr2 | `.hr2`, `.hci` | ✅ | ✅ | — | — | — |
| AtariIce | `.ice`, `.icn` | ✅ | ✅ | — | — | — |
| AtariImageManager | `.im`, `.col` | ✅ | ✅ | — | — | — |
| AtariMaxi | `.max8`, `.amx` | ✅ | ✅ | — | — | — |
| AtariPaintworks | `.cl0`, `.cl1`, `.cl2`, `.pg0`, `.pg1`, `.pg2`, `.pg3`, `.sc0`, `.sc1`, `.sc2` | ✅ | ✅ | — | — | — |
| AtariPi5 | `.pi5` | ✅ | ✅ | — | — | — |
| AtariPi8 | `.pi8` | ✅ | ✅ | — | — | — |
| AtariPi9 | `.pi9` | ✅ | ✅ | — | — | — |
| AtariPicture | `.apc`, `.apa`, `.plm`, `.aps`, `.mga`, `.pls` | ✅ | ✅ | — | — | — |
| AtariPicworks | `.cp3` | ✅ | ✅ | — | — | — |
| AtariPlayer | `.pmg`, `.plm` | ✅ | ✅ | — | — | — |
| AtariPlayerEditor | `.apl` | ✅ | ✅ | — | — | — |
| AtariSif | `.sif` | ✅ | ✅ | — | — | — |
| AtariTools800 | `.4pl`, `.4mi`, `.4pm` | ✅ | ✅ | — | — | — |
| AtariTools800Font | `.acs` | ✅ | ✅ | — | — | — |
| AtariTt | `.pi5`, `.pi4`, `.pi6` | ✅ | ✅ | — | — | — |
| AtariTxs | `.txs` | ✅ | ✅ | — | — | — |
| AttGroup4 | `.att` | ✅ | ✅ | — | — | — |
| AutodeskCel | `.cel` | ✅ | ✅ | — | — | — |
| AutoFx | `.afx` | ✅ | ✅ | — | — | — |
| Autologic | `.gm`, `.gm2`, `.gm4` | ✅ | ✅ | — | — | — |
| AvhrrImage | `.sst` | ✅ | ✅ | — | — | — |
| Avif | `.avif` | ✅ | — | — | — | — |
| Avs | `.avs`, `.x`, `.mbfavs`, `.mbfs` | ✅ | ✅ | — | — | — |
| AwardBmp | `.epa`, `.awbm` | ✅ | ✅ | — | — | — |
| Awd | `.awd` | ✅ | ✅ | — | — | — |
| AxialisScreensaver | `.ssp` | ✅ | ✅ | — | ✅ | — |
| Bam | `.bam` | ✅ | ✅ | ✅ | — | — |
| BbcMicro | `.bbc` | ✅ | ✅ | — | — | — |
| BbcMicroScreen | `.bb4`, `.bb0`, `.bb1`, `.bb2`, `.bb5` | ✅ | ✅ | — | — | — |
| BennetYeeFace | `.ybm` | ✅ | ✅ | — | — | — |
| BestPaint | `.bp` | ✅ | ✅ | — | — | — |
| Bfli | `.bfl`, `.bfli`, `.flp` | ✅ | ✅ | — | — | — |
| BfxBitware | `.bfx` | ✅ | ✅ | — | — | — |
| BigTiff | `.btf`, `.tf8` | ✅ | ✅ | — | ✅ | — |
| BioRadPic | `.pic` | ✅ | ✅ | — | — | — |
| BkScreen | `.bks` | ✅ | ✅ | — | — | — |
| Blazing | `.blz`, `.pi` | ✅ | ✅ | — | — | — |
| BlazingPaddlesWindow | `.wnd` | ✅ | ✅ | — | — | — |
| Blazon | `.bpl` | ✅ | ✅ | — | — | — |
| Blp | `.blp` | ✅ | ✅ | — | — | — |
| Bmp | `.bmp`, `.dib`, `.bga`, `.rl4`, `.rl8`, `.vga`, `.sys`, `.bum`, `.thb`, `.2d`, `.bmc`, `.stm`, `.upi`, `.msk`, `.flt` | ✅ | ✅ | ✅ | — | ✅ |
| Bob | `.bob` | ✅ | ✅ | — | — | — |
| BodyPaint3D | `.b3d`, `.b2d` | ✅ | ✅ | — | — | — |
| BoogieDownPaint | `.bdp` | ✅ | ✅ | — | — | — |
| Botticelli | `.p4i` | ✅ | ✅ | — | — | — |
| Bpg | `.bpg` | ✅ | ✅ | — | — | — |
| BrooktroutFax | `.brk`, `.301`, `.brt` | ✅ | ✅ | — | — | — |
| BrotherFax | `.uni` | ✅ | ✅ | — | — | — |
| Brus | `.brus` | ✅ | ✅ | — | — | — |
| Bsave | `.bsv` | ✅ | ✅ | — | — | — |
| Bsb | `.kap`, `.bsb` | ✅ | ✅ | — | — | — |
| BugbiterApac | `.bgp` | ✅ | ✅ | — | — | — |
| BugBitmap | `.bbm`, `.bug` | ✅ | ✅ | — | — | — |
| ByLight | `.bif` | ✅ | ✅ | — | — | — |
| ByuSir | `.sir` | ✅ | ✅ | — | — | — |
| C128 | `.c128`, `.vdc` | ✅ | ✅ | — | — | — |
| C128Hires | `.c1h` | ✅ | ✅ | — | — | — |
| C128Multi | `.c1m` | ✅ | ✅ | — | — | — |
| C128VDC | `.vdc`, `.vdc3` | ✅ | ✅ | — | — | — |
| C16Plus4 | `.c16`, `.plus4` | ✅ | ✅ | — | — | — |
| C64Multi | `.ocp`, `.hires`, `.ami` | ✅ | ✅ | — | — | — |
| Calamus | `.cpi`, `.crg` | ✅ | ✅ | — | — | — |
| Cals | `.cal`, `.cals`, `.gp4`, `.mil` | ✅ | ✅ | — | — | — |
| CameraRaw | `.cr2`, `.nef`, `.arw`, `.orf`, `.rw2`, `.pef`, `.raf`, `.raw`, `.srw`, `.dcs`, `.dcr`, `.kdc`, `.srf`, `.sr2`, `.mos`, `.3fr`, `.mef`, `.nrw`, `.rwl`, `.erf`, `.iiq` | ✅ | ✅ | — | — | — |
| CanonNavFax | `.can` | ✅ | ✅ | — | — | — |
| Canvas | `.cvs` | ✅ | ✅ | — | — | — |
| CanvasRaster | `.ful` | ✅ | ✅ | — | — | — |
| CartesMichelin | `.big` | ✅ | — | — | — | — |
| CasioQv | `.cam` | ✅ | ✅ | — | — | — |
| Ccitt | `.g3`, `.g4`, `.ccitt`, `.fax` | ✅ | ✅ | — | — | — |
| CDUPaint | `.cdu` | ✅ | ✅ | — | — | — |
| Cdxl | `.cdxl` | ✅ | ✅ | — | — | — |
| Cel | `.cel` | ✅ | ✅ | — | — | — |
| CelGrey | `.cel` | ✅ | ✅ | — | — | — |
| Centauri | `.cnt`, `.cen` | ✅ | ✅ | — | — | — |
| CentauriLogoEditor | `.cle` | ✅ | ✅ | — | — | — |
| CfliDesigner | `.cfli` | ✅ | ✅ | — | — | — |
| Cgm | `.cgm` | ✅ | ✅ | — | — | — |
| ChampionsInterlace | `.cin` | ✅ | ✅ | — | — | — |
| CharPad | `.ctm` | ✅ | ✅ | — | — | — |
| CharSet64 | `.chr64` | ✅ | ✅ | — | — | — |
| Cheese | `.che`, `.chs` | ✅ | ✅ | — | — | — |
| ChinonEs1000 | `.cmt` | ✅ | ✅ | — | — | — |
| ChrDollar | `.ch$` | ✅ | ✅ | — | — | — |
| CImage | `.dsi` | ✅ | ✅ | — | — | — |
| CinemasterAtari | `.cin8` | ✅ | ✅ | — | — | — |
| Cineon | `.cin` | ✅ | ✅ | — | — | — |
| CiscoIp | `.cip` | ✅ | ✅ | — | — | — |
| ClipArtCatalog | `.cat` | ✅ | ✅ | — | ✅ | — |
| Cloe | `.clo`, `.cloe` | ✅ | ✅ | — | — | — |
| Clp | `.clp` | ✅ | ✅ | — | — | — |
| Cmu | `.cmu` | ✅ | ✅ | — | — | — |
| CmuWindowManager | `.cmu`, `.cmuwm` | ✅ | ✅ | — | — | — |
| CoCo | `.coc` | ✅ | ✅ | — | — | — |
| CoCo3 | `.cc3` | ✅ | ✅ | — | — | — |
| CoCoMax | `.max`, `.p41` | ✅ | ✅ | — | — | — |
| CocoP11 | `.p11` | ✅ | ✅ | — | — | — |
| CokeAtari | `.tg1` | ✅ | ✅ | — | — | — |
| ColoRix | `.rix`, `.sc0`, `.sc1`, `.sc2`, `.sc3`, `.sc4`, `.sc5`, `.sc6`, `.sc7`, `.sc8`, `.sc9`, `.sca`, `.scb`, `.scc`, `.scd`, `.sce`, `.scf`, `.scg`, `.sch`, `.sci`, `.scj`, `.sck`, `.scl`, `.scm`, `.scn`, `.sco`, `.scp`, `.scq`, `.scr`, `.scs`, `.sct`, `.scu`, `.scv`, `.scw`, `.scx`, `.scy`, `.scz` | ✅ | ✅ | — | — | — |
| ColorStar | `.bil` | ✅ | ✅ | — | — | — |
| ColorStarObject | `.obj` | ✅ | ✅ | — | — | — |
| ColrObjectEditor | `.mur` | ✅ | ✅ | — | — | — |
| Commodore64Font | `.64c`, `.g` | ✅ | ✅ | — | — | — |
| CommodoreGrafix | `.cgx` | ✅ | ✅ | — | — | — |
| CommodorePet | `.pet` | ✅ | ✅ | — | — | — |
| CompuServeRle | `.rle` | ✅ | ✅ | — | — | — |
| ComputerEyes | `.ce`, `.ce1`, `.ce2` | ✅ | ✅ | — | — | — |
| ComputerEyesSt | `.ce3` | ✅ | ✅ | — | — | — |
| CompW | `.wlm` | ✅ | ✅ | — | — | — |
| CoreIdc | `.idc` | ✅ | ✅ | — | — | — |
| CorelGallery | `.bmf` | ✅ | ✅ | — | — | — |
| Cp8Gray | `.cp8` | ✅ | ✅ | — | — | — |
| CpcAdvanced | `.cpa` | ✅ | ✅ | — | — | — |
| CpcFont | `.cpf` | ✅ | ✅ | — | — | — |
| CpcOverscan | `.cpo` | ✅ | ✅ | — | — | — |
| CpcPlus | `.cpp` | ✅ | ✅ | — | — | — |
| CpcSprite | `.cps` | ✅ | ✅ | — | — | — |
| Crack | `.ca2` | ✅ | ✅ | — | — | — |
| CrackArt | `.ca1`, `.ca2`, `.ca3` | ✅ | ✅ | — | — | — |
| CranachPaint | `.esm` | ✅ | ✅ | — | — | — |
| Crd | `.crd` | ✅ | — | — | — | — |
| CreateWithGarfield | `.cwg` | ✅ | ✅ | — | — | — |
| Crw | `.crw` | ✅ | — | — | — | — |
| CsvImage | `.csv` | ✅ | ✅ | — | — | — |
| Cur | `.cur` | ✅ | ✅ | — | ✅ | ✅ |
| CutCreator | `.cut` | ✅ | ✅ | — | — | — |
| DaisyDotFont | `.nlq` | ✅ | ✅ | — | — | — |
| DaliCompressed | `.lpk`, `.mpk`, `.hpk` | ✅ | ✅ | — | — | — |
| DaliST | `.sd0`, `.sd1`, `.sd2` | ✅ | ✅ | — | — | — |
| DbwRender | `.dbw` | ✅ | ✅ | — | — | — |
| Dcx | `.dcx` | ✅ | ✅ | — | ✅ | — |
| Dds | `.dds` | ✅ | ✅ | — | — | — |
| Degas | `.pi1`, `.pi2`, `.pi3`, `.pc1`, `.pc2`, `.pc3`, `.suh` | ✅ | ✅ | — | — | — |
| DegasBrush | `.bru` | ✅ | ✅ | — | — | — |
| DegasIcon | `.icn` | ✅ | ✅ | — | — | — |
| DelmPaint | `.del`, `.dph` | ✅ | ✅ | — | — | — |
| Deluxe | `.dps`, `.dlx` | ✅ | ✅ | — | — | — |
| DGraphCompressed | `.p3c` | ✅ | ✅ | — | — | — |
| Dicom | `.dcm`, `.dicom`, `.acr`, `.dic`, `.dc3` | ✅ | ✅ | — | — | — |
| DigiSpec | `.dgs` | ✅ | ✅ | — | — | — |
| DigitalFx | `.tdim` | ✅ | ✅ | — | — | — |
| DigiView | `.dgv` | ✅ | ✅ | — | — | — |
| Din | `.din` | ✅ | ✅ | — | — | — |
| DirLogoMaker | `.dlm` | ✅ | ✅ | — | — | — |
| DispThumbnail | `.tnl` | ✅ | ✅ | — | — | — |
| DivGameMap | `.fpg` | ✅ | ✅ | — | — | — |
| DjVu | `.djvu`, `.djv`, `.iw4` | ✅ | ✅ | — | — | — |
| Dng | `.dng` | ✅ | ✅ | — | — | — |
| DolphinEd | `.dol`, `.bed` | ✅ | ✅ | — | — | — |
| Doodle | `.dd`, `.ddp` | ✅ | ✅ | — | — | — |
| DoodleAtari | `.doo` | ✅ | ✅ | — | — | — |
| DoodleComp | `.jj` | ✅ | ✅ | — | — | — |
| DoodlePacked | `.dpk` | ✅ | ✅ | — | — | — |
| DoomFlat | `.flat` | ✅ | ✅ | — | — | — |
| Dpx | `.dpx` | ✅ | ✅ | — | — | — |
| Dragon | `.dgn` | ✅ | ✅ | — | — | — |
| DrawIt | `.dit` | ✅ | ✅ | — | — | — |
| Drazlace | `.dlp`, `.drl` | ✅ | ✅ | — | — | — |
| DrazPaint | `.drz`, `.drp` | ✅ | ✅ | — | — | — |
| DrHalo | `.cut` | ✅ | ✅ | — | — | — |
| DuneGraph | `.dg1`, `.dc1` | ✅ | ✅ | — | — | — |
| Duo | `.duo`, `.du1` | ✅ | ✅ | — | — | — |
| DuoMedium | `.du2` | ✅ | ✅ | — | — | — |
| Dwg | `.dwg` | ✅ | — | — | — | — |
| Dxf | `.dxf` | ✅ | — | — | — | — |
| EccHeader | `.ecc` | ✅ | ✅ | — | — | — |
| EciGraphicEditor | `.eci`, `.ecp` | ✅ | — | — | — | — |
| EclipseTile | `.tile` | ✅ | ✅ | — | — | — |
| Ecw | `.ecw` | ✅ | ✅ | — | — | — |
| EdmicsC4 | `.c4` | ✅ | ✅ | — | — | — |
| EggPaint | `.trp` | ✅ | ✅ | — | — | — |
| ElectricImage | `.ei`, `.eidi` | ✅ | — | — | ✅ | — |
| Electronika | `.bk`, `.ekr` | ✅ | ✅ | — | — | — |
| EmbeddedDib | `.cdr`, `.cmx`, `.zmf`, `.skf`, `.cad`, `.sdg`, `.ipg`, `.btn` | ✅ | — | — | — | — |
| EmcEditor | `.emc` | ✅ | ✅ | — | — | — |
| Emf | `.emf` | ✅ | ✅ | — | — | — |
| Enterprise128 | `.ep`, `.elan` | ✅ | ✅ | — | — | — |
| Envi | `.hdr` | ✅ | ✅ | — | — | — |
| EpaBios | `.epa` | ✅ | ✅ | — | — | — |
| Eps | `.eps`, `.epsf`, `.epsi`, `.epi`, `.ept` | ✅ | ✅ | — | — | — |
| Eroiica | `.eif` | ✅ | — | — | ✅ | — |
| EscapePaint | `.esp` | ✅ | ✅ | — | — | — |
| EsmSoftwarePix | `.pix` | ✅ | ✅ | — | — | — |
| EverexFax | `.efx`, `.ef3` | ✅ | ✅ | — | — | — |
| Exr | `.exr` | ✅ | ✅ | — | — | — |
| ExtendedGemImg | `.ximg` | ✅ | ✅ | — | — | — |
| ExtendSuperHires | `.esh` | ✅ | ✅ | — | — | — |
| EzArt | `.eza` | ✅ | ✅ | — | — | — |
| FacePainter | `.fpt`, `.fcp` | ✅ | ✅ | — | — | — |
| FaceSaver | `.face`, `.fac` | ✅ | ✅ | — | — | — |
| FaceServer | `.fac`, `.face` | ✅ | ✅ | — | — | — |
| FalconFuckpaint | `.pi4`, `.pi7`, `.pi9` | ✅ | ✅ | — | — | — |
| FalconPaint | `.fpn` | ✅ | ✅ | — | — | — |
| FalconRes | `.frs` | ✅ | ✅ | — | — | — |
| Farbfeld | `.ff`, `.farbfeld` | ✅ | ✅ | — | — | — |
| FastgraphPixelRun | `.prf` | ✅ | ✅ | — | — | — |
| FaxG3 | `.g3` | ✅ | ✅ | — | — | — |
| FaxMan | `.fmf` | ✅ | ✅ | — | — | — |
| Fbm | `.fbm` | ✅ | ✅ | — | — | — |
| Fff | `.fff` | ✅ | ✅ | — | — | — |
| Ffli | `.ffli`, `.ffl` | ✅ | ✅ | — | — | — |
| FirstPublisher | `.art` | ✅ | ✅ | — | — | — |
| Fits | `.fits`, `.fit`, `.fts` | ✅ | ✅ | — | — | — |
| FitsDocument | `.fits`, `.fit`, `.fts` | ✅ | ✅ | — | ✅ | — |
| Fl32 | `.fl32` | ✅ | ✅ | — | — | — |
| FlashImage | `.fi` | ✅ | ✅ | — | — | — |
| Fli | `.fli`, `.flc` | ✅ | ✅ | — | ✅ | — |
| Fli64 | `.fli64` | ✅ | ✅ | — | — | — |
| FliDesigner | `.fd2` | ✅ | ✅ | — | — | — |
| FliDesigner2 | `.fd2` | ✅ | ✅ | — | — | — |
| FliEditor | `.fed` | ✅ | ✅ | — | — | — |
| Flif | `.flif` | ✅ | ✅ | — | — | — |
| FliGraph | `.flg`, `.bml`, `.fli` | ✅ | ✅ | — | — | — |
| Flimatic | `.flm` | ✅ | ✅ | — | — | — |
| Flip64 | `.fbi` | ✅ | ✅ | — | — | — |
| FliProfi | `.fpr` | ✅ | ✅ | — | — | — |
| FloorDesigner | `.fge` | ✅ | ✅ | — | — | — |
| FmTowns | `.fmt` | ✅ | ✅ | — | — | — |
| FontasyGrafik | `.bsg` | ✅ | ✅ | — | — | — |
| Fpx | `.fpx`, `.mix` | ✅ | — | — | — | — |
| FreeHand | `.fhs` | ✅ | ✅ | — | — | — |
| FremontFax | `.f96` | ✅ | ✅ | — | — | — |
| Fsh | `.fsh` | ✅ | ✅ | — | — | — |
| Fuckpaint | `.fp` | ✅ | ✅ | — | — | — |
| FullscreenKit | `.kid` | ✅ | ✅ | — | — | — |
| FunGraphicsMachine | `.fgs` | ✅ | ✅ | — | — | — |
| FunPainter | `.fp2`, `.fun` | ✅ | — | — | — | — |
| FunPhotor | `.fpr` | ✅ | ✅ | — | — | — |
| FuntasticPaint | `.fun8`, `.ftp` | ✅ | ✅ | — | — | — |
| FunWithArt | `.fwa` | ✅ | ✅ | — | — | — |
| G9b | `.g9b` | ✅ | ✅ | — | — | — |
| Gaf | `.gaf` | ✅ | ✅ | — | — | — |
| GameBoyTile | `.2bpp`, `.cgb` | ✅ | ✅ | — | — | — |
| GammaFax | `.gmf` | ✅ | ✅ | — | — | — |
| GbaTile | `.4bpp`, `.gba` | ✅ | ✅ | — | — | — |
| Gbr | `.gbr` | ✅ | ✅ | — | — | — |
| Gd2 | `.gd2` | ✅ | ✅ | — | — | — |
| GedPicture | `.ged` | ✅ | ✅ | — | — | — |
| GeGenesis | `.fre`, `.pd`, `.t1`, `.t2` | ✅ | ✅ | — | — | — |
| Gem | `.gem` | ✅ | — | — | — | — |
| GemImg | `.img` | ✅ | ✅ | — | — | — |
| GeoPaint | `.geo` | ✅ | ✅ | — | — | — |
| GephardHires | `.ghg` | ✅ | ✅ | — | — | — |
| GfaPaint | `.gfp` | ✅ | ✅ | — | — | — |
| GfaRaytrace | `.sul` | ✅ | ✅ | — | — | — |
| Gif | `.gif`, `.giff`, `.bpr` | ✅ | ✅ | — | ✅ | ✅ |
| Gigacad | `.gcd` | ✅ | ✅ | — | — | — |
| GigaPaint | `.gih`, `.gig`, `.rpo` | ✅ | ✅ | — | — | — |
| GoDot4Bit | `.4bt`, `.4bit`, `.clp` | ✅ | ✅ | — | — | — |
| GodPaint | `.gpn`, `.gdp`, `.god` | ✅ | ✅ | — | — | — |
| Grafix | `.grx` | ✅ | ✅ | — | — | — |
| Graph2Font | `.g2f` | ✅ | ✅ | — | — | — |
| Graph2FontMch | `.mch` | ✅ | ✅ | — | — | — |
| Graph2FontScroll | `.vsc` | ✅ | — | — | — | — |
| Graphics10Plus | `.gr10p` | ✅ | ✅ | — | — | — |
| Graphics9Plus | `.gr9p` | ✅ | ✅ | — | — | — |
| GraphicsMaster | `.gms`, `.gm8` | ✅ | ✅ | — | — | — |
| GraphLogo | `.all` | ✅ | ✅ | — | — | — |
| GraphSaurus | `.sr5`, `.grs`, `.sr8`, `.srs` | ✅ | ✅ | — | — | — |
| GraphSaurus6 | `.sr6` | ✅ | ✅ | — | — | — |
| GraphSaurus7 | `.sr7` | ✅ | ✅ | — | — | — |
| GraphSaurusInterlaced | `.sri` | ✅ | ✅ | — | — | — |
| GraspGl | `.gl` | ✅ | ✅ | — | — | — |
| GrassSlideshow | `.hpm` | ✅ | ✅ | — | — | — |
| GreatPaint | `.gpt` | ✅ | ✅ | — | — | — |
| GrfBitmap | `.grf` | ✅ | ✅ | — | — | — |
| Grs16 | `.g16` | ✅ | ✅ | — | — | — |
| GunPaint | `.gun`, `.ifl` | ✅ | ✅ | — | — | — |
| HalfLifeMdl | `.mdltex` | ✅ | ✅ | — | — | — |
| HalfLifeModel | `.mdl` | ✅ | — | — | — | — |
| HandyScanner | `.hs2` | ✅ | ✅ | — | — | — |
| HardColorMap | `.hcm` | ✅ | ✅ | — | — | — |
| HardInterlace | `.hip` | ✅ | ✅ | — | — | — |
| HayesJtfax | `.jtf` | ✅ | ✅ | — | — | — |
| HcbEditor | `.hcb` | ✅ | ✅ | — | — | — |
| Hdr | `.hdr`, `.hdri`, `.rgbe`, `.xyze`, `.rad` | ✅ | ✅ | — | — | — |
| Heif | `.heic`, `.heif` | ✅ | ✅ | ✅ | ✅ | — |
| HereticM8 | `.m8` | ✅ | ✅ | — | — | — |
| HfImage | `.hf` | ✅ | ✅ | — | — | — |
| HiEddi | `.hed` | ✅ | ✅ | — | — | — |
| HighResAtari | `.hra` | ✅ | ✅ | — | — | — |
| HighresMedium | `.hrm` | ✅ | ✅ | — | — | — |
| HighResST | `.hst`, `.hrs` | ✅ | ✅ | — | — | — |
| HinterGrundBild | `.hgb` | ✅ | ✅ | — | — | — |
| HiPicCreator | `.hpc`, `.aas` | ✅ | ✅ | — | — | — |
| HiresC64 | `.hir`, `.hbm`, `.hpi` | ✅ | ✅ | — | — | — |
| HiResEditor | `.het`, `.rph` | ✅ | ✅ | — | — | — |
| HiresFliCrest | `.hfc`, `.hfd` | ✅ | ✅ | — | — | — |
| HiresInterlaceFeniks | `.hlf`, `.hie` | ✅ | ✅ | — | — | — |
| Hireslace | `.hle` | ✅ | ✅ | — | — | — |
| HiresManager | `.him` | ✅ | ✅ | — | — | — |
| HomeworldLif | `.lif` | ✅ | ✅ | — | — | — |
| Hp48Grob | `.grb`, `.gro` | ✅ | ✅ | — | — | — |
| Hpgl | `.hpgl`, `.hgl`, `.hpg`, `.prn`, `.prt`, `.spl` | ✅ | — | — | — | — |
| HpGrob | `.grob`, `.hp`, `.gro2`, `.gro4` | ✅ | ✅ | — | — | — |
| Hpi | `.hpi` | ✅ | ✅ | — | — | — |
| Hru | `.hru` | ✅ | ✅ | — | — | — |
| Hrz | `.hrz` | ✅ | ✅ | ✅ | — | — |
| Hta | `.hta` | ✅ | ✅ | — | ✅ | — |
| IbmKips | `.kps` | ✅ | ✅ | — | — | — |
| IcDraw | `.ibi`, `.ib3` | ✅ | ✅ | — | — | — |
| Ice | `.irg`, `.ir2`, `.icn`, `.imn`, `.ipc` | ✅ | ✅ | — | — | — |
| IcePcinPlus | `.ip2` | ✅ | ✅ | — | — | — |
| Icns | `.icns` | ✅ | ✅ | — | ✅ | — |
| Ico | `.ico` | ✅ | ✅ | — | ✅ | ✅ |
| IconLibrary | `.icl` | ✅ | — | — | — | — |
| Ics | `.ics` | ✅ | ✅ | — | — | — |
| IffAcbm | `.acbm`, `.iff`, `.blk` | ✅ | ✅ | — | — | — |
| IffAnim | `.anim` | ✅ | ✅ | — | — | — |
| IffAnim8 | `.an8`, `.anim8` | ✅ | — | — | — | — |
| IffDctv | `.dctv` | ✅ | — | — | — | — |
| IffDeep | `.deep`, `.iff`, `.blk` | ✅ | ✅ | — | — | — |
| IffDpan | `.dpan` | ✅ | — | — | — | — |
| IffHame | `.hame` | ✅ | — | — | — | — |
| IffMultiPalette | `.mpl`, `.mpal` | ✅ | — | — | — | — |
| IffPbm | `.lbm`, `.pbm`, `.blk` | ✅ | ✅ | — | — | — |
| IffRgb8 | `.rgb8`, `.iff`, `.blk` | ✅ | ✅ | — | — | — |
| IffRgbn | `.rgbn`, `.iff`, `.blk` | ✅ | ✅ | — | — | — |
| IffSham | `.sham` | ✅ | — | — | — | — |
| Ilbm | `.lbm`, `.ilbm`, `.iff`, `.blk`, `.ham`, `.ham6`, `.ham8`, `.256`, `.ap2`, `.beam`, `.dct`, `.dr`, `.mp`, `.bl1`, `.bl2`, `.bl3` | ✅ | ✅ | — | — | — |
| Im5Visilog | `.im5` | ✅ | ✅ | — | — | — |
| ImageLabBw | `.b&w`, `.b_w`, `.dit` | ✅ | ✅ | — | — | — |
| ImageSysC64 | `.isc` | ✅ | ✅ | — | — | — |
| ImageSystem | `.ish`, `.ism` | ✅ | ✅ | — | — | — |
| Imagic | `.ic1`, `.ic2`, `.ic3` | ✅ | ✅ | — | — | — |
| ImagicPaint | `.imp`, `.igp` | ✅ | ✅ | — | — | — |
| ImagingFax | `.g3n` | ✅ | ✅ | — | — | — |
| ImnetImage | `.imt` | ✅ | ✅ | — | — | — |
| IndyPaint | `.ipn`, `.idy`, `.tru` | ✅ | ✅ | — | — | — |
| Ingr | `.cit`, `.itg` | ✅ | ✅ | — | — | — |
| InShape | `.iim` | ✅ | ✅ | — | — | — |
| Int95a | `.int` | ✅ | ✅ | — | — | — |
| Interfile | `.hv` | ✅ | ✅ | — | — | — |
| Interlace8 | `.int8` | ✅ | ✅ | — | — | — |
| InterlacedLogoEditor | `.ile` | ✅ | ✅ | — | — | — |
| InterlaceGraphicsEditor | `.ige` | ✅ | ✅ | — | — | — |
| InterlaceHiresEditor | `.ihe` | ✅ | ✅ | — | — | — |
| InterlaceLogoDesigner | `.ild` | ✅ | ✅ | — | — | — |
| InterlaceStudio | `.ist` | ✅ | ✅ | — | — | — |
| InterleafImage | `.iimg` | ✅ | ✅ | — | — | — |
| InterPainter | `.inp`, `.ing`, `.ins` | ✅ | ✅ | — | — | — |
| InterPaintHi | `.iph`, `.hre` | ✅ | ✅ | — | — | — |
| InterPaintMc | `.ipt`, `.lre` | ✅ | ✅ | — | — | — |
| Ioca | `.ica`, `.ioca`, `.ioc`, `.mod` | ✅ | ✅ | — | — | — |
| IPaint | `.ip` | ✅ | ✅ | — | — | — |
| Ipl | `.ipl` | ✅ | ✅ | — | — | — |
| Ipsm | `.pan` | ✅ | ✅ | — | — | — |
| Iss | `.iss` | ✅ | ✅ | — | — | — |
| It01 | `.fit` | ✅ | ✅ | — | — | — |
| Jbig | `.jbg`, `.bie`, `.jbig` | ✅ | ✅ | — | — | — |
| Jbig2 | `.jb2`, `.jbig2` | ✅ | ✅ | — | — | — |
| JetGraphicsPlanner | `.jgp` | ✅ | ✅ | — | — | — |
| JigsawPicture | `.jig` | ✅ | ✅ | — | — | — |
| JigsawPuzzle | `.jig` | ✅ | ✅ | — | — | — |
| Jng | `.jng` | ✅ | ✅ | — | — | — |
| JovianVi | `.vi` | ✅ | ✅ | — | — | — |
| Jpeg | `.jpg`, `.jpeg`, `.jpe`, `.jfif`, `.jps`, `.thm`, `.j`, `.jif`, `.fsy`, `.mph`, `.ncy`, `.frm` | ✅ | ✅ | — | — | ✅ |
| Jpeg2000 | `.jp2`, `.j2k`, `.j2c`, `.jpx`, `.jpc`, `.jpf`, `.jpt`, `.jpm` | ✅ | ✅ | — | — | — |
| JpegLs | `.jls` | ✅ | ✅ | — | — | — |
| JpegXl | `.jxl` | ✅ | — | — | — | — |
| JpegXr | `.jxr`, `.wdp`, `.hdp` | ✅ | ✅ | — | — | — |
| JupiterAce | `.jac`, `.ace` | ✅ | ✅ | — | — | — |
| Kitty | `.kty`, `.kt4` | ✅ | ✅ | — | — | — |
| Koala | `.koa`, `.koala`, `.kla` | ✅ | ✅ | — | — | — |
| KoalaCompressed | `.gg` | ✅ | ✅ | — | — | — |
| KodakDc25 | `.k25` | ✅ | ✅ | — | — | — |
| KofaxKfx | `.kfx` | ✅ | ✅ | — | — | — |
| Kqp | `.kqp` | ✅ | ✅ | — | — | — |
| Krita | `.kra` | ✅ | ✅ | — | — | — |
| KssPaint | `.kss` | ✅ | ✅ | — | — | — |
| Ktx | `.ktx`, `.ktx2` | ✅ | ✅ | — | — | — |
| LarkaObjectEditor | `.leo` | ✅ | ✅ | — | — | — |
| LaserData | `.lda` | ✅ | ✅ | — | — | — |
| LastWordFont | `.f80` | ✅ | ✅ | — | — | — |
| LdPic | `.bbg` | ✅ | ✅ | — | — | — |
| LightWorkImage | `.lwi` | ✅ | ✅ | — | — | — |
| LogoPainter | `.lp3` | ✅ | ✅ | — | — | — |
| LogoSys | `.sys`, `.logo` | ✅ | ✅ | — | — | — |
| Lss16 | `.lss`, `.16` | ✅ | ✅ | — | — | — |
| LucasFilm | `.lff` | ✅ | ✅ | — | — | — |
| LudekMaker | `.ldm` | ✅ | ✅ | — | — | — |
| LViewPro | `.lvp` | ✅ | ✅ | — | — | — |
| MacPaint | `.mac`, `.macp`, `.pntg`, `.pnt`, `.paint`, `.mpnt` | ✅ | ✅ | — | — | — |
| MadDesigner | `.mbg` | ✅ | ✅ | — | — | — |
| MadStudio | `.an4`, `.an2`, `.an5`, `.gr1`, `.gr2` | ✅ | ✅ | — | — | — |
| MadStudioMissile | `.msl` | ✅ | ✅ | — | — | — |
| MadStudioTile | `.tl4` | ✅ | ✅ | — | — | — |
| Mag | `.mag`, `.mki` | ✅ | ✅ | — | — | — |
| MagicPainter | `.mgp` | ✅ | ✅ | — | — | — |
| Mamut | `.rys` | ✅ | ✅ | — | — | — |
| MapletownMl1 | `.ml1` | ✅ | — | — | — | — |
| MapletownMx1 | `.mx1` | ✅ | ✅ | — | — | — |
| MapletownNl3 | `.nl3` | ✅ | ✅ | — | — | — |
| MasterSystemTile | `.sms`, `.gg` | ✅ | ✅ | — | — | — |
| MatLab | `.mat` | ✅ | ✅ | — | — | — |
| MawWareTexture | `.mtx` | ✅ | ✅ | — | — | — |
| MayaIff | `.iff`, `.maya`, `.tdi` | ✅ | ✅ | — | — | — |
| McPainter | `.mcp` | ✅ | ✅ | — | — | — |
| Mcs | `.mcs` | ✅ | ✅ | — | — | — |
| Mda | `.mda` | ✅ | ✅ | — | — | — |
| Mdp | `.mdp` | ✅ | ✅ | — | — | — |
| MegaluxFrame | `.frm` | ✅ | ✅ | — | — | — |
| MegaPaint | `.bld` | ✅ | ✅ | — | — | — |
| MetaImage | `.mha`, `.mhd` | ✅ | ✅ | — | — | — |
| MgrBitmap | `.mgr` | ✅ | ✅ | — | — | — |
| MicroDesignCut | `.cut` | ✅ | ✅ | — | — | — |
| MicroDesignGrf | `.grf` | ✅ | ✅ | — | — | — |
| MicroDynamicsMars | `.pbt` | ✅ | ✅ | — | — | — |
| MicroIllustrator | `.mil` | ✅ | ✅ | — | — | — |
| MicroIllustratorA8 | `.mia` | ✅ | ✅ | — | — | — |
| MicroPainter8 | `.mpt8`, `.mp8` | ✅ | ✅ | — | — | — |
| Miff | `.miff`, `.mif` | ✅ | ✅ | — | — | — |
| MiniPaint | `.mg` | ✅ | ✅ | — | — | — |
| Mlt | `.mlt` | ✅ | ✅ | — | — | — |
| Mng | `.mng` | ✅ | ✅ | — | ✅ | — |
| MobileFax | `.rfa` | ✅ | ✅ | — | — | — |
| MobyDick | `.mby`, `.mbd` | ✅ | ✅ | — | — | — |
| MonoMagic | `.mon` | ✅ | ✅ | — | — | — |
| MonoStar | `.obj` | ✅ | ✅ | — | — | — |
| MovieMakerBackground | `.bkg` | ✅ | ✅ | — | — | — |
| Mpo | `.mpo` | ✅ | ✅ | — | ✅ | — |
| Mrc | `.mrc`, `.map` | ✅ | ✅ | — | — | — |
| Mrf | `.mrf` | ✅ | ✅ | — | — | — |
| Mrw | `.mrw` | ✅ | — | — | — | — |
| Msp | `.msp` | ✅ | ✅ | — | — | — |
| Msx | `.sc2`, `.sc5`, `.sc7`, `.sc8`, `.ge7`, `.ge8` | ✅ | ✅ | — | — | — |
| MsxFont | `.fnt`, `.mft` | ✅ | ✅ | — | — | — |
| MsxGl16 | `.gl5`, `.sh5`, `.gl7`, `.sh7` | ✅ | ✅ | — | — | — |
| MsxGl6 | `.gl6`, `.sh6`, `.stp` | ✅ | ✅ | — | — | — |
| MsxGl8 | `.gl8`, `.sh8` | ✅ | ✅ | — | — | — |
| MsxGlYjk | `.glc`, `.gls`, `.shc`, `.gla`, `.glb`, `.sha`, `.shb` | ✅ | ✅ | — | — | — |
| MsxMig | `.mig` | ✅ | ✅ | — | — | — |
| MsxScc | `.scc`, `.yjk` | ✅ | ✅ | — | — | — |
| MsxScreen10 | `.sca`, `.scb` | ✅ | ✅ | — | — | — |
| MsxScreen2 | `.sc2`, `.grp` | ✅ | ✅ | — | — | — |
| MsxScreen3 | `.sc3` | ✅ | ✅ | — | — | — |
| MsxScreen4 | `.sc4` | ✅ | ✅ | — | — | — |
| MsxScreen5 | `.sc5`, `.ge5` | ✅ | ✅ | — | — | — |
| MsxScreen6 | `.sc6` | ✅ | ✅ | — | — | — |
| MsxScreen8 | `.sc8` | ✅ | ✅ | — | — | — |
| MsxSprite | `.spt` | ✅ | ✅ | — | — | — |
| MsxVideo | `.mvi` | ✅ | ✅ | — | — | — |
| MsxView | `.mvw`, `.msv` | ✅ | ✅ | — | — | — |
| Mtv | `.mtv`, `.pic` | ✅ | ✅ | — | — | — |
| MuifliEditor | `.muf`, `.mui`, `.mup` | ✅ | ✅ | — | — | — |
| MultiLaceEditor | `.mle` | ✅ | ✅ | — | — | — |
| MultiPainter | `.mpt`, `.mlt64` | ✅ | ✅ | — | — | — |
| MultiPalettePicture | `.mpp` | ✅ | ✅ | — | — | — |
| NcrImage | `.ncr` | ✅ | ✅ | — | — | — |
| NdsTexture | `.nbfs`, `.nds` | ✅ | ✅ | — | — | — |
| NeoBookCartoon | `.car` | ✅ | — | — | — | — |
| Neochrome | `.neo` | ✅ | ✅ | — | — | — |
| NeoGeoPocket | `.ngp`, `.ngpc` | ✅ | ✅ | — | — | — |
| NeoGeoSprite | `.spr` | ✅ | ✅ | — | — | — |
| NeroCoverDesigner | `.cde`, `.nct`, `.ncd` | ✅ | ✅ | — | — | — |
| NesChr | `.chr` | ✅ | ✅ | — | — | — |
| Netpbm | `.pbm`, `.pgm`, `.ppm`, `.pnm`, `.pam`, `.ppma`, `.rpbm`, `.rpgm`, `.rppm`, `.rpnm` | ✅ | ✅ | — | — | — |
| NewsRoom | `.nsr`, `.ph`, `.bn` | ✅ | ✅ | — | — | — |
| Nfo | `.nfo`, `.diz` | ✅ | ✅ | — | — | — |
| Nhdr | `.nhdr` | ✅ | ✅ | — | — | — |
| Nie | `.nie` | ✅ | ✅ | — | — | — |
| Nifti | `.nii` | ✅ | ✅ | — | — | — |
| Nifti2 | `.nii` | ✅ | ✅ | — | — | — |
| Nifti2Gzip | `.nii.gz` | ✅ | ✅ | — | — | — |
| NiftiGzip | `.nii.gz` | ✅ | ✅ | — | — | — |
| NiftiPair | `.hdr`, `.img` | ✅ | ✅ | — | — | — |
| NistIHead | `.nst` | ✅ | ✅ | — | — | — |
| Nitf | `.ntf`, `.nitf` | ✅ | ✅ | — | — | — |
| NokiaGroupGraphics | `.ngg` | ✅ | ✅ | — | — | — |
| NokiaLogo | `.nol`, `.ngg` | ✅ | ✅ | — | — | — |
| NokiaNlm | `.nlm` | ✅ | ✅ | — | — | — |
| NokiaOperatorLogo | `.nol` | ✅ | ✅ | — | — | — |
| NokiaPictureMessage | `.npm` | ✅ | ✅ | — | — | — |
| Nrrd | `.nrrd`, `.nhdr` | ✅ | ✅ | — | — | — |
| NufliEditor | `.nuf`, `.nup` | ✅ | ✅ | — | — | — |
| OazFax | `.oaz`, `.xfx` | ✅ | ✅ | — | — | — |
| OcpArtStudioWindow | `.win` | ✅ | ✅ | — | — | — |
| OcsPics | `.ocs` | ✅ | ✅ | — | — | — |
| OdFontEditor | `.odf` | ✅ | ✅ | — | — | — |
| Oil | `.oil` | ✅ | ✅ | — | — | — |
| OlicomFax | `.ofx` | ✅ | ✅ | — | — | — |
| Olpc565 | `.565` | ✅ | ✅ | — | — | — |
| OpenRaster | `.ora` | ✅ | ✅ | — | — | — |
| Optocat | `.abs` | ✅ | ✅ | — | — | — |
| Oric | `.oric`, `.tap` | ✅ | ✅ | — | — | — |
| Otb | `.otb` | ✅ | ✅ | — | — | — |
| PabloPaint | `.pa3` | ✅ | ✅ | — | — | — |
| Pagefox | `.pfx` | ✅ | ✅ | — | — | — |
| PaintMagic | `.pmg` | ✅ | ✅ | — | — | — |
| PaintPro | `.ppro` | ✅ | ✅ | — | — | — |
| PaintShop | `.da4` | ✅ | ✅ | — | — | — |
| PaintShopBrowser | `.jbf` | ✅ | ✅ | — | ✅ | — |
| PaintShopCompressed | `.psc` | ✅ | ✅ | — | — | — |
| Palm | `.palm`, `.pdb` | ✅ | ✅ | — | — | — |
| PalmImageViewer | `.pdb` | ✅ | ✅ | — | — | — |
| PalmPdb | `.pdb` | ✅ | ✅ | — | — | — |
| Paradox | `.mcpp` | ✅ | ✅ | — | — | — |
| Pat | `.pat` | ✅ | ✅ | — | — | — |
| Pc88 | `.pc8` | ✅ | ✅ | — | — | — |
| Pc98Ebd | `.ebd` | ✅ | ✅ | — | — | — |
| Pcd | `.pcd` | ✅ | ✅ | — | — | — |
| Pcds | `.pcds` | ✅ | ✅ | — | — | — |
| PcEngineTile | `.pce` | ✅ | ✅ | — | — | — |
| Pcl | `.pcl`, `.prn` | ✅ | ✅ | — | — | — |
| Pco16Bit | `.b16` | ✅ | ✅ | — | — | — |
| PcPaint | `.pic`, `.clp`, `.sim` | ✅ | ✅ | — | — | — |
| PcpBitmap | `.pcp` | ✅ | ✅ | — | — | — |
| Pcx | `.pcx`, `.pcc`, `.fcx`, `.bmg`, `.ibg` | ✅ | ✅ | — | — | ✅ |
| Pdf | `.pdf` | ✅ | ✅ | — | ✅ | — |
| Pdn | `.pdn` | ✅ | ✅ | — | — | — |
| Pds | `.pds`, `.lbl` | ✅ | ✅ | — | — | — |
| PeResource | `.exe`, `.dll`, `.ocx`, `.scr`, `.cpl` | ✅ | — | — | ✅ | — |
| PerfectPix | `.pph` | ✅ | ✅ | — | — | — |
| PetDraw | `.pdr` | ✅ | ✅ | — | — | — |
| PetsciiBot | `.pbot` | ✅ | ✅ | — | — | — |
| Pfm | `.pfm` | ✅ | ✅ | — | — | — |
| Pgx | `.pgx` | ✅ | ✅ | — | — | — |
| Phm | `.phm` | ✅ | ✅ | — | — | — |
| PhotoChrome | `.pcf`, `.phc` | ✅ | ✅ | — | — | — |
| PhotoChromePcs | `.pcs` | ✅ | ✅ | — | — | — |
| PhotoLine | `.pld` | ✅ | ✅ | — | — | — |
| PhotoPaint | `.cpt` | ✅ | ✅ | — | — | — |
| PhotoParade | `.php` | ✅ | ✅ | — | ✅ | — |
| PhotoStudio | `.psf` | ✅ | ✅ | — | — | — |
| PhotoSuiteProject | `.pzp` | ✅ | — | — | — | — |
| Pi | `.pi` | ✅ | ✅ | — | — | — |
| Pic2 | `.p2` | ✅ | ✅ | — | — | — |
| Picasso | `.pic0` | ✅ | ✅ | — | — | — |
| Picasso64 | `.p64`, `.fly` | ✅ | ✅ | — | — | — |
| Pict | `.pict`, `.pct`, `.pict2`, `.bum`, `.x` | ✅ | ✅ | — | — | — |
| PictureEditor | `.ped` | ✅ | ✅ | — | — | — |
| PicturePublisher | `.pp5` | ✅ | ✅ | — | — | — |
| PicturePublisher4 | `.pp4` | ✅ | ✅ | — | — | — |
| PicWorks | `.pwk`, `.pws` | ✅ | ✅ | — | — | — |
| PixarRib | `.pxr`, `.pixar`, `.picio` | ✅ | ✅ | — | — | — |
| Pixel64 | `.px64`, `.px` | ✅ | ✅ | — | — | — |
| PixelPerfect | `.pp`, `.ppp` | ✅ | ✅ | — | — | — |
| PixelPowerCollage | `.i17`, `.i18`, `.ib7`, `.if9` | ✅ | ✅ | — | — | — |
| Pixia | `.pxa`, `.pxs` | ✅ | ✅ | — | — | — |
| Pixibox | `.pxb` | ✅ | ✅ | — | — | — |
| Pkm | `.pkm` | ✅ | ✅ | — | — | — |
| Pl4Picture | `.pl4` | ✅ | ✅ | — | — | — |
| PlaybackBitmapSequence | `.bms` | ✅ | ✅ | — | — | — |
| PlotMaker | `.plt`, `.plm2` | ✅ | ✅ | — | — | — |
| PmBitmap | `.pm1`, `.pm2`, `.pm3`, `.pm4` | ✅ | ✅ | — | — | — |
| PmgDesigner | `.pmd` | ✅ | ✅ | — | — | — |
| PmView | `.pm` | ✅ | ✅ | — | — | — |
| Png | `.png`, `.frm` | ✅ | ✅ | — | — | ✅ |
| PntrFalcon | `.pnf`, `.pfl` | ✅ | ✅ | — | — | — |
| PocketPc2bp | `.2bp` | ✅ | ✅ | — | — | — |
| PocketPcTheme | `.tsk` | ✅ | — | — | — | — |
| PortfolioGraphics | `.pgf`, `.pgc` | ✅ | ✅ | — | — | — |
| Portrait | `.cvp` | ✅ | ✅ | — | — | — |
| PostScript | `.ps`, `.ps1`, `.ps2`, `.ps3`, `.eps`, `.epsf`, `.epsi`, `.epi`, `.prn`, `.pdx` | ✅ | ✅ | — | — | — |
| PowerGraphics | `.pgr` | ✅ | ✅ | — | — | — |
| PowerPoint | `.ppt`, `.pps` | ✅ | — | — | — | — |
| PrinterPageSegment | `.pse`, `.psg` | ✅ | ✅ | — | — | — |
| Printfox | `.gb` | ✅ | ✅ | — | — | — |
| PrintfoxPagefox | `.bs`, `.pg` | ✅ | ✅ | — | — | — |
| PrintMaster | `.pm` | ✅ | ✅ | — | — | — |
| PrintShop | `.psa`, `.psb` | ✅ | ✅ | — | — | — |
| PrintShopIcon | `.psf` | ✅ | ✅ | — | — | — |
| PrintTechnik | `.hir` | ✅ | ✅ | — | — | — |
| PrismPaint | `.pnt`, `.tpi` | ✅ | ✅ | — | — | — |
| Prisms | `.pri`, `.lff` | ✅ | ✅ | — | — | — |
| ProfiGrf | `.grf` | ✅ | ✅ | — | — | — |
| Ps2Txc | `.txc` | ✅ | ✅ | — | — | — |
| Psb | `.psb` | ✅ | ✅ | — | — | — |
| Psd | `.psd`, `.pdd` | ✅ | ✅ | — | — | — |
| PsionPic | `.pic`, `.icn`, `.ch3` | ✅ | ✅ | — | — | — |
| Psp | `.psp`, `.pspimage`, `.tub`, `.psptube`, `.pspbrush`, `.pspframe`, `.pfr`, `.pspmask`, `.msk`, `.pspt`, `.tex` | ✅ | ✅ | — | — | — |
| Ptif | `.ptif`, `.ptiff` | ✅ | ✅ | — | — | — |
| PublicPainter | `.cmp` | ✅ | ✅ | — | — | — |
| Pvr | `.pvr` | ✅ | ✅ | — | — | — |
| Q0 | `.q0` | ✅ | ✅ | — | — | — |
| QdvImage | `.qdv` | ✅ | ✅ | — | — | — |
| Qoi | `.qoi` | ✅ | ✅ | — | — | — |
| Qrt | `.qrt` | ✅ | ✅ | — | — | — |
| Qtif | `.qtif`, `.qti` | ✅ | ✅ | — | — | — |
| QuakeLmp | `.lmp` | ✅ | ✅ | — | — | — |
| QuakeSpr | `.spr` | ✅ | ✅ | — | — | — |
| QuantelVpb | `.vpb` | ✅ | ✅ | — | — | — |
| QuantumPaint | `.pbx` | ✅ | ✅ | — | — | — |
| RagD | `.rag`, `.ragc` | ✅ | ✅ | — | — | — |
| RagePaint | `.rge` | ✅ | ✅ | — | — | — |
| RainbowPainter | `.rp` | ✅ | ✅ | — | — | — |
| RamBrandt | `.rm0`, `.rm1`, `.rm2`, `.rm3`, `.rm4` | ✅ | ✅ | — | — | — |
| RawGreyscale | `.gry`, `.grey`, `.raw` | ✅ | ✅ | — | — | — |
| RawWorkshop | `.rwl`, `.rwh` | ✅ | ✅ | — | — | — |
| RedStormRsb | `.rsb` | ✅ | ✅ | — | — | — |
| Rembrandt | `.tcp` | ✅ | ✅ | — | — | — |
| Rgf | `.rgf` | ✅ | ✅ | — | — | — |
| RicohFax | `.ric`, `.001` | ✅ | ✅ | — | — | — |
| RicohIs30 | `.pig` | ✅ | ✅ | — | — | — |
| RicohJ6i | `.j6i` | ✅ | ✅ | — | — | — |
| RiscOsSprite | `.spr`, `.ros` | ✅ | ✅ | — | — | — |
| Rla | `.rla`, `.rlb`, `.rpf` | ✅ | ✅ | — | — | — |
| Rlc2 | `.rlc` | ✅ | ✅ | — | — | — |
| RockyInterlace | `.rip` | ✅ | ✅ | — | — | — |
| RunPaint | `.rpm` | ✅ | ✅ | — | — | — |
| SamarHiresMap | `.shc` | ✅ | ✅ | — | — | — |
| SamCoupe | `.sam` | ✅ | ✅ | — | — | — |
| SamCoupeLce | `.lce` | ✅ | ✅ | — | — | — |
| SamCoupeMode4 | `.ss4`, `.scs4` | ✅ | ✅ | — | — | — |
| SamCoupeScreen | `.ss1`, `.ss2`, `.ss3` | ✅ | ✅ | — | — | — |
| SamCoupeSsx | `.ssx` | ✅ | ✅ | — | — | — |
| SaracenPaint | `.sar` | ✅ | ✅ | — | — | — |
| SbigCcd | `.st4`, `.stx`, `.st5`, `.st6`, `.st7`, `.st8` | ✅ | ✅ | — | — | — |
| SciFax | `.scf` | ✅ | ✅ | — | — | — |
| ScitexCt | `.sct`, `.ct`, `.ch` | ✅ | ✅ | — | — | — |
| ScreenBlaster | `.sbl` | ✅ | ✅ | — | — | — |
| ScreenMaker | `.smk` | ✅ | ✅ | — | — | — |
| Sdt | `.sdt` | ✅ | ✅ | — | — | — |
| SeattleFilmWorks | `.sfw`, `.pwp` | ✅ | ✅ | — | — | — |
| SecondNatureSlideShow | `.cat` | ✅ | ✅ | — | ✅ | — |
| SecretPhotos | `.xp0` | ✅ | ✅ | — | — | — |
| SegaGenTile | `.gen`, `.sgd` | ✅ | ✅ | — | — | — |
| SegaSj1 | `.sj1` | ✅ | ✅ | — | — | — |
| SemiGraphicLogo | `.sge` | ✅ | ✅ | — | — | — |
| SeqImage | `.seq` | ✅ | ✅ | — | — | — |
| SeuckSprites | `.a` | ✅ | ✅ | — | — | — |
| SevenuP | `.sev` | ✅ | ✅ | — | — | — |
| Sf3 | `.sf3` | ✅ | ✅ | — | — | — |
| Sff | `.sff` | ✅ | ✅ | — | — | — |
| Sgi | `.sgi`, `.rgb`, `.bw`, `.iris`, `.rgba`, `.inta` | ✅ | ✅ | — | — | ✅ |
| ShapeTableFileType | `.shp` | ✅ | ✅ | — | — | — |
| SharpX68k | `.x68`, `.x68k` | ✅ | ✅ | — | — | — |
| ShfXlEdit | `.shx` | ✅ | ✅ | — | — | — |
| SiemensBmx | `.bmx` | ✅ | ✅ | — | — | — |
| SifImage | `.sif` | ✅ | ✅ | — | — | — |
| SinbadSlideshow | `.ssb` | ✅ | ✅ | — | — | — |
| SinclairBasic | `.p` | ✅ | ✅ | — | — | — |
| Sixel | `.six`, `.sixel` | ✅ | ✅ | — | — | — |
| Skantek | `.skn` | ✅ | ✅ | — | — | — |
| SketchPaddles | `.skp` | ✅ | ✅ | — | — | — |
| SmartFax | `.smf`, `.001` | ✅ | ✅ | — | — | — |
| SmartST | `.sst`, `.sst2` | ✅ | ✅ | — | — | — |
| SnesTile | `.sfc`, `.snes` | ✅ | ✅ | — | — | — |
| SoftImage | `.pic`, `.si` | ✅ | ✅ | — | — | — |
| SoftwareAutomation | `.sag`, `.swa` | ✅ | ✅ | — | — | — |
| SonyMavica | `.411` | ✅ | ✅ | — | — | — |
| SonyPmp | `.pmp` | ✅ | ✅ | — | — | — |
| SpcPainter | `.spp`, `.spc2` | ✅ | ✅ | — | — | — |
| SpeccyExtended | `.sxg` | ✅ | ✅ | — | — | — |
| SpecScii | `.zxs` | ✅ | ✅ | — | — | — |
| Spectrum512 | `.spu` | ✅ | ✅ | — | — | — |
| Spectrum512Comp | `.spc` | ✅ | ✅ | — | — | — |
| Spectrum512Ext | `.spx` | ✅ | ✅ | — | — | — |
| Spectrum512Smoosh | `.sps` | ✅ | — | — | — | — |
| SpeederFalcon | `.spf` | ✅ | ✅ | — | — | — |
| Spiff | `.spf`, `.spiff` | ✅ | ✅ | — | — | — |
| SpookySpritesFalcon | `.tre` | ✅ | ✅ | — | — | — |
| SpotImage | `.dat` | ✅ | ✅ | — | — | — |
| Sprite64 | `.s64`, `.spr64` | ✅ | ✅ | — | — | — |
| SpritePad | `.spd` | ✅ | ✅ | — | — | — |
| SriSun | `.ssi` | ✅ | ✅ | — | — | — |
| Stad | `.pac` | ✅ | ✅ | — | — | — |
| StarPainter | `.gr`, `.cs` | ✅ | ✅ | — | — | — |
| StarPainterFont | `.zs` | ✅ | ✅ | — | — | — |
| StelaRaw | `.hsi` | ✅ | ✅ | — | — | — |
| Stellar | `.stl` | ✅ | ✅ | — | — | — |
| StTrueColor | `.stc` | ✅ | ✅ | — | — | — |
| SunIcon | `.icon`, `.pr` | ✅ | ✅ | — | — | — |
| SunRaster | `.ras`, `.sun`, `.rast`, `.rs`, `.sr` | ✅ | ✅ | — | — | — |
| SuperHires | `.shi` | ✅ | ✅ | — | — | — |
| SuperHiresEditor | `.she` | ✅ | ✅ | — | — | — |
| SuperHiresEditor1 | `.sh1` | ✅ | ✅ | — | — | — |
| SuperHiresEditor2 | `.sh2` | ✅ | ✅ | — | — | — |
| SuperHiresFli | `.shf` | ✅ | ✅ | — | — | — |
| SuperHiresStudio | `.shs` | ✅ | ✅ | — | — | — |
| Svg | `.svg` | ✅ | ✅ | — | — | — |
| Svgz | `.svgz` | ✅ | ✅ | — | — | — |
| SyberiaTexture | `.syj` | ✅ | ✅ | — | — | — |
| SymbianMbm | `.mbm` | ✅ | ✅ | — | — | — |
| SymbOsGraphic | `.sgx` | ✅ | ✅ | — | — | — |
| SyntheticArts | `.srt` | ✅ | ✅ | — | — | — |
| Synu | `.synu`, `.syn` | ✅ | ✅ | — | — | — |
| Taac | `.vff`, `.taac`, `.suniff` | ✅ | ✅ | — | — | — |
| TaquartInterlace | `.tip` | ✅ | ✅ | — | — | — |
| TechnicolorDream | `.lum` | ✅ | ✅ | — | — | — |
| TeliFax | `.mh` | ✅ | ✅ | — | — | — |
| TextureEditorMikey | `.txe` | ✅ | ✅ | — | — | — |
| TextureMaker0 | `.tx0` | ✅ | ✅ | — | — | — |
| Tg4 | `.tg4` | ✅ | ✅ | — | — | — |
| Tga | `.tga`, `.vda`, `.icb`, `.vst`, `.bpx`, `.targa`, `.ivb` | ✅ | ✅ | — | — | ✅ |
| Thomson | `.map` | ✅ | ✅ | — | — | — |
| TiBitmap | `.8xi`, `.89i` | ✅ | ✅ | — | — | — |
| Tiff | `.tif`, `.tiff`, `.ftf`, `.stw`, `.fx3`, `.xif`, `.ctf` | ✅ | ✅ | — | ✅ | ✅ |
| TilePic | `.tjp` | ✅ | ✅ | — | — | — |
| TilezTexture | `.til` | ✅ | ✅ | — | — | — |
| Tim | `.tim` | ✅ | ✅ | — | — | — |
| Tim2 | `.tm2` | ✅ | ✅ | — | — | — |
| TimexGigascreen | `.hrg`, `.scr` | ✅ | ✅ | — | — | — |
| Tiny | `.tny`, `.tn1`, `.tn2`, `.tn3`, `.tn4`, `.tn5`, `.tn6` | ✅ | ✅ | — | — | — |
| TiPicture | `.73i`, `.82i`, `.83i`, `.85i`, `.86i` | ✅ | ✅ | — | — | — |
| TmSat | `.imi` | ✅ | ✅ | — | — | — |
| TobiasRichterSlideshow | `.pci` | ✅ | ✅ | — | — | — |
| TriPaint | `.tpf` | ✅ | ✅ | — | — | — |
| Trs80 | `.hr` | ✅ | ✅ | — | — | — |
| TrsPix | `.pix` | ✅ | ✅ | — | — | — |
| TrueColorImg | `.timg` | ✅ | ✅ | — | — | — |
| TruePaint | `.mci` | ✅ | ✅ | — | — | — |
| TrueType | `.ttf` | ✅ | — | — | — | — |
| TrzmielCompressed | `.cpr` | ✅ | ✅ | — | — | — |
| TurboRascal | `.flf` | ✅ | ✅ | — | — | — |
| TurboView | `.tvw`, `.tbv` | ✅ | ✅ | — | — | — |
| UfliEditor | `.ufl` | ✅ | ✅ | — | — | — |
| Uhdr | `.uhdr` | ✅ | ✅ | — | — | — |
| UifliEditor | `.uif` | ✅ | ✅ | — | — | — |
| Uimg | `.bp1`, `.bp2`, `.bp4`, `.bp6`, `.bp8`, `.c01`, `.c02`, `.c04`, `.c06`, `.c08`, `.c16`, `.c24`, `.c32` | ✅ | ✅ | — | — | — |
| UleadAlbumTemplate | `.pe4` | ✅ | ✅ | — | ✅ | — |
| UleadImageLibrary | `.pst` | ✅ | ✅ | — | ✅ | — |
| UtahRle | `.rle`, `.urt` | ✅ | ✅ | — | — | — |
| UyvyRaw | `.uyvy`, `.qtl` | ✅ | ✅ | — | — | — |
| VbxeSlideShow | `.dap` | ✅ | ✅ | — | — | — |
| VdcBitmap | `.vbm`, `.bm` | ✅ | ✅ | — | — | — |
| Vector06c | `.v06`, `.scr` | ✅ | ✅ | — | — | — |
| VentaFax | `.vfx` | ✅ | ✅ | — | — | — |
| VerticalHiresInterlace | `.vhi` | ✅ | ✅ | — | — | — |
| VertiZontalInterlacing | `.vzi` | ✅ | ✅ | — | — | — |
| Vic20 | `.vic20`, `.prg` | ✅ | ✅ | — | — | — |
| Vicar | `.vic`, `.vicar`, `.img` | ✅ | ✅ | — | — | — |
| Vidcom64 | `.vid` | ✅ | ✅ | — | — | — |
| VidiChrome | `.vdc`, `.vdc2` | ✅ | ✅ | — | — | — |
| VidigPaint | `.rap` | ✅ | ✅ | — | — | — |
| Viff | `.viff`, `.xv`, `.vif` | ✅ | ✅ | — | — | — |
| Vips | `.v`, `.vips` | ✅ | ✅ | — | — | — |
| VirtualBoyTile | `.vbt`, `.vb`, `.vboy` | ✅ | ✅ | — | — | — |
| Vitec | `.vit` | ✅ | ✅ | — | — | — |
| Vivid | `.vivid`, `.dis` | ✅ | ✅ | — | — | — |
| Vrml | `.wrl`, `.vrml` | ✅ | ✅ | — | — | — |
| Vtf | `.vtf` | ✅ | ✅ | — | — | — |
| Vue | `.vob` | ✅ | ✅ | — | — | — |
| Wad2 | `.wad` | ✅ | ✅ | — | — | — |
| Wad3 | `.wad` | ✅ | ✅ | — | — | — |
| Wal | `.wal` | ✅ | ✅ | — | — | — |
| Wbmp | `.wbmp`, `.wbm`, `.wap` | ✅ | ✅ | — | — | — |
| WebP | `.webp`, `.wep` | ✅ | ✅ | — | ✅ | ✅ |
| WebShots | `.wb1`, `.wbc`, `.wbp`, `.wbz` | ✅ | ✅ | — | — | — |
| WigmoreArtist | `.wig` | ✅ | ✅ | — | — | — |
| WinFax | `.fxs`, `.fxo`, `.fxr`, `.fxd`, `.fxm` | ✅ | ✅ | — | — | — |
| WizSolitaireDeck | `.dec` | ✅ | ✅ | — | — | — |
| Wmf | `.wmf` | ✅ | ✅ | — | — | — |
| WonderSwanTile | `.wst`, `.ws` | ✅ | ✅ | — | — | — |
| WorldportFax | `.wpf`, `.wfx` | ✅ | ✅ | — | — | — |
| Wpg | `.wpg` | ✅ | ✅ | — | — | — |
| Wsq | `.wsq` | ✅ | ✅ | — | — | — |
| Wzl | `.wzl` | ✅ | ✅ | — | — | — |
| X11Puzzle | `.pzl` | ✅ | ✅ | — | — | — |
| X3f | `.x3f` | ✅ | — | — | — | — |
| Xar | `.xar` | ✅ | ✅ | — | — | — |
| XBin | `.xb`, `.xbin` | ✅ | ✅ | — | — | — |
| Xbm | `.xbm`, `.icon`, `.ico`, `.cbm`, `.x` | ✅ | ✅ | — | — | — |
| XbmColor | `.xbm` | ✅ | ✅ | — | — | — |
| Xcf | `.xcf` | ✅ | ✅ | — | — | — |
| Xcursor | `.xcur`, `.cursor` | ✅ | ✅ | — | — | — |
| XFliEditor | `.xfl` | ✅ | ✅ | — | — | — |
| Ximage | `.xim` | ✅ | ✅ | — | — | — |
| XionicsSmp | `.smp` | ✅ | ✅ | — | — | — |
| Xld4 | `.q4` | ✅ | — | — | — | — |
| XlPaint | `.xlp` | ✅ | ✅ | — | — | — |
| Xpm | `.xpm`, `.picon` | ✅ | ✅ | — | — | — |
| XvThumbnail | `.xv`, `.p7` | ✅ | ✅ | — | — | — |
| Xwd | `.xwd`, `.x11` | ✅ | ✅ | — | — | — |
| Xyz | `.xyz` | ✅ | ✅ | — | — | — |
| Ybm | `.ybm` | ✅ | ✅ | — | — | — |
| YuvRaw | `.yuv` | ✅ | ✅ | — | — | — |
| ZeissBivas | `.dta` | ✅ | ✅ | — | — | — |
| ZeissLsm | `.lsm` | ✅ | ✅ | — | — | — |
| Zinc | `.zinc` | ✅ | ✅ | — | — | — |
| ZonerBrush | `.zbr` | ✅ | ✅ | — | — | — |
| Zoom4 | `.zm4` | ✅ | ✅ | — | — | — |
| Zoomatic | `.zom` | ✅ | ✅ | — | — | — |
| ZsStaffKid98 | `.zim` | ✅ | ✅ | — | — | — |
| Zx81 | `.zx81`, `.p81` | ✅ | ✅ | — | — | — |
| ZxArtStudio | `.zas` | ✅ | ✅ | — | — | — |
| ZxAttributes | `.atr` | ✅ | ✅ | — | — | — |
| ZxAttributesGigascreen | `.hlr` | ✅ | ✅ | — | — | — |
| ZxBigFont | `.chx` | ✅ | ✅ | — | — | — |
| ZxBorderMulticolor | `.bmc4` | ✅ | ✅ | — | — | — |
| ZxBorderScreen | `.bsc` | ✅ | ✅ | — | — | — |
| ZxChrd | `.chr`, `.chrd` | ✅ | ✅ | — | — | — |
| ZxFlash | `.zfl` | ✅ | ✅ | — | — | — |
| ZxFont | `.ch8`, `.ch4`, `.ch6` | ✅ | ✅ | — | — | — |
| ZxGigascreen | `.gsc`, `.img` | ✅ | ✅ | — | — | — |
| ZxMlg | `.mlg` | ✅ | ✅ | — | — | — |
| ZxMultiArtist | `.mg1`, `.mg2`, `.mg4`, `.mg8` | ✅ | ✅ | — | — | — |
| ZxMulticolor | `.mlt`, `.mc` | ✅ | ✅ | — | — | — |
| ZxNext | `.nxt` | ✅ | ✅ | — | — | — |
| ZxNextImage | `.nxi` | ✅ | ✅ | — | — | — |
| ZxPaintbrush | `.zxp` | ✅ | ✅ | — | — | — |
| ZxPaintyOne | `.zp1` | ✅ | ✅ | — | — | — |
| ZxRgb3 | `.3` | ✅ | ✅ | — | — | — |
| ZxSnapshot | `.sna` | ✅ | ✅ | — | — | — |
| ZxSpectrum | `.scr`, `.$s`, `.$c`, `.!s` | ✅ | ✅ | — | — | — |
| ZxTimex | `.tmx`, `.scr` | ✅ | ✅ | — | — | — |
| ZxTrefiBorderScreen | `.bsp` | ✅ | ✅ | — | — | — |
| ZxTricolor | `.3cl` | ✅ | ✅ | — | — | — |
| ZxUlaPlus | `.ulp`, `.scr` | ✅ | ✅ | — | — | — |
| ZzRough | `.rgh` | ✅ | ✅ | — | — | — |
<!-- IMAGE-FORMATS:END -->

### Optimizers

The optimizers live beside the package in the same repository ([`Optimizers/`](https://github.com/Hawkynt/PNGCrushCS/tree/main/Optimizers)) and drive the [`Crush.Image`](https://github.com/Hawkynt/PNGCrushCS/tree/main/Crush.Image) CLI. Every one of them is lossless by contract: the pixels that come out are the pixels that went in, and only the representation — compression, filter, palette order, container layout, metadata — changes. The smallest candidate wins; a candidate that would change a pixel is not a candidate.

| Format | In place | What it searches | What it strips | Limits |
| --- | :---: | --- | --- | --- |
| PNG | pixels re-encoded, ancillary chunks carried over from the original bytes on request | colour type and bit depth valid for the picture, palette order, Adam7 against progressive, five row filters through single, scanline-adaptive, weighted-continuity and partition-aware strategies, DEFLATE at default, maximum and Zopfli-class Ultra/Hyper with two-phase screening | ancillary chunks unless `PreserveAncillaryChunks` | 8-bit pipeline: 16-bit sources are reduced only when every sample fits; palette *reduction* is an explicit lossy opt-in (`AllowLossyPalette`) and off by default |
| APNG | untouched | — | — | detected as its own format so the PNG optimizer never flattens an animation to its first frame; per-frame optimisation is not attempted |
| GIF | rewritten from the parsed `GifFile` | palette order (original, frequency, luminance, LZW-run aware), global against local colour tables, frame disposal, transparent-margin trimming, frame deduplication, frame differencing, standard against deferred-clear LZW | comment and application extensions except the Netscape loop | optimises the palette a file already has; never quantises |
| TIFF | rewritten from the decoded page | none, PackBits, LZW, DEFLATE and Zopfli-class DEFLATE, horizontal-differencing predictor, original/grey/palette colour mode, rows per strip, optional tiles | private tags not needed to decode | first page only; alpha is carried as an unassociated extra sample and 16-bit sources keep their depth |
| BMP | rewritten | 32/24/16/8/4/1-bit layouts the picture fits without loss, RLE8/RLE4 | gap and padding bytes | 16-bit 5/6/5 is tried only when every pixel survives it exactly |
| TGA | rewritten | 32/24/8-bit and palette layouts, RLE against raw | developer and extension areas | 3-byte candidates only when the alpha channel is fully opaque |
| PCX | rewritten | 8-bit palette, 24-bit planar, 1-bit; RLE | none | — |
| SGI | rewritten | RLE against verbatim rows, 8-bit against 16-bit only where the samples fit | none | image name is kept |
| JPEG | marker segments rewritten, entropy-coded data untouched | Huffman table optimisation, progressive against baseline scan scripts, restart-interval choice | APPn/COM metadata on request (`--strip`) | never re-quantises; arithmetic-coded and lossless JPEG are passed through |
| WebP | chunks rewritten, VP8/VP8L payload untouched | RIFF layout with and without VP8X | EXIF/XMP/ICCP on request | alpha (`ALPH`) and animation (`ANIM`/`ANMF`) are carried through verbatim; the codec payload is never re-encoded |
| ICO / CUR | directory rewritten, entries re-encoded | BMP against PNG storage per entry, bit depth per entry | none | one directory; the hotspot of a cursor is kept |
| ANI | RIFF rewritten | per-frame cursor optimisation through the CUR optimizer | `INFO` list on request | frame timing and sequence chunks are kept |

Formats not in this table are optimised by conversion only: `Crush.Image auto` writes the picture through every format that can encode it and keeps the smallest, which is a change of format rather than an optimisation of one. Formats with nothing to optimise are left alone on purpose — QOI has one encoding and no metadata; Farbfeld and the Netpbm family are raw samples behind a header.

### Conformance evidence for the modern codecs

For these formats `✅` means more than "the project can read what it wrote". A capability is promoted only with evidence beyond a self-round-trip: normative bitstream assertions for deterministic syntax, decoding files an independent implementation produced, an independent decoder accepting our output, or a pixel comparison against an independent decoder (exact for lossless, a justified error bound for lossy). Native codec bindings are never used to manufacture a green cell; the package stays managed code.

| Format | Read | Write | Evidence and exact scope |
| --- | :---: | :---: | --- |
| WebP | ✅ | ✅ | VP8 lossy decode is bit-exact with `dwebp -nofancy` and, with the libwebp fancy chroma upsampler now the default, matches `dwebp` within ±1; VP8L lossless is exact. Animated read/write emits `VP8X`/`ANIM`/`ANMF` with offsets, durations, blend and disposal, verified frame by frame. Writer: keyframe-only VP8, no multi-pass rate control; alpha written as an uncompressed `ALPH`. |
| MNG | ✅ | ✅ | Writer is MNG-VLC: `MHDR`/`TERM` wire values, VLC layer/frame/play-time accounting and a truthful simplicity profile are asserted byte for byte. Full MNG-LC/MNG object buffers, loops, JNG and delta-PNG are not written and not claimed. |
| JPEG XR | ✅ | ✅ | T.832 core is a managed port of JXRLib (see `Formats/JpegXr/Reference/UPSTREAM.md`), oracle-tested upstream against `JxrEncApp`/`JxrDecApp`/WIC; the public path writes real `WMPHOTO` codestreams with standard WIC pixel-format GUIDs and decodes the independent JXRLib `red.jxr` fixture (frequency-order YUV444 plus a planar alpha plane). Gray8, RGB24 and RGBA32 are exposed; other WIC layouts may be refused at the container adapter even though the core handles them. |
| JPEG 2000 | ⚠️ | ⚠️ | Reader: our own codestreams round-trip, but every JPEG 2000 written by OpenJPEG or ffmpeg decodes to a flat mid-grey field — the Tier-2/Tier-1 path agrees with our writer and with nothing else, so the reader is not interoperable and this cell stays amber until it decodes independent codestreams. Writer: 8-bit Gray/RGB reversible 5/3 baseline; the Tier-2 packet headers serialise inclusion and zero-bit-plane counts as plain values where the standard requires tag trees, so independent decoders do not accept the output. |
| HEIF / HEIC | ⚠️ | ⚠️ | Directly coded HEVC items decode through the shared managed H.265 decoder for Main-profile intra 8-bit 4:2:0; 10- and 12-bit streams (what `libheif`/x265 write by default) are refused with the clause that says why rather than decoded wrongly. Writer: a managed HEVC encoder is registered and builds the picture out of 64x64 intra PCM coding units, which is ordinary Main-profile syntax rather than a private escape — every sample is carried verbatim, so the file is large and exactly lossless. It is amber, not green, because the evidence for it is our own decoder reading it back; no independent HEVC decoder has been run against the output here. |
| AVIF | ⚠️ | — | The ISO-BMFF container parses (size and item layout come back), but the AV1 decoder under `Formats/Avif/Codec` lacks the context-adaptive CDF machinery and would return a uniform field, so an AV1 payload is refused. The raw-`mdat` writer is deliberately not registered. |
| JPEG XL | ⚠️ | — | Container, `SizeHeader`, `ImageMetadata` and `FrameHeader` are spec-conformant, so signature, dimensions and image-level metadata of real `.jxl` files are correct. The modular and VarDCT pixel codecs are not ISO/IEC 18181 interoperable: real files refuse their pixels, and the writer is unregistered because `djxl` rejects what it emits. |

### Measured against other decoders

The claims above are checked, not asserted, by the parity tooling under [`Tools/parity/`](https://github.com/Hawkynt/PNGCrushCS/tree/main/Tools/parity): every format a third-party tool can write is written by that tool, kept only if the tool reads its own file back, and then decoded here and compared pixel for pixel against the tool's decode. As of this audit, against ImageMagick 7.1.2 (135 formats it writes and reads back) and ffmpeg (27): we open 116 and 26 of them. Of what opens, every sample decodes to the reference picture except the JPEG 2000 family (flat grey, see above), CALS Type I, JBIG1 and Palm pixmaps (wrong pixels — under repair), and one 16-bit RLE SGI variant. The refusals are the codec gaps tabled above (HEIC at 12 bits, AVIF, JPEG XL), ImageMagick's own scratch formats, video containers, and Aseprite, which is not yet registered. Lossy WebP from either tool is bit-exact with `dwebp -nofancy`; the difference to the default `dwebp` output is libwebp's fancy chroma upsampler alone.

Formats no installed tool writes are judged the other way round — what we write is handed to RECOIL, ImageMagick, XnView and IrfanView (whichever are present) and must be accepted; `WriterAcceptanceTests` walks the whole registry so a new writer is covered the day it appears. A format nothing else knows is recorded as unverifiable rather than counted as a pass.

### Registered but read-only

These 35 entries read but have no writer; each is a decision, not an oversight. **Not bounded** (an encoder or a model this package does not have): Avif (AV1), Crw, Mrw, X3f (camera-raw sensor models), Dwg, Dxf, Hpgl (CAD/vector), TrueType (font), PeResource, PowerPoint, Fpx, PocketPcTheme (executable, OLE and CAB containers), IffAnim8, IffDpan, IffHame, IffDctv (multi-frame or hardware-mode Amiga animation), IffSham, IffMultiPalette (per-line palette encoders whose identity is unverified), Spectrum512Smoosh (its packing is not specified), Graph2FontScroll, Xld4, EciGraphicEditor, FunPainter, Gem, IconLibrary, PhotoSuiteProject (compressed or container layouts read from one sample each). **Deliberately not written** because a file built from arbitrary pixels would not be what the name promises: EmbeddedDib, Eroiica, Crd, NeoBookCartoon, CartesMichelin. **Written but not registered**: JpegXl — an encoder exists and its codec tests drive it, but what it assembles is a `0x4D`-prefixed payload behind a bare component-count byte rather than the ImageMetadata bundle ISO/IEC 18181-1 puts after the `SizeHeader`. `djxl` will not decode it and neither will the reader in the same folder, so it is not reachable through the registry. **Bounded and not yet written**: MapletownMl1, ElectricImage, HalfLifeModel — simple raster layouts with specified headers; nothing blocks an encoder for them beyond the work, and none is written today.
## 🚀 Quick start

```csharp
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

var input = new FileInfo("mystery.bin");
var format = FormatRegistry.DetectFromFile(input);
var raw = FormatRegistry.Read(input);
var png = FormatRegistry.Write(raw!, ImageFormat.Png);
File.WriteAllBytes("out.png", png!);
```

### Detect from different inputs

```csharp
ImageFormat fromExtension = FormatRegistry.DetectFromExtension(".webp");
ImageFormat fromMime = FormatRegistry.DetectFromMimeType("image/png");
ImageFormat fromBytes = FormatRegistry.DetectFromBytes(headerBuffer);

using var stream = File.OpenRead("photo.bin");
ImageFormat fromStream = FormatRegistry.DetectFromStream(stream);

var (detected, replay) = FormatRegistry.DetectFromStreamRewound(networkStream);
RawImage? image = FormatRegistry.Read(replay);
```

### Read and normalize any image

```csharp
RawImage? image = FormatRegistry.Read(new FileInfo("anything.tga"));
if (image != null) {
  Console.WriteLine($"{image.Width}x{image.Height} {image.Format} HasAlpha={image.HasAlpha}");
  byte[] rgba = image.ToRgba32();
}
```

### Encode a `RawImage`

```csharp
var raw = new RawImage {
  Width = 256,
  Height = 256,
  Format = PixelFormat.Rgba32,
  PixelData = pixelBytes,
};

byte[]? png = FormatRegistry.Write(raw, ImageFormat.Png);
byte[]? webp = FormatRegistry.Write(raw, ImageFormat.WebP);
byte[]? qoi = FormatRegistry.Write(raw, ImageFormat.Qoi);

using var output = File.Create("out.bmp");
bool ok = FormatRegistry.Write(raw, ImageFormat.Bmp, output);
```

### Look up extensions and MIME types

```csharp
string ext = FormatRegistry.PrimaryExtension(ImageFormat.Jpeg);
var aliases = FormatRegistry.AllExtensions(ImageFormat.Jpeg);
string mime = FormatRegistry.PrimaryMimeType(ImageFormat.WebP);
var mimes = FormatRegistry.AllMimeTypes(ImageFormat.Png);
```

### Inspect and filter capabilities

```csharp
var roundTrippable = FormatRegistry.AllFormats
  .Where(e => e.SupportsRead && e.SupportsWrite)
  .OrderBy(e => e.Name);

var multiImage = FormatRegistry.AllFormats.Where(e => e.SupportsMultiImage);
var mimed = FormatRegistry.AllFormats.Where(e => e.MimeTypes.Length > 0);
```

### Cross-format conversion

```csharp
File.WriteAllBytes(
  "out.png",
  FormatRegistry.Write(FormatRegistry.Read(new FileInfo("in.tga"))!, ImageFormat.Png)!);
```

### Metadata without decoding pixels

```csharp
var entry = FormatRegistry.GetEntry(ImageFormat.Jpeg);
ImageInfo? info = entry?.ReadImageInfo?.Invoke(File.ReadAllBytes("photo.jpg"));
if (info is { } meta)
  Console.WriteLine($"{meta.Width}x{meta.Height} @ {meta.BitsPerPixel}bpp ({meta.ColorMode})");
```

`ReadImageInfo` is `null` when a format has no fast metadata-only path; callers can fall back to `Read`.

### Multi-image formats

```csharp
var entry = FormatRegistry.GetEntry(ImageFormat.Tiff);
if (entry?.SupportsMultiImage == true) {
  int pages = entry.GetImageCount!(new FileInfo("multi.tif"));
  for (var i = 0; i < pages; ++i) {
    RawImage? page = entry.LoadRawImageAtIndex!(new FileInfo("multi.tif"), i);
    // ...
  }

  var allPages = entry.LoadAllRawImages!(new FileInfo("multi.tif"));
}
```

## 🧭 Key types at a glance

### `FormatRegistry`

| Member | Purpose |
| --- | --- |
| `DetectFromExtension(string)` | Map an extension to `ImageFormat`. |
| `DetectFromMimeType(string)` | Map MIME type or alias to `ImageFormat`. |
| `DetectFromBytes(ReadOnlySpan<byte>)` | Detect from magic bytes/custom signature logic. |
| `DetectFromStream(Stream, int)` | Detect while restoring seekable-stream position. |
| `DetectFromStreamRewound(Stream, int)` | Detect and return a replayable stream for non-seekable inputs. |
| `DetectFromFile(FileInfo)` | Magic detection with extension fallback. |
| `Read(FileInfo / byte[] / Stream)` | Detect and decode to `RawImage`. |
| `Write(RawImage, ImageFormat)` | Encode to bytes when a writer exists. |
| `Write(RawImage, ImageFormat, Stream)` | Encode directly to a stream. |
| `GetEntry(ImageFormat)` | Get extensions, MIME types, signatures and capabilities. |
| `PrimaryExtension` / `AllExtensions` | Canonical extension and aliases. |
| `PrimaryMimeType` / `AllMimeTypes` | Preferred MIME type and aliases. |
| `AllFormats` | Enumerate every registered format. |
| `SupportedReadFormats` / `SupportedWriteFormats` | Enumerate by capability. |

### `FormatEntry`

The source generator produces typed registry entries rather than using runtime reflection. The public record carries the format identity, names/extensions/MIME types, capabilities/signatures/detection priority, read/write delegates, optional metadata reader, and optional multi-image delegates.

Key computed properties are:

| Property | Meaning |
| --- | --- |
| `PrimaryMimeType` | First registered MIME type, otherwise `application/octet-stream`. |
| `SupportsRead` | A reader is registered. |
| `SupportsWrite` | A conversion from arbitrary `RawImage` is registered. |
| `SupportsMultiImage` | Image count/index/all-image delegates are registered. |

### `MagicSignature`

```csharp
public readonly record struct MagicSignature(
  byte[] Signature,
  int Offset,
  int MinHeaderLength);
```

Signatures are emitted from `[FormatMagicBytes(...)]`; `MinHeaderLength` prevents matchers from reading beyond the supplied header.

### `ImageFormat`

`ImageFormat` is generated at compile time. `Unknown = 0`; every discovered image format contributes another member. Consumers should not copy a hand-maintained enum list into application code—use the package's generated enum/registry.

### `RawImage`

```csharp
public sealed class RawImage {
  public required int Width { get; init; }
  public required int Height { get; init; }
  public required PixelFormat Format { get; init; }
  public required byte[] PixelData { get; init; }

  public byte[]? Palette { get; init; }
  public int PaletteCount { get; init; }
  public byte[]? AlphaTable { get; init; }

  public bool IsIndexed { get; }
  public bool HasAlpha { get; }

  public byte[] ToBgra32();
  public byte[] ToRgba32();
  public byte[] ToRgb24();

  public static int BytesPerPixel(PixelFormat format);
  public static int BitsPerPixel(PixelFormat format);
}
```

### `PixelFormat`

| Value | Layout | Bits |
| --- | --- | ---: |
| `Bgra32` | B, G, R, A | 32 |
| `Rgba32` | R, G, B, A | 32 |
| `Argb32` | A, R, G, B | 32 |
| `Rgb24` | R, G, B | 24 |
| `Bgr24` | B, G, R | 24 |
| `Gray8` | grayscale | 8 |
| `Gray16` | 16-bit grayscale | 16 |
| `GrayAlpha16` | grayscale + alpha | 16 |
| `Indexed8` | palette index | 8 |
| `Indexed4` | packed palette index | 4 |
| `Indexed1` | packed palette index | 1 |
| `Rgba64` | 16-bit R/G/B/A | 64 |
| `Rgb48` | 16-bit R/G/B | 48 |
| `Rgb565` | 5/6/5 RGB | 16 |

### `FormatCapability`

`FormatCapability` contains format-level flags such as `HasDedicatedOptimizer` and `MultiImage`. Per-format geometry/palette/display restrictions are modeled separately through `VideoMode` rather than being flattened into capability bits.

### `VideoMode`

```csharp
public sealed record VideoMode(
  string Name,
  (IntegerRange Width, IntegerRange Height)[] Dimensions,
  IntegerRange[]? AllowedPaletteRanges = null,
  FixedPalette[]? AvailablePalettes = null,
  PixelAspectRatio? PixelAspectRatio = null,
  DisplayFilter DisplayFilter = DisplayFilter.None,
  string? Description = null);
```

A format declares its selectable modes through `IImageFormatMetadata<TSelf>.VideoModes`. Multiple resolutions sharing one palette profile stay in one mode; palette variants for the same dimensions belong in `AvailablePalettes`.

Representative declarations include:

```csharp
// Arbitrary-resolution full colour.
static VideoMode[] VideoModes => [
  new("Default", [(IntegerRange.Any, IntegerRange.Any)])
];

// Atari ST NeoChrome.
static VideoMode[] VideoModes => [
  new("Low resolution", [(320, 200)], [16]),
  new("Medium resolution", [(640, 200)], [4]),
  new("High resolution", [(640, 400)], [2]),
];

// CGA palette variants stay attached to the same geometry/profile.
static VideoMode[] VideoModes => [
  new("4-colour", [(320, 200)], [4],
      [_LowIntensity0, _HighIntensity0, _LowIntensity1, _HighIntensity1]),
  new("Monochrome", [(640, 200)], [2], [_MonochromePalette]),
];

// NES CHR can additionally describe pixel aspect and display filtering.
static VideoMode[] VideoModes => [
  new("Tilesheet (2bpp)",
      [(128, new IntegerRange(8, 8192, step: 8))],
      [new IntegerRange(2, 4)],
      [_NesMaster64],
      (8, 7),
      DisplayFilter.NtscComposite),
];
```

### `ImageInfo`

```csharp
public readonly record struct ImageInfo(
  int Width,
  int Height,
  int BitsPerPixel,
  string? ColorMode = null,
  string? Compression = null,
  int FrameCount = 1);
```

It is the lightweight metadata result for formats that can inspect dimensions/properties without a full pixel decode.

## 🏗️ Registry and detection architecture

`FileFormat.Registry.Generator` scans the compilation and referenced format assemblies at build time for the static image contracts and emits the `ImageFormat` enum plus registration code wired directly to the concrete static methods. There is no runtime reflection step.

Detection runs priority-ordered custom `MatchesSignature(ReadOnlySpan<byte>)` logic for formats that need more than fixed magic bytes, then the generated magic-signature table. `DetectFromFile` falls back to extension only after byte-level detection fails.

MIME types come from `[FormatMimeType(...)]`. The first annotation is the primary MIME type and later values are aliases; formats without a MIME annotation simply expose no registered MIME values rather than receiving guessed ones.

## 📚 Extended format-family reference

The exact current Read/Write/Multi-image state for every individual entry remains `FormatRegistry.AllFormats`. The tables here preserve the package's long-form inventory without freezing old capability counts or resurrecting obsolete states.

### Lossless / scientific / HDR long tail

| Family / format | Coverage note | Reference |
| --- | --- | --- |
| Farbfeld | simple 16-bit-per-channel lossless raster | [farbfeld](https://tools.suckless.org/farbfeld/) |
| Netpbm PBM/PGM/PPM/PAM/P7 | portable bitmap/graymap/pixmap/anymap family | [Netpbm](http://netpbm.sourceforge.net/doc/) |
| Analyze 7.5 | medical/scientific volume imagery | [Analyze 7.5](https://eeg.sourceforge.net/ANALYZE75.pdf) |
| MetaImage `.mhd` / `.mha` | ITK MetaIO image data | [MetaImage](https://itk.org/Wiki/ITK/MetaIO) |
| MRC2014 | electron microscopy/volume data | [CCP-EM MRC](https://www.ccpem.ac.uk/mrc_format/mrc2014.php) |
| DICOM | medical image/container paths | [DICOM](https://www.dicomstandard.org/) |
| ENVI | remote-sensing raster/header format | [ENVI header files](https://www.l3harrisgeospatial.com/docs/enviheaderfiles.html) |
| VICAR | NASA/JPL image format | [VICAR](https://www-mipl.jpl.nasa.gov/external/VICAR_file_fmt.pdf) |
| PDS | NASA Planetary Data System imagery | [PDS](https://pds.nasa.gov/) |

### Professional / authoring

| Family / format | Coverage note | Reference |
| --- | --- | --- |
| Photoshop PSD / PSB | Adobe Photoshop documents; exact writer state is registry-defined | [Adobe PSD/PSB](https://www.adobe.com/devnet-apps/photoshop/fileformatashtml/) |
| Krita KRA | Krita document container/image data | [Krita](https://docs.krita.org/) |
| OpenRaster ORA | layered raster interchange | [OpenRaster](https://www.openraster.org/) |
| GIMP XCF | GIMP native image document | [XCF specification](https://developer.gimp.org/core/standards/xcf/) |
| MagicaVoxel VOX | voxel scene/image data | [VOX format](https://github.com/ephtracy/voxel-model/blob/master/MagicaVoxel-file-format-vox.txt) |
| WMF / EMF | Windows metafile/vector-rendering paths | [MS-WMF](https://learn.microsoft.com/openspecs/windows_protocols/ms-wmf/) |
| EPS / PostScript | raster-preview/extraction paths | [PostScript reference](https://www.adobe.com/jp/print/postscript/pdfs/PLRM.pdf) |
| PDF | image extraction rather than page renderer/editor | [ISO 32000 background](https://opensource.adobe.com/dc-acrobat-sdk-docs/pdfstandards/) |
| PE EXE/DLL | image/resource extraction, not executable editing | [PE/COFF](https://learn.microsoft.com/windows/win32/debug/pe-format) |
| VIPS | libvips native image format | [libvips](https://www.libvips.org/) |
| SoftImage / Maya IFF | authoring/renderer image outputs | [IFF](https://en.wikipedia.org/wiki/Interchange_File_Format) |

### GPU textures / 3D

| Format | Coverage note | Reference |
| --- | --- | --- |
| DDS | DirectDraw/DirectX textures | [DDS](https://learn.microsoft.com/windows/win32/direct3ddds/dx-graphics-dds) |
| KTX / KTX2 | Khronos texture containers | [KTX](https://registry.khronos.org/KTX/specs/) |
| PVR | PowerVR texture container | [PVR format](https://docs.imgtec.com/PVR-File-Format-Specification/) |
| ASTC | Adaptive Scalable Texture Compression | [ASTC encoder/spec resources](https://github.com/ARM-software/astc-encoder) |
| PKM / ETC1 / ETC2 | Ericsson texture compression container/data | [Khronos ETC](https://registry.khronos.org/OpenGL/extensions/OES/OES_compressed_ETC1_RGB8_texture.txt) |
| VTF | Valve Texture Format | [Valve VTF](https://developer.valvesoftware.com/wiki/Valve_Texture_Format) |
| BLP | Blizzard texture family | [BLP](https://wowdev.wiki/BLP) |
| FSH | EA Sports texture container | [FSH](https://wiki.simtropolis.com/wiki/FSH) |
| WAD/WAD2/WAD3, MipTex | Quake/Half-Life texture archives and embedded texture data | [Quake file formats](https://quakewiki.org/wiki/Quake_file_formats) |
| Block codecs | BC1–BC7, ETC1/ETC2, ASTC LDR, PVRTC helpers used by texture formats | [DirectX BC formats](https://learn.microsoft.com/windows/win32/direct3d11/texture-block-compression-in-direct3d-11) |

### Animation / multi-image

| Format | Coverage note | Reference |
| --- | --- | --- |
| GIF | animated frame/page access through the multi-image contract | [GIF89a](https://www.w3.org/Graphics/GIF/spec-gif89a.txt) |
| APNG | animated PNG | [APNG spec](https://wiki.mozilla.org/APNG_Specification) |
| MNG | Multiple-image Network Graphics; writer is a documented subset | [MNG](http://www.libpng.org/pub/mng/spec/) |
| FLI/FLC | Autodesk Animator family | [FLIC](https://www.compuphase.com/flic.htm) |
| TIFF / BigTIFF | multi-page image families | [TIFF](https://www.adobe.io/open/standards/TIFF.html), [BigTIFF](https://www.awaresystems.be/imaging/tiff/bigtiff.html) |
| DCX | multi-page PCX/WinFax family | [DCX](https://fileformats.archiveteam.org/wiki/DCX) |
| MPO | multi-picture JPEG | [CIPA DC-007](https://www.cipa.jp/std/documents/e/DC-007_E.pdf) |
| ICNS | Apple icon resources with multiple representations | [ICNS](https://en.wikipedia.org/wiki/Apple_Icon_Image_format) |

### Icons / cursors / fonts

| Format | Coverage note | Reference |
| --- | --- | --- |
| ICO / CUR | Windows icons/cursors | [ICO/CUR](https://en.wikipedia.org/wiki/ICO_(file_format)) |
| ANI | animated Windows cursors | [ANI](https://en.wikipedia.org/wiki/ANI_(file_format)) |
| ICNS | Apple icon images | [ICNS](https://en.wikipedia.org/wiki/Apple_Icon_Image_format) |
| Xcursor | X11 cursor images | [Xcursor](https://www.x.org/releases/X11R7.7/doc/man/man3/Xcursor.3.xhtml) |
| SunIcon | Sun/X bitmap-family icon data | [XBM background](https://en.wikipedia.org/wiki/X_BitMap) |
| MS FONT / FNT | bitmap-font glyph imagery | [Windows font resources](https://learn.microsoft.com/windows/win32/menurc/font-resource) |

### Document / fax

The package includes CCITT Group 3/4 primitives plus numerous fax-container variants including AccessFax, AdTechFax, BfxBitware, BrotherFax, CanonNavFax, EverexFax, FaxMan, FremontFax, GammaFax, HayesJtfax, ImagingFax, KofaxKfx, MobileFax, OazFax, OlicomFax, RicohFax, SciFax, SmartFax, TeliFax, Tg4, VentaFax, WinFax, WorldportFax, BrooktroutFax, EdmicsC4 and AttGroup4. Exact writer state is per registry entry.

| Format/family | Reference |
| --- | --- |
| CCITT Fax Group 3 / Group 4 | [ITU-T T.4](https://www.itu.int/rec/T-REC-T.4) / [T.6](https://www.itu.int/rec/T-REC-T.6) |
| WSQ fingerprint imagery | [FBI WSQ specification](https://www.fbibiospecs.cjis.gov/Document/Get?fileName=WSQ_Gray-scale_Specification_Version_3_1_Final.pdf) |
| Symbian MBM | [Symbian bitmap background](https://en.wikipedia.org/wiki/Symbian) |

### RAW camera

| Format | Coverage note | Reference |
| --- | --- | --- |
| Adobe DNG | TIFF-derived digital negative | [DNG specification](https://helpx.adobe.com/camera-raw/digital-negative.html) |
| Canon CR2 | lossless-JPEG/slice-oriented paths | [CR2 background](https://en.wikipedia.org/wiki/Raw_image_format#Canon) |
| Canon CR3 | HEIF/ISOBMFF-derived subset | [CR3 background](https://en.wikipedia.org/wiki/Raw_image_format#Canon) |
| Nikon NEF | manufacturer RAW paths including compressed variants | [NEF background](https://en.wikipedia.org/wiki/Raw_image_format#Nikon) |
| Sony ARW2 | Sony RAW path including delta coding | [ARW background](https://en.wikipedia.org/wiki/Raw_image_format#Sony) |
| Olympus ORF | Olympus RAW | [ORF background](https://en.wikipedia.org/wiki/Raw_image_format#Olympus) |
| Panasonic RW2 | Panasonic RAW | [RW2 background](https://en.wikipedia.org/wiki/Raw_image_format#Panasonic) |

### Other notable formats

The long tail also includes TGA/Targa, PCX, SGI/Iris, Sun Raster, X PixMap (XPM), X BitMap (XBM), Wireless Bitmap (WBMP), AAI/DuneHD, HRZ slow-scan TV, CMU bitmap, GEM/GTM, PageMaker-related formats, Macromedia FreeHand, Pixar PXR, GD2/libgd, MIFF/ImageMagick, ECW, JNG, VIFF/Khoros, RLA/RPF/Wavefront, ART/PFS and AliasPix. `FormatRegistry.AllFormats` is the definitive individual list.

## 🧪 Detection, registration, and verification notes

- Registry generation is compile-time and typed; adding a format that implements the static contracts extends the generated enum/registration without runtime reflection.
- Custom signature matchers may return `true`, `false`, or `null` when a header is insufficient; fixed magic signatures run through the generated priority table.
- A format's MIME aliases are explicit metadata, not guesses derived from extensions.
- A registered reader is not evidence for a writer; `SupportsWrite` is derived from the actual conversion delegate.
- Writers should be validated against a specification, external implementation, or other independent evidence. Two project methods agreeing only with each other is not treated as sufficient conformance evidence.
- Exact fast-moving counts belong to the generated registry/build rather than repeated prose.

## 📚 API reference

<!-- API:BEGIN generated by Hawkynt/RepositoryTemplate/package-readme — edit the XML docs in source, not here -->

Every public and protected member of all 3241 types, generated from the built assembly and its XML documentation, is in [REFERENCE.md](https://github.com/Hawkynt/PNGCrushCS/blob/main/Hawkynt.FileFormats.Images/REFERENCE.md).

<!-- API:END -->

## 🔌 Dependencies

| Dependency | Role |
| --- | --- |
| `FileFormat.Core` | `RawImage`, pixel formats, metadata and format contracts; bundled from this repository. |
| `Compression.Core` | Shared compression primitives used by several formats; bundled. |
| `FileFormat.TextMode` | Text-mode image infrastructure; bundled. |
| `BitMiracle.LibTiff.NET` | Managed TIFF support used by TIFF paths. |
| `BitMiracle.LibJpeg.NET` | Managed JPEG support used by JPEG paths. |
| `FrameworkExtensions.Backports` | Backported framework primitives. |
| `System.IO.Hashing` | Managed hashing primitives. |

## ⚠️ Limitations

- **Lossy advanced features** — VP8 lossy is keyframe-only; multi-pass rate control and token-partition threading are not implemented yet. Alpha IS preserved (the encoder writes an ALPH chunk on RGBA input; uncompressed method 0 — VP8L-encoded alpha is a future optimization).
- **Codec subsets** — HEIF/HEIC now resolves and decodes directly coded HEVC image items through the shared managed H.265 decoder; that path currently targets Main-profile intra-picture 8-bit 4:2:0 content and rejects unsupported HEVC profiles/features instead of fabricating pixels. AVIF container parsing exists, but real AV1 pixel decoding remains disabled until the AV1 entropy syntax is conforming. BPG remains an I-frame-oriented managed subset. **JPEG 2000** writing uses a deliberately narrow 8-bit Gray/RGB conforming baseline profile; unsupported optional coding modes are outside that authoring profile rather than encoded with private syntax. **JPEG XL**: container + SizeHeader + ImageMetadata + FrameHeader (ISO/IEC 18181-1 §3.6.2 / §3.6.3 / §3.6.5) are spec-conformant — the all_default fast path that most libjxl-encoded files use is fully supported, and the non-default conditional plumbing (orientation, bit_depth, num_extra_channels, extra_channel_info, color_encoding, tone_mapping, frame_type, encoding flag) is in place. Pixel codec (modular sub-codec body and VarDCT) is the remaining workstream — arbitrary real-world `.jxl` files will not decode their pixels yet, but signature, dimensions, and image-level metadata are extracted correctly. **JPEG XR** decodes real codestreams through a managed port of JXRLib's T.832 core and writes real `WMPHOTO`; what it refuses is narrower than the whole format — a component count outside one, three or four, and interleaved alpha, which needs a planar BCC2/BCC3 codestream instead. Gray8, RGB24 and RGBA32 are exposed; other WIC layouts may be turned down at the container adapter even where the core handles them. Camera RAW supports DNG lossless JPEG, Canon CR2, Nikon NEF, Sony ARW2; other manufacturer-specific compressions are future work.
- **Write coverage** — Read support does not imply authoring support. Use the exhaustive matrix above or filter `FormatRegistry.AllFormats` by `SupportsWrite` for the exact current set of formats that can encode an arbitrary `RawImage`.
- **PDF / PE** — image extraction only. PDF rendering, page composition, vector graphics, and PE writing are out of scope.
- **Bundle size** — `~4.9 MB`, four assemblies. There is no way to take only the formats you need; if that matters, per-format NuGet packages may be published in future.
- **TFM** — targets `net8.0`. Older runtimes are not supported.
- Coverage breadth is larger than conformance depth. Some historical formats have scarce or no public samples; registry presence is not a promise that every obscure producer variant has been verified.
- The current JPEG XL pixel path is not general libjxl interoperability; do not treat its internal round-trip as proof of arbitrary `.jxl` compatibility.

## ❤️ Support

If this project saves you time or money, consider supporting its development:

[![GitHub Sponsors](https://img.shields.io/badge/GitHub-Sponsor-EA4AAA?logo=githubsponsors)](https://github.com/sponsors/Hawkynt)
[![PayPal](https://img.shields.io/badge/PayPal-Donate-00457C)](https://www.paypal.me/hawkynt)

## 📜 License

Licensed under LGPL-3.0-or-later — see the repository [LICENSE](https://github.com/Hawkynt/PNGCrushCS/blob/main/LICENSE).
