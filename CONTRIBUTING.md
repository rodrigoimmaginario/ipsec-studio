# Contributing / Contribuição

Maintainer / mantenedor: **Rodrigo Immaginario** ([@rodrigoimmaginario](https://github.com/rodrigoimmaginario)).

## Authorship / Autoria

- Every commit, tag, release, issue and document presents the GitHub user of its human author. Default authorship in this repository is `rodrigoimmaginario`.
- No attribution to code or text generation tools: no `Co-authored-by` trailers for automated tools, no mentions in commit messages, file headers, documentation or release notes. Tools are means of work; responsibility and credit belong to the author.
- Set your git identity before contributing:
  ```bash
  git config user.name "Rodrigo Immaginario"
  git config user.email "rodrigoimmaginario@users.noreply.github.com"
  ```
  Pull requests whose commits lack identifiable human authorship are rewritten or declined.

- Todo commit, tag, release, issue e documento apresenta o usuário GitHub do autor humano. Nenhuma atribuição a ferramentas de geração de código ou texto. Pull requests sem autoria humana identificável são reescritos ou recusados.

## Languages / Idiomas

- The application, this site and its documentation are **bilingual**: Brazilian Portuguese (default) and English. Every user-facing string, page and document ships in both languages in the same change (`app/IpsecStudio/L10n.cs`, `wwwroot/i18n.js`, `docs/pt-br`, `docs/en`, `README.md` + `README.en.md`).
- Aplicação, site e documentação são **bilíngues**: português do Brasil (padrão) e inglês. Toda string, página e documento voltados ao usuário entram nos dois idiomas na mesma mudança.

## Development routine / Rotina de desenvolvimento

1. **TDD**: write the failing test first (`tests/IpsecStudio.Tests/*.cs` for rules, simulator, compiler and persistence; `tests/browser/prototype-render.mjs` for the UI), then the code, then refactor.
2. `scripts/run-checks.ps1` must be green before any push (`scripts/install-hooks.ps1` enforces it with a pre-push hook). Guide: `docs/pt-br/tdd.md` / `docs/en/tdd.md`.
3. Changes to `Simulator.cs`, `Compiler.cs` or `Deployment.cs` require a real AD lab cycle; record *expected × observed*, failures and resumed runs in the pull request. Expansion requires complete, conclusive matching evidence. Intentionally indeterminate counterexamples must prove that expansion is refused, never be counted as green matches. C# coverage or fake AD alone is not cmdlet validation.
4. No secrets, keys, SAS tokens or credential files in the repository (`.gitignore`). No real third-party environment data.

## Brand assets / Identidade visual

- Canonical mark: `app/IpsecStudio/wwwroot/brand.svg` (approved compact mesh). `node scripts/generate-branding.mjs` derives the inverse mark, app/site favicons and the Windows ICO; requires Chrome/Chromium, selectable with `BROWSER_EXE`. It uses an isolated temporary profile and no remote resources.
- Fonte da marca: `app/IpsecStudio/wwwroot/brand.svg` (Malha compacta aprovada). O comando acima gera as versões derivadas, sem acesso a contas ou recursos remotos. Não redesenhar variantes independentemente.
- `node tests/branding/branding-smoke.mjs` checks consistency and ICO frames. On Windows, `scripts/run-checks.ps1 -Publish` also compares the published EXE's icon with the ICO and checks the served SVG assets.
- O gate Windows com `-Publish` confere o ícone incorporado ao EXE e os SVGs servidos. Alterações de marca não justificam mudar políticas nem religar o laboratório AD.

## License / Licença

MIT. Contributions are accepted under the same license; the maintainer is not obliged to accept pull requests.
