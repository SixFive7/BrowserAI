<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

| Run | Feed folder | Channel asked | Assets the source listed | CheckForUpdatesAsync returned |
|---|---|---|---|---|
| M1-B-1 | `after-1.0.1` | (default: win) | 1.0.1, 1.0.0 | 1.0.1 (IsDowngrade False, deltas 0) |
| M1-B-2 | `after-1.0.1` | (default: win) | 1.0.1, 1.0.0 | 1.0.1 (IsDowngrade False, deltas 0) |
| M1-B-3 | `after-1.0.1` | (default: win) | 1.0.1, 1.0.0 | 1.0.1 (IsDowngrade False, deltas 0) |
| M1-C-1 | `after-1.0.2-alpha.1` | (default: win) | 1.0.2-alpha.1, 1.0.1, 1.0.0 | 1.0.2-alpha.1 (IsDowngrade False, deltas 0) |
| M1-C-2 | `after-1.0.2-alpha.1` | (default: win) | 1.0.2-alpha.1, 1.0.1, 1.0.0 | 1.0.2-alpha.1 (IsDowngrade False, deltas 0) |
| M1-C-3 | `after-1.0.2-alpha.1` | (default: win) | 1.0.2-alpha.1, 1.0.1, 1.0.0 | 1.0.2-alpha.1 (IsDowngrade False, deltas 0) |
| M1-D-1 | `after-1.0.2` | (default: win) | 1.0.2, 1.0.2-alpha.1, 1.0.1, 1.0.0 | 1.0.2 (IsDowngrade False, deltas 0) |
| M1-D-2 | `after-1.0.2` | (default: win) | 1.0.2, 1.0.2-alpha.1, 1.0.1, 1.0.0 | 1.0.2 (IsDowngrade False, deltas 0) |
| M1-D-3 | `after-1.0.2` | (default: win) | 1.0.2, 1.0.2-alpha.1, 1.0.1, 1.0.0 | 1.0.2 (IsDowngrade False, deltas 0) |
| M1-E-1 | `win-1.0.1-plus-beta-alpha` | (default: win) | 1.0.1, 1.0.0 | 1.0.1 (IsDowngrade False, deltas 0) |
| M1-E-2 | `win-1.0.1-plus-beta-alpha` | (default: win) | 1.0.1, 1.0.0 | 1.0.1 (IsDowngrade False, deltas 0) |
| M1-E-3 | `win-1.0.1-plus-beta-alpha` | (default: win) | 1.0.1, 1.0.0 | 1.0.1 (IsDowngrade False, deltas 0) |
| M1-F-1 | `win-1.0.1-plus-beta-alpha` | beta | 1.0.2-alpha.1 | 1.0.2-alpha.1 (IsDowngrade False, deltas 0) |
| M1-F-2 | `win-1.0.1-plus-beta-alpha` | beta | 1.0.2-alpha.1 | 1.0.2-alpha.1 (IsDowngrade False, deltas 0) |
| M1-F-3 | `win-1.0.1-plus-beta-alpha` | beta | 1.0.2-alpha.1 | 1.0.2-alpha.1 (IsDowngrade False, deltas 0) |

| Run | Update.exe pid, life, exit | Restarted process | Its args (as .NET parsed them) | VELOPACK variables at entry | Parent pid (state when read) | Update.exe created -> restarted created | Restarted created -> Update.exe exit |
|---|---|---|---|---|---|--:|--:|
| M2-1 | 16420, 1827.2 ms, exit 0 | `current\BrowserAI.Measure.exe` v1.0.1 | `["restarted", "--run=M2-1", "two words", "embedded\"quote", "trailing\\"]` | VELOPACK_RESTART=true | 16420 (exited 0) = that Update.exe | 1818.3 | 8.9 |
| M2-2 | 99444, 2152.0 ms, exit 0 | `current\BrowserAI.Measure.exe` v1.0.2 | `["restarted", "--run=M2-2", "two words", "embedded\"quote", "trailing\\"]` | VELOPACK_RESTART=true | 99444 (exited 0) = that Update.exe | 2142.2 | 9.8 |
| M2-3 | 70296, 1873.1 ms, exit 0 | `current\BrowserAI.Measure.exe` v1.0.3 | `["restarted", "--run=M2-3", "two words", "embedded\"quote", "trailing\\"]` | VELOPACK_RESTART=true | 70296 (exited 0) = that Update.exe | 1864.2 | 8.9 |
| M3-recover | 5088, 2087.0 ms, exit 0 | `current\BrowserAI.Measure.exe` v1.0.4 | `["restarted", "--run=M3-recover", "two words", "embedded\"quote", "trailing\\"]` | VELOPACK_RESTART=true | 5088 (exited 0) = that Update.exe | 2078.2 | 8.9 |
| M4a-1 | 58744, 17200.3 ms, exit 0 | `current\BrowserAI.Measure.exe` v1.0.5 | `["restarted", "--run=M4a-1", "two words", "embedded\"quote", "trailing\\"]` | VELOPACK_RESTART=true | 58744 (exited 0) = that Update.exe | 17186.7 | 13.6 |
| M4a-2 | 57616, 17429.0 ms, exit 0 | `current\BrowserAI.Measure.exe` v1.0.6 | `["restarted", "--run=M4a-2", "two words", "embedded\"quote", "trailing\\"]` | VELOPACK_RESTART=true | 57616 (exited 0) = that Update.exe | 17411.6 | 17.3 |
| M4a-3 | 43804, 17110.1 ms, exit 0 | `current\BrowserAI.Measure.exe` v1.0.7 | `["restarted", "--run=M4a-3", "two words", "embedded\"quote", "trailing\\"]` | VELOPACK_RESTART=true | 43804 (exited 0) = that Update.exe | 17101.2 | 9.0 |

