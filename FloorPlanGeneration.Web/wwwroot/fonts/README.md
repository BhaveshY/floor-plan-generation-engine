# Fonts

The studio UI can be themed with the **EBA (Eike Becker_Architekten) corporate identity**,
which uses the **Akkurat Std** typeface (sans) and **Akkurat Mono Std** (mono).

## Akkurat is not committed

Akkurat is a **commercially licensed** typeface (Lineto). It is intentionally **not**
checked into this repository (see the `.gitignore` entry). The UI references it via
`@font-face` in `styles.css`, and **gracefully falls back** to the system sans-serif
stack when the files are absent — so the app is fully functional without them.

## To enable EBA branding locally

If you hold a valid Akkurat license, drop these `.otf` files into this directory:

```
AkkuratStd-Light.otf
AkkuratStd-LightItalic.otf
AkkuratStd-Regular.otf
AkkuratStd-Italic.otf
AkkuratStd-Bold.otf
AkkuratStd-BoldItalic.otf
AkkuratMonoStd.otf
```

Then hard-refresh the browser (Cmd/Ctrl+Shift+R). No rebuild is required — these are
static assets served from `wwwroot/`.

`InterVariable.woff2` (open-licensed) remains in this directory as the previous default.
