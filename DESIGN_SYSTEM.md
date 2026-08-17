# HSSuite Design System

The goal is recognizable family resemblance without making every utility identical.

## Visual character

Clean Windows-office utility: light, restrained, compact, modern, high-contrast and functional. No gradients, glass, animations or decorative complexity by default.

## Typography

- Font family: Segoe UI
- Window/application title: 20 px, Semibold
- Section/card title: 14 px, Semibold
- Body/control text: 13 px
- Secondary/helper text: 12 px
- Status text: 12 px

## Spacing

Use an 8 px base rhythm.

- 4: micro spacing
- 8: related items
- 12: compact control groups
- 16: normal card padding/gaps
- 24: major groups
- 32: page-level separation

## Geometry

- Standard control height: 36 px
- Compact button minimum width: 88 px
- Border thickness: 1 px
- Control corner radius: 6 px
- Card corner radius: 8 px
- HS logo tile: 36 × 36 px

## Canonical palette

Defined as actual brushes in `src/HSTemplate/Themes/Colors.xaml`.

- App background: `#F4F6F8`
- Surface/card: `#FFFFFF`
- Header: `#172033`
- Primary text: `#172033`
- Secondary text: `#667085`
- Border: `#D9DEE7`
- Accent: `#246BFD`
- Accent hover: `#1557D6`
- Success: `#18864B`
- Warning: `#B86800`
- Error: `#C9362B`

## Header

The header is the strongest family marker. Keep the same structure across tools:

`[ HS ]  AppName`
`        one-line purpose                         vX.Y.Z`

The header is dark; the rest of the application is predominantly light.

## Cards

Use white surfaces on the light-gray application background. Cards should group meaningful tasks, not wrap every individual control.

## Buttons

- Primary: blue filled, white text.
- Secondary: white, subtle border, dark text.
- Quiet: minimal/no chrome for low-priority actions.
- Destructive: only when needed; use error color and confirmation.

Prefer one primary button per immediate task context.

## Inputs

Inputs use 36 px height, white background, 1 px neutral border, 6 px radius and visible blue focus border.

## Tables

Keep tables flat and information-dense:

- white rows
- subtle horizontal separators
- muted header background
- no heavy grid boxes unless required
- selection uses a pale accent background

## Status bar

Persistent bottom strip separated by a 1 px border. Left: state/message. Right: optional counts, progress percentage or version/context.

## Product freedom

The central workspace can vary radically. A scanner can use a table; a renamer can use a preview split view; a comparer can use two panes. Consistency comes from shell, spacing, typography, controls and state language.