| Run | Update.exe pid, life, exit | Restarted process | Its args (as .NET parsed them) | VELOPACK variables at entry | Parent pid (state when read) | Update.exe created -> restarted created | Restarted created -> Update.exe exit |
|---|---|---|---|---|---|--:|--:|
| M3-1 | 106996, 11026.9 ms, exit 1 | `current\BrowserAI.Measure.exe` v1.0.3 | `["restarted", "--run=M3-1", "two words", "embedded\"quote", "trailing\\"]` | VELOPACK_RESTART=true | 106996 (exited 1) = that Update.exe | 11018.8 | 8.1 |
| M3-2 | 70752, 11346.2 ms, exit 1 | `current\BrowserAI.Measure.exe` v1.0.3 | `["restarted", "--run=M3-2", "two words", "embedded\"quote", "trailing\\"]` | VELOPACK_RESTART=true | 70752 (exited 1) = that Update.exe | 11337.2 | 9.0 |
| M3-3 | 100900, 11000.9 ms, exit 1 | `current\BrowserAI.Measure.exe` v1.0.3 | `["restarted", "--run=M3-3", "two words", "embedded\"quote", "trailing\\"]` | VELOPACK_RESTART=true | 100900 (exited 1) = that Update.exe | 10991.8 | 9.1 |

| Run | Hook pid | Created | Killed (exit code) | Lifetime | Heartbeats, last at elapsed | Update.exe says | Version after |
|---|---|---|---|--:|---|---|---|
| M4a-1 | 12648 | 2026-10-08T14:14:14.6985451Z | 2026-10-08T14:14:29.7067634Z (1) | 15008.2 ms | 145, 14653 ms | [ERROR] Process timed out after 15s and was killed.; [INFO] Package applied successfully.; [INFO] Package version 1.0.5 applied successfully. | 1.0.5 |
| M4a-2 | 72092 | 2026-10-08T14:14:36.5012004Z | 2026-10-08T14:14:51.5097992Z (1) | 15008.6 ms | 146, 14840 ms | [ERROR] Process timed out after 15s and was killed.; [INFO] Package applied successfully.; [INFO] Package version 1.0.6 applied successfully. | 1.0.6 |
| M4a-3 | 20156 | 2026-10-08T14:14:57.1658410Z | 2026-10-08T14:15:12.1796873Z (1) | 15013.8 ms | 147, 14848 ms | [ERROR] Process timed out after 15s and was killed.; [INFO] Package applied successfully.; [INFO] Package version 1.0.7 applied successfully. | 1.0.7 |

| Run | Hook exit (code) | Child under root: created, last heartbeat, exit (code) | Child killed after hook exit | Update.exe exit (code) | Child outside root: last line | Outside child exit (code) | Outside child outlived Update.exe by |
|---|---|---|--:|---|---|---|--:|
| M4b-1 | 2026-10-08T14:15:20.6926257Z (0) | 2026-10-08T14:15:17.6524972Z, 2026-10-08T14:15:21.0879960Z (beat 13), 2026-10-08T14:15:21.1545810Z (1) | 462.0 ms | 2026-10-08T14:15:21.8899836Z (0) | `2026-10-08T14:16:48.4123624Z finished beats=352 elapsedMs=90247` | 2026-10-08T14:16:48.4278414Z (0) | 86537.9 ms |
| M4b-2 | 2026-10-08T14:16:56.0463026Z (0) | 2026-10-08T14:16:52.9376344Z, 2026-10-08T14:16:56.3887981Z (beat 13), 2026-10-08T14:16:56.6300229Z (1) | 583.7 ms | 2026-10-08T14:16:57.5661952Z (0) | `2026-10-08T14:18:23.4801980Z finished beats=357 elapsedMs=90115` | 2026-10-08T14:18:23.4951328Z (0) | 85928.9 ms |
| M4b-3 | 2026-10-08T14:18:31.3394761Z (0) | 2026-10-08T14:18:28.3092858Z, 2026-10-08T14:18:31.7952352Z (beat 13), 2026-10-08T14:18:31.8293575Z (1) | 489.9 ms | 2026-10-08T14:18:32.6888528Z (0) | `2026-10-08T14:19:58.8073497Z finished beats=357 elapsedMs=90062` | 2026-10-08T14:19:58.8185143Z (0) | 86129.7 ms |
