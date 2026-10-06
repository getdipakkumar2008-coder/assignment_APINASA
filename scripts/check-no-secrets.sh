#!/usr/bin/env bash
# Verifies the NASA key (from the git-ignored .env) is absent from every tracked file and from git history.
# Prints only "clean" or the names of offending files / commits - never the key itself.
set -u
cd "$(git rev-parse --show-toplevel)" || exit 2
key="$(sed -n 's/^NASA__ApiKey=//p' .env 2>/dev/null | tr -d '\r' | head -n 1)"
if [ -z "$key" ]; then
  echo "no key found in .env; scanning for key-like assignments only"
fi
bad=0
if [ -n "$key" ]; then
  files="$(git grep -lF -e "$key" -- . 2>/dev/null)"
  if [ -n "$files" ]; then echo "KEY FOUND in tracked files:"; echo "$files"; bad=1; fi
  commits="$(git log --all --no-textconv --format=%h -S"$key" 2>/dev/null)"
  if [ -n "$commits" ]; then echo "KEY FOUND in history, commits:"; echo "$commits"; bad=1; fi
fi
pattern='(api[_-]?key|apikey|secret|token|password)[^A-Za-z0-9]{0,5}[:=][[:space:]]*["'"'"']?[A-Za-z0-9_-]{32,}'
hits="$(git grep -nIE -i -l -e "$pattern" -- . ':!scripts/' ':!.claude/hooks/' ':!tests/MarsPhotos.Tests/Configuration/RepoSecretScanTests.cs' 2>/dev/null)"
if [ -n "$hits" ]; then echo "Key-like assignments in:"; echo "$hits"; bad=1; fi
if git ls-files --error-unmatch .env >/dev/null 2>&1; then echo ".env is TRACKED"; bad=1; fi
if [ "$bad" -eq 0 ]; then echo clean; fi
exit "$bad"
