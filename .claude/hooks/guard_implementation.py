"""PreToolUse (Write|Edit): block app-code edits until governance artifacts exist."""
import json, sys, glob, os, re
d = json.load(sys.stdin)
def norm(p):
    p = p.replace("\\", "/")
    m = re.match(r"^/([a-zA-Z])/(.*)", p)  # MSYS-style /c/... -> C:/...
    return (m.group(1).upper() + ":/" + m.group(2)) if m else p
path = norm((d.get("tool_input") or {}).get("file_path", ""))
root = norm(os.environ.get("CLAUDE_PROJECT_DIR", os.getcwd()))
rel = path[len(root):].lstrip("/") if path.lower().startswith(root.lower()) else path
if not rel.startswith(("src/", "tests/")):
    sys.exit(0)
missing = []
if not os.path.exists(os.path.join(root, ".specify/memory/constitution.md")):
    missing.append("constitution")
for name in ("spec.md", "plan.md", "tasks.md"):
    if not glob.glob(os.path.join(root, "specs", "*", name)):
        missing.append(name)
if missing:
    sys.stderr.write("BLOCKED: application code edit requires governance artifacts. Missing: "
                     + ", ".join(missing) + ". Run the Spec Kit phases first.\n")
    sys.exit(2)
