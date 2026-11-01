# Style inventory (extracted from the prototype's inline styles)

Every value below is used literally in `reference/AskRaffa V4.dc.html`. Counts = occurrences. Use this to verify a re-implementation has no stray sizes/colours.

## Font sizes (px)
| value | uses |
|---|---|
| `13px` | 70 |
| `12px` | 66 |
| `11px` | 61 |
| `10px` | 29 |
| `14px` | 19 |
| `15px` | 12 |
| `16px` | 5 |
| `22px` | 4 |
| `17px` | 3 |
| `18px` | 3 |
| `30px` | 2 |
| `19px` | 2 |
| `20px` | 2 |
| `28px` | 1 |
| `24px` | 1 |
| `34px` | 1 |
| `clamp(24px,2.4vw,32px)` | 1 |

## Font weights
| value | uses |
|---|---|
| `800` | 34 |
| `600` | 24 |
| `700` | 17 |
| `400` | 7 |

## Letter-spacing
| value | uses |
|---|---|
| `.08em` | 31 |
| `.1em` | 24 |
| `.06em` | 3 |
| `-.02em` | 3 |

## Colour tokens referenced
| token | uses |
|---|---|
| `--color-neutral-600` | 96 |
| `--color-divider` | 62 |
| `--color-text` | 51 |
| `--color-neutral-700` | 34 |
| `--color-accent` | 15 |
| `--color-accent-700` | 12 |
| `--color-neutral-400` | 7 |
| `--color-neutral-300` | 5 |
| `--color-accent-100` | 5 |
| `--color-neutral-200` | 4 |
| `--color-accent-200` | 3 |
| `--color-bg` | 2 |
| `--color-surface` | 2 |
| `--color-neutral-900` | 1 |
| `--color-accent-400` | 1 |
| `--color-accent-300` | 1 |
| `--color-neutral-500` | 1 |

## Literal colours (not tokens)
| value | uses |
|---|---|
| `#fff` | 15 |
| `rgba(32,30,29,.2)` | 2 |
| `rgba(0,0,0,.04)` | 2 |
| `rgba(32,30,29,.3)` | 1 |

## Borders
| value | uses |
|---|---|
| `1px solid var(--color-divider)` | 60 |
| `2px solid var(--color-text)` | 40 |
| `0` | 13 |
| `1px solid var(--color-neutral-600)` | 4 |
| `1px solid var(--color-text)` | 3 |
| `2px solid var(--color-accent)` | 2 |
| `3px solid {{ st.bar }}` | 2 |
| `1px solid rgba(32,30,29,.2)` | 2 |
| `3px solid {{ n.bar }}` | 1 |
| `1px solid {{ ch.bd }}` | 1 |
| `1.5px solid currentColor` | 1 |
| `1px solid {{ o.bd }}` | 1 |
| `1px solid rgba(32,30,29,.3)` | 1 |
| `1px solid var(--color-accent-300)` | 1 |
| `2px solid var(--color-divider)` | 1 |
| `3px solid {{ c.bar }}` | 1 |
| `2px solid var(--color-neutral-300)` | 1 |
| `3px solid var(--color-accent)` | 1 |

## Paddings
| value | uses |
|---|---|
| `0` | 11 |
| `0 8px` | 6 |
| `10px 0` | 6 |
| `9px 0` | 5 |
| `8px 0` | 5 |
| `32px 40px 48px` | 4 |
| `5px 0` | 4 |
| `6px 0` | 4 |
| `0 20px` | 3 |
| `8px 0 10px` | 3 |
| `8px 14px` | 3 |
| `10px 14px` | 3 |
| `12px 14px` | 3 |
| `2px 8px` | 3 |
| `22px 40px 48px` | 3 |
| `0 10px` | 2 |
| `0 40px` | 2 |
| `14px 0` | 2 |
| `10px 4px 10px 0` | 2 |
| `14px 8px 14px 0` | 2 |
| `8px 12px` | 2 |
| `0 12px` | 2 |
| `3px 10px` | 2 |
| `4px 14px 12px` | 2 |
| `4px 10px` | 2 |
| `6px 6px 6px 14px` | 2 |
| `16px 20px` | 2 |
| `14px 16px 14px 0` | 2 |
| `12px 8px 12px 0` | 2 |
| `20px 24px` | 2 |
| `16px 0 13px` | 1 |
| `6px 12px` | 1 |
| `26px 30px 24px` | 1 |
| `8px 8px 0 0` | 1 |
| `28px 32px 26px` | 1 |
| `20px 0` | 1 |
| `14px 40px 0` | 1 |
| `12px 0 14px` | 1 |
| `22px 40px 20px` | 1 |
| `9px 6px 9px 0` | 1 |
| `4px 0 14px 20px` | 1 |
| `20px 24px 22px` | 1 |
| `7px 0` | 1 |
| `0 0 6px` | 1 |
| `0 4px` | 1 |
| `8px 40px 14px` | 1 |
| `24px 0` | 1 |
| `7px 10px` | 1 |
| `12px 0` | 1 |
| `16px 16px 16px 0` | 1 |
| `12px 12px` | 1 |
| `0 0 24px` | 1 |
| `0 18px` | 1 |
| `14px 18px 4px` | 1 |
| `9px 16px 9px 15px` | 1 |
| `8px 18px` | 1 |
| `24px 40px` | 1 |
| `8px 40px 16px` | 1 |
| `6px 8px 0 0` | 1 |
| `10px 12px 10px 0` | 1 |
| `14px 16px` | 1 |
| `18px 20px 24px` | 1 |

