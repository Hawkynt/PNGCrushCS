JWIC fixtures

synth_33x17_rgb.ppm and synth_33x17_grey.pgm are synthetic pictures (sine and XOR patterns) made
for these tests; they carry no third-party content.

Each <source>_t<T>_g<G>_q<Q>.wic was written from that source by the JWICTranslator 1.01 core
(coder.cpp, zerotree.cpp, 2dheap.cpp, transform.cpp, conversions.cpp from bejwic2000.zip), built
natively on x86-64 Linux with U32 narrowed to 32 bits, and driven like the BeOS translator:

  wicoracle e <source> <out.wic> <T> <Q> <G>

  T  transform: 0 Daubechies-4, 1 biorthogonal 9/7, 2 Haar
  Q  the translator's quality slider; the encoder's budget is 1.5*Q bits per padded pixel
  G  header generation: g101 = FA DE BA BE 01 01, g100 = 1B 7A FB 30 01 00 (1.0 channel order)

The .ppm/.pgm of the same name is that core's decode of the .wic (wicoracle d), cropped to the
picture size. For every file decodable by the javawic applet (transforms 0 and 1) the applet's
decode, captured through its MemoryImageSource, is byte-identical to it.

These files hold the reader to the oracle's pixels and the writer to the oracle's bytes.
