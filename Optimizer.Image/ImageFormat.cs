namespace Optimizer.Image;

/// <summary>Supported image formats for optimization and conversion.</summary>
public enum ImageFormat {
  Unknown,
  // With dedicated optimizers
  Png, Gif, Tiff, Bmp, Tga, Pcx, Jpeg, Ico, Cur, Ani, WebP,
  // Lossless raster
  Qoi, Farbfeld, Sgi, SunRaster, Netpbm, Wbmp, Xbm, Xpm,
  Hrz, Cmu, Mtv, Qrt, Otb, Avs, Msp,
  // Complex raster
  Hdr, Pfm, Psd, Xcf, Dds, Vtf, Ktx, Exr, Dpx, Miff,
  AliasPix, Rla, ScitexCt, Viff, Xwd, Dicom, Cineon,
  // Indexed/planar/retro
  MacPaint, DrHalo, Palm, Sixel, Ccitt, Cals, Sff, Oric, Vicar,
  Koala, ZxSpectrum, BbcMicro, AmstradCpc, C64Multi, Bsave,
  Degas, Neochrome, CrackArt, Spectrum512, Tiny, GemImg,
  AppleII, AppleIIgs, Msx, SamCoupe, Acorn,
  // Planar/container
  Ilbm, Pict, Fli, OpenRaster, Jng, Nifti, Nrrd, Fits,
  // Container/multi-image
  Apng, Mng, Dcx, Wad, Wad3, Art, Tim, Tim2, Wal, Wpg, Pkm, Astc, Pvr,
  // Utah
  UtahRle,
  // Clipboard
  Clp,
  // Wave 4: Trivial formats
  Aai, Rgf, Fbm, Gbr, Pat, Xyz, Lss16, ColoRix,
  SunIcon, Cel, AmigaIcon, Gaf, GunPaint, GeoPaint,
  // Wave 5: Extensions & containers
  Psb, Icns, Blp, Fsh, Mpo, Pds, Ics, BioRadPic, Ptif,
  // Wave 6: Medium complexity
  Bsb, Awd, Psp, Qtif, Ingr, Nitf, Uhdr, PalmPdb, Pcd, PhotoPaint, Pdn, Fpx,
  // Wave 7: Complex codecs
  JpegLs, Jbig, Wsq, DjVu, Jbig2, Flif, Jpeg2000, JpegXr,
  // Wave 8: Advanced codecs
  Heif, Avif, JpegXl, Bpg, Dng, CameraRaw,
  // Wave 9: Additional formats
  Krita, Analyze, MetaImage, Eps, Wmf, Emf, Vips, QuakeSpr, NesChr, GameBoyTile, Atari8Bit, IffAnim,
  // Wave 10: IFF variants, professional 3D, scientific, retro
  SoftImage, MayaIff, Envi, Xcursor, IffPbm, PcPaint, IffAcbm, IffDeep, IffRgb8, Interfile, AtariFalcon, Trs80,
  // Wave 11: Console tiles, containers, scientific, retro
  SnesTile, SegaGenTile, PcEngineTile, MasterSystemTile, SymbianMbm, XvThumbnail, IffRgbn, Mrc, Gd2, BigTiff, AutodeskCel, Wad2,
}
