# SpriteForge — Style Spec (WPF handoff)

> Source: Claude Design project `a302fc84-def9-41c5-8643-305e3fd1c627` (`STYLE_SPEC.md` + `AppWindow.dc.html`).
> Translated into `src/SpriteForge.Gui/Theme.xaml` (brushes + control templates). Use the Theme.xaml
> resource keys when restyling; this doc is the source of truth for values, layout metrics, and states.

Dark theme · Windows desktop · **Segoe UI** · compact density · target window **1100 × 720** (resizable).
Accent is a runtime/setting value; defaults to **Forge Amber `#E8943A`**. Hover/pressed/tint derive from accent.

---

## 1. Color palette → Theme.xaml brush keys

### Surfaces
| Role | Hex | Theme key |
|---|---|---|
| Title bar / status bar | `#1F2022` | `TitleBarBrush` |
| Menu bar | `#232427` | `MenuBarBrush` |
| Left rail / window | `#1A1B1D` | `RailBrush` / `WindowBrush` |
| Panel (group card) | `#202123` | `PanelBrush` |
| Panel header | `#26272A` | `PanelHeaderBrush` |
| Editor / preview / input field | `#161719` | `EditorBrush` |
| Secondary control (button) | `#303236` | `SecondaryControlBrush` |
| Control pressed | `#46484D` | `ControlPressedBrush` |
| Disabled control fill | `#2A2B2F` / `#232427` | `DisabledFillBrush` / `DisabledFill2Brush` |

### Text
| Role | Hex | Theme key |
|---|---|---|
| Primary | `#E8E9EC` | `TextBrush` |
| Secondary | `#A6A8AE` | `TextSecondaryBrush` |
| Control text | `#D7D8DC` | `ControlTextBrush` |
| Header (UPPERCASE) | `#9A9CA2` | `HeaderTextBrush` |
| Menu / status text | `#C5C7CC` | `MenuTextBrush` |
| Label / dim | `#7E8189` | `TextLabelBrush` |
| Placeholder | `#6B6D73` | `PlaceholderBrush` |
| Muted / disabled | `#5B5D63` | `TextMutedBrush` |

### Lines
| Role | Hex | Theme key |
|---|---|---|
| Border subtle (panels, dividers) | `#2C2D31` | `BorderSubtleBrush` |
| Input border | `#34363B` | `InputBorderBrush` |
| Border strong / hover | `#3A3C41` → `#46484D` | `BorderStrongBrush` / `BorderHoverBrush` |
| Checkbox border | `#3C3F45` | `CheckboxBorderBrush` |

### Accent & status
| Role | Value | Theme key |
|---|---|---|
| Accent (default) | `#E8943A` | `AccentBrush` |
| On-accent text | `#15120E` | `OnAccentBrush` |
| Accent hover | `#EBA152` | `AccentHoverBrush` |
| Accent pressed | `#CC8233` | `AccentPressedBrush` |
| Accent border (primary btn) | `#BE7930` | `AccentBorderBrush` |
| Accent tint (selection/active) | accent @ 16% (`#29E8943A`) | `AccentTintBrush` |
| Success / done | `#5FB37A` | `SuccessBrush` |
| Close-button hover | `#E1342C` (text `#FFFFFF`) | `CloseHoverBrush` |

> Swatch presets: `#E8943A` Amber · `#4C8DFF` Blue · `#8BD24F` Green · `#34C9D6` Cyan · `#E5559E` Magenta.

---

## 2. Typography (Segoe UI)
| Use | Size | Weight | Notes |
|---|---|---|---|
| Title bar / button label | 12 | 600 | — |
| Menu bar / body | 12 | 400 | — |
| Group header | 11 | 600 | +0.06em, UPPERCASE, `#9A9CA2` |
| Field label | 11.5 | 500 | `#A6A8AE` |
| Micro label (RENDER PX, FPS…) | 10 | 600 | +0.05em, UPPERCASE, `#7E8189` |
| Value / input | 12 | 400 | tabular-nums |
| Status bar | 11.5 | 400 | — |
| Empty-state title | 13 | 600 | — |

