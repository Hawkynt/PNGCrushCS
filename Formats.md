# Image Format Cross-Reference

Cross-reference of implemented formats against four major image processing tools. Tracks reader/writer/optimizer coverage and identifies gaps.

## Legend

| Symbol | Meaning                     |
| ------ | --------------------------- |
| **Y**  | Implemented in this project |
| **R**  | Read-only support           |
| **W**  | Write-only support          |
| **RW** | Read and write support      |
| **—**  | Not supported / not listed  |

**Our columns**: Reader/Writer/Optimizer indicate whether this project has the capability.
**External tool columns**: Indicate the tool's support level (R, W, or RW).

---

## Implemented Formats

All formats below have dedicated `FileFormat.*` libraries with both reader and writer implementations (except GIF, which uses an external `GifFileFormat` library).

| Format                  | Extensions                                           | Description                      | Reader | Writer | Optimizer | Tom's | IM  | XnView | IrfanView |
| ----------------------- | ---------------------------------------------------- | -------------------------------- | ------ | ------ | --------- | ----- | --- | ------ | --------- |
| AAI                     | .aai                                                 | Dune HD media player image       | Y      | Y      | —         | RW    | RW  | —      | —         |
| Access Fax              | .acc                                                 | Access fax format                | Y      | Y      | —         | R     | —   | R      | —         |
| Acorn Sprite            | .acorn                                               | Acorn RISC OS sprite             | Y      | Y      | —         | R     | —   | R      | —         |
| Ad-Tech Fax             | .adt                                                 | Ad-Tech fax format               | Y      | Y      | —         | —     | —   | —      | —         |
| ADEX Image              | .adx                                                 | ADEX image format                | Y      | Y      | —         | —     | —   | R      | —         |
| Advanced Art Studio     | .ocp                                                 | C64 Advanced Art Studio          | Y      | Y      | —         | RW    | —   | —      | —         |
| AFLI                    | .afl                                                 | C64 Advanced FLI hires           | Y      | Y      | —         | R     | —   | —      | —         |
| AIM Grayscale           | .aim                                                 | AIM grayscale image              | Y      | Y      | —         | —     | —   | R      | —         |
| Aladdin Paint           | .alp                                                 | Atari ST Aladdin Paint           | Y      | Y      | —         | R     | —   | —      | —         |
| Alias PIX               | .als, .alias                                         | Alias/Wavefront PIX              | Y      | Y      | —         | RW    | —   | —      | —         |
| Amica Paint             | .ami                                                 | C64 Amica Paint multicolor       | Y      | Y      | —         | R     | —   | —      | —         |
| Amiga Icon              | .info                                                | Amiga Workbench icon             | Y      | Y      | —         | R     | —   | —      | —         |
| Amstrad CPC             | .scr, .win                                           | Amstrad CPC screen dump          | Y      | Y      | —         | R     | —   | —      | R         |
| Analyze 7.5             | .hdr, .img                                           | Medical neuroimaging format      | Y      | Y      | —         | —     | —   | R      | —         |
| Andrew Toolkit          | .atk                                                 | CMU Andrew Toolkit raster        | Y      | Y      | —         | —     | —   | —      | —         |
| ANI                     | .ani                                                 | Windows animated cursor          | Y      | Y      | Y         | R     | —   | —      | R         |
| Anim Painter            | .anp                                                 | C64 Anim Painter animation       | Y      | Y      | —         | R     | —   | —      | —         |
| APNG                    | .apng                                                | Animated PNG                     | Y      | Y      | —         | R     | RW  | R      | —         |
| Apple II                | .hgr                                                 | Apple II hi-res graphics         | Y      | Y      | —         | R     | —   | —      | —         |
| Apple II DHR            | .dhr, .a2d                                           | Apple II Double Hi-Res graphics  | Y      | Y      | —         | R     | —   | —      | —         |
| Apple II HGR            | .hgr                                                 | Apple II Hi-Res 280x192          | Y      | Y      | —         | R     | —   | —      | —         |
| Apple IIgs              | .shr, .sh3                                           | Apple IIgs super hi-res          | Y      | Y      | —         | R     | —   | —      | —         |
| ART                     | .art                                                 | PFS: 1st Publisher clip art      | Y      | Y      | —         | RW    | RW  | —      | —         |
| Art Director            | .art                                                 | Atari ST Art Director            | Y      | Y      | —         | R     | —   | —      | —         |
| Art Studio (Atari 8)    | .as8                                                 | Atari 8-bit Art Studio 320x192   | Y      | Y      | —         | R     | —   | —      | —         |
| Artist 64               | .a64                                                 | C64 Wigmore Artist 64            | Y      | Y      | —         | R     | —   | —      | —         |
| ASTC                    | .astc                                                | Adaptive Scalable Texture        | Y      | Y      | —         | —     | —   | —      | —         |
| AT&T Group 4 Fax        | .att                                                 | AT&T Group 4 fax                 | Y      | Y      | —         | R     | —   | R      | —         |
| Atari 2600              | .a26, .tia                                           | Atari 2600 playfield graphics    | Y      | Y      | —         | —     | —   | —      | —         |
| Atari 7800              | .a78, .a7800                                         | Atari 7800 tile graphics         | Y      | Y      | —         | —     | —   | —      | —         |
| Atari 8-Bit             | .gr7, .gr8, .gr9, .gr15, .hip, .mic, .int            | Atari 8-bit graphics modes       | Y      | Y      | —         | R     | —   | —      | —         |
| Atari AGP               | .agp                                                 | Atari 8-bit AGP graphics         | Y      | Y      | —         | R     | —   | —      | —         |
| Atari Animation         | .aan                                                 | Atari 8-bit multi-frame anim     | Y      | Y      | —         | —     | —   | —      | —         |
| Atari ANTIC Mode        | .ame, .anm                                           | Atari ANTIC Mode E/F screen      | Y      | Y      | —         | —     | —   | —      | —         |
| Atari Artist            | .aat                                                 | Atari 8-bit Artist 160x192       | Y      | Y      | —         | R     | —   | —      | —         |
| Atari CAD               | .acd                                                 | Atari CAD screen                 | Y      | Y      | —         | —     | —   | —      | —         |
| Atari Compressed        | .acr, .acp                                           | Atari 8-bit RLE compressed       | Y      | Y      | —         | R     | —   | —      | —         |
| Atari DRG               | .drg                                                 | Atari 8-bit DRG 160x192 2bpp    | Y      | Y      | —         | R     | —   | —      | —         |
| Atari Dump              | .asd, .adm                                           | Atari 8-bit generic screen dump  | Y      | Y      | —         | R     | —   | —      | —         |
| Atari Falcon True Color | .ftc                                                 | Atari Falcon 320x240 RGB565      | Y      | Y      | —         | R     | —   | —      | —         |
| Atari Falcon XGA        | .xga                                                 | Atari Falcon XGA 16-bit TC      | Y      | Y      | —         | R     | —   | —      | —         |
| Atari Font              | .fnt8                                                | Atari 8-bit character set        | Y      | Y      | —         | —     | —   | —      | —         |
| Atari GFB               | .gfb                                                 | Atari 8-bit GFB 320x192 mono    | Y      | Y      | —         | R     | —   | —      | —         |
| Atari GR.7              | .gr7                                                 | Atari 8-bit ANTIC Mode 7        | Y      | Y      | —         | R     | —   | —      | —         |
| Atari GR.8              | .gr8                                                 | Atari 8-bit ANTIC Mode 8        | Y      | Y      | —         | R     | —   | —      | —         |
| Atari Grafik            | .pcp                                                 | Atari graphics format            | Y      | Y      | —         | —     | —   | —      | —         |
| Atari Graphics 10       | .gr10, .g10                                          | Atari GTIA 9-color mode          | Y      | Y      | —         | R     | —   | —      | —         |
| Atari Graphics 11       | .gr11, .g11                                          | Atari GTIA 16-luminance mode     | Y      | Y      | —         | R     | —   | —      | —         |
| Atari Graphics 9        | .gr9, .g9                                            | Atari GTIA 16-shade grayscale    | Y      | Y      | —         | R     | —   | —      | —         |
| Atari HR                | .hr                                                  | Atari 8-bit HR hires 320x192    | Y      | Y      | —         | R     | —   | —      | —         |
| Atari Maxi              | .max8, .amx                                          | Atari 8-bit Maxi paint 160x192  | Y      | Y      | —         | R     | —   | —      | —         |
| Atari Picture           | .apc                                                 | Atari 8-bit screen capture       | Y      | Y      | —         | —     | —   | —      | —         |
| Atari Player/Missile    | .pmg, .plm                                           | Atari 8-bit Player/Missile GFX   | Y      | Y      | —         | —     | —   | —      | —         |
| Atari ST Dali            | .sd0, .sd1, .sd2                                    | Atari ST Dali planar image       | Y      | Y      | —         | R     | —   | —      | —         |
| Atari ST Paintworks     | .cl0, .cl1, .cl2, .pg0, .pg1, .pg2, .pg3             | Atari ST Paintworks/DeskPic      | Y      | Y      | —         | R     | —   | —      | —         |
| Autodesk CEL            | .cel                                                 | Autodesk Animator CEL            | Y      | Y      | —         | R     | —   | —      | —         |
| AVHRR Image             | .sst                                                 | NOAA satellite imagery           | Y      | Y      | —         | —     | —   | R      | —         |
| AVIF                    | .avif                                                | AV1 Image File Format            | Y      | Y      | —         | —     | RW  | R      | R         |
| AVS                     | .avs                                                 | Advanced Visualization Studio    | Y      | Y      | —         | RW    | RW  | —      | —         |
| AWD                     | .awd                                                 | Microsoft Fax document           | Y      | Y      | —         | —     | —   | R      | —         |
| BBC Micro               | .bb0, .bb1, .bb2, .bb4, .bb5                         | BBC Micro screen modes           | Y      | Y      | —         | R     | —   | —      | —         |
| Bennet Yee Face         | .ybm                                                 | Bennet Yee Face monochrome       | Y      | Y      | —         | —     | —   | —      | —         |
| BFLI                    | .bfl, .bfli                                          | C64 Big FLI                      | Y      | Y      | —         | R     | —   | RW     | —         |
| BFX Bitware Fax         | .bfx                                                 | BFX Bitware fax                  | Y      | Y      | —         | —     | —   | —      | —         |
| BigTIFF                 | .btf, .tf8                                           | 64-bit offset TIFF               | Y      | Y      | —         | —     | RW  | —      | —         |
| Bio-Rad PIC             | .pic                                                 | Bio-Rad confocal microscopy      | Y      | Y      | —         | —     | —   | R      | —         |
| Blazing Paddles         | .blz                                                 | C64 Blazing Paddles hires        | Y      | Y      | —         | R     | —   | —      | —         |
| BLP                     | .blp                                                 | Blizzard game texture            | Y      | Y      | —         | —     | —   | R      | —         |
| BMP                     | .bmp, .dib, .rle, .vga, .rl4, .rl8, .sys             | Windows Bitmap                   | Y      | Y      | Y         | RW    | RW  | RW     | RW        |
| BOB                     | .bob                                                 | BOB ray-tracer image             | Y      | Y      | —         | —     | —   | —      | —         |
| BPG                     | .bpg                                                 | Better Portable Graphics         | Y      | Y      | —         | RW    | RW  | R      | —         |
| Brooktrout Fax          | .brt, .301                                           | Brooktrout fax format            | Y      | Y      | —         | —     | —   | —      | —         |
| Brother Fax             | .uni                                                 | Brother fax format               | Y      | Y      | —         | —     | —   | —      | —         |
| BSAVE                   | .bsv                                                 | IBM PC BSAVE screen dump         | Y      | Y      | —         | —     | —   | —      | —         |
| BSB                     | .bsb                                                 | Maptech nautical chart           | Y      | Y      | —         | —     | —   | —      | —         |
| Bug Bitmap              | .bbm, .bug                                           | C64 Bug Bitmap                   | Y      | Y      | —         | R     | —   | —      | —         |
| BYU SIR                 | .sir                                                 | BYU synthetic aperture radar     | Y      | Y      | —         | —     | —   | —      | —         |
| C128 Hires              | .c1h                                                 | C128 hires 320x200 monochrome   | Y      | Y      | —         | R     | —   | —      | —         |
| C128 Multicolor         | .c1m                                                 | C128 multicolor 160x200         | Y      | Y      | —         | R     | —   | —      | —         |
| C128 VDC (640)          | .vdc, .vdc3                                          | C128 VDC 640x200 mono bitmap    | Y      | Y      | —         | R     | —   | —      | —         |
| C64 Multicolor          | .64c, .ami                                           | C64 Art Studio / Amica Paint     | Y      | Y      | —         | R     | —   | R      | —         |
| Calamus                 | .cpi, .crg                                           | Atari ST Calamus raster          | Y      | Y      | —         | R     | —   | —      | —         |
| CALS                    | .cal, .cals, .gp4                                    | CALS Type 1 raster               | Y      | Y      | —         | R     | RW  | —      | —         |
| Camera RAW              | .cr2, .nef, .arw, .orf, .rw2, .pef, .raf, .srw, .dcs | Generic camera RAW catch-all     | Y      | Y      | —         | R     | R   | R      | R         |
| Canon Navigator Fax     | .can                                                 | Canon Navigator fax              | Y      | Y      | —         | —     | —   | —      | —         |
| Canvas ST               | .cvs                                                 | Atari ST Canvas paint            | Y      | Y      | —         | R     | —   | —      | —         |
| CCITT Group 3/4         | .g3, .g4                                             | ITU-T T.4/T.6 fax coding         | Y      | Y      | —         | R     | RW  | R      | R         |
| CDU-Paint               | .cdu                                                 | C64 CDU-Paint                    | Y      | Y      | —         | RW    | —   | —      | —         |
| Centauri                | .cnt, .cen                                           | C64 Centauri paint               | Y      | Y      | —         | R     | —   | —      | —         |
| CFLI Designer           | .cfli                                                | C64 CFLI color FLI multicolor    | Y      | Y      | —         | R     | —   | —      | —         |
| Champions Interlace     | .cin                                                 | C64 Champions Interlace MC       | Y      | Y      | —         | R     | —   | —      | —         |
| CharSet 64              | .chr64                                               | C64 character set 256x8          | Y      | Y      | —         | —     | —   | —      | —         |
| Cheese                  | .che, .chs                                           | C64 Cheese paint                 | Y      | Y      | —         | R     | —   | —      | —         |
| Cinemaster (Atari)      | .cin8                                                | Atari 8-bit Cinemaster anim      | Y      | Y      | —         | —     | —   | —      | —         |
| Cineon                  | .cin                                                 | Kodak Cineon film scan           | Y      | Y      | —         | RW    | RW  | RW     | R         |
| Cisco IP Phone          | .cip                                                 | Cisco IP phone display           | Y      | Y      | —         | W     | —   | —      | —         |
| CLOE Ray-Tracer         | .clo                                                 | CLOE ray-tracer output           | Y      | Y      | —         | —     | —   | —      | —         |
| CMU Window Manager      | .cmu                                                 | CMU 1bpp packed bitmap           | Y      | Y      | —         | —     | —   | —      | —         |
| CoCo 3                  | .cc3                                                 | CoCo 3 GIME 320x200x16          | Y      | Y      | —         | R     | —   | —      | —         |
| CoCo PMODE 4            | .coc                                                 | TRS-80 CoCo 256x192 mono        | Y      | Y      | —         | R     | —   | —      | —         |
| CoCoMax                 | .max                                                 | CoCoMax paint 256x192 mono       | Y      | Y      | —         | R     | —   | —      | —         |
| COKE Atari              | .tg1                                                 | Atari Falcon COKE 16-bit TC     | Y      | Y      | —         | R     | —   | —      | —         |
| ColoRIX                 | .rix                                                 | ColoRIX VGA paint                | Y      | Y      | —         | —     | —   | —      | —         |
| Commodore 128           | .c128, .vdc                                          | C128 VDC bitmap                  | Y      | Y      | —         | R     | —   | —      | —         |
| Commodore 16/Plus4      | .plus4                                               | C16/Plus4 graphics               | Y      | Y      | —         | R     | —   | —      | —         |
| Commodore PET           | .pet                                                 | PET PETSCII screen               | Y      | Y      | —         | R     | —   | —      | —         |
| ComputerEyes            | .ce, .ce1, .ce2                                      | ComputerEyes digitizer           | Y      | Y      | —         | R     | —   | —      | —         |
| CompW                   | .wlm                                                 | CompW image format               | Y      | Y      | —         | —     | —   | —      | —         |
| Corel Photo-Paint       | .cpt                                                 | Corel Photo-Paint native         | Y      | Y      | —         | —     | —   | —      | R         |
| CP8 Grayscale           | .cp8                                                 | CP8 grayscale image              | Y      | Y      | —         | —     | —   | —      | —         |
| CPC Advanced            | .cpa                                                 | CPC Advanced Mode 0 160x200     | Y      | Y      | —         | R     | —   | —      | —         |
| CPC Font                | .cpf                                                 | CPC character set 256x8          | Y      | Y      | —         | —     | —   | —      | —         |
| CPC Overscan            | .cpo                                                 | CPC overscan 384x272 Mode 1     | Y      | Y      | —         | R     | —   | —      | —         |
| CPC Plus                | .cpp                                                 | CPC Plus Mode 1 320x200         | Y      | Y      | —         | R     | —   | —      | —         |
| CPC Sprite              | .cps                                                 | CPC sprite 16x16 Mode 1         | Y      | Y      | —         | —     | —   | —      | —         |
| CrackArt                | .ca1, .ca2, .ca3                                     | Atari ST CrackArt                | Y      | Y      | —         | R     | —   | —      | —         |
| Create with Garfield    | .cwg                                                 | C64 Create with Garfield hires   | Y      | Y      | —         | R     | —   | —      | —         |
| CSV Image               | .csv                                                 | Comma-separated pixel values     | Y      | Y      | —         | —     | —   | —      | —         |
| CUR                     | .cur                                                 | Windows static cursor            | Y      | Y      | Y         | R     | —   | —      | R         |
| Dali Raw                | .sd0, .sd1, .sd2                                     | Atari ST Dali raw                | Y      | Y      | —         | R     | —   | —      | —         |
| DBW Render              | .dbw                                                 | DBW ray-tracer output            | Y      | Y      | —         | —     | —   | —      | —         |
| DCTV                    | .dctv                                                | Amiga DCTV composite video       | Y      | Y      | —         | R     | —   | —      | —         |
| DCX                     | .dcx                                                 | Multi-page PCX container         | Y      | Y      | —         | RW    | RW  | RW     | R         |
| DDS                     | .dds                                                 | DirectDraw Surface               | Y      | Y      | —         | RW    | RW  | RW     | R         |
| DEGAS                   | .pi1, .pi2, .pi3, .pc1, .pc2, .pc3                   | Atari ST DEGAS/Elite             | Y      | Y      | —         | R     | —   | RW     | —         |
| Deluxe Paint ST         | .dps, .dlx                                           | Atari ST Deluxe Paint            | Y      | Y      | —         | R     | —   | —      | —         |
| DICOM                   | .dcm, .acr, .dic, .dc3                               | Medical imaging DICOM            | Y      | Y      | —         | R     | R   | R      | R         |
| Digi Spec               | .dgs                                                 | Atari ST Digi Spec digitizer     | Y      | Y      | —         | —     | —   | —      | —         |
| DigiView                | .dgv                                                 | DigiView digitizer image         | Y      | Y      | —         | R     | —   | —      | —         |
| DIV Game Map            | .fpg                                                 | DIV Games Studio map             | Y      | Y      | —         | —     | —   | —      | —         |
| DjVu                    | .djvu, .djv, .iw4                                    | DjVu document image              | Y      | Y      | —         | —     | R   | RW     | R         |
| DNG                     | .dng                                                 | Adobe Digital Negative           | Y      | Y      | —         | R     | R   | R      | R         |
| Dolphin-Ed              | .dol                                                 | C64 Dolphin Ed                   | Y      | Y      | —         | RW    | —   | —      | —         |
| Doodle                  | .dd                                                  | C64 Doodle                       | Y      | Y      | —         | RW    | —   | —      | —         |
| Doodle Atari ST         | .doo                                                 | Atari ST Doodle                  | Y      | Y      | —         | RW    | —   | —      | —         |
| Doodle Compressed       | .jj                                                  | C64 Doodle compressed            | Y      | Y      | —         | R     | —   | —      | —         |
| Doodle Packed           | .dpk                                                 | C64 Doodle RLE packed            | Y      | Y      | —         | R     | —   | —      | —         |
| Doom Flat               | .flat                                                | Doom engine flat texture         | Y      | Y      | —         | —     | —   | —      | —         |
| DPX                     | .dpx                                                 | Digital Picture Exchange         | Y      | Y      | —         | RW    | RW  | RW     | R         |
| Dr. Halo CUT            | .cut                                                 | Dr. Halo CUT indexed             | Y      | Y      | —         | R     | —   | —      | —         |
| Dragon                  | .dgn                                                 | Dragon 32/64 graphics            | Y      | Y      | —         | —     | —   | —      | —         |
| DrawIt                  | .dit                                                 | DrawIt 8-bit indexed image       | Y      | Y      | —         | —     | —   | —      | —         |
| Drazlace                | .dlp, .drl                                           | C64 Drazlace interlace MC        | Y      | Y      | —         | R     | —   | —      | —         |
| DrazPaint               | .drz                                                 | C64 DrazPaint                    | Y      | Y      | —         | RW    | —   | —      | —         |
| DuneGraph               | .dg1, .dc1                                           | Atari Falcon DuneGraph indexed   | Y      | Y      | —         | R     | —   | —      | —         |
| ECI Graphic Editor      | .eci, .ecp                                           | C64 ECI Extended Color Interlace | Y      | Y      | —         | R     | —   | —      | —         |
| ECW                     | .ecw                                                 | Enhanced Compressed Wavelet      | Y      | Y      | —         | —     | —   | RW     | RW        |
| EDMICS C4               | .edc                                                 | EDMICS CCITT Group 4             | Y      | Y      | —         | —     | —   | —      | —         |
| Egg Paint               | .trp                                                 | Atari Falcon EggPaint            | Y      | Y      | —         | R     | —   | —      | —         |
| Electronika BK          | .ekr                                                 | Electronika BK screen            | Y      | Y      | —         | —     | —   | —      | —         |
| EMC Editor              | .emc                                                 | C64 EMC extended multicolor      | Y      | Y      | —         | R     | —   | —      | —         |
| EMF                     | .emf                                                 | Enhanced Metafile                | Y      | Y      | —         | R     | R   | RW     | RW        |
| Enterprise 128          | .ep, .elan                                           | Enterprise 128 graphics          | Y      | Y      | —         | —     | —   | —      | —         |
| ENVI                    | .hdr                                                 | ENVI remote sensing header       | Y      | Y      | —         | —     | —   | —      | —         |
| EPA BIOS Logo           | .epa                                                 | Award BIOS splash logo           | Y      | Y      | —         | R     | —   | —      | —         |
| EPS                     | .eps, .epi, .ept                                     | Encapsulated PostScript          | Y      | Y      | —         | RW    | RW  | RW     | R         |
| Escape Paint            | .esp                                                 | Atari ST Escape Paint            | Y      | Y      | —         | R     | —   | —      | —         |
| Everex Fax              | .efx, .ef3                                           | Everex fax format                | Y      | Y      | —         | —     | —   | —      | —         |
| Extended GEM IMG        | .ximg                                                | Extended GEM Bit Image (XIMG)    | Y      | Y      | —         | R     | —   | —      | —         |
| EZ-Art                  | .eza                                                 | Atari ST EZ-Art Professional     | Y      | Y      | —         | R     | —   | —      | —         |
| Face Painter            | .fpt                                                 | C64 Face Painter                 | Y      | Y      | —         | RW    | —   | —      | —         |
| Face Server             | .fac, .face                                          | Unix Face Server                 | Y      | Y      | —         | —     | —   | —      | —         |
| FaceSaver               | .face, .fac                                          | Usenix FaceSaver (hex grayscale) | Y      | Y      | —         | —     | —   | —      | —         |
| Falcon Paint            | .fpn                                                 | Atari Falcon Paint               | Y      | Y      | —         | R     | —   | —      | —         |
| Falcon Res              | .frs                                                 | Atari Falcon Res screen dump     | Y      | Y      | —         | R     | —   | —      | —         |
| Farbfeld                | .ff                                                  | Farbfeld RGBA16 raw              | Y      | Y      | —         | —     | RW  | —      | —         |
| Fax Group 3             | .g3                                                  | Raw Group 3 fax                  | Y      | Y      | —         | RW    | RW  | R      | R         |
| FaxMan                  | .fmf                                                 | FaxMan fax format                | Y      | Y      | —         | —     | —   | —      | —         |
| FBM                     | .fbm                                                 | Fuzzy Bitmap format              | Y      | Y      | —         | —     | —   | —      | —         |
| FFLI                    | .ffli                                                | C64 Full FLI multicolor          | Y      | Y      | —         | R     | —   | —      | —         |
| FITS                    | .fits, .fit, .fts                                    | Flexible Image Transport System  | Y      | Y      | —         | RW    | RW  | RW     | R         |
| FL32                    | .fl32                                                | FilmLight 32-bit float image     | Y      | Y      | —         | —     | RW  | —      | —         |
| FlashPix                | .fpx                                                 | FlashPix multi-resolution        | Y      | Y      | —         | RW    | RW  | RW     | R         |
| FLI 64                  | .fli64                                               | C64 FLI Designer multicolor      | Y      | Y      | —         | R     | —   | —      | —         |
| FLI Designer 2          | .fd2                                                 | C64 FLI Designer 2 enhanced      | Y      | Y      | —         | R     | —   | —      | —         |
| FLI Editor              | .fed                                                 | C64 FLI Editor multicolor        | Y      | Y      | —         | R     | —   | —      | —         |
| FLI Graph               | .flg                                                 | C64 FLI Graph variant            | Y      | Y      | —         | R     | —   | —      | —         |
| FLI Profi               | .fpr                                                 | C64 FLI Profi per-raster-line    | Y      | Y      | —         | R     | —   | —      | —         |
| FLI/FLC                 | .fli, .flc                                           | Autodesk Animator animation      | Y      | Y      | —         | R     | —   | R      | —         |
| FLIF                    | .flif                                                | Free Lossless Image Format       | Y      | Y      | —         | —     | RW  | RW     | R         |
| Flimatic                | .flm                                                 | C64 Flimatic FLI multicolor      | Y      | Y      | —         | R     | —   | —      | —         |
| Flip64                  | .fbi                                                 | C64 Flip interlaced multicolor   | Y      | Y      | —         | R     | —   | —      | —         |
| FM Towns                | .fmt                                                 | FM Towns graphics                | Y      | Y      | —         | —     | —   | —      | —         |
| Fontasy Grafik          | .bsg                                                 | Atari ST Fontasy Grafik          | Y      | Y      | —         | —     | —   | —      | —         |
| FreeHand ST             | .fhs                                                 | Atari ST FreeHand bitmap         | Y      | Y      | —         | —     | —   | —      | —         |
| Fremont Fax             | .f96                                                 | Fremont fax format               | Y      | Y      | —         | —     | —   | —      | —         |
| FSH                     | .fsh                                                 | EA Shape container               | Y      | Y      | —         | —     | —   | —      | —         |
| Fullscreen Kit          | .kid                                                 | Atari ST Fullscreen overscan     | Y      | Y      | —         | R     | —   | —      | —         |
| Fun Graphics Machine    | .fgs                                                 | C64 Fun Graphics Machine         | Y      | Y      | —         | RW    | —   | —      | —         |
| Fun Painter             | .fp2, .fun                                           | C64 Fun Painter                  | Y      | Y      | —         | R     | —   | —      | —         |
| Fun Photor              | .fpr                                                 | C64 Fun Photor / FLI Profi       | Y      | Y      | —         | R     | —   | —      | —         |
| Fun*tastic Paint        | .fun8, .ftp                                          | Atari 8-bit GTIA 16-shade       | Y      | Y      | —         | R     | —   | —      | —         |
| G9B                     | .g9b                                                 | V9990 GFX9000 image              | Y      | Y      | —         | R     | —   | —      | —         |
| GAF                     | .gaf                                                 | Total Annihilation texture       | Y      | Y      | —         | —     | —   | R      | —         |
| Game Boy Tile           | .2bpp, .cgb                                          | Game Boy 2bpp tile data          | Y      | Y      | —         | —     | —   | —      | —         |
| GammaFax                | .gmf                                                 | GammaFax fax format              | Y      | Y      | —         | —     | —   | —      | —         |
| GBA Tile                | .gba                                                 | Game Boy Advance tile            | Y      | Y      | —         | —     | —   | —      | —         |
| GBR                     | .gbr                                                 | GIMP Brush                       | Y      | Y      | —         | —     | —   | RW     | —         |
| GD2                     | .gd2                                                 | libgd version 2                  | Y      | Y      | —         | —     | —   | —      | —         |
| GEM IMG                 | .img                                                 | GEM raster image                 | Y      | Y      | —         | R     | —   | —      | R         |
| GeoPaint                | .geo                                                 | GEOS GeoPaint monochrome         | Y      | Y      | —         | —     | —   | —      | —         |
| Gephard Hires           | .ghg                                                 | C64 Gephard Hires                | Y      | Y      | —         | R     | —   | —      | —         |
| GFA Paint               | .gfp                                                 | Atari ST GFA Paint               | Y      | Y      | —         | R     | —   | —      | —         |
| GFA Raytrace            | .sul                                                 | Atari ST GFA Raytrace            | Y      | Y      | —         | —     | —   | —      | —         |
| GIF                     | .gif, .giff                                          | Graphics Interchange Format      | Y      | Y      | Y         | RW    | RW  | RW     | RW        |
| GigaCAD                 | .gcd                                                 | Atari ST GigaCAD                 | Y      | Y      | —         | RW    | —   | —      | —         |
| GigaPaint               | .gig, .gih                                           | C64 GigaPaint                    | Y      | Y      | —         | RW    | —   | —      | —         |
| GoDot 4-Bit             | .4bt, .4bit                                          | C64 GoDot 4-bit                  | Y      | Y      | —         | R     | —   | —      | —         |
| GodPaint                | .gpn, .gdp                                           | Atari Falcon GodPaint            | Y      | Y      | —         | R     | —   | —      | —         |
| Graph Saurus            | .grs, .sr5, .sr7, .sr8, .srs                         | MSX2 Graph Saurus Screen 8      | Y      | Y      | —         | R     | —   | —      | —         |
| Graphics Master         | .gms, .gm8                                           | Atari 8-bit Graphics Master      | Y      | Y      | —         | R     | —   | —      | —         |
| Great Paint             | .gpt                                                 | Atari 8-bit Great Paint 160x192 | Y      | Y      | —         | R     | —   | —      | —         |
| GRS 16-bit              | .g16                                                 | GRS 16-bit grayscale             | Y      | Y      | —         | —     | —   | —      | —         |
| GunPaint                | .gun                                                 | C64 GunPaint                     | Y      | Y      | —         | R     | —   | —      | —         |
| Half-Life MDL           | .mdltex                                              | Half-Life model texture          | Y      | Y      | —         | —     | —   | R      | —         |
| HAM-E                   | .hame                                                | Amiga HAM Enhanced 18-bit        | Y      | Y      | —         | R     | —   | —      | —         |
| Hard Interlace          | .hip                                                 | C64 Hard Interlace hires         | Y      | Y      | —         | R     | —   | —      | —         |
| Hayes JT Fax            | .jtf                                                 | Hayes JT Fax                     | Y      | Y      | —         | —     | —   | —      | —         |
| HDR                     | .hdr, .rad                                           | Radiance RGBE HDR                | Y      | Y      | —         | RW    | RW  | RW     | —         |
| HEIF                    | .heic, .heif                                         | High Efficiency Image Format     | Y      | Y      | —         | R     | RW  | R      | R         |
| Heretic II M8           | .m8                                                  | Heretic II MIP texture           | Y      | Y      | —         | —     | —   | —      | —         |
| HF Image                | .hf                                                  | HF heightfield image             | Y      | Y      | —         | —     | —   | —      | —         |
| Hi-Eddi                 | .hed                                                 | C64 Hi-Eddi                      | Y      | Y      | —         | RW    | —   | —      | —         |
| Hi-Pic Creator          | .hpc                                                 | C64 Hi-Pic Creator multicolor    | Y      | Y      | —         | R     | —   | —      | —         |
| Hi-Res Editor           | .hre                                                 | C64 Hires Editor 320x200        | Y      | Y      | —         | R     | —   | —      | —         |
| Hi-Res Paint (Atari)    | .hra                                                 | Atari 8-bit Hi-Res 320x192      | Y      | Y      | —         | R     | —   | —      | —         |
| Highres Medium          | .hrm                                                 | Atari ST interlaced 640x200      | Y      | Y      | —         | R     | —   | —      | —         |
| HighRes ST              | .hst, .hrs                                           | Atari ST 640x400 monochrome     | Y      | Y      | —         | R     | —   | —      | —         |
| HinterGrundBild         | .hgb                                                 | C64 HinterGrundBild multicolor   | Y      | Y      | —         | R     | —   | —      | —         |
| Hires                   | .hir, .hbm                                           | C64 Hires bitmap                 | Y      | Y      | —         | RW    | —   | —      | —         |
| Hires Bitmap            | .hbm                                                 | C64 Hires Bitmap 320x200        | Y      | Y      | —         | RW    | —   | —      | —         |
| Hires FLI Crest         | .hfc                                                 | C64 Hires FLI by Crest           | Y      | Y      | —         | R     | —   | —      | —         |
| Hires Interlace Feniks  | .hlf                                                 | C64 Hires Interlace Feniks       | Y      | Y      | —         | R     | —   | —      | —         |
| Hires Manager           | .him                                                 | C64 Hires Manager by Cosmos      | Y      | Y      | —         | R     | —   | —      | —         |
| Hireslace               | .hle                                                 | C64 Hireslace Editor             | Y      | Y      | —         | R     | —   | —      | —         |
| Homeworld LIF           | .lif                                                 | Homeworld LIF texture            | Y      | Y      | —         | —     | —   | —      | —         |
| HP GROB                 | .grob, .gro2, .gro4                                  | HP calculator graphic object     | Y      | Y      | —         | —     | —   | —      | —         |
| HRZ                     | .hrz                                                 | Slow-scan TV image               | Y      | Y      | —         | RW    | —   | —      | —         |
| ICNS                    | .icns                                                | Apple Icon Image format          | Y      | Y      | —         | —     | —   | RW     | —         |
| ICO                     | .ico                                                 | Windows icon                     | Y      | Y      | Y         | R     | —   | RW     | RW        |
| Icon Library            | .icl                                                 | Windows icon library             | Y      | Y      | —         | —     | —   | —      | R         |
| ICS                     | .ics                                                 | Image Cytometry Standard         | Y      | Y      | —         | —     | —   | —      | R         |
| IFF ACBM                | .acbm, .iff                                          | Amiga Contiguous Bitmap          | Y      | Y      | —         | R     | —   | —      | —         |
| IFF ANIM                | .anim                                                | IFF animation container          | Y      | Y      | —         | —     | —   | —      | —         |
| IFF ANIM8               | .an8, .anim8                                         | IFF Long-word delta animation    | Y      | Y      | —         | R     | —   | —      | —         |
| IFF DEEP                | .deep, .iff                                          | IFF Deep Paint format            | Y      | Y      | —         | R     | —   | —      | —         |
| IFF DPAN                | .dpan                                                | IFF DPaint animation info        | Y      | Y      | —         | —     | —   | —      | —         |
| IFF ILBM                | .iff, .lbm, .ilbm                                    | Amiga Interleaved Bitmap         | Y      | Y      | —         | R     | —   | RW     | R         |
| IFF Multi-Palette       | .mpl, .mpal                                          | IFF dynamic palette changes      | Y      | Y      | —         | R     | —   | —      | —         |
| IFF PBM                 | .lbm, .pbm                                           | IFF Packed Bitmap (chunky)       | Y      | Y      | —         | R     | —   | —      | —         |
| IFF RGB8                | .rgb8, .iff                                          | IFF 24-bit RGB format            | Y      | Y      | —         | R     | —   | —      | —         |
| IFF RGBN                | .rgbn, .iff                                          | IFF 13-bit RGB + genlock         | Y      | Y      | —         | R     | —   | —      | —         |
| IM5 Visilog             | .im5                                                 | IM5 Visilog image                | Y      | Y      | —         | —     | —   | —      | —         |
| Image System            | .ish, .ism                                           | C64 Image System                 | Y      | Y      | —         | RW    | —   | —      | —         |
| Imagic Paint            | .imp, .igp                                           | Atari ST Imagic Paint            | Y      | Y      | —         | R     | —   | —      | —         |
| Imaging Fax             | .g3n                                                 | Imaging fax format               | Y      | Y      | —         | —     | —   | —      | —         |
| IndyPaint               | .ipn, .idy                                           | Atari Falcon IndyPaint           | Y      | Y      | —         | R     | —   | —      | —         |
| Inter Paint Hires       | .iph                                                 | C64 Inter Paint hires            | Y      | Y      | —         | RW    | —   | —      | —         |
| Inter Paint Multicolor  | .ipt                                                 | C64 Inter Paint multicolor       | Y      | Y      | —         | R     | —   | —      | —         |
| Interfile               | .hv                                                  | Nuclear medicine Interfile       | Y      | Y      | —         | —     | —   | —      | —         |
| Intergraph              | .cit, .ingr                                          | Intergraph raster                | Y      | Y      | —         | —     | —   | —      | —         |
| Interlace 8             | .int8                                                | Atari 8-bit interlace 320x192   | Y      | Y      | —         | R     | —   | —      | —         |
| Interlace Hires Editor  | .ihe                                                 | C64 Interlace Hires Editor       | Y      | Y      | —         | R     | —   | —      | —         |
| Interlace Studio        | .ist                                                 | C64 Interlace Studio MC          | Y      | Y      | —         | R     | —   | —      | —         |
| IOCA                    | .ica, .ioca                                          | Image Object Content Arch.       | Y      | Y      | —         | —     | —   | —      | —         |
| IPL                     | .ipl                                                 | Image Processing Library         | Y      | Y      | —         | —     | —   | —      | —         |
| JBIG                    | .jbg, .bie, .jbig                                    | Joint Bi-level Image Group       | Y      | Y      | —         | RW    | RW  | RW     | —         |
| JBIG2                   | .jb2, .jbig2                                         | JBIG2 bi-level compression       | Y      | Y      | —         | —     | —   | —      | —         |
| JNG                     | .jng                                                 | JPEG Network Graphics            | Y      | Y      | —         | RW    | RW  | RW     | R         |
| JPEG                    | .jpg, .jpeg, .jpe, .jfif, .jps, .thm                 | Joint Photographic Experts Group | Y      | Y      | Y         | RW    | RW  | RW     | RW        |
| JPEG 2000               | .jp2, .j2k, .jpc, .jpx, .jpf, .jpt, .j2c, .jpm       | JPEG 2000 wavelet codec          | Y      | Y      | —         | RW    | RW  | RW     | RW        |
| JPEG XL                 | .jxl                                                 | JPEG XL next-gen codec           | Y      | Y      | —         | —     | RW  | RW     | RW        |
| JPEG XR                 | .jxr, .hdp, .wdp                                     | JPEG eXtended Range / HD Photo   | Y      | Y      | —         | RW    | RW  | RW     | R         |
| JPEG-LS                 | .jls                                                 | JPEG Lossless/Near-lossless      | Y      | Y      | —         | —     | —   | —      | RW        |
| Jupiter Ace             | .jac                                                 | Jupiter Ace screen dump          | Y      | Y      | —         | —     | —   | —      | —         |
| KiSS CEL                | .cel                                                 | KiSS paper doll cell             | Y      | Y      | —         | R     | —   | —      | —         |
| Koala                   | .koa, .kla                                           | C64 Koala Painter                | Y      | Y      | —         | RW    | —   | —      | —         |
| Koala Compressed        | .gg                                                  | C64 Koala compressed             | Y      | Y      | —         | R     | —   | —      | —         |
| Kofax KFX               | .kfx                                                 | Kofax Group 4 fax                | Y      | Y      | —         | RW    | —   | —      | —         |
| Krita                   | .kra                                                 | Krita native format              | Y      | Y      | —         | —     | —   | —      | —         |
| KTX                     | .ktx, .ktx2                                          | Khronos GPU Texture              | Y      | Y      | —         | —     | —   | —      | —         |
| Logo Painter            | .lp3                                                 | C64 Logo Painter 3/3+ MC         | Y      | Y      | —         | R     | —   | —      | —         |
| Logo.sys                | .sys, .logo                                          | Windows 95/98 boot logo          | Y      | Y      | —         | —     | —   | —      | —         |
| LSS16                   | .lss, .16                                            | SYSLINUX splash screen           | Y      | Y      | —         | —     | —   | —      | —         |
| LucasFilm               | .lff                                                 | LucasFilm image format           | Y      | Y      | —         | —     | —   | —      | —         |
| MacPaint                | .mac, .pntg, .pnt, .paint, .mpnt, .macp              | Apple MacPaint monochrome        | Y      | Y      | —         | R     | —   | —      | —         |
| MAG                     | .mag, .mki                                           | MAKI-chan Graphics (Japanese)    | Y      | Y      | —         | R     | —   | —      | —         |
| Magic Painter           | .mgp                                                 | Magic Painter MGP image          | Y      | Y      | —         | R     | —   | —      | —         |
| Master System Tile      | .sms, .gg                                            | Sega Master System tile          | Y      | Y      | —         | —     | —   | —      | —         |
| MATLAB                  | .mat                                                 | MATLAB image format              | Y      | Y      | —         | R     | —   | —      | —         |
| Maya IFF                | .iff, .maya                                          | Maya IFF (FOR4/CIMG)             | Y      | Y      | —         | —     | —   | —      | —         |
| MCS                     | .mcs                                                 | C64 Mcs multicolor screen        | Y      | Y      | —         | R     | —   | —      | —         |
| MegaPaint               | .bld                                                 | Atari ST MegaPaint               | Y      | Y      | —         | R     | —   | —      | —         |
| MetaImage               | .mha, .mhd                                           | MetaImage medical format         | Y      | Y      | —         | —     | —   | —      | —         |
| MGR Bitmap              | .mgr                                                 | MGR Window Manager bitmap        | Y      | Y      | —         | —     | —   | —      | —         |
| Micro Illustrator       | .mil                                                 | C64 Micro Illustrator            | Y      | Y      | —         | RW    | —   | —      | —         |
| Micro Illustrator (A8)  | .mia                                                 | Atari 8-bit Micro Illustrator   | Y      | Y      | —         | R     | —   | —      | —         |
| Micro Painter (Atari)   | .mpt8, .mp8                                          | Atari 8-bit Micro Painter       | Y      | Y      | —         | R     | —   | —      | —         |
| MIFF                    | .miff                                                | ImageMagick native format        | Y      | Y      | —         | RW    | RW  | RW     | —         |
| MLT                     | .mlt                                                 | C64 Mlt multicolor art           | Y      | Y      | —         | R     | —   | —      | —         |
| MNG                     | .mng                                                 | Multiple Network Graphics        | Y      | Y      | —         | RW    | —   | RW     | R         |
| Mobile Fax              | .rfa                                                 | Mobile fax format                | Y      | Y      | —         | —     | —   | —      | —         |
| Moby Dick               | .mby, .mbd                                           | Moby Dick paint                  | Y      | Y      | —         | —     | —   | —      | —         |
| Mono Magic              | .mon                                                 | C64 Mono Magic                   | Y      | Y      | —         | RW    | —   | —      | —         |
| MonoStar                | .mst, .mns                                           | Atari ST MonoStar 640x400 mono  | Y      | Y      | —         | R     | —   | —      | —         |
| MPO                     | .mpo                                                 | Multi-Picture Object (stereo)    | Y      | Y      | —         | —     | —   | —      | —         |
| MRC                     | .mrc                                                 | Medical Research Council         | Y      | Y      | —         | —     | —   | R      | —         |
| MSP                     | .msp                                                 | Microsoft Paint v1/v2            | Y      | Y      | —         | R     | —   | RW     | —         |
| MSX                     | .sc2                                                 | MSX Screen 2 graphics            | Y      | Y      | —         | R     | —   | —      | —         |
| MSX Font                | .fnt, .mft                                           | MSX character set 256x8          | Y      | Y      | —         | —     | —   | —      | —         |
| MSX Screen 2            | .sc2, .grp                                           | MSX Screen 2 (TMS9918)           | Y      | Y      | —         | R     | —   | —      | —         |
| MSX Screen 5            | .sc5, .ge5                                           | MSX2 Screen 5 indexed            | Y      | Y      | —         | R     | —   | —      | —         |
| MSX Screen 8            | .sc8                                                 | MSX2 Screen 8 256-color          | Y      | Y      | —         | R     | —   | —      | —         |
| MSX Sprite              | .spt                                                 | MSX sprite pattern table         | Y      | Y      | —         | —     | —   | —      | —         |
| MSX Video               | .mvi                                                 | MSX2 Video screen capture        | Y      | Y      | —         | —     | —   | —      | —         |
| MSX View                | .mvw, .msv                                           | MSX2 View Screen 8 image        | Y      | Y      | —         | —     | —   | —      | —         |
| MTV Ray Tracer          | .mtv                                                 | MTV ray-tracer output            | Y      | Y      | —         | RW    | —   | —      | —         |
| MUIFLI Editor           | .muf, .mui, .mup                                    | C64 MUIFLI Interlace image       | Y      | Y      | —         | R     | —   | —      | —         |
| Multi Painter           | .mpt, .mlt64                                         | C64 Multi Painter                | Y      | Y      | —         | R     | —   | —      | —         |
| Multi-Lace Editor       | .mle                                                 | C64 Multi-Lace multicolor        | Y      | Y      | —         | R     | —   | —      | —         |
| Multi-Palette Picture   | .mpp                                                 | Atari ST Multi-Palette Picture   | Y      | Y      | —         | R     | —   | —      | —         |
| NDS Texture             | .nbfs                                                | Nintendo DS tile texture         | Y      | Y      | —         | —     | —   | —      | —         |
| Neo Geo Pocket          | .ngp, .ngpc                                          | Neo Geo Pocket tile              | Y      | Y      | —         | —     | —   | —      | —         |
| Neo Geo Sprite          | .spr                                                 | Neo Geo sprite data              | Y      | Y      | —         | —     | —   | —      | —         |
| NEOchrome               | .neo                                                 | Atari ST NEOchrome               | Y      | Y      | —         | R     | —   | —      | —         |
| NES CHR                 | .chr                                                 | NES character ROM tile           | Y      | Y      | —         | —     | —   | —      | —         |
| Netpbm                  | .pbm, .pgm, .ppm, .pnm, .pam                         | Portable anymap family           | Y      | Y      | —         | RW    | RW  | RW     | RW        |
| NewsRoom                | .nsr                                                 | NewsRoom clip art                | Y      | Y      | —         | —     | —   | —      | —         |
| NIE                     | .nie                                                 | Wuffs Naive Image format         | Y      | Y      | —         | —     | —   | —      | —         |
| NIfTI                   | .nii, .nii.gz                                        | Neuroimaging Informatics         | Y      | Y      | —         | —     | —   | R      | —         |
| NIST IHead              | .nst                                                 | NIST IHead biometric             | Y      | Y      | —         | —     | —   | —      | —         |
| NITF                    | .ntf, .nitf                                          | National Imagery Transmission    | Y      | Y      | —         | —     | —   | —      | —         |
| Nokia Group Graphics    | .ngg                                                 | Nokia group graphic message      | Y      | Y      | —         | RW    | —   | RW     | —         |
| Nokia Logo              | .nol                                                 | Nokia operator logo              | Y      | Y      | —         | RW    | —   | —      | —         |
| Nokia NLM               | .nlm                                                 | Nokia Logo Manager               | Y      | Y      | —         | RW    | —   | —      | —         |
| Nokia Operator Logo     | .nol                                                 | Nokia Operator Logo bitmap       | Y      | Y      | —         | RW    | —   | —      | —         |
| Nokia Picture Message   | .npm                                                 | Nokia Picture Message mono       | Y      | Y      | —         | RW    | —   | —      | —         |
| NRRD                    | .nrrd                                                | Nearly Raw Raster Data           | Y      | Y      | —         | —     | —   | —      | —         |
| NUFLI Editor            | .nuf, .nup                                           | C64 NUFLI multicolor image       | Y      | Y      | —         | R     | —   | —      | —         |
| OAZ Fax                 | .oaz, .xfx                                           | OAZ fax format                   | Y      | Y      | —         | —     | —   | —      | —         |
| Olicom Fax              | .ofx                                                 | Olicom fax format                | Y      | Y      | —         | —     | —   | —      | —         |
| OLPC 565                | .565                                                 | OLPC RGB565 bitmap               | Y      | Y      | —         | —     | —   | —      | —         |
| OpenEXR                 | .exr                                                 | HDR image format by ILM          | Y      | Y      | —         | RW    | RW  | RW     | R         |
| OpenRaster              | .ora                                                 | Open raster layered image        | Y      | Y      | —         | —     | R   | RW     | —         |
| Oric                    | .hir, .tap                                           | Oric hi-res screen               | Y      | Y      | —         | R     | —   | —      | —         |
| OTB                     | .otb                                                 | Nokia Over The Air Bitmap        | Y      | Y      | —         | RW    | —   | —      | —         |
| Pablo Paint             | .pa3                                                 | Atari ST Pablo Paint 640x400     | Y      | Y      | —         | R     | —   | —      | —         |
| Pagefox (Hires)         | .pfx                                                 | C64 Pagefox 640x200 monochrome  | Y      | Y      | —         | —     | —   | —      | —         |
| Paint Magic             | .pmg                                                 | C64 Paint Magic                  | Y      | Y      | —         | RW    | —   | —      | —         |
| Paint Pro               | .ppro                                                | Atari ST Paint Pro               | Y      | Y      | —         | R     | —   | —      | —         |
| Palm                    | .palm                                                | Palm OS bitmap                   | Y      | Y      | —         | RW    | —   | —      | —         |
| Palm PDB                | .pdb                                                 | Palm Database image viewer       | Y      | Y      | —         | R     | —   | —      | —         |
| PAT                     | .pat                                                 | GIMP Pattern                     | Y      | Y      | —         | —     | —   | —      | —         |
| PC Engine Tile          | .pce                                                 | PC Engine/TurboGrafx tile        | Y      | Y      | —         | —     | —   | —      | —         |
| PC Paint/Pictor         | .pic, .clp                                           | PC Paint/Pictor page format      | Y      | Y      | —         | R     | —   | —      | —         |
| PC-88                   | .pc8                                                 | NEC PC-88 graphics               | Y      | Y      | —         | —     | —   | —      | —         |
| PCO 16-bit              | .b16                                                 | PCO 16-bit image                 | Y      | Y      | —         | —     | —   | —      | —         |
| PCX                     | .pcx, .pcc, .fcx                                     | ZSoft PC Paintbrush              | Y      | Y      | Y         | RW    | RW  | RW     | RW        |
| PDF                     | .pdf                                                 | PDF embedded image extractor     | Y      | Y      | —         | R     | RW  | R      | RW        |
| PDN                     | .pdn                                                 | Paint.NET native format          | Y      | Y      | —         | —     | —   | —      | R         |
| PDS                     | .pds                                                 | Planetary Data System            | Y      | Y      | —         | —     | —   | —      | —         |
| PFM                     | .pfm                                                 | Portable Float Map               | Y      | Y      | —         | RW    | —   | —      | —         |
| PHM                     | .phm                                                 | Portable Half Map (fp16)         | Y      | Y      | —         | —     | RW  | —      | —         |
| Photo CD                | .pcd                                                 | Kodak Photo CD                   | Y      | Y      | —         | RW    | RW  | —      | R         |
| PhotoChrome             | .pcf, .phc                                           | Atari Falcon PhotoChrome         | Y      | Y      | —         | R     | —   | —      | —         |
| Pi                      | .pi                                                  | Japanese NEC PC-98 image         | Y      | Y      | —         | R     | —   | —      | —         |
| PIC2                    | .p2                                                  | Japanese PIC2 format             | Y      | Y      | —         | —     | —   | —      | —         |
| Picasso 64              | .p64                                                 | C64 Picasso 64                   | Y      | Y      | —         | RW    | —   | —      | —         |
| PICT                    | .pict, .pct                                          | Apple QuickDraw PICT             | Y      | Y      | —         | RW    | —   | —      | R         |
| Picture Editor          | .ped                                                 | Atari 8-bit Picture Editor       | Y      | Y      | —         | R     | —   | —      | —         |
| PicWorks                | .pwk, .pws                                           | Atari ST PicWorks paint          | Y      | Y      | —         | R     | —   | —      | —         |
| Pixar RIB               | .pxr, .pixar, .picio                                 | Pixar RenderMan image            | Y      | Y      | —         | —     | —   | —      | —         |
| Pixel 64                | .px64, .px                                           | C64 Pixel Perfect paint          | Y      | Y      | —         | R     | —   | —      | —         |
| Pixel Perfect           | .pp, .ppp                                            | C64 Pixel Perfect multicolor     | Y      | Y      | —         | R     | —   | —      | —         |
| PKM                     | .pkm                                                 | Ericsson ETC texture             | Y      | Y      | —         | —     | —   | —      | —         |
| Plot Maker              | .plt, .plm2                                          | Plot Maker monochrome image      | Y      | Y      | —         | —     | —   | —      | —         |
| PM Bitmap               | .pm1, .pm2, .pm3, .pm4                               | PM bitmap format                 | Y      | Y      | —         | —     | —   | —      | —         |
| PNG                     | .png                                                 | Portable Network Graphics        | Y      | Y      | Y         | RW    | RW  | RW     | RW        |
| PntrFalcon              | .pnf, .pfl                                           | Atari Falcon PntrFalcon          | Y      | Y      | —         | R     | —   | —      | —         |
| Pocket PC 2BP           | .2bp                                                 | Pocket PC 2-bit bitmap           | Y      | Y      | —         | —     | —   | —      | —         |
| Portfolio Graphics      | .pgf, .pgc                                           | Atari Portfolio graphics         | Y      | Y      | —         | R     | —   | —      | —         |
| Print Shop              | .psa                                                 | Print Shop sign/graphic          | Y      | Y      | —         | —     | —   | —      | —         |
| Printfox/Pagefox        | .bs, .pg                                             | C64 Printfox/Pagefox             | Y      | Y      | —         | —     | —   | —      | —         |
| PrintMaster             | .pm                                                  | PrintMaster clip art             | Y      | Y      | —         | —     | —   | —      | —         |
| Prism Paint             | .pnt, .tpi                                           | Atari Falcon Prism Paint         | Y      | Y      | —         | R     | —   | —      | —         |
| PS2 TXC                 | .txc                                                 | PlayStation 2 texture            | Y      | Y      | —         | —     | —   | —      | —         |
| PSB                     | .psb                                                 | Photoshop Large Document         | Y      | Y      | —         | RW    | RW  | R      | —         |
| PSD                     | .psd                                                 | Adobe Photoshop Document         | Y      | Y      | —         | RW    | RW  | RW     | R         |
| Psion PIC               | .ppic                                                | Psion Series 3 picture           | Y      | Y      | —         | R     | —   | —      | —         |
| PSP                     | .psp, .pspimage                                      | Paint Shop Pro native            | Y      | Y      | —         | —     | —   | R      | R         |
| Public Painter          | .cmp                                                 | Atari ST Public Painter mono     | Y      | Y      | —         | R     | —   | —      | —         |
| PVR                     | .pvr                                                 | PowerVR GPU texture              | Y      | Y      | —         | —     | —   | —      | R         |
| Pyramid TIFF            | .ptif                                                | Pyramid-encoded TIFF             | Y      | Y      | —         | RW    | RW  | —      | —         |
| Q0                      | .q0                                                  | Japanese Q0 image                | Y      | Y      | —         | —     | —   | —      | —         |
| QDV Image               | .qdv                                                 | QDV image format                 | Y      | Y      | —         | —     | —   | —      | —         |
| QOI                     | .qoi                                                 | Quite OK Image Format            | Y      | Y      | —         | —     | RW  | —      | RW        |
| QRT Ray Tracer          | .qrt                                                 | QRT ray-tracer output            | Y      | Y      | —         | —     | —   | —      | —         |
| QTIF                    | .qtif, .qti                                          | QuickTime Image Format           | Y      | Y      | —         | —     | —   | RW     | R         |
| Quake LMP               | .lmp                                                 | Quake engine lump texture        | Y      | Y      | —         | —     | —   | —      | —         |
| Quake SPR               | .spr                                                 | Quake sprite format              | Y      | Y      | —         | —     | —   | —      | —         |
| Quantel VPB             | .vpb                                                 | Quantel Paintbox image           | Y      | Y      | —         | —     | —   | —      | —         |
| QuantumPaint            | .pbx                                                 | Atari ST QuantumPaint 320x200   | Y      | Y      | —         | R     | —   | —      | —         |
| Rage Paint              | .rge                                                 | Atari Falcon Rage Paint TC       | Y      | Y      | —         | R     | —   | —      | —         |
| Rainbow Painter         | .rp                                                  | C64 Rainbow Painter              | Y      | Y      | —         | RW    | —   | —      | —         |
| Ram Brandt              | .rmb, .rbr                                           | Atari 8-bit Ram Brandt 160x192  | Y      | Y      | —         | R     | —   | —      | —         |
| Red Storm RSB           | .rsb                                                 | Red Storm Entertainment tex      | Y      | Y      | —         | —     | —   | —      | —         |
| Rembrandt               | .tcp                                                 | Atari Falcon Rembrandt TC        | Y      | Y      | —         | R     | —   | —      | —         |
| RGF                     | .rgf                                                 | LEGO Mindstorms EV3 graphic      | Y      | Y      | —         | RW    | —   | —      | —         |
| Ricoh Fax               | .ric                                                 | Ricoh fax format                 | Y      | Y      | —         | —     | —   | —      | —         |
| RISC OS Sprite          | .ros                                                 | RISC OS sprite format            | Y      | Y      | —         | R     | —   | R      | —         |
| RLA/RPF                 | .rla, .rlb, .rpf                                     | Wavefront Advanced Visualizer    | Y      | Y      | —         | R     | R   | RW     | R         |
| RLC2                    | .rlc                                                 | RLC2 image format                | Y      | Y      | —         | —     | —   | —      | —         |
| Rocky Interlace         | .rip                                                 | C64 Rocky Interlace hires        | Y      | Y      | —         | R     | —   | —      | —         |
| Run Paint               | .rpm                                                 | C64 Run Paint                    | Y      | Y      | —         | RW    | —   | —      | —         |
| SAM Coupé               | .scs4, .ss4                                          | SAM Coupé screen                 | Y      | Y      | —         | R     | —   | —      | —         |
| Saracen Paint           | .sar                                                 | C64 Saracen Paint                | Y      | Y      | —         | RW    | —   | —      | —         |
| SBIG CCD                | .st4, .stx, .st5, .st6, .st7, .st8                   | SBIG CCD camera image            | Y      | Y      | —         | —     | —   | —      | —         |
| Sci-Fax                 | .scf                                                 | Sci-Fax fax format               | Y      | Y      | —         | —     | —   | —      | —         |
| Scitex CT               | .sct, .ct, .ch                                       | Scitex Continuous Tone           | Y      | Y      | —         | R     | —   | RW     | —         |
| Screen Blaster          | .sbl                                                 | Atari ST Screen Blaster          | Y      | Y      | —         | R     | —   | —      | —         |
| Screen Maker            | .smk                                                 | Screen Maker image               | Y      | Y      | —         | —     | —   | —      | —         |
| SDT                     | .sdt                                                 | SDT image format                 | Y      | Y      | —         | —     | —   | —      | —         |
| Seattle Film Works      | .sfw, .pwp                                           | Seattle FilmWorks JPEG-wrapped   | Y      | Y      | —         | —     | —   | RW     | R         |
| Sega Genesis Tile       | .gen, .sgd                                           | Sega Genesis/Mega Drive tile     | Y      | Y      | —         | —     | —   | —      | —         |
| Sega SJ-1               | .sj1                                                 | Sega SJ-1 camera image           | Y      | Y      | —         | —     | —   | —      | —         |
| SEQ Image               | .seq                                                 | SEQ sequence image               | Y      | Y      | —         | —     | —   | —      | —         |
| SFF                     | .sff                                                 | Structured Fax File              | Y      | Y      | —         | —     | —   | R      | R         |
| SGI                     | .sgi, .rgb, .rgba, .bw, .iris, .inta                 | Silicon Graphics RGB             | Y      | Y      | —         | RW    | RW  | RW     | R         |
| SHAM                    | .sham                                                | Amiga Sliced HAM                 | Y      | Y      | —         | R     | —   | —      | —         |
| Sharp X68000            | .x68, .x68k                                          | Sharp X68000 graphics            | Y      | Y      | —         | R     | —   | —      | —         |
| Siemens BMX             | .bmx                                                 | Siemens phone bitmap             | Y      | Y      | —         | —     | —   | —      | —         |
| SIF Image               | .sif                                                 | SIF image format                 | Y      | Y      | —         | —     | —   | —      | R         |
| Sinbad Slideshow        | .ssb                                                 | Atari ST Sinbad Slideshow        | Y      | Y      | —         | R     | —   | —      | —         |
| Sixel                   | .six, .sixel                                         | DEC terminal Sixel graphics      | Y      | Y      | —         | —     | —   | —      | —         |
| SmartFax                | .smf                                                 | SmartFax fax format              | Y      | Y      | —         | —     | —   | —      | —         |
| SNES Tile               | .sfc, .snes                                          | Super NES 4bpp tile              | Y      | Y      | —         | —     | —   | —      | —         |
| Softimage PIC           | .pic, .si                                            | Softimage 3D texture             | Y      | Y      | —         | —     | —   | —      | —         |
| Software Automation     | .sag, .swa                                           | Atari 8-bit Software Automation  | Y      | Y      | —         | R     | —   | —      | —         |
| Sony Mavica             | .411                                                 | Sony Mavica still image          | Y      | Y      | —         | —     | —   | —      | —         |
| SPC Painter             | .spp, .spc2                                          | Atari ST SPC Painter             | Y      | Y      | —         | R     | —   | —      | —         |
| Speccy eXtended         | .sxg                                                 | ZX Spectrum extended graphics    | Y      | Y      | —         | R     | —   | —      | —         |
| Spectrum 512            | .spu                                                 | Atari ST Spectrum 512            | Y      | Y      | —         | R     | —   | —      | —         |
| Spectrum 512 Compressed | .spc                                                 | Atari ST Spectrum 512 comp.      | Y      | Y      | —         | R     | —   | —      | —         |
| Spectrum 512 Extended   | .spx                                                 | Atari ST Spectrum 512 Extended   | Y      | Y      | —         | R     | —   | —      | —         |
| Spectrum 512 Smooshed   | .sps                                                 | Atari ST Spectrum 512 smsh.      | Y      | Y      | —         | R     | —   | —      | —         |
| Speeder Falcon          | .spf                                                 | Atari Falcon Speeder TC          | Y      | Y      | —         | R     | —   | —      | —         |
| Spooky Sprites Falcon   | .tre                                                 | Atari Falcon Spooky Sprites TC   | Y      | Y      | —         | R     | —   | —      | —         |
| SPOT Image              | .spot                                                | SPOT satellite image             | Y      | Y      | —         | —     | —   | —      | —         |
| Sprite 64               | .s64, .spr64                                         | C64 sprite data 24x21            | Y      | Y      | —         | —     | —   | —      | —         |
| SpritePad               | .spd                                                 | C64 SpritePad sprite collection  | Y      | Y      | —         | R     | —   | —      | —         |
| ST True Color           | .stc                                                 | Atari STE 12-bit true color      | Y      | Y      | —         | R     | —   | —      | —         |
| STAD                    | .pac                                                 | Atari ST STAD packed image       | Y      | Y      | —         | RW    | —   | —      | —         |
| Stela RAW               | .hsi                                                 | Stela HSI raw image              | Y      | Y      | —         | —     | —   | —      | —         |
| Sun Icon                | .icon                                                | Sun Microsystems icon (text)     | Y      | Y      | —         | —     | —   | —      | —         |
| Sun Raster              | .ras, .rast, .sun, .rs                               | Sun Microsystems raster          | Y      | Y      | —         | RW    | —   | RW     | R         |
| Super Hires             | .shi                                                 | C64 interlace hires              | Y      | Y      | —         | R     | —   | —      | —         |
| Super Hires Editor      | .she                                                 | C64 Super Hires Editor           | Y      | Y      | —         | R     | —   | —      | —         |
| Symbian MBM             | .mbm                                                 | Symbian OS multi-bitmap          | Y      | Y      | —         | —     | —   | —      | —         |
| Synthetic Arts          | .srt                                                 | Atari ST Synthetic Arts medium   | Y      | Y      | —         | R     | —   | —      | —         |
| Teli Fax                | .mh                                                  | Teli fax format                  | Y      | Y      | —         | —     | —   | —      | —         |
| TG4 Fax                 | .tg4                                                 | TG4 fax format                   | Y      | Y      | —         | —     | —   | —      | —         |
| TGA                     | .tga, .icb, .vda, .vst, .bpx, .targa, .ivb           | Truevision Targa                 | Y      | Y      | Y         | RW    | RW  | RW     | RW        |
| Thomson                 | .map                                                 | Thomson MO/TO screen             | Y      | Y      | —         | —     | —   | —      | —         |
| TI Bitmap               | .8xi, .89i                                           | TI calculator bitmap             | Y      | Y      | —         | —     | —   | —      | —         |
| TIFF                    | .tif, .tiff, .ftf                                    | Tagged Image File Format         | Y      | Y      | Y         | RW    | RW  | RW     | RW        |
| TIM                     | .tim                                                 | PlayStation 1 texture            | Y      | Y      | —         | R     | R   | —      | —         |
| TIM2                    | .tm2                                                 | PlayStation 2/PSP texture        | Y      | Y      | —         | —     | —   | —      | —         |
| Tiny                    | .tn1, .tn2, .tn3, .tny                               | Atari ST Tiny compressed         | Y      | Y      | —         | R     | —   | —      | —         |
| TriPaint                | .tpf                                                 | Atari Falcon TriPaint TC         | Y      | Y      | —         | R     | —   | —      | —         |
| TRS-80                  | .hr                                                  | TRS-80 hi-res screen dump        | Y      | Y      | —         | R     | —   | —      | —         |
| True Paint              | .mci                                                 | C64 True Paint interlace MC      | Y      | Y      | —         | R     | —   | —      | —         |
| Turbo View              | .tvw, .tbv                                           | Atari ST Turbo View              | Y      | Y      | —         | R     | —   | —      | —         |
| UFLI Editor             | .ufl                                                 | C64 UFLI multicolor image        | Y      | Y      | —         | R     | —   | —      | —         |
| Ultra HDR               | .uhdr                                                | Ultra HDR JPEG container         | Y      | Y      | —         | —     | RW  | —      | —         |
| Utah RLE                | .rle, .urt                                           | Utah Raster Toolkit RLE          | Y      | Y      | —         | R     | R   | —      | R         |
| Vector-06c              | .v06                                                 | Vector-06c Soviet computer       | Y      | Y      | —         | —     | —   | —      | —         |
| VentaFax                | .vfx                                                 | VentaFax fax format              | Y      | Y      | —         | —     | —   | —      | —         |
| VIC-20                  | .vic20                                               | Commodore VIC-20 graphics        | Y      | Y      | —         | R     | —   | —      | —         |
| VICAR                   | .vicar                                               | VICAR planetary image            | Y      | Y      | —         | RW    | RW  | —      | —         |
| Vidcom 64               | .vid                                                 | C64 Vidcom 64Paint               | Y      | Y      | —         | R     | —   | —      | —         |
| VidiChrome              | .vdc, .vdc2                                          | VidiChrome 320x240 RGB565 TC    | Y      | Y      | —         | —     | —   | —      | —         |
| VIFF                    | .viff                                                | Khoros Visualization image       | Y      | Y      | —         | RW    | RW  | —      | —         |
| VIPS                    | .v, .vips                                            | VIPS image processing lib        | Y      | Y      | —         | —     | —   | —      | —         |
| Virtual Boy Tile        | .vbt, .vboy                                          | Virtual Boy 2bpp tile            | Y      | Y      | —         | —     | —   | —      | —         |
| Vivid Ray Tracer        | .vivid, .dis                                         | Vivid ray-tracer output          | Y      | Y      | —         | —     | —   | —      | —         |
| VTF                     | .vtf                                                 | Valve Texture Format             | Y      | Y      | —         | —     | —   | R      | —         |
| WAD                     | .wad                                                 | Doom WAD container               | Y      | Y      | —         | —     | —   | R      | R         |
| WAD2                    | .wad                                                 | Quake WAD2 texture pack          | Y      | Y      | —         | —     | —   | —      | —         |
| WAD3                    | .wad                                                 | Half-Life WAD3 texture pack      | Y      | Y      | —         | —     | —   | —      | R         |
| WAL                     | .wal                                                 | Quake 2 MIP texture              | Y      | Y      | —         | —     | —   | R      | R         |
| WBMP                    | .wbmp                                                | Wireless Application Bitmap      | Y      | Y      | —         | RW    | RW  | —      | R         |
| WebP                    | .webp, .wep                                          | Google WebP image                | Y      | Y      | Y         | RW    | RW  | RW     | RW        |
| WebShots                | .wb1, .wbc, .wbp, .wbz                               | WebShots wallpaper               | Y      | Y      | —         | —     | —   | —      | R         |
| Wigmore Artist          | .wig                                                 | C64 Wigmore Artist hires         | Y      | Y      | —         | R     | —   | —      | —         |
| Windows Clipboard       | .clp                                                 | Windows Clipboard bitmap         | Y      | Y      | —         | RW    | —   | —      | R         |
| Windows PE Resource     | .exe, .dll, .ocx, .scr, .cpl                         | PE icon/cursor/bitmap extractor  | Y      | Y      | —         | —     | —   | —      | —         |
| WinFax                  | .fxs, .fxo, .fxr, .fxd, .fxm                         | WinFax fax format                | Y      | Y      | —         | —     | —   | R      | —         |
| WMF                     | .wmf                                                 | Windows Metafile                 | Y      | Y      | —         | R     | R   | RW     | R         |
| WonderSwan Tile         | .wst, .ws                                            | WonderSwan 2bpp tile             | Y      | Y      | —         | —     | —   | —      | —         |
| Worldport Fax           | .wpf                                                 | Worldport fax format             | Y      | Y      | —         | —     | —   | —      | —         |
| WPG                     | .wpg                                                 | WordPerfect Graphics             | Y      | Y      | —         | R     | R   | —      | —         |
| WSQ                     | .wsq                                                 | Wavelet Scalar Quantization      | Y      | Y      | —         | —     | —   | —      | R         |
| X-FLI Editor            | .xfl                                                 | C64 X-FLI extended FLI MC        | Y      | Y      | —         | R     | —   | —      | —         |
| XBM                     | .xbm                                                 | X11 Bitmap (text-based)          | Y      | Y      | —         | RW    | RW  | RW     | R         |
| XCF                     | .xcf                                                 | GIMP native image                | Y      | Y      | —         | R     | —   | RW     | R         |
| Xcursor                 | .xcur, .cursor                                       | X11 cursor theme format          | Y      | Y      | —         | —     | —   | —      | —         |
| XPM                     | .xpm                                                 | X11 PixMap (text-based)          | Y      | Y      | —         | RW    | RW  | RW     | R         |
| XV Thumbnail            | .xv                                                  | XV Visual Schnauzer thumb        | Y      | Y      | —         | RW    | —   | —      | —         |
| XWD                     | .xwd                                                 | X Window Dump                    | Y      | Y      | —         | RW    | RW  | —      | —         |
| XYZ                     | .xyz                                                 | RPG Maker XYZ indexed            | Y      | Y      | —         | —     | —   | —      | —         |
| YJK Image               | .yjk                                                 | MSX2+ YJK color encoding         | Y      | Y      | —         | —     | —   | —      | —         |
| YUV Raw                 | .yuv                                                 | Raw YUV pixel data               | Y      | Y      | —         | —     | RW  | —      | RW        |
| Zeiss BIVAS             | .dta                                                 | Zeiss BIVAS microscopy           | Y      | Y      | —         | —     | —   | —      | —         |
| Zeiss LSM               | .lsm                                                 | Zeiss LSM confocal microscopy    | Y      | Y      | —         | —     | —   | —      | —         |
| Zoomatic                | .zom                                                 | C64 Zoomatic multicolor art      | Y      | Y      | —         | R     | —   | —      | —         |
| ZX Art Studio           | .zas                                                 | ZX Spectrum Art Studio           | Y      | Y      | —         | R     | —   | —      | —         |
| ZX Border Multicolor    | .bmc4                                                | ZX Spectrum border multicolor    | Y      | Y      | —         | R     | —   | —      | —         |
| ZX Border Screen        | .bsc                                                 | ZX Spectrum border screen        | Y      | Y      | —         | R     | —   | —      | —         |
| ZX CHRD                 | .chr, .chrd                                          | ZX Spectrum character set        | Y      | Y      | —         | —     | —   | —      | —         |
| ZX Flash                | .zfl                                                 | ZX Spectrum Flash cycling        | Y      | Y      | —         | —     | —   | —      | —         |
| ZX Gigascreen           | .gsc                                                 | ZX Spectrum dual-screen blend    | Y      | Y      | —         | R     | —   | —      | —         |
| ZX MLG                  | .mlg                                                 | ZX Spectrum MLG editor           | Y      | Y      | —         | R     | —   | —      | —         |
| ZX MultiArtist          | .mg1, .mg2, .mg4, .mg8                               | ZX Spectrum MultiArtist          | Y      | Y      | —         | R     | —   | —      | —         |
| ZX Multicolor           | .mlt                                                 | ZX Spectrum per-scanline attr    | Y      | Y      | —         | R     | —   | —      | —         |
| ZX Next                 | .nxt                                                 | ZX Spectrum Next 256-color       | Y      | Y      | —         | —     | —   | —      | —         |
| ZX Spectrum             | .scr, .$s, .$c, .!s                                  | ZX Spectrum screen dump          | Y      | Y      | —         | R     | —   | R      | R         |
| ZX Timex                | .tmx                                                 | Timex HiColor extended attrs     | Y      | Y      | —         | R     | —   | —      | —         |
| ZX Tricolor             | .3cl                                                 | ZX Spectrum triple-screen blend  | Y      | Y      | —         | R     | —   | —      | —         |
| ZX ULAplus              | .ulp                                                 | ZX Spectrum ULAplus palette      | Y      | Y      | —         | —     | —   | —      | —         |
| ZX-Paintbrush           | .zxp                                                 | ZX Spectrum ZX-Paintbrush        | Y      | Y      | —         | R     | —   | —      | —         |
| ZX81                    | .zx81, .p81                                          | Sinclair ZX81 screen             | Y      | Y      | —         | —     | —   | —      | —         |

