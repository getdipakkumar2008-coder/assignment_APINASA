"""PreToolUse (Bash): on `git commit`, block secrets/unwanted files; run build+tests if a solution exists."""
import json, sys, re, os, subprocess, glob, shutil
d = json.load(sys.stdin)
cmd = (d.get("tool_input") or {}).get("command", "")
if not re.search(r"\bgit\s+(-\S+\s+)*commit\b", cmd):
    sys.exit(0)
root = os.environ.get("CLAUDE_PROJECT_DIR", os.getcwd())
def run(a, **k): return subprocess.run(a, cwd=root, capture_output=True, text=True, encoding="utf8", errors="replace", **k)
# `git commit -a` stages tracked changes implicitly; also inspect the working diff in that case
staged = run(["git", "diff", "--cached", "--name-only"]).stdout.split()
errs = []
for f in staged:
    n = f.replace("\\", "/")
    if re.search(r"(^|/)\.env$|\.local\.json$|secrets\.json$|^photos/|(^|/)(bin|obj|node_modules)/", n):
        errs.append(f"forbidden file staged: {f}")
diff = run(["git", "diff", "--cached", "-U0"]).stdout
added, dbg, cur = [], [], ""
for l in diff.splitlines():
    if l.startswith("+++ "):
        cur = l[6:]
    elif l.startswith("+"):
        added.append(l[1:])
        if not cur.startswith(".claude/"):
            dbg.append(l[1:])
# exact value of the real key from .env, if present
envp = os.path.join(root, ".env")
if os.path.exists(envp):
    for line in open(envp, encoding="utf8"):
        if "=" in line:
            v = line.split("=", 1)[1].strip()
            if len(v) >= 12 and any(v in a for a in added):
                errs.append("a value from .env appears in staged changes")
pat = re.compile(r"(api[_-]?key|apikey|secret|token|password)\W{0,5}[:=]\s*[\"']?[A-Za-z0-9_\-]{32,}", re.I)
for a in added:
    if pat.search(a) and "YOUR_" not in a:
        errs.append("possible secret in staged change (line redacted)")
for a in dbg:
    if re.search(r"Console\.WriteLine\(\s*\"DEBUG|debugger;|console\.log\(", a):
        errs.append("debug statement staged: " + a.strip()[:60])
if not errs:
    sln = glob.glob(os.path.join(root, "*.sln")) + glob.glob(os.path.join(root, "*.slnx"))
    if sln:
        b = run(["dotnet", "build", "--nologo", "-v", "q"])
        if b.returncode: errs.append("dotnet build failed:\n" + b.stdout[-1500:])
        else:
            t = run(["dotnet", "test", "--no-build", "--nologo", "-v", "q"])
            if t.returncode: errs.append("dotnet test failed:\n" + t.stdout[-1500:])
            ui = os.path.join(root, "src", "marsphotos-ui")
            npm = shutil.which("npm")
            if not errs and npm and os.path.exists(os.path.join(ui, "package.json")) and os.path.isdir(os.path.join(ui, "node_modules")):
                u = run([npm, "--prefix", ui, "test", "--", "--watch=false"])
                if u.returncode: errs.append("UI tests failed:\n" + (u.stdout + u.stderr)[-1500:])
if errs:
    sys.stderr.write("COMMIT BLOCKED by pre-commit gate:\n - " + "\n - ".join(errs) + "\n")
    sys.exit(2)
