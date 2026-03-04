# GTI-ModTools

Modding toolkit for **Pokemon Mystery Dungeon: Gates to Infinity**.

This repo focuses on two pipelines:
- Image conversion (`.img <-> .png`) with BSJI-aware metadata updates.
- Archive scanning/extraction for `.bin` containers (FARC and other detected types).

## Use WPF For Converting

For any real conversion work, use the **WPF app**.

- It is the primary workflow for IMG/PNG conversion.
- It prevents unsafe PNG -> IMG runs when base data is missing (`No base data from GTI` overlay).
- It uses the same conversion core as CLI but gives better visibility and safer defaults.
- For normal modding, prefer WPF and treat CLI as automation-only.

CLI is still available for automation and batch scripts.

## Projects

- `GTI-ModTools.WPF`: desktop UI with `Image Converter` and `Binary Explorer` tabs.
- `GTI-ModTools.Types.Images`: IMG codecs, PNG handling, conversion engine, BSJI parser/editor, base indexing.
- `GTI-ModTools.Types.Databases`: shared parsers/models for GTI database-style binaries (`*_db_1.0`).
- `GTI-ModTools.Images.CLI`: CLI wrapper around image conversion logic.
- `GTI-ModTools.Types.FARC`: modular archive detection/extraction handlers.
- `GTI-ModTools.FARC.CLI`: CLI wrapper around archive scan/extract services.
- `*.Tests`: unit tests for image types, image CLI, archive types, and WPF guard logic.
  - includes `GTI-ModTools.Types.Databases.Tests` for database parser coverage.

## Folder Setup

The tools auto-create these folders in repo root:
- `Base`
- `Image_In`
- `Image_Out`
- `ExportedFiles`

What each folder is for:
- `Base`: original GTI files extracted from `romfs/image_2d` (`.img` + `.bsji`, original structure preserved).
- `Image_In`: files you want to convert.
- `Image_Out`: conversion output with folder structure preserved.
- `ExportedFiles`: default output for binary extraction (FARC/SIR0/NARC/ExeFS/GTI DB).

WPF stores path settings in a persistent JSON file next to the WPF executable:
- `GTI-ModTools.WPF.config.json`
- Change a path in the UI once and it is reused on next start.

## Recommended Workflow (WPF)

1. Start the app from repo root:

```bat
RunWPF.bat
```

2. In `Image Converter`, do `IMG -> PNG` first for your source set.
3. Edit only the PNG files you actually want to change.
4. Put edited PNGs back into `Image_In` (preserve relative paths).
5. Run `PNG -> IMG`.
6. Collect converted `.img` and any adjusted `.bsji` files from `Image_Out`.

You can convert back any number of edited PNGs in one run.

## Image Format Support

### IMG Header (`.img`)

All values are little-endian. Header length is `0x20` bytes.

| Offset | Size | Meaning |
|---|---:|---|
| `0x00` | 4 | Magic `.cte` (`00 63 74 65`) |
| `0x04` | 4 | Pixel format (`ImgPixelFormat`) |
| `0x08` | 4 | Width |
| `0x0C` | 4 | Height |
| `0x10` | 4 | Pixel length bits |
| `0x18` | 4 | Pixel data offset (commonly `0x80`) |

### IMG Pixel Formats

Current decode support (`.img -> .png`):
- `0x01`
- `0x02` (`rgb8`)
- `0x03` (`rgba8888`)
- `0x04` (ETC1)
- `0x05` (ETC1+A4 style path)
- `0x06` (XBGR1555 path)
- `0x07`
- `0x08`

Current encode support (`.png -> .img`):
- `0x01`
- `0x02`
- `0x03`
- `0x07`
- `0x08`

Decode-only right now:
- `0x04`
- `0x05`
- `0x06`

### PNG Naming Rule (Important)

When converting `.img -> .png`, output names include format suffix:
- `icon_start.img` -> `icon_start_0x02.png`

During `.png -> .img`, suffix is used as format source when present, then stripped from output filename.

If no suffix exists:
1. Base IMG format is used when matching base image exists.
2. Otherwise format is inferred from alpha (`opaque => rgb8`, `has alpha => rgba8888`).

### BSJI Handling

BSJI files are treated as SIR0-based metadata containers that reference image names and size-related values.

During `.png -> .img`:
- Matching base image is resolved from `Base`.
- Referencing `.bsji` files are found via base index.
- Known width/height fields are updated where safe.
- Unknown bytes are preserved.
- Updated `.bsji` files are emitted to `Image_Out` with original relative paths.

Safety rules:
- No base data => PNG -> IMG blocked in WPF.
- Ambiguous image name mapping in base => BSJI update for that name is skipped.

## Archive Formats Addressed (`.bin`)

Archive detection/extraction is modular (`IArchiveHandler`) and UI adapts by detected type.

Currently detected:
- `FARC`
- `SIR0`
- `NARC`
- `ExeFS`
- `Unknown` (listed, not hidden)

### FARC

Header pattern used:
- Magic `FARC` at `0x00`
- Section count at `0x20` (`u32`)
- Section offset table starts at `0x24` (`count * 4`)

Extraction output:
- section files
- `manifest.json`
- referenced names (if found)
- optional BCH carving (`--carve-bch`)

### SIR0

Header pattern used:
- Magic `SIR0`
- pointer/data offsets read from header

Extraction output:
- `sir0_payload.bin`
- `sir0_full.bin`
- `sir0_pointers.txt`
- `sir0_manifest.json`

### NARC

Header/blocks recognized:
- Magic `NARC` or `CRAN`
- `BTAF/FATB` file allocation block
- `FIMG/GMIF` file image block

Extraction output:
- per-entry files
- `narc_manifest.json`

### ExeFS

Header pattern used:
- 0x200-byte header
- up to 10 entries, each 0x10 bytes (`name[8]`, `relative offset[4]`, `length[4]`)

Extraction output:
- extracted entries (`*.bin`)
- `exefs_manifest.json`

## CLI (Optional)

### Image CLI

Run auto conversion:

```bat
RunCli.bat
```

Direct CLI:

```bash
dotnet run --project GTI-ModTools.Images.CLI/GTI-ModTools.Images.CLI.csproj -- --auto
dotnet run --project GTI-ModTools.Images.CLI/GTI-ModTools.Images.CLI.csproj -- --to-png
dotnet run --project GTI-ModTools.Images.CLI/GTI-ModTools.Images.CLI.csproj -- --to-img --format rgba8888
```

### Archive CLI

```bash
dotnet run --project GTI-ModTools.FARC.CLI -- scan GameSource --recursive
dotnet run --project GTI-ModTools.FARC.CLI -- extract GameSource ExportedFiles --recursive --carve-bch
```

## Tests

Run all tests:

```bash
dotnet test GTI-ModTools.slnx
```
