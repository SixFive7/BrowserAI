# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# Scratch rig (durability research, 2026-10-03). Prints each cited source range so every
# file:line in the report can be checked against the exact revision it was read at.
# Chromium: chromium/chromium @ 155.0.8059.12 (chromium-1247's browserVersion).
# Firefox C++/yaml/js: mozilla-firefox/firefox @ 3bf8f468258c2181f455e23d4ffcd6acb8f4cdb1
#   (BASE_REVISION in microsoft/playwright browser_patches/firefox/UPSTREAM_CONFIG.sh at the
#   commit that rolled firefox to r1553, 8db08308251b88015f2b4019f821c9ac2ecfceda).
# Firefox session store and Juggler: read out of firefox-1553's own omni.ja.
import os
S = r'C:\Source\SixFive7\BrowserAI\.work\durability\src'
O = r'C:\Source\SixFive7\BrowserAI\.work\durability\omni'
C = [
 ('cr', 'net_extras_sqlite_sqlite_persistent_cookie_store.cc', 1231, 1291, 'cookie batch: 30 s timer from the first op, or 512 ops'),
 ('cr', 'net_cookies_cookie_monster.cc', 513, 521, 'FlushStore'),
 ('cr', 'net_cookies_cookie_monster.cc', 909, 949, 'DeleteCanonicalCookie flushes whatever matched'),
 ('cr', 'net_cookies_cookie_monster.cc', 951, 992, 'DeleteMatchingCookies flushes even when nothing matched'),
 ('cr', 'content_browser_devtools_protocol_storage_handler.cc', 618, 638, 'Storage.clearDataForOrigin'),
 ('cr', 'content_browser_storage_partition_impl.cc', 3232, 3248, 'ClearData(origin): host filter; cleanup only for an opaque origin'),
 ('cr', 'content_browser_storage_partition_impl.cc', 3041, 3069, 'cookie deletion through the cookie manager'),
 ('cr', 'content_browser_devtools_protocol_network_handler.cc', 392, 418, 'Network.deleteCookies deletes only what matched'),
 ('cr', 'components_services_storage_dom_storage_local_storage_impl.cc', 44, 60, 'localStorage default 5 s, 60 commits/hour'),
 ('cr', 'components_services_storage_dom_storage_local_storage_impl.cc', 124, 130, 'OnNoBindings: immediate commit'),
 ('cr', 'components_services_storage_dom_storage_local_storage_impl.cc', 264, 273, 'Flush'),
 ('cr', 'components_services_storage_dom_storage_storage_area_impl.cc', 685, 716, 'commit timer; aggressive = 1 s'),
 ('cr', 'content_browser_storage_partition_impl.cc', 272, 280, 'the switch reaches the storage service'),
 ('cr', 'components_sessions_core_command_storage_manager.cc', 36, 40, 'kSaveDelay 2500 ms'),
 ('cr', 'components_sessions_core_command_storage_manager.cc', 320, 330, 'StartSaveTimer'),
 ('cr', 'chrome_browser_sessions_session_service.cc', 122, 143, 'post-crash: session saving off until the crash is acknowledged'),
 ('cr', 'chrome_browser_sessions_exit_type_service.cc', 194, 247, 'exit type Crashed at start; acknowledgement'),
 ('cr', 'chrome_browser_ui_startup_startup_browser_creator.cc', 1599, 1605, 'HasPendingUncleanExit and --hide-crash-restore-bubble'),
 ('cr', 'chrome_browser_ui_startup_startup_browser_creator.cc', 932, 939, '--restore-last-session ignored for a new profile'),
 ('cr', 'chrome_browser_profiles_profile_impl.cc', 1657, 1668, 'IsNewProfile = Preferences created this launch'),
 ('cr', 'chrome_browser_ui_startup_startup_browser_creator_impl.cc', 820, 829, 'no restore on a post-crash launch'),
 ('cr', 'chrome_common_chrome_switches.h', 408, 412, 'hide-crash-restore-bubble'),
 ('cr', 'chrome_browser_sessions_session_restore.cc', 1304, 1308, 'restored browser is not a startup browser'),
 ('cr', 'base_files_important_file_writer.cc', 44, 47, 'kDefaultCommitInterval 10 s'),
 ('cr', 'base_files_important_file_writer.cc', 483, 494, 'ScheduleWrite: timer from the first change'),
 ('cr', 'base_files_important_file_writer.cc', 336, 341, 'flush before rename'),
 ('cr', 'components_prefs_json_pref_store.cc', 150, 160, 'JsonPrefStore uses the default interval'),
 ('cr', 'components_prefs_json_pref_store.cc', 530, 538, 'lossy prefs wait for a non-lossy write'),
 ('cr', 'content_browser_cache_storage_cache_storage.cc', 939, 952, 'CacheStorage index 20 s'),
 ('cr', 'content_browser_cache_storage_cache_storage.cc', 1135, 1150, 'index written at once on cache creation'),
 ('cr', 'net_disk_cache_simple_simple_index.cc', 44, 47, 'simple cache index 20 s'),
 ('cr', 'content_browser_indexed_db_instance_leveldb_backing_store.cc', 1458, 1469, 'IDB fsync only for strict'),
 ('cr', 'content_browser_indexed_db_instance_leveldb_backing_store.cc', 4433, 4437, 'IDB commit per transaction'),
 ('cr', 'sql_database.cc', 2314, 2340, 'sqlite synchronous'),
 ('fx', 'dom_localstorage_ActorsParent.cpp', 270, 283, 'kFlushTimeoutMs 5000'),
 ('fx', 'dom_localstorage_ActorsParent.cpp', 4047, 4058, 'Connection::Close flushes'),
 ('fx', 'dom_localstorage_ActorsParent.cpp', 4100, 4106, 'EndUpdateBatch schedules the flush'),
 ('fx', 'dom_localstorage_ActorsParent.cpp', 4257, 4272, 'ScheduleFlush'),
 ('fx', 'dom_localstorage_ActorsParent.cpp', 5230, 5236, 'Datastore::MaybeClose'),
 ('fx', 'netwerk_cookie_CookiePersistentStorage.cpp', 725, 760, 'cookie written per change'),
 ('fx', 'netwerk_cookie_CookiePersistentStorage.cpp', 2229, 2236, 'cookies WAL + synchronous NORMAL'),
 ('fx', 'dom_indexedDB_ActorsParent.cpp', 610, 612, 'IDB default synchronous'),
 ('fx', 'dom_indexedDB_ActorsParent.cpp', 6926, 6950, 'IDB durability to synchronous'),
 ('fx', 'toolkit_components_sessionstore_SessionStoreChangeListener.cpp', 372, 387, 'content collection timer = interval'),
 ('fx', 'modules_libpref_init_StaticPrefList.yaml', 2038, 2042, 'browser.sessionstore.interval 15000'),
 ('fx', 'browser_app_profile_firefox.js', 1539, 1558, 'session prefs'),
 ('fx', 'modules_libpref_Preferences.cpp', 4008, 4019, 'prefs.js 500 ms'),
 ('omni', r'gre-omni\moz-src\browser\components\sessionstore\SessionSaver.sys.mjs', 167, 199, 'runDelayed: 2 s floor, interval'),
 ('omni', r'gre-omni\moz-src\browser\components\sessionstore\SessionSaver.sys.mjs', 395, 443, 'interval prefs, idle'),
 ('omni', r'gre-omni\moz-src\browser\components\sessionstore\SessionStore.sys.mjs', 1384, 1391, 'full interval after a startup restore'),
 ('omni', r'gre-omni\moz-src\browser\components\sessionstore\SessionStartup.sys.mjs', 413, 432, 'resume_session_once wins over a crash'),
 ('omni', r'gre-omni\moz-src\browser\components\sessionstore\SessionFile.sys.mjs', 145, 162, 'read order'),
 ('omni', r'browser-omni\defaults\preferences\firefox.js', 511, 519, 'session prefs in the build'),
]
for kind, f, a, b, what in C:
    p = os.path.join(O, f) if kind == 'omni' else os.path.join(S, kind, f)
    lines = open(p, encoding='utf-8', errors='replace').read().split('\n')
    print('=== %s %s:%d-%d  %s' % (kind, f, a, b, what))
    for i in range(a, b + 1):
        print('%5d %s' % (i, lines[i - 1]))
