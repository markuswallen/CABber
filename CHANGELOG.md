# Changelog

All notable changes to this project are documented in this file.

## [0.1.0-alpha.1] - 2026-07-02

Initial prerelease. Published for early testing and feedback only — the public API may still change before a stable `1.0.0` release.

### Added
- FCI-backed cabinet builder (`CabinetBuilder`, `ICabinetBuilder`) for creating `.cab` archives.
- FDI-backed cabinet extractor (`CabinetExtractor`, `ICabinetExtractor`, `ICabinetExtractorFactory`) for reading and extracting `.cab` archives.
- NuGet package metadata (readme, license, release notes) for distribution via NuGet.org.
