> [!TIP]
> Start the title with a prefix so the PR is labeled automatically: `feat:` `fix:` `docs:` `ci:` `build:` `i18n:`
> Example: `fix(hwp): keep tables when converting to DOCX`

## What does this change?

<!-- One or two sentences: what changes, and why. -->


<!-- Link the issue this PR resolves, e.g. "Fixes #123". It closes the issue when the PR is merged. Delete the line if there is no issue. -->
Issue (optional): Fixes #

## Type of change

- [ ] Bug fix
- [ ] New feature or improvement
- [ ] New format or engine
- [ ] Translation
- [ ] Documentation
- [ ] Build, CI or dependencies

## How was it tested?

- [ ] `dotnet format --verify-no-changes`
- [ ] `dotnet test`
- [ ] Tried it in the app (what you converted or clicked, and on which OS):

## Screenshots (UI changes)

<!-- Drag images into this box. Rendered pages are in tests/Filee.App.Tests/bin/<config>/net10.0/screenshots -->

## Checklist

- [ ] New UI strings are in all `src/Filee.App/Assets/i18n/*.json` files
- [ ] New third-party code or binaries are listed in `THIRD-PARTY-NOTICES.md`
- [ ] The PR does one thing (unrelated changes go in a separate PR)