---

## Not Implemented — Out of Scope

Formats we consciously chose not to implement, with reason codes:

- **vector** — Vector/document format, not a raster image
- **proprietary** — Proprietary codec with no public specification
- **app-specific** — Application project file, not an image format
- **3d-model** — 3D model format, not a 2D image
- **video** — Video/animation container (not still image)
- **font** — Font file, not an image
- **audio** — Audio format
- **meta** — ImageMagick pseudo-format or metaformat

| Format            | Extensions  | Reason       | Tom's | IM  | XnView | IrfanView |
| ----------------- | ----------- | ------------ | ----- | --- | ------ | --------- |
| 3DS Max Thumbnail | .max        | app-specific | —     | —   | R      | —         |
| Adobe Illustrator | .ai         | vector       | R     | RW  | R      | —         |
| Affinity Designer | .afdesign   | app-specific | —     | —   | R      | —         |
| Affinity Photo    | .afphoto    | app-specific | —     | —   | R      | —         |
| AVI               | .avi        | video        | R     | R   | —      | —         |
| Blender           | .blend      | app-specific | —     | —   | —      | —         |
| CGM               | .cgm        | vector       | R     | —   | —      | —         |
| Cinema 4D         | .c4d        | 3d-model     | —     | —   | —      | —         |
| Clip Studio Paint | .clip       | app-specific | —     | —   | R      | —         |
| CorelDRAW         | .cdr        | vector       | —     | —   | R      | —         |
| Crayola           | .crayola    | app-specific | —     | —   | —      | —         |
| DWG               | .dwg        | vector       | —     | —   | R      | —         |
| DXF               | .dxf        | vector       | R     | —   | R      | R         |
| FIG               | .fig        | vector       | R     | —   | —      | —         |
| Flash SWF         | .swf        | video/vector | —     | —   | —      | R         |
| GnuPlot           | .gplt       | vector       | R     | —   | —      | —         |
| Graphviz DOT      | .dot        | vector       | —     | R   | —      | —         |
| HPGL              | .hpgl       | vector       | R     | —   | —      | —         |
| LuraWave          | .lwf        | proprietary  | —     | —   | RW     | —         |
| MVG               | .mvg        | meta         | R     | RW  | —      | —         |
| MrSID             | .sid        | proprietary  | —     | —   | —      | R         |
| PCL               | .pcl        | vector       | —     | —   | —      | —         |
| PES (embroidery)  | .pes        | app-specific | R     | R   | —      | —         |
| SVG               | .svg        | vector       | R     | RW  | R      | R         |
| TrueType Font     | .ttf        | font         | R     | —   | —      | R         |
| MPEG video        | .mpg, .mpeg | video        | R     | —   | —      | —         |

