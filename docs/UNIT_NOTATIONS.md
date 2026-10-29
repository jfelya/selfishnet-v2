# Unit Notations — Speeds & Limits

Reference for the main project: how speeds and caps are displayed, entered,
converted and stored. Implemented in `Services/SpeedFormatter.cs`.

## 1. Two unit systems (toggleable at runtime)

| Mode | Label in UI | Used for |
| :--- | :--- | :--- |
| Bytes | `KByte/s` | Kilobytes per second (classic SelfishNet style) |
| Bits | `KBit/s` | Kilobits per second (how ISPs advertise speed) |

Toolbar button shows the active system (`Units: KByte/s` / `Unidades: KBit/s`).
Clicking it converts every speed readout and limit spinner in place.
Choice persists in `%AppData%\SelfishNetV2\settings.json` (`UnitMode`).

Spelled-out prefixes (`KByte`/`MByte` vs `KBit`/`MBit`) are used instead of
`KB`/`Kb` so bytes and bits are never confused at a glance.

## 2. Display rules (auto-scaling with marker)

Speed cells always carry their marker. Thresholds:

- Bytes mode (divisor **1024**):
  - `< 1024 B/s` → `N B/s` (e.g. `512 B/s`)
  - `< 1024 KByte/s` → `N.N KByte/s` (e.g. `849.3 KByte/s`)
  - `< 1024 MByte/s` → `N.N MByte/s`
  - else → `N.NN GByte/s`
- Bits mode: `bps = bytes × 8`, divisor **1000** (networking convention):
  - `< 1000 b/s` → `N b/s`
  - `< 1000 KBit/s` → `N.N KBit/s`
  - `< 1000 MBit/s` → `N.N MBit/s`
  - else → `N.NN GBit/s`

Case convention: **`Byte` = bytes, `Bit` = bits** (capital `B` vs lowercase `b`
in the short forms `B/s` / `b/s`).

## 3. Conversion formulas

```
bytes/sec  →  KByte/s :  value = bytes / 1024
KByte/s    →  bytes   :  bytes = value × 1024

bytes/sec  →  KBit/s  :  value = bytes × 8 / 1000
KBit/s     →  bytes   :  bytes = value × 1000 / 8
```

`0` in a limit spinner always means **unlimited** (`0 (Unlimited)` / `0 (Ilimitado)`),
in either unit system. Spinners accept whole numbers only. A real cap below 1 KByte/s
is displayed as `<1` in Bytes mode so it cannot be mistaken for unlimited; switch to
Bits mode to edit it, or leave it unchanged to preserve the stored value.

## 4. Worked examples

| Stored (bytes/s) | Shown (Bytes mode) | Shown (Bits mode) |
| ---: | :--- | :--- |
| 0 | `0 B/s` / cap `0` | `0 b/s` / cap `0` |
| 512 | `512 B/s` | `4.1 KBit/s` |
| 102 400 | `100.0 KByte/s` (cap `100`) | `819.2 KBit/s` (cap `819`) |
| 1 048 576 | `1.0 MByte/s` (cap `1024`) | `8.4 MBit/s` (cap `8389`) |
| 12 500 000 | `11.9 MByte/s` | `100.0 MBit/s` |

## 5. Storage rule (important for the main project)

- Internally the engine and `known_devices.json` store **bytes/sec integers only**.
- The unit mode affects **display + input only**, never the stored value or the
  token-bucket math. Switching units mid-session cannot corrupt limits.
- Toggling units **converts the displayed number** (same bandwidth, both ways,
  interchangeably): a `100 KByte/s` cap shows as `819 KBit/s`; switching back
  restores `100`, up to whole-number rounding (<0.1% drift). The number is never
  kept-as-is with a new meaning — that would silently change the limit.
- Limit spinners keep whole numbers (`DecimalPlaces = 0`, no thousands
  separators so copy/paste is plain digits); entry is rounded to the nearest
  whole byte on commit.
