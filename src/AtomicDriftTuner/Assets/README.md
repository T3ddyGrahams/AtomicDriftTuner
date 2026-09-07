# ADT application icon

ADT.png is the transparent source for the application mark. ADT.ico contains 16, 24, 32, 48, 64, 128, and 256 pixel frames. The executable, WPF windows, and Inno Setup installer use this icon; the custom title bar uses the PNG.

Adapted from artwork supplied by the maintainer with the built-in image-generation tool. Final edit specification: preserve the large white italic ADT lettering, cyan border, and dark rounded badge; remove the two small subtitle/tagline lines, simplify background detail for small icons, and make the exterior corners transparent. A second background-extraction pass preserved the icon and added true alpha transparency. The resulting RGBA PNG was converted to a multi-size ICO without further artwork changes.

Keep both files in source control so builds do not depend on a user's image-generation cache.