---

## Not Implemented — Potential Future

Formats with public or partially-public specifications that could be added in future waves.

| Format                 | Extensions | Description                   | Tom's | IM  | XnView | IrfanView |
| ---------------------- | ---------- | ----------------------------- | ----- | --- | ------ | --------- |
| Artweaver              | .awd       | Artweaver paint program       | —     | —   | —      | R         |
| Basis Universal        | .basis     | GPU supercompressed texture   | —     | —   | —      | —         |
| BodyPaint 3D           | .b3d       | Maxon texture paint           | —     | —   | —      | R         |
| Casio CAM              | .cam       | Casio camera format           | —     | —   | —      | R         |
| CDXL                   | .cdxl      | Amiga CDXL animation          | —     | —   | —      | —         |
| Cloud Optimized GeoTIFF| .tif       | COG tile-optimized TIFF       | —     | —   | —      | —         |
| ECAT                   | .v         | PET scanner sinogram data     | —     | —   | —      | —         |
| GnuPlot output         | .gplt      | GnuPlot raster output         | R     | —   | —      | —         |
| GRASP GL               | .gl        | GRASP animation               | —     | —   | —      | —         |
| Image Cytometry (full) | .ics       | Full ICS stack support        | —     | —   | —      | R         |
| JPEG XS                | .jxs       | JPEG XS low-latency codec     | —     | —   | —      | —         |
| JPEG-XT                | .jxl       | JPEG XT extension             | —     | —   | —      | —         |
| Logluv TIFF            | .tif       | TIFF LogLuv HDR extension     | —     | —   | —      | —         |
| LuraDocument           | .ldf       | LuraDocument format           | —     | —   | RW     | —         |
| MINC                   | .mnc       | Medical NetCDF neuroimaging   | —     | —   | —      | —         |
| OME-TIFF               | .ome.tif   | Open Microscopy multi-channel | —     | —   | —      | —         |
| ORA (full layers)      | .ora       | Full OpenRaster layer support | —     | R   | RW     | —         |
| PaintShop (Atari)      | .da4       | Atari ST PaintShop            | RW    | —   | —      | —         |
| PCL raster             | .pcl       | HP PCL raster subset          | —     | —   | —      | —         |
| PIX (TRS-80)           | .pix       | TRS-80 Color Computer PIX     | R     | —   | —      | —         |
| Psion Series 3 (full)  | .icn       | Psion multi-format support    | R     | —   | —      | —         |
| QSS/Fuji print         | .qss       | Fuji digital print format     | —     | —   | —      | —         |
| SIF (Atari)            | .sif       | Atari 8-bit SIF font          | R     | —   | —      | —         |
| SPIFF                  | .spf       | Still Picture Interchange FF  | —     | —   | —      | —         |
| X BitMap (color)       | .xbm       | Extended color XBM            | —     | —   | —      | —         |


