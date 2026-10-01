"""PreToolUse guard (workflow F40): refuse to edit Unity scripts/assets while the Editor is in Play Mode.

Reads the hook payload on stdin. For Edit/Write/MultiEdit on .cs/.shader/.prefab/.asset/.unity files, asks the
running Editor (`unity command editor_status`) for its play mode; "playing" or "paused" blocks the edit (exit 2,
message on stderr). If the CLI fails or no Editor answers, the edit is allowed.
"""
import json
import re
import subprocess
import sys

GUARDED = (".cs", ".shader", ".prefab", ".asset", ".unity")

try:
    payload = json.load(sys.stdin)
except Exception:
    sys.exit(0)

path = str((payload.get("tool_input") or {}).get("file_path") or "").lower()
if not path.endswith(GUARDED):
    sys.exit(0)

try:
    out = subprocess.run("unity command editor_status", shell=True, capture_output=True, text=True, timeout=15).stdout
except Exception:
    sys.exit(0)

m = re.search(r'"playMode"\s*:\s*"(\w+)"', out or "")
if m and m.group(1).lower() in ("playing", "paused"):
    sys.stderr.write("The Unity Editor is in Play Mode - stop Play Mode before editing scripts or assets (F40).\n")
    sys.exit(2)
sys.exit(0)
