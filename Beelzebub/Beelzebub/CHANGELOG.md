# Changelog

All notable changes to this project will be documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and
this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - Unreleased

Initial scaffold.

- BepInEx IL2CPP plugin entry point with VCF dependency wired up.
- `BuildToServer` MSBuild target that copies `Beelzebub.dll` into the local dedicated server's `BepInEx\plugins` folder after each Release build.
- Thunderstore manifest (`thunderstore.toml`) prepared for future publish.