---

## Statistics

| Metric                                     | Count                  |
| ------------------------------------------ | ---------------------- |
| Implemented formats (with reader + writer) | 531                    |
| Formats with dedicated optimizer           | 11                     |
| Formats in Tom's Editor (approx.)          | 613+ input, 150 output |
| Formats in ImageMagick (approx.)           | 150+                   |
| Formats in XnView (approx.)               | 500+ input, 70 output  |
| Formats in IrfanView (approx.)             | 84+ graphic formats    |

### Optimizer Coverage

| Format | Optimizer                                                |
| ------ | -------------------------------------------------------- |
| PNG    | Zopfli DEFLATE, color mode/filter/interlace optimization |
| GIF    | LZW, palette reorder, frame optimization                 |
| TIFF   | PackBits, LZW, DEFLATE/Zopfli, predictor                 |
| BMP    | RLE8/RLE4, color mode, row order                         |
| TGA    | Pixel-width RLE, color mode, origin                      |
| PCX    | PCX RLE, plane config, color mode                        |
| JPEG   | Lossless Huffman, lossy quality/subsampling/mode         |
| ICO    | BMP vs PNG per entry (2^n combinations)                  |
| CUR    | BMP vs PNG per entry, hotspot preservation               |
| ANI    | BMP vs PNG per frame entry                               |
| WebP   | Container-level metadata stripping                       |
