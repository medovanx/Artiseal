# Changelog

All notable changes to Artiseal are written here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html). While the version is below 1.0.0, a minor release may change behaviour.

## [Unreleased]

## [0.1.1] - 2026-11-15

### Fixed

- `SHA256SUMS.txt` now uses LF line endings, so `sha256sum -c SHA256SUMS.txt` works on every platform.

## [0.1.0] - 2026-11-15

### Added

- Windows app that checks a downloaded file's GitHub build provenance with Sigstore.
- Finds the publishing repo by itself, from the file's fingerprint in Sigstore's public log or from where the browser downloaded it.
- Shows the workflow, branch or tag, commit, signing time and build run of a verified file.
- Warns when more than one repo has signed the same file.
- "Check with Artiseal" on the Explorer right-click menu, installed per user without admin rights.
- `artiseal` command line tool, with exit code 0 only for a verified file.

### Security

- Checks that the signed build record names the file's exact fingerprint, a step the Sigstore .NET library skips for DSSE bundles.
- Matches repo names the way GitHub does (ignoring letter case) and never lets a dot or a prefix stand in for a real name.

[Unreleased]: https://github.com/medovanx/Artiseal/compare/v0.1.1...HEAD
[0.1.1]: https://github.com/medovanx/Artiseal/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/medovanx/Artiseal/releases/tag/v0.1.0
