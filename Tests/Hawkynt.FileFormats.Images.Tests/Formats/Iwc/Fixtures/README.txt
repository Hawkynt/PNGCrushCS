IWC fixtures

<name>.ppm         a synthetic picture made for these tests (no third-party content)
<name>.iwc         this package's IwcWriter output for that picture
                   (synth_37x23: levels chosen by the writer; checker_16x12_l1: Levels = 1;
                   tiny_2x2: the smallest picture IWC can hold)
<name>.applet.ppm  the WaveL Java applet's decode of <name>.iwc: the classes of javaiwc.jar
                   (applet v1.1, 1996 class files) run on a desktop JVM by a harness that calls
                   Decompr.Decompress on the file and writes Decompr.pixels out as RGB

The writer must still produce <name>.iwc byte for byte, and the reader must rebuild
<name>.applet.ppm exactly: together they pin both sides to what the applet actually did.
