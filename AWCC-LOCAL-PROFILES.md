# Local AWCC modes and OEM OC profiles

**English** | [简体中文](AWCC-LOCAL-PROFILES.zh-CN.md)

> Follow-up, 2026-11-06: OC02 has now been activated and verified through the original
> AWCC Custom CPU editor. The live worker confirmed both E-core target groups at 45,
> P-core/voltage/current targets, Performance cooling, and 253/253 W power limits.
> AWCC logged Overclock 02 activation and BIOS profile index 2 with no reboot required.
> Main preset mappings remain unchanged. See [preset controls](PRESET-CONTROLS.md)
> and [validation](VERIFICATION.md). The read-only audit below describes the evidence
> available before this separate live test; its "not activated" statements are historical.

Inspected on 2026-11-06 using only the installed AWCC files, WMI metadata, and existing
local service logs. Target: Alienware Aurora R16 / Intel Core i9-14900KF / RTX 4090,
AWCC 6.14.105.0. This audit made no profile changes and did not activate OC02.

## Conclusions

- The local OEM CPU configuration contains exactly three predefined profiles:
  **Off**, **Overclock 01**, and **Overclock 02**. It does not define OC03.
- With AWCC overclocking enabled, Quiet and Balanced select CPU Off; Performance
  selects CPU OC01. They independently select different thermal profiles.
- OC02 has the same P-core frequency field, PL1, PL2, and IccMax as OC01, but raises
  32 E-core control values from 44 to 45. Its stored memory fields also differ.
- Memory OC and GPU OC are unavailable according to the inspected AWCC runtime
  capability response. Their successful service results can be no-ops.
- The main preset, CPU OC profile, memory profile, and thermal profile are separate
  settings. Neither a shared GUID nor the word Performance makes them interchangeable.

Later implementation validation also measured a BIOS power override: final PL1 is
170 W in Quiet, 180 W in Balanced, and 253 W in Performance cooling; PL2 is 253 W.
Performance cooling with OC Off retains the stock OC ratios/current limit but has
253 W PL1. The stored Off profile's 180 W field is therefore not a universal final
runtime limit. See [the validated controls](PRESET-CONTROLS.md) for the current result.

## Local evidence

Under the installed AWCC directory:

`Subsystems/Performance/SubAgent/SCSubAgent/OCControlService/`

| File | Evidence |
| --- | --- |
| `OCControl.Service.Overclocking.Data/Config.json` | R16/14900KF OEM CPU profiles, XTU control targets, frequency tables, custom-setting ranges |
| `OCControl.Service.Overclocking.Data/Profile.json` | Cached active CPU/GPU/memory IDs and the saved custom profile |
| `OCControl.Service.Thermal.Data/Profile.json` | Balanced/Performance/Quiet firmware thermal profiles and a custom profile |
| `OCControl.Service.Overclocking.Logs/2026-11-06.log` | Previously authorized Off/OC01 transitions, full XTU proposals, results, and runtime capabilities |
| `OCControl.Service.Thermal.Logs/2026-11-06.log` | Matching thermal profile applications |

The performance preset requests are recorded separately in
`%ProgramData%/Dell/AWPerformance.SCSubAgent/AWPerformance.SCSubAgent.03.log`.
Pure metadata/IL inspection covered `PerformanceTables`, `PerformanceControl`, the
Intel CPU/memory tuners, the NVIDIA tuner, and `Alienware.BiosOcKnobs`.

The OEM configuration is version 1. SHA-256 at inspection:
`dd788dc4d8718787ef9184df4588e88b7855c3bdcf7fd59b0b4690db2e886b5e`.
The local machine-readable comparison is saved under the Git-ignored
`.local/awcc-sync/local-profiles-audit.json`.

## Mode selection and actual calls

The following mapping applies to normal app mode with AWCC overclocking enabled:

| Main UI choice | Preset enum | CPU target | Logical GPU target | Memory target | Thermal target |
| --- | --- | --- | --- | --- | --- |
| Quiet | Quiet = 1 | Off | Off; tuner skips this GPU | Off | UsttQuiet, 163 / 0xA3 |
| Balanced | Normal = 2 | Off | Off; tuner skips this GPU | Off | UsttBalanced, 160 / 0xA0 |
| Performance | Fast = 3 | OC01 | OC01; tuner skips this GPU | Off | UsttPerformance, 161 / 0xA1 |

The observed UI click path is:

