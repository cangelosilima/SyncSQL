# SQL lint and formatting

`sql-style.json` is the shared SQL style configuration. It contains no connection
settings and is published with the site at `config/sql-style.json`. The browser
formatter imports it at build time; changing the deployed JSON alone does not
change the built app. Edit this source file and rebuild the site.

The CLI looks for `./config/sql-style.json` from its working directory. If absent,
it uses the same JSON embedded when the CLI was built. Use
`syncsql lint --config path/to/sql-style.json` to select another file. An explicit
missing or invalid file fails the command rather than falling back silently.

## Lint

`lint.failOn` is `warning` or `error`. `--fail-on` overrides this threshold for one
run. Each entry in `lint.rules` accepts `off`, `warning`, or `error`. Keep all four
rule entries: `syntax-error`, `select-star`, `nolock-hint`, and `cursor-usage`.
Syntax errors default to errors; the three style rules default to warnings.
Lint checks run in the T-SQL CLI, not in the browser.

## Format

| Setting | Values | Meaning |
| --- | --- | --- |
| `tabWidth` | 1–8 | Spaces per indentation level, including aligned table columns. |
| `keywordCase` | `preserve`, `upper`, `lower` | SQL keyword capitalization. |
| `linesBetweenQueries` | 0–10 | Blank lines between statements. |
| `alignColumns` | `true`, `false` | Align column names, data types, and modifiers in table definitions. |
| `columnSpacing` | 1–8 | Minimum spaces between the aligned name, type, and modifier fields. |

Formatting affects the Formatted view only. Original SQL, exports, and revision
diffs retain captured text. SQL Server and Oracle dialect handling remains automatic.
