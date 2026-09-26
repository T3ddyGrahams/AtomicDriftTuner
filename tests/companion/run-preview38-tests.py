"""Mocked companion tests; no Assetto Corsa process or hardware API is used."""
from pathlib import Path
import sys

repo = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(repo / '.tools/python'))
from lupa.lua51 import LuaRuntime
from lupa.luajit21 import LuaRuntime as LuaJitRuntime

folder = repo / 'companion/apps/lua/ADTCompanion'
client, entry, capture, pit, track = [folder / name for name in
    ['companion_client.lua', 'ADTCompanion.lua', 'setup_capture.lua', 'pit_setup.lua', 'track_position.lua']]
for test in ['client-tests.lua', 'ui-tests.lua', 'capture-client-tests.lua', 'setup-capture-tests.lua', 'pit-setup-tests.lua', 'pit-client-tests.lua']:
    lua = LuaRuntime()
    args = {1: str(capture)} if test == 'setup-capture-tests.lua' else {1: str(pit)} if test == 'pit-setup-tests.lua' else {1: str(client), 2: str(entry), 3: str(capture), 4: str(pit)}
    lua.globals().arg = lua.table_from(args)
    lua.execute((repo / 'tests/companion' / test).read_text(encoding='utf-8'))
for source in [client, entry, capture, pit, track]:
    LuaRuntime().execute('assert(loadfile(...))', str(source))
lua = LuaJitRuntime()
lua.globals().arg = lua.table_from({1: str(track)})
lua.execute((repo / 'tests/companion/track-position-tests.lua').read_text(encoding='utf-8'))
print('PASS integrated companion: all five Lua files compile, six existing suites and position wire-layout/failure-isolation checks pass.')