Numeric readouts/fields use **tabular figures** (`TextOptions.NumberSubstitution`/`Typography.NumeralAlignment` or just monospace-tabular).

---

## 3. Spacing, radius, border
- **Spacing scale (px):** 2 · 4 · 6 · 8 · 9 · 10 · 12 · 16
- **Panel padding:** 9 · **Rail padding & inter-panel gap:** 10 · **Control gap:** 8 · **Label→control:** 4
- **Radius:** controls **3** · panels **5** · window **7** · swatch **4–5**
- **Border width:** 1 everywhere

### Layout metrics
| Element | Value |
|---|---|
| Left rail width | 340 |
| Title bar h | 34 |
| Menu bar h | 28 |
| Preview header h | 30 |
| Status bar h | 26 |
| Input h | 24 |
| Secondary button h | 26 |
| Primary button / transport h | 28 |
| Equipment list row h | 27 |
| Checkbox | 15 × 15 (inner check 11) |
| Slider track / thumb | 4 / 12 round — timeline thumb 14 square |
| Window-control buttons | 46 × 34 |

### Default field values
Render px **256** · FPS **12** · Directions **8** · Pitch **26.5°** · Yaw **0°** · Zoom **1.00×** · Distance **0 (auto)** · Sprite size **48** · Max colors **32**

---

## 4. Control states (already encoded in Theme.xaml)
- **Primary button** (`PrimaryButton` style): accent fill, on-accent text; hover `#EBA152`; pressed `#CC8233`; disabled fill `#2A2B2F` / text `#5B5D63`.
- **Secondary button** (implicit `Button`): `#303236`; hover `#3A3C41`; pressed `#46484D`; disabled `#232427`.
- **Slider**: track `#34363B`, accent fill, thumb `#E8E9EC` 1px `#15120E` border.
- **Checkbox** (15px): unchecked bg `#161719` border `#3C3F45`; checked bg/border accent, check `#15120E`; hover border `#46484D`.
- **Text/numeric field**: bg `#161719`, border `#34363B`; focus border accent; disabled bg `#202123`.
- **List row (equipment, 27px)**: transparent; hover `#26272A`; selected accent-16% tint + 2px left accent bar; name `#D7D8DC`, socket `#7E8189` italic.

---

## 5. Preview canvas
- Backdrop = transparency **checkerboard**: base `#161719`, squares `#202123`, **20 px** cell.
- Subtle radial vignette toward edges; optional 16 px pixel-grid overlay (off by default).
- Character rendered pixel art (nearest-neighbor), centered, soft contact-shadow ellipse.
- Corner badges (`rgba(18,19,21,.82)` chip, border `#2C2D31`): resolution, direction count, clip name, fps.

## 6. Window states
- **A — Empty:** Model panel active; model-dependent panels at **40% opacity + desaturated + no input**. Status dot `#5B5D63` — "Load a model to begin."
- **B — Loaded + animating:** full color; transport play/pause active (accent-tinted when playing); status dot = accent.
- **C — Sheet generated:** Output thumbnail populated (rows = directions × cols = frames), Save enabled, progress + status dot `#5FB37A`.

## 7. Window chrome (shell)
- Custom title bar (use `System.Windows.Shell.WindowChrome`, `GlassFrameThickness=0`, `CaptionHeight=34`, `ResizeBorderThickness=6`): app glyph (accent rounded square) + "SpriteForge" + "— {docName}", then min / max / close buttons (46×34; close hover `#E1342C`/white). Wire to `SystemCommands.MinimizeWindow/MaximizeWindow/RestoreWindow/CloseWindow`; mark interactive bits `WindowChrome.IsHitTestVisibleInChrome="True"`.
- Menu bar (28h): File / Edit / View / Help (can be a real `Menu` or static labels for now).
- Preview header (30h): "PREVIEW / {info}" left; zoom-out / {zoom%} / zoom-in / fit buttons right.
- Status bar (26h): colored state dot + message left; right-aligned "{dir} · {px} · {fps}".