1. `PerformancePresetsView` receives an item click followed by selection change.
2. `PerformancePresetsViewModel.ApplySelectedProfile` reads the current app mode and
   active game GUID and calls `SetPerformancePreset(gameId, preset)`. When leaving
   Custom, it also calls `SetCustomMode(false)`.
3. The preset plugin saves the relevant setting and signals the manual-change event.
4. `PerformanceControl` reads the current app/game/OC state and independently resolves
   the target CPU, GPU, memory, and thermal profiles.
5. For a normal automatic/manual preset change, it compares each target profile ID
   with the current ID. Changed profiles are applied in CPU, GPU, memory, then thermal
   order. Unchanged components are skipped.
6. It updates restart-reason state and reports completion or failure. Setter return,
   UI selection, and full application completion are different events.

For the existing observed system context this means:

| Transition | Expected work, assuming the other profile states already match |
| --- | --- |
| Performance to Balanced | CPU Off; logical GPU Off; memory unchanged; thermal Balanced |
| Performance to Quiet | CPU Off; logical GPU Off; memory unchanged; thermal Quiet |
| Balanced or Quiet to Performance | CPU OC01; logical GPU OC01; memory unchanged; thermal Performance |
| Quiet to Balanced, or Balanced to Quiet | CPU/GPU/memory IDs already match; only thermal changes |
| Click an already-selected mode | Frontend normally returns without another preset request |

The first three transition types have local service evidence from earlier authorized
tests. Quiet/Balanced direct transition skipping is established from the code's ID
comparisons, not a new live test in this audit. The workflow is not an atomic hardware
transaction: an error can leave some components changed, so a replacement must read
back state and handle partial failure.

Additional state-dependent behavior found in the local code:

- **Overclocking disabled:** profile resolution targets Off for CPU, GPU, and memory,
  while thermal selection still follows the selected main preset. Performance does
  not unconditionally mean OC01 in this state.
- **Custom:** CPU/GPU/memory choices come from saved custom OC modes, and thermal uses
  the custom profile. A custom data-change event applies the relevant component.
- **System versus game:** the selected preset belongs to the active context. Automatic
  game/power changes reach the same profile-resolution workflow. Hardcoding only the
  system GUID would miss these state changes.
- GKey, Stealth, EnergySaver, and Turbo exist in general contracts/code, but their
  presence does not establish support on this R16. The local cached standard thermal
  profiles are Quiet, Balanced, and Performance, plus Custom.

## Off, OC01, and OC02: complete stored differences

Every OEM profile has 46 XTU entries with unique IDs.

| Field | Off | OC01 | OC02 |
| --- | --- | --- | --- |
| CPU enabled | Yes | Yes | Yes |
| CPU frequency field | 6.0 GHz | 6.1 GHz | 6.1 GHz |
| E-core ratios, IDs 1500-1515 | 44 | 44 | 45 |
| Active E-core count ratios, IDs 4500-4515 | 44 | 44 | 45 |
| Turbo Boost Power Max, ID 48 | 180 W | 253 W | 253 W |
| Turbo Boost Short Power Max, ID 47 | 253 W | 253 W | 253 W |
| Processor Core IccMax, ID 102 | 307 A | 316 A | 316 A |
| Turbo power time window, ID 66 | 56 s | 56 s | 56 s |
| CPU voltage mode | Auto | Auto | Auto |
| CPU voltage field / offset | 0 / 0 | 0 / 0 | 0 / 0 |
| Intel Turbo Boost, ID 26 | Enabled | Enabled | Enabled |
| Stored memory enabled / index | False / 0 | False / 0 | True / 1 |
| Default predefined profile | Yes | No | No |

Excluding identity/default metadata, Off to OC01 changes only three stored values:
`cpu.frequency`, XTU ID 48, and XTU ID 102. All other stored CPU/XTU/memory values match.

OC01 to OC02 changes 34 stored settings: the two memory fields and the 32 E-core
control values listed above. Its CPU frequency, voltage, PL1, PL2, IccMax, and the
remaining XTU values match OC01. Both frequency-table rows are `[6.1, 6.1, 0]` in the
all-core and per-core tables; the Off rows are `[6.0, 6.0, 0]`.

These are target configuration fields, not sustained measured operating values.
Auto voltage with a numeric zero does not mean the CPU supply voltage is zero.
Off disables the OEM OC preset, not normal Intel Turbo Boost.

## Actual Off versus OC01 XTU proposals

The existing 2026-11-06 log contains:

- CPU Off activation at **13:26:46.179**, source log line 2021.
- CPU OC01 activation at **13:27:18.179**, source log line 2423.

Each `Proposal` contains **142 unique control IDs**. The following six values differ;
the other 136 match numerically. The subsequent `ProposalDelta` block was excluded
from this comparison to avoid counting entries twice.

| XTU control | Off target | OC01 target |
| --- | --- | --- |
| 29: 1 Active Performance Core | 60 | 61 |
| 30: 2 Active Performance Cores | 60 | 61 |
| 120: Performance Core 4, favored | 60 | 61 |
| 121: Performance Core 5, favored | 60 | 61 |
| 48: Turbo Boost Power Max | 180 W | 253 W |
| 102: Processor Core IccMax | 307 A | 316 A |

The ratios for 3-8 active P-cores remain 57 in both captured proposals. Other individual
P-core targets also remain 57; the E-core ratio controls remain 44. Thus the 6.1 GHz
field is not an all-core 6.1 GHz target. Favored-core indices are specific to this
captured system. These are submitted control targets accompanied by service success
results, not an independent per-control hardware readback or stability test.

The local Intel tuner performs:

`DiscardChanges -> build voltage/frequency/TVB/TDP proposals -> ProposeChange -> ApplyChanges -> BIOS OC operations`

The BIOS operations include `BiosOcKnobs.SetVoltageOffset` where supported and
`BiosOcKnobs.ApplyProfile`. Existing logs show level 0 for Off and level 1 for OC01.
This explains why merely writing one thermal ID or one BIOS OC level has not yet been
established as equivalent to AWCC's full CPU workflow.

## What OC02 existence does and does not establish

OC02 is an actual entry in this machine's OEM configuration and an explicit mode in
the installed service contract. There is code to select the third CPU profile. The
normal Quiet/Balanced/Performance mappings above do not choose it.

The search of **209 existing OCControl overclocking log files**, dated 2026-01-30
through 2026-11-06, found no exact `ActivateCpuProfile(Overclock 02)` entry. This audit
also did not activate OC02, test stability, or confirm whether the current advanced
UI exposes it as an enabled choice.

The inspected runtime system-info response reports:

- CPU `overclockable: true`.
- RTX 4090 `overclockable: false` in AWCC's capability model.
- `memory_overclockable: false`, `num_memory_profiles: 0`.

`IntelCpuTuner.ActivateMemProfile` returns success without changing memory when memory
OC is unavailable. Additionally, its read-only predefined-profile path uses the
current `MemoryOCControl.ActiveProfile`; the saved memory index is used for non-read-only
custom profiles. Therefore OC02's stored `enabled: true, index: 1` must not be presented
as proof that selecting CPU OC02 applies XMP1 on this machine. The CPU and memory
application paths are separate. Any firmware side effects of applying BIOS level 2
remain untested.

Likewise, the NVIDIA tuner logs its selected profile name and then returns success
before tuning if the GPU capability is false. The logical GPU OC01 state in the cache
does not prove a GPU clock increase.

## Keep the identifiers separate

| CPU profile | OverclockMode enum | BIOS OC level | Profile GUID |
| --- | --- | --- | --- |
| Off | OC0 = 1 | 0 | `92BA744B-4CF8-4281-A8DE-F8F1D3524D8C` |
| OC01 | OC1 = 2 | 1 | `F6E1318B-9DF4-4557-9D70-36586465FCD2` |
| OC02 | OC2 = 3 | 2 in the standard-profile index mapping; not activated here | `8F6E0D5E-E1FA-4518-8819-C04B8F7E3048` |

The thermal service reuses those GUIDs for **Balanced, Performance, and Quiet**,
respectively. In particular, **Quiet thermal and CPU OC02 share a GUID but are different
profiles in different services**. Quiet selects CPU Off, not CPU OC02.

The inspected OverclockMode contract also includes UserDefined and NoChange. It does
not contain OC03. Additional thermal GUIDs and the maximum custom-profile capacity
are not evidence of additional CPU OC levels.

## Scope of verification

- Re-read local configuration and code; no external research was used for this audit.
- Compared every stored XTU entry and both 142-control runtime proposals; validated
  counts, unique IDs, and the six runtime differences.
- Checked current cached CPU OC01, logical GPU OC01, memory Off, and Performance
  thermal state. Cached state is not independent hardware readback.
- Queried no tuning setters, made no BIOS change, ran no stress test, and did not
  select OC02. Production HardwareDeck buttons remain unchanged by this audit.
