# Mount Initializer
A simple CLI tool to initialize an ASCOM mount. It allows syncing the mount to CWD position RA & DEC coordinates and then does some sanity checks to ensure the mount is in the desired state.

---
### Usage

| Argument (bold = required) | Description                   |
|----------------------------|-------------------------------|
|<b>`--condition <cond>`</b> | Precondition for initializing the mount. If the condition is not met the program exits. Available conditions: `always`, `unknownSideOfPier` |
|`--telescope <ID>`          | ASCOM driver ID of the mount. If not specified the user is prompted to select the mount via ASCOM dialog. |
|`--timeout <seconds>`       | Timeout in seconds before the program exits with a failure. Default: 60s |
|`--silent`                  | Suppress any output except for `--status` output. |
|`--status`                  | Print exit status to stdout. Possible exit status values: `OK`, `FAILURE` |
|`--unpark`                  | Unpark mount. Required if mount may be parked when running this program. |
|`--stopTracking`            | Stops mount tracking after initialization. |
|`--check`                   | Check mode. If enabled the program only checks whether the specified condition is met and then exits with `OK` or `FAILURE` depending on the condition. |

---

| Exit codes | Description |
|------------|-------------|
| 0          | Success.<br>Or when in check mode: condition is met.    |
| 1          | Initialization failed.<br>Or when in check mode: condition is not met. |
| 2          | Invalid usage/arguments. |

---
### Example use case

Syncing the home position of an M-Uno mount. The mount reports unknown side of pier until it is properly aligned with the CWD position. I want to automate this initial "Sync Home Position".

Command:
```
MountInitializer.exe --telescope ASCOM.AvalonStarGo.NET.Telescope --condition unknownSideOfPier --stopTracking --silent --status
```
The condition ensures that this command only does anything if the mount is not yet aligned. After alignment tracking is stopped. The program will output `OK` or `FAILURE` depending on whether the sync was successful and the mount ended up in the expected state (i.e. it reports correct RA & DEC coordinates, is no longer tracking and side of pier is no longer unknown).