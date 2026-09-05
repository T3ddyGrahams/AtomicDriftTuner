# v0.8.1-share-test.1 — Validation Notes

Preparation environment does not include the Windows .NET/WPF SDK, so a real
Visual Studio compile was not possible here.

Static validation performed:

- all WPF XAML files parse successfully as XML;
- new XAML event handlers are present in code-behind;
- C# brace-balance checks passed for all modified/new C# files;
- current app/distribution version metadata is `0.8.1-share-test.1`;
- share payload model contains no full local path, telemetry, calibration, token,
  or credential fields;
- no `.env`, settings, calibration, telemetry, binary, `bin`, `obj`, `.vs`, or
  `node_modules` artifacts are present;
- TuningEngine, telemetry engines/readers, active-car scanner/identity reader,
  Atomic Remote server/web app, AZOM live controller/client/debounce service,
  and the SimHub bridge are byte-for-byte unchanged from the v0.8.0-beta.1
  source baseline;
- required SimHub bridge remains v0.7.2.

The first required test on Windows is:

**Build → Rebuild Solution**

If Visual Studio reports any compile errors, copy the exact errors before making
other changes.