## Gaps
| value | uses |
|---|---|
| `10px` | 20 |
| `12px` | 18 |
| `8px` | 12 |
| `6px` | 7 |
| `16px` | 6 |
| `14px` | 5 |
| `22px` | 3 |
| `6px 14px` | 2 |
| `12px 24px` | 2 |
| `0 14px` | 2 |
| `6px 16px` | 2 |
| `0 28px` | 2 |
| `12px 20px` | 2 |
| `0 40px` | 2 |
| `6px 28px` | 1 |
| `18px` | 1 |
| `0 18px` | 1 |
| `24px 40px` | 1 |
| `0 24px` | 1 |
| `14px 32px` | 1 |
| `8px 20px` | 1 |
| `4px 12px` | 1 |
| `6px 12px` | 1 |
| `4px` | 1 |
| `1px` | 1 |
| `10px 28px` | 1 |
| `0 32px` | 1 |
| `6px 10px` | 1 |
| `6px 18px` | 1 |
| `2px` | 1 |
| `0 20px` | 1 |

## Grid templates
| value | uses |
|---|---|
| `repeat(auto-fit,minmax(200px,1fr))` | 3 |
| `repeat(auto-fit,minmax(170px,1fr))` | 2 |
| `minmax(110px,.5fr) minmax(0,2.5fr)` | 2 |
| `84px minmax(0,1fr)` | 2 |
| `{{ cd.table.grid }}` | 2 |
| `repeat(auto-fit,minmax(220px,1fr))` | 2 |
| `84px minmax(0,1fr) auto` | 2 |
| `{{ cols }}` | 1 |
| `minmax(0,1.6fr) minmax(260px,1fr)` | 1 |
| `repeat(auto-fit,minmax(240px,1fr))` | 1 |
| `minmax(100px,.6fr) minmax(0,2.4fr)` | 1 |
| `70px minmax(0,1fr)` | 1 |
| `minmax(0,1.4fr) auto` | 1 |
| `repeat(auto-fill,minmax(170px,1fr))` | 1 |
| `minmax(0,2fr) minmax(70px,.6fr) minmax(100px,1fr) minmax(100px,1fr)` | 1 |
| `repeat(auto-fit,minmax(280px,1fr))` | 1 |
| `repeat(auto-fit,minmax(118px,1fr))` | 1 |
| `repeat(auto-fit,minmax(340px,1fr))` | 1 |
| `minmax(80px,.7fr) minmax(0,2.4fr) auto` | 1 |
| `minmax(110px,.9fr) minmax(0,2fr) auto` | 1 |
| `minmax(80px,.7fr) minmax(0,2fr) auto` | 1 |
| `repeat(auto-fit,minmax(380px,1fr))` | 1 |
| `260px minmax(0,1fr)` | 1 |
| `minmax(0,2fr) minmax(90px,.8fr) minmax(100px,1fr) minmax(100px,1fr)` | 1 |

## Max-widths
| value | uses |
|---|---|
| `1400px` | 4 |
| `1100px` | 4 |
| `60ch` | 3 |
| `64ch` | 2 |
| `70ch` | 2 |
| `900px` | 2 |
| `640px` | 1 |
| `{{ casoMax }}` | 1 |
| `600px` | 1 |
| `520px` | 1 |
| `760px` | 1 |
| `76ch` | 1 |
| `66ch` | 1 |
| `360px` | 1 |

## Min-heights (controls)
| value | uses |
|---|---|
| `0` | 9 |
| `44px` | 7 |
| `30px` | 4 |
| `46px` | 3 |
| `34px` | 3 |
| `38px` | 2 |
| `200px` | 1 |
| `36px` | 1 |
| `90px` | 1 |
| `40px` | 1 |
| `100px` | 1 |
| `28px` | 1 |
| `26px` | 1 |

## Fixed heights
| value | uses |
|---|---|
| `10px` | 8 |
| `12px` | 5 |
| `22px` | 4 |
| `8px` | 2 |
| `14px` | 2 |
| `18px` | 2 |
| `56px` | 2 |
| `36px` | 1 |
| `6px` | 1 |
| `150px` | 1 |

## Button / tag classes used
- `btn btn-ghost`
- `btn btn-primary`
- `btn btn-secondary`
- `btn {{…}}`
- `input`
- `r-row`
- `tag {{…}}`
