---
name: ai-code-review
description: Checklist for reviewing AI-generated code in this project. Use before accepting any significant generated code.
---
Check: culture-dependent parsing; blocking async (.Result/.Wait); missing CancellationToken; HttpClient created with `new`; undisposed streams; buffering whole images; retrying 4xx; swallowed exceptions; secrets in logs/URLs; path built from untrusted strings; unbounded concurrency; non-atomic writes; tests that assert nothing; mocks hiding real behavior. Record real defects and fixes in AI_NOTES.md.
