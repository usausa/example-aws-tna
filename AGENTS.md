# Coding Style

- **General:** Follow the rules defined in .editorconfig
- **Instance field:** Do not use `_` prefix for member variables
- **Warnings:** Ensure there are no build warnings
- **Suppress warnings:** If warning suppression is needed, ask before applying the fix
- **Language:** Use English for code comments, log messages, and script messages. Use Japanese for UI text
- **Line endings:** Never change existing line endings, use CRLF for newly created text files

# Project

- **Specification first:** The documents under `docs/` (OpenAPI, calculation and leave specifications, data model) are the source of truth. Update them before changing the behavior
- **Native AOT:** Lambda functions are published with Native AOT. Do not depend on reflection (see `docs/design/architecture.md` section 4.6)
- **Names:** Generic documents do not name companies or products. Such information goes to Markdown files whose names start with `__` (not in git)
- **Quality gate:** Run `scripts/check.ps1` before asking for a review
