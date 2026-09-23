# Ordinary packed-data format references

For the preview.31 camera-only packed/unpacked comparison exception, the [CSP SDK seat-parameter declarations](https://github.com/ac-custom-shaders-patch/acc-lua-sdk/blob/main/common/ac_game.lua) were reviewed on 2026-09-23. They identify `GRAPHICS/DRIVEREYES` as driver-eye position and `GRAPHICS/ON_BOARD_PITCH_ANGLE` as seat pitch. The independently written comparison masks only finite numeric values of these two fields, requires all remaining content to match exactly, rejects ambiguous syntax and preserves raw-source fingerprints. It does not change the archive decoder or installed car data.

`PackedCarDataReader` is an independently implemented, bounded, read-only parser. It adds no external runtime dependency and does not embed another project's source or executable. These primary implementation references were reviewed on 2026-09-22 to establish the interoperable file-format arithmetic:

- [Content Manager's AcdReader](https://github.com/gro-ove/actools/blob/master/AcTools/AcdFile/AcdReader.cs) establishes the optional `-1111` marker plus one opaque 32-bit field, repeated name/data records and the four-byte storage stride.
- [Content Manager's binary reader](https://github.com/gro-ove/actools/blob/master/AcTools/ReadAheadBinaryReader.cs) establishes little-endian 32-bit lengths and length-prefixed names.
- [Content Manager's encryption factory](https://github.com/gro-ove/actools/blob/master/AcTools/AcdFile/AcdEncryption.cs) selects the containing car folder name for `data` archives. Its public code requires an external encryption factory; it is not a standalone portable decoding API.
- [Bovis's cipher implementation](https://github.com/bovis/acd_extractor/blob/main/lib/cipher.rb) documents the eight arithmetic accumulators combined into a decimal, hyphen-separated key. Its [gemspec](https://github.com/bovis/acd_extractor/blob/main/acd_extractor.gemspec) declares MIT. The arithmetic was independently implemented for ADT; no Ruby component is distributed.
- [Content Manager's writer](https://github.com/gro-ove/actools/blob/master/AcTools/AcdFile/AcdWriter.cs) confirms records contain a length-prefixed name, original data length and a fourfold byte buffer.

The AcTools repository carries [Ms-PL](https://github.com/gro-ove/actools/blob/master/LICENSE.txt). ADT does not redistribute its code. Other publicly available readers have different or absent license terms and were not imported as dependencies.

## Supported subset and limits

ADT retains only flat `.ini`, `.lut`, `.rto` and `.lua` text entries. Names and the car identifier must be safe ASCII; retained content must be valid UTF-8 (ASCII is a subset), with no binary control characters except tabs and line endings. Other text encodings are explicitly unsupported instead of being guessed. Lua is read as data, never executed.

Archive size is bounded at 64 MiB, decoded entries at 1 MiB each, combined declared decoded data at 16 MiB and entry count at 1,024. Length arithmetic is checked against the remaining input before allocation. All names, including ignored entries, are checked for case-insensitive duplication and unsafe paths. Ordinary zero padding is required; nonzero padding is treated as an unsupported variant. Ignored files may contain arbitrary bytes but still have to satisfy framing and resource limits.

Ordinary ACD framing has no authenticated content checksum. Successfully decoding readable bytes is not proof that the car ID is correct, that additional protection is absent, or that the data describes the active in-game physics. Semantic parsers must validate the settings they use and preserve unknown/partial states. A mismatched folder name, corrupt file, protected payload or unsupported encoding can all produce similar failures; error messages must not claim one of those causes was proven. No key guessing, protective-layer removal, script execution or modification of installed car data is attempted.

The optional marker field is opaque, matching the ordinary reference reader's behavior; a complete header and complete subsequent records are still required. Mixed packed/unpacked sources and external CSP configuration need separate source-selection and compatibility handling above this parser.
