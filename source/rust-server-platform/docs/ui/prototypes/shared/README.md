# RustServer Prototype Design System

**Required for all new prototypes.** This is a design checkpoint, not production React code.

## Source of truth

- `theme.css`: global typography, Rust palette, and general presentation.
- `shell.css`: persistent top bar, Rust logo, sidebar, application layout.
- `components.css`: reusable buttons, panels, modal presentation, status indicators.
- `shell.js`: injects the **same actual header and sidebar DOM** in every prototype, based on `data-page`.

Page-specific CSS, markup, and JS must stay within their own prototype.
Do not copy the header, sidebar, or shared CSS into individual pages. Any shell adjustment belongs in shared files.

Each prototype includes three CSS links and puts the following script tags inside `.shell` and `.wrap` respectively:

```html
<script src="../shared/shell.js" data-part="header" data-page="plugins"></script>
<script src="../shared/shell.js" data-part="nav" data-page="plugins"></script>
```

Use `server` for the Server prototype. **Relative paths require keeping the folder structure**. For easiest preview, run a local static server in `docs/ui/prototypes` (e.g. `python3 -m http.server 8000`). These HTML files also work as local files in current browsers, though remote icon and Google Fonts require internet.

## Rules for agents

1. Changes to visual shell, buttons and typography must update `shared/`, never just one page.
2. Keep backend/API assumptions and business content inside page-specific prototypes; **do not combine page logic**.
3. Do not add unfinished navigation areas or invented endpoints.
4. Test both Plugins and Server after shared changes, including modal dialogs and buttons.
5. The active selection is controlled by `data-page`; all pages display the same shell geometry.
6. The Rust icon references `https://rust.facepunch.com/favicon.ico` and has a fallback.

**Note:** External route buttons for `Overview` and `World` are visual placeholders in this checkpoint; `Plugins` and `Server` navigate between HTML samples. Both prototypes contain simulated data and actions.
