namespace AiLocal.Node.Roles;

/// <summary>Self-contained dashboard served by Launcher, Host, and Overseer.</summary>
internal static class Dashboard
{
    public const string Html = """
    <!doctype html>
    <html lang="sv">
    <head>
      <meta charset="utf-8">
      <meta name="viewport" content="width=device-width, initial-scale=1">
      <title>AiLocal</title>
      <style>
        /* Both palettes are lifted from the actual Hermes Agent app (its
           built-in "Nous" light theme and "Mono" dark theme, extracted from
           the shipped bundle) rather than approximated by eye - the user
           compared the two apps side by side and wants this one to read the
           same. Notable consequences encoded as tokens instead of hardcoded
           colors: --accent-fg (Hermes' dark-mode primary button is near-WHITE
           with dark text, so "color: white" on .primary would vanish) and
           --sidebar-bg (Hermes' sidebar is a step darker than the content
           area, they are not the same surface). */
        :root {
          color-scheme: light;
          --bg: #f8faff;
          --sidebar-bg: #f3f7ff;
          --surface: #ffffff;
          --surface-2: #f2f6ff;
          --surface-soft: #fbfcff;
          --surface-active: #e6eeff;
          --surface-selected: #eff4ff;
          --topbar-bg: rgba(248,250,255,.88);
          --line: #d7e1fa;
          --line-strong: #bcccf4;
          --kv-line: #eef2fc;
          --user-bubble-border: #c2d3fe;
          --text: #17171a;
          --muted: #666678;
          --accent: #0053fd;
          --accent-fg: #fcfcfc;
          --accent-2: #1540b1;
          --ok: #1f8a65;
          --ok-bg: #eefaf4;
          --ok-border: #b8dcca;
          --ok-text: #1a6e51;
          --warn: #c08532;
          --bad: #cf2d56;
          --bad-bg: #fdf1f4;
          --bad-border: #f0c2ce;
          --radius: 6px;
          --shadow: 0 1px 2px rgba(23,23,26,.05);
        }
        :root[data-theme="dark"] {
          color-scheme: dark;
          --bg: #0e0e0e;
          --sidebar-bg: #0a0a0a;
          --surface: #141414;
          --surface-2: #1e1e1e;
          --surface-soft: #111111;
          --surface-active: #1a1a1a;
          --surface-selected: #191919;
          --topbar-bg: rgba(10,10,11,.92);
          --line: #242424;
          --line-strong: #303030;
          --kv-line: #1a1a1a;
          --user-bubble-border: #363636;
          --text: #eaeaea;
          --muted: #808080;
          --accent: #eaeaea;
          --accent-fg: #0e0e0e;
          --accent-2: #9a9a9a;
          --ok: #55a583;
          --ok-bg: #0f1f18;
          --ok-border: #1d4034;
          --ok-text: #7fc8a9;
          --warn: #c08532;
          --bad: #e75e78;
          --bad-bg: #241318;
          --bad-border: #4a2530;
          --radius: 6px;
          --shadow: 0 1px 2px rgba(0,0,0,.35);
        }
        * { box-sizing: border-box; }
        html, body { height: 100%; }
        body { overflow-x: hidden; }
        /* Above the mobile breakpoint, the 3-column layout should stay locked
           to the viewport so each panel scrolls internally (.messages,
           .content, etc.) instead of the whole page scrolling and carrying
           the side panels off-screen. Below it, panels stack vertically and
           the page itself needs to scroll - see the max-width media query. */
        @media (min-width: 981px) {
          body { overflow-y: hidden; }
          .app { height: 100%; }
        }
        body {
          margin: 0;
          background: var(--bg);
          color: var(--text);
          font-family: "Segoe WPC", "Segoe UI", -apple-system, BlinkMacSystemFont, "SF Pro Text", system-ui, sans-serif;
          font-size: 13px;
          line-height: 1.45;
        }
        button, input, textarea, select { font: inherit; }
        /* Exactly one transition rule existed anywhere in this stylesheet
           before this (.node:hover) - every hover/focus/active state below
           was instant. This one shared rule covers the rest. */
        button, input, textarea, select, .session-item, .view-tab, .node, .icon-mini {
          transition: background-color .12s ease, border-color .12s ease, color .12s ease, opacity .12s ease, transform .08s ease;
        }
        button {
          border: 1px solid var(--line);
          background: var(--surface);
          color: var(--text);
          min-height: 34px;
          padding: 0 12px;
          border-radius: var(--radius);
          cursor: pointer;
        }
        button:hover { border-color: var(--line-strong); background: var(--surface-soft); }
        button:active { transform: scale(.97); }
        button.primary { background: var(--accent); border-color: var(--accent); color: var(--accent-fg); }
        button.ghost { background: transparent; }
        button.icon { width: 30px; min-height: 30px; padding: 0; display: inline-flex; align-items: center; justify-content: center; }
        .icon-svg { display: block; flex: 0 0 auto; }
        button.active { border-color: var(--accent); color: var(--accent); background: var(--surface-active); }
        input, textarea, select {
          border: 1px solid var(--line);
          background: var(--surface);
          color: var(--text);
          border-radius: var(--radius);
          outline: none;
        }
        input:focus, textarea:focus, select:focus { border-color: var(--accent); box-shadow: 0 0 0 3px color-mix(in srgb, var(--accent) 14%, transparent); }
        /* Native <select> looks like a Win95 relic against the rest of the
           theme - replace the OS arrow with our own chevron and give the
           control the same surface language as every other input. The open
           popup list itself is OS-drawn and can only take flat colors. */
        select {
          appearance: none;
          -webkit-appearance: none;
          background-image: url("data:image/svg+xml;charset=utf-8,%3Csvg xmlns='http://www.w3.org/2000/svg' width='12' height='12' viewBox='0 0 24 24' fill='none' stroke='%23808080' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'%3E%3Cpolyline points='6 9 12 15 18 9'/%3E%3C/svg%3E");
          background-repeat: no-repeat;
          background-position: right 9px center;
          padding: 0 30px 0 10px;
          min-height: 34px;
          cursor: pointer;
        }
        select option { background: var(--surface); color: var(--text); }
        /* Same treatment for checkboxes - the native ones read as "Windows
           form", these read as part of the app. */
        input[type="checkbox"] {
          appearance: none;
          -webkit-appearance: none;
          width: 16px;
          height: 16px;
          min-height: 0;
          border: 1px solid var(--line-strong);
          border-radius: 4px;
          background: var(--surface);
          display: inline-grid;
          place-content: center;
          cursor: pointer;
          padding: 0;
          flex: 0 0 auto;
        }
        input[type="checkbox"]::before {
          content: "";
          width: 9px;
          height: 9px;
          transform: scale(0);
          transition: transform .1s ease;
          clip-path: polygon(14% 44%, 0 60%, 40% 100%, 100% 20%, 86% 8%, 38% 72%);
          background: var(--accent-fg);
        }
        input[type="checkbox"]:checked { background: var(--accent); border-color: var(--accent); }
        input[type="checkbox"]:checked::before { transform: scale(1); }
        input[type="checkbox"]:disabled { opacity: .4; cursor: default; }
        /* Ambient depth (Hermes-style): two faint accent glows behind
           everything - decorative only, never intercepts input. */
        body::before {
          content: "";
          position: fixed;
          inset: 0;
          pointer-events: none;
          z-index: 0;
          background:
            radial-gradient(900px 500px at 85% -10%, color-mix(in srgb, var(--accent) 5%, transparent), transparent 60%),
            radial-gradient(700px 420px at -10% 110%, color-mix(in srgb, var(--accent-2) 4%, transparent), transparent 60%);
        }
        .app { position: relative; z-index: 1; }
        .app {
          min-height: 100%;
          display: grid;
          grid-template-rows: auto auto 1fr auto;
        }
        .statusbar {
          height: 30px;
          padding: 0 16px;
          display: flex;
          align-items: center;
          justify-content: space-between;
          gap: 12px;
          border-top: 1px solid var(--line);
          background: var(--sidebar-bg);
          color: var(--muted);
          font-size: 12px;
          overflow: hidden;
        }
        .statusbar-left, .statusbar-right {
          display: flex;
          align-items: center;
          gap: 8px;
          min-width: 0;
          overflow: hidden;
          white-space: nowrap;
        }
        .statusbar-sep { display: inline-block; width: 1px; height: 12px; background: var(--line-strong); }
        @media (max-width: 980px) {
          .statusbar-left span:not(:first-child):not(:nth-child(2)) { display: none; }
        }
        .topbar {
          height: 54px;
          padding: 0 6px 0 14px;
          display: flex;
          align-items: center;
          gap: 16px;
          border-bottom: 1px solid var(--line);
          background: var(--topbar-bg);
          backdrop-filter: blur(12px);
          position: sticky;
          top: 0;
          z-index: 5;
          /* In the WebView2 shell the OS caption is stripped and THIS bar is
             the window titlebar: draggable, double-click maximizes (WebView2
             non-client region support). Interactive children opt out below. */
          -webkit-app-region: drag;
          app-region: drag;
        }
        .topbar button, .topbar input, .topbar select, .topbar a {
          -webkit-app-region: no-drag;
          app-region: no-drag;
        }
        .win-controls { display: none; align-items: center; gap: 2px; margin-left: 4px; }
        .desktop-shell .win-controls { display: flex; }
        .win-btn {
          width: 40px;
          min-height: 32px;
          border: none;
          background: transparent;
          color: var(--muted);
          border-radius: 6px;
          display: inline-flex;
          align-items: center;
          justify-content: center;
        }
        .win-btn:hover { background: var(--surface-2); color: var(--text); border: none; }
        .win-btn.win-close:hover { background: #c42b1c; color: #fff; }
        .brand {
          display: flex;
          align-items: center;
          gap: 10px;
          width: 210px;
          flex: 0 0 auto;
        }
        .mark {
          width: 30px;
          height: 30px;
          border-radius: 7px;
          background: linear-gradient(135deg, var(--accent), var(--accent-2));
          color: var(--accent-fg);
          display: grid;
          place-items: center;
          font-weight: 800;
          font-size: 12px;
        }
        .brand-title { font-size: 14px; font-weight: 650; letter-spacing: 0; }
        .role-strip {
          display: grid;
          grid-template-columns: repeat(3, minmax(112px, 1fr));
          gap: 8px;
          flex: 1 1 auto;
          max-width: 520px;
        }
        .role-btn { display: flex; align-items: center; justify-content: center; gap: 8px; font-weight: 650; }
        .status-line {
          margin-left: auto;
          display: flex;
          align-items: center;
          gap: 10px;
          color: var(--muted);
          min-width: 210px;
          justify-content: flex-end;
        }
        .dot { width: 8px; height: 8px; border-radius: 50%; background: var(--warn); }
        .dot.ok { background: var(--ok); }
        .dot.bad { background: var(--bad); }
        .view-tab {
          min-height: 34px;
          padding: 0 10px;
          font-weight: 640;
          font-size: 13px;
          background: transparent;
          border-color: transparent;
          border-radius: 6px;
          color: var(--muted);
          display: flex;
          align-items: center;
          justify-content: flex-start;
          gap: 8px;
          width: 100%;
          box-shadow: inset 3px 0 0 transparent;
        }
        .view-tab:hover { background: var(--surface-soft); border-color: transparent; color: var(--text); }
        .view-tab .icon-svg { color: var(--muted); }
        /* Activity-bar style active state (accent rail, no boxed border) -
           closer to the Hermes/Claude-Code sidebar than a bordered button. */
        .view-tab.active {
          color: var(--accent);
          background: var(--surface-active);
          border-color: transparent;
          box-shadow: inset 3px 0 0 var(--accent);
        }
        .view-tab.active .icon-svg { color: var(--accent); }
        .hidden { display: none !important; }
        .shell {
          display: grid;
          grid-template-columns: 260px 1fr;
          min-height: 0;
          overflow: hidden;
          /* Chromium animates grid tracks, so collapsing the sidebar slides
             it shut instead of snapping - same feel as Hermes' panel toggle. */
          transition: grid-template-columns .18s ease;
        }
        .shell.sidebar-collapsed { grid-template-columns: 0px 1fr; }
        .shell.sidebar-collapsed .sidebar { border-right: none; }
        .sidebar {
          display: grid;
          grid-template-rows: auto auto auto auto 1fr;
          min-height: 0;
          overflow: hidden;
          border-right: 1px solid var(--line);
          background: var(--sidebar-bg);
        }
        .sidebar-top { padding: 10px 10px 0; }
        .new-session-btn {
          width: 100%;
          display: flex;
          align-items: center;
          justify-content: space-between;
          gap: 8px;
          font-weight: 640;
          font-size: 13px;
        }
        .kbd {
          font-size: 10px;
          padding: 1px 6px;
          border: 1px solid var(--line-strong);
          border-radius: 4px;
          color: var(--muted);
          font-family: "Cascadia Code", ui-monospace, Consolas, monospace;
        }
        .soon {
          font-size: 9.5px;
          padding: 1px 6px;
          border: 1px solid var(--line-strong);
          border-radius: 999px;
          color: var(--muted);
          margin-left: 6px;
          text-transform: uppercase;
          letter-spacing: .05em;
          vertical-align: middle;
        }
        .sidebar-nav { padding: 10px; display: grid; gap: 3px; }
        .sidebar-section { padding: 10px; border-top: 1px solid var(--line); min-height: 0; }
        .sidebar-section-head {
          display: flex;
          align-items: center;
          justify-content: space-between;
          gap: 8px;
        }
        .new-session-form { display: grid; gap: 8px; margin-top: 8px; }
        .new-session-form input { min-height: 32px; padding: 0 9px; font-size: 13px; }
        .new-session-form .composer-actions { display: flex; gap: 8px; justify-content: flex-end; padding: 0; background: none; border: none; }
        #sessionSearchInput { width: 100%; min-height: 32px; padding: 0 9px; font-size: 13px; }
        .sessions-list {
          overflow-y: auto;
          min-height: 0;
          padding: 6px 4px 10px;
          display: grid;
          gap: 2px;
          align-content: start;
        }
        .session-group-label {
          font-size: 11px;
          text-transform: uppercase;
          letter-spacing: .05em;
          color: var(--muted);
          padding: 10px 6px 4px;
        }
        .session-item {
          border-radius: 7px;
          padding: 6px;
          border: 1px solid transparent;
          cursor: pointer;
        }
        .session-item:hover { background: var(--surface-soft); }
        .session-item.active { background: var(--surface-selected); border-color: var(--line); }
        .session-item-row { display: flex; align-items: center; gap: 4px; }
        .session-item-title {
          flex: 1 1 auto;
          min-width: 0;
          white-space: nowrap;
          overflow: hidden;
          text-overflow: ellipsis;
          font-weight: 610;
          font-size: 13px;
        }
        .session-item-meta {
          margin-left: 26px;
          font-size: 11px;
          color: var(--muted);
          white-space: nowrap;
          overflow: hidden;
          text-overflow: ellipsis;
        }
        .icon-mini {
          width: 22px;
          height: 22px;
          min-height: 22px;
          padding: 0;
          border: none;
          background: transparent;
          font-size: 12px;
          border-radius: 5px;
          display: inline-flex;
          align-items: center;
          justify-content: center;
          flex: 0 0 auto;
          /* Hidden until the row is hovered/focused (see .session-item:hover
             below) - showing 2 icon buttons on every single row at all times
             was a real contributor to the "blocky/busy" look. */
          opacity: 0;
        }
        .session-item:hover .icon-mini,
        .session-item:focus-within .icon-mini {
          opacity: 1;
        }
        .icon-mini:hover { background: var(--surface-2); }
        .icon-mini:focus-visible { opacity: 1; }
        .views { min-height: 0; overflow: hidden; display: grid; }
        /* :not(.hidden) re-matches (and so replays) every time a view
           becomes the visible one via switchView()'s classList.toggle -
           no JS-side class juggling needed, animations restart on their own
           whenever the selector newly applies to a now-rendered element. */
        .views > main:not(.hidden) { animation: viewFadeIn .16s ease; }
        @keyframes viewFadeIn {
          from { opacity: 0; transform: translateY(4px); }
          to { opacity: 1; transform: translateY(0); }
        }
        .chat-only-workspace {
          display: flex;
          justify-content: center;
          padding: 14px;
          min-height: 0;
          min-width: 0;
        }
        /* Hermes/Claude-Code chat layout: the conversation is NOT a framed
           card - it sits directly on the app background as one centered
           column, and the composer is the only "object" (a floating rounded
           card at the bottom of that column). The .panel border/background
           the section markup carries is neutralized here. */
        .chat-only-workspace .chat-panel {
          width: 100%;
          max-width: 100%;
          background: transparent;
          border: none;
          box-shadow: none;
          border-radius: 0;
          display: flex;
          flex-direction: column;
          min-height: 0;
          position: relative; /* anchor for the .chat-outline jump rail */
        }
        .chat-only-workspace .chat-head,
        .chat-only-workspace .git-bar,
        .chat-only-workspace .composer {
          width: min(880px, 100%);
          margin-inline: auto;
        }
        .chat-only-workspace .chat-head { border-bottom: none; min-height: 52px; }
        .chat-only-workspace .composer { border-top: none; padding-bottom: 16px; }
        .office-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); gap: 10px; margin-top: 10px; }
        .office-card { border: 1px solid var(--border); border-radius: 10px; padding: 10px 12px; background: var(--panel); }
        .office-card.offline { opacity: .55; }
        .office-card .node-main { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
        .chat-only-workspace .messages {
          flex: 1;
          width: min(880px, 100%);
          margin-inline: auto;
        }
        .messages .empty {
          border: none;
          background: transparent;
          margin: auto;
          text-align: center;
        }
        .empty-hero { display: grid; justify-items: center; gap: 10px; padding: 40px 16px; max-width: 420px; }
        .hero-mark {
          width: 56px;
          height: 56px;
          border-radius: 14px;
          background: linear-gradient(135deg, var(--accent), var(--accent-2));
          color: var(--accent-fg);
          display: grid;
          place-items: center;
          font-weight: 800;
          font-size: 20px;
          box-shadow: 0 8px 30px color-mix(in srgb, var(--accent) 22%, transparent);
        }
        .hero-title { font-size: 16px; font-weight: 680; color: var(--text); }
        .workspace {
          display: grid;
          grid-template-columns: 300px 1fr;
          gap: 14px;
          padding: 14px;
          min-height: 0;
          min-width: 0;
        }
        .topology-workspace {
          display: grid;
          grid-template-columns: minmax(560px, 1fr) 340px;
          gap: 14px;
          padding: 14px;
          min-height: 0;
          min-width: 0;
        }
        .studio-workspace {
          display: grid;
          grid-template-columns: 280px minmax(0, 1fr) 380px;
          gap: 12px;
          padding: 12px;
          min-height: 0;
          min-width: 0;
        }
        .studio-tree-panel .content, .studio-term-panel .content { overflow: auto; }
        .studio-tabs { display: flex; gap: 4px; }
        .studio-tab { background: transparent; border: 1px solid rgba(255,255,255,.12); color: var(--fg, #e6edf3); padding: 3px 10px; border-radius: 6px; cursor: pointer; font-size: 12px; }
        .studio-tab.active { background: rgba(88,166,255,.18); border-color: rgba(88,166,255,.4); }
        .studio-root-row { display: flex; align-items: center; justify-content: space-between; gap: 8px; margin-bottom: 6px; }
        .branch-row { border: 1px solid rgba(255,255,255,.08); border-radius: 6px; padding: 6px 8px; margin-bottom: 6px; }
        .branch-row .mono { font-size: 12px; }
        .branch-actions { display: flex; gap: 4px; margin-top: 4px; }
        .iso-diff { background: #0b0f14; border: 1px solid rgba(255,255,255,.1); border-radius: 4px; padding: 6px; font-size: 11px; white-space: pre-wrap; }
        .iso-diff-side { background: #0b0f14; border: 1px solid rgba(255,255,255,.1); border-radius: 4px; margin-top: 6px; max-height: 360px; overflow: auto; font-family: var(--mono, ui-monospace, Menlo, Consolas, monospace); font-size: 11px; }
        .iso-file { border-bottom: 1px solid rgba(255,255,255,.06); }
        .iso-file:last-child { border-bottom: none; }
        .iso-file-name { padding: 4px 8px; background: rgba(255,255,255,.04); color: #8ab4f8; font-size: 11px; }
        .iso-cols { display: grid; grid-template-columns: 1fr 1fr; }
        .iso-col { overflow-x: auto; }
        .iso-col.old { border-right: 1px solid rgba(255,255,255,.06); }
        .iso-line { display: flex; white-space: pre; line-height: 1.5; }
        .iso-line .iso-ln { flex: 0 0 38px; text-align: right; padding-right: 8px; color: rgba(255,255,255,.3); user-select: none; }
        .iso-line .iso-txt { flex: 1; padding-right: 8px; }
        .iso-line.add { background: rgba(63,185,80,.14); }
        .iso-line.add .iso-txt { color: #7ee787; }
        .iso-line.del { background: rgba(248,81,73,.14); }
        .iso-line.del .iso-txt { color: #ffa198; }
        .iso-line.ctx { color: rgba(255,255,255,.82); }
        .iso-verify-out { margin-top: 6px; padding: 6px 8px; background: #0b0f14; border: 1px solid rgba(255,255,255,.1); border-radius: 4px; font-family: var(--mono, ui-monospace, Menlo, Consolas, monospace); font-size: 11px; white-space: pre-wrap; max-height: 200px; overflow: auto; }
        .iso-verify-out.err { border-color: rgba(248,81,73,.5); color: #ffa198; }
        .studio-editor-panel { display: grid; grid-template-rows: auto 1fr; overflow: hidden; }
        .studio-editor-content { display: grid; grid-template-rows: 1fr auto; min-height: 0; padding: 0; }
        .studio-tabs { display: flex; gap: 2px; overflow-x: auto; padding: 4px 8px 0; background: var(--bg-2, #090c10); border-bottom: 1px solid var(--line, #1f2630); }
        .studio-tab { display: flex; align-items: center; gap: 6px; padding: 5px 10px; font-size: 12px; border: 1px solid transparent; border-bottom: none; border-radius: 6px 6px 0 0; cursor: pointer; white-space: nowrap; max-width: 200px; overflow: hidden; text-overflow: ellipsis; }
        .studio-tab.active { background: var(--bg-1, #0d1117); border-color: var(--line, #1f2630); }
        .studio-tab .x { opacity: .5; }
        .studio-tab .x:hover { opacity: 1; color: #f85149; }
        .studio-tab.dirty .name::after { content: ' •'; color: #d29922; }
        .studio-editor-wrap { min-height: 0; overflow: auto; background: var(--bg-1, #0d1117); }
        #studioEditorView {
          min-height: 100%;
          padding: 12px;
          outline: none;
          font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
          font-size: 13px;
          line-height: 1.5;
          tab-size: 4;
          white-space: pre-wrap;
          word-break: break-word;
          color: var(--fg, #e6edf3);
        }
        #studioEditorView:empty::before { content: attr(placeholder); opacity: .4; }
        .studio-runbar { display: flex; align-items: center; gap: 8px; padding: 6px 10px; border-top: 1px solid var(--line, #1f2630); background: var(--bg-2, #090c10); }
        .studio-output { margin: 0; padding: 10px 12px; max-height: 200px; overflow: auto; background: #05070a; color: #9da7b3; font-family: ui-monospace, Menlo, Consolas, monospace; font-size: 12px; line-height: 1.4; border-top: 1px solid var(--line, #1f2630); white-space: pre-wrap; }
        .studio-output.err { color: #f85149; }
        /* syntax tokens */
        .tok-kw { color: #ff7b72; }
        .tok-str { color: #a5d6ff; }
        .tok-com { color: #8b949e; font-style: italic; }
        .tok-num { color: #79c0ff; }
        .tok-type { color: #ffa657; }
        .tok-punc { color: #c9d1d9; }
        .file-tree { font-family: ui-monospace, Menlo, Consolas, monospace; font-size: 13px; }
        .file-tree .node-row {
          display: flex; align-items: center; gap: 6px;
          padding: 2px 6px; border-radius: 4px; cursor: pointer; white-space: nowrap;
        }
        .file-tree .node-row:hover { background: rgba(255,255,255,.06); }
        .file-tree .node-row.selected { background: rgba(88,166,255,.18); }
        .file-tree .dir { color: #58a6ff; }
        .file-tree .indent { display: inline-block; width: 14px; }
        .studio-term-panel { display: grid; grid-template-rows: auto 1fr; overflow: hidden; }
        .studio-term-content { display: grid; grid-template-rows: 1fr auto; min-height: 0; padding: 0; }
        #studioTermOut {
          margin: 0; padding: 10px; overflow: auto;
          font-family: ui-monospace, Menlo, Consolas, monospace; font-size: 12px;
          color: #c9d1d9; white-space: pre-wrap; word-break: break-word;
        }
        .term-input-row { display: flex; gap: 6px; padding: 8px; border-top: 1px solid rgba(255,255,255,.08); }
        .term-input-row input { flex: 1; }
        .topology-panel {
          display: grid;
          grid-template-rows: auto 1fr;
          overflow: hidden;
        }
        .schedules-workspace {
          display: grid;
          grid-template-columns: minmax(360px, 1fr) 320px;
          gap: 14px;
          padding: 14px;
          min-height: 0;
          min-width: 0;
        }
        .topology-scroll {
          position: relative;
          min-height: 560px;
          overflow: auto;
          background: var(--surface-soft);
        }
        .topology-canvas {
          position: relative;
          width: 100%;
          min-height: 560px;
        }
        .topology-lines {
          position: absolute;
          inset: 0;
          width: 100%;
          height: 100%;
          pointer-events: none;
          overflow: visible;
        }
        .topology-lines line {
          stroke: var(--line-strong);
          stroke-width: 2;
        }
        .topology-node {
          position: absolute;
          width: 154px;
          min-height: 72px;
          padding: 9px 10px;
          transform: translate(-50%, -50%);
          display: grid;
          align-content: center;
          gap: 3px;
          text-align: left;
          box-shadow: var(--shadow);
          background: var(--surface);
        }
        /* The global button:active { transform: scale(.97) } press-feedback
           REPLACES this node's positioning transform (translate(-50%,-50%)),
           teleporting it half a node-width down-right while pressed - restate
           the translate so the press effect composes with it instead. */
        .topology-node:active { transform: translate(-50%, -50%) scale(.98); }
        .topology-node.selected {
          border-color: var(--accent);
          box-shadow: 0 0 0 3px color-mix(in srgb, var(--accent) 18%, transparent);
        }
        .topology-node.host { border-top: 3px solid var(--accent); }
        .topology-node.overseer { border-top: 3px solid var(--accent-2); }
        .topology-node.worker { border-top: 3px solid var(--ok); }
        .topology-node.offline { border-top-color: var(--bad); opacity: .78; }
        .topology-node-name {
          overflow: hidden;
          text-overflow: ellipsis;
          white-space: nowrap;
          font-weight: 760;
        }
        .topology-role { color: var(--muted); font-size: 12px; }
        .topology-detail {
          display: grid;
          grid-template-rows: auto 1fr;
          overflow: hidden;
        }
        .topology-empty {
          position: absolute;
          inset: 20px;
          display: grid;
          place-items: center;
          color: var(--muted);
        }
        .panel {
          background: var(--surface);
          border: 1px solid var(--line);
          border-radius: var(--radius);
          box-shadow: var(--shadow);
          min-height: 0;
          min-width: 0;
        }
        .side, .inspector {
          display: grid;
          grid-template-rows: auto 1fr;
          overflow: hidden;
        }
        .panel-head {
          min-height: 48px;
          display: flex;
          align-items: center;
          justify-content: space-between;
          gap: 10px;
          padding: 0 14px;
          border-bottom: 1px solid var(--line);
        }
        .panel-title { font-weight: 680; font-size: 11px; text-transform: uppercase;
          letter-spacing: .06em; color: var(--muted); margin-bottom: 10px; }
        /* Inside a flex panel-head the title sits centered next to its
           subtitle - the standalone section-label margin above would push
           it off its baseline there. */
        .panel-head .panel-title, .sidebar-section-head .panel-title { margin-bottom: 0; }
        .small { font-size: 12px; color: var(--muted); }
        .content { padding: 12px; overflow: auto; }
        .kv {
          display: grid;
          gap: 6px;
          margin-bottom: 12px;
        }
        .kv-row {
          display: flex;
          justify-content: space-between;
          gap: 10px;
          padding: 6px 0;
          border-bottom: 1px solid var(--kv-line);
        }
        .mono { font-family: "Cascadia Code", "JetBrains Mono", ui-monospace, SFMono-Regular, Consolas, monospace; font-size: 11.5px; }
        .node-list, .task-list { display: grid; gap: 8px; }
        /* The Jobb list is capped at 8 items but can still be taller than the
           inspector panel has room for - give it its own bounded scroll area
           instead of pushing the panel (and the rest of the page) taller. */
        #tasks { max-height: 340px; overflow-y: auto; padding-right: 4px; }
        .node {
          border: 1px solid var(--line);
          border-radius: 7px;
          padding: 10px;
          display: grid;
          gap: 6px;
          cursor: pointer;
        }
        .node:hover { border-color: var(--line-strong); background: var(--surface-soft); }
        .node.selected { border-color: var(--accent); background: var(--surface-selected); }
        .node-main { display: flex; justify-content: space-between; gap: 8px; }
        .node-status { display: flex; align-items: center; gap: 7px; }
        .node-status .dot { flex: 0 0 auto; }
        .node-meta { display: flex; flex-wrap: wrap; gap: 5px; }
        .pill {
          display: inline-flex;
          align-items: center;
          min-height: 22px;
          padding: 0 8px;
          border-radius: 999px;
          background: var(--surface-2);
          color: var(--muted);
          font-size: 12px;
          white-space: nowrap;
        }
        .chat-panel {
          display: grid;
          grid-template-rows: auto 1fr auto;
          overflow: hidden;
        }
        .chat-head {
          min-height: 56px;
          display: flex;
          align-items: center;
          justify-content: space-between;
          gap: 12px;
          padding: 0 14px;
          border-bottom: 1px solid var(--line);
        }
        .chat-title { font-size: 14px; font-weight: 680; }
        .messages {
          padding: 14px 18px 8px;
          overflow: auto;
          display: flex;
          flex-direction: column;
          gap: 10px;
        }
        /* Hermes/Claude-Code transcript: text and text - NO bubbles at all.
           The user's turns get a thin quote-rail on the left (Claude Code's
           input marker) instead of a box; the assistant's turns are bare
           text on the background. white-space: pre-wrap lives on .msg-text
           (the actual content node) rather than the whole article - on the
           article it also preserved the HTML template's own indentation
           newlines between child divs, which rendered as mysterious blank
           vertical gaps inside every message. */
        .message {
          width: 100%;
          max-width: 100%;
          border: none;
          background: transparent;
          border-radius: 0;
          padding: 3px 2px;
        }
        .msg-text { white-space: pre-wrap; line-height: 1.55; }
        /* User turns sit on the RIGHT (Hermes-style) but stay plain text -
           a thin rail on the right edge instead of a bubble. */
        .message.user {
          width: fit-content;
          max-width: 82%;
          margin-left: auto;
          text-align: right;
          border-right: 2px solid var(--line-strong);
          padding-right: 10px;
        }
        .message.user .message-meta { justify-content: flex-end; }
        .message.user .msg-text { color: var(--muted); }
        .message.assistant { margin-right: auto; }
        /* "Tänker..."-shimmer while a reply is pending/streaming - a light
           sweep across the status word, same feel as Hermes' working lines. */
        @keyframes shimmerSweep {
          0% { background-position: -180px 0; }
          100% { background-position: 180px 0; }
        }
        .thinking-shimmer {
          background: linear-gradient(90deg, var(--muted) 20%, var(--text) 50%, var(--muted) 80%);
          background-size: 180px 100%;
          background-repeat: repeat;
          -webkit-background-clip: text;
          background-clip: text;
          color: transparent;
          animation: shimmerSweep 1.4s linear infinite;
          width: fit-content;
        }
        /* Agentens live-steg i Claude Code/Hermes-stil: resonemang som
           löpande text, verktygsanrop som kompakta rader med namn + kort
           argument, resultat som dämpade enradare - inte en scrollbox. */
        .step-flow { margin-top: 8px; display: flex; flex-direction: column; gap: 3px; }
        .step-think {
          white-space: pre-wrap; line-height: 1.55; opacity: .88;
          margin: 4px 0;
        }
        .step-row {
          display: flex; align-items: baseline; gap: 8px;
          font-size: 12.5px; padding: 1px 0; min-width: 0;
        }
        /* Bara det SENASTE steget animeras in - transkriptet byggs om vid
           varje nytt steg, och en animation pa alla rader hade fatt hela
           flodet att flimra om for varje frame. */
        .step-flow > *:last-child { animation: stepIn .18s ease; }
        .step-row .k { color: var(--muted); flex: none; width: 14px; text-align: center; }
        .step-tool-name {
          font-family: ui-monospace, 'Cascadia Mono', Consolas, monospace;
          font-weight: 600; flex: none;
        }
        .step-tool-args {
          font-family: ui-monospace, 'Cascadia Mono', Consolas, monospace;
          color: var(--muted); overflow: hidden; text-overflow: ellipsis;
          white-space: nowrap; min-width: 0;
        }
        .step-result {
          font-family: ui-monospace, 'Cascadia Mono', Consolas, monospace;
          font-size: 12px; color: var(--muted); opacity: .85;
          padding-left: 22px; overflow: hidden; text-overflow: ellipsis;
          white-space: nowrap;
        }
        .step-error, .step-error .step-tool-args { color: var(--bad, #cf2d56); opacity: 1; }
        @keyframes stepIn { from { opacity: 0; transform: translateY(3px); } to { opacity: 1; transform: none; } }
        .elapsed-timer {
          font-variant-numeric: tabular-nums;
          color: var(--accent, #6ea8fe);
          font-weight: 600;
        }
        .notice.warn {
          background: rgba(207, 45, 86, .12);
          border: 1px solid var(--bad, #cf2d56);
          color: var(--bad, #cf2d56);
          padding: 8px 10px;
          border-radius: 8px;
          margin-bottom: 8px;
          font-size: 13px;
        }

        /* Message-jump rail (Hermes' right-edge minimap): one tick per user
           turn. The whole rail is one generous hover zone - hovering
           ANYWHERE on it slides out a panel listing every sent message
           (click to jump), so nobody has to aim at a 2px line. */
        .chat-outline {
          position: absolute;
          right: 4px;
          top: 50%;
          transform: translateY(-50%);
          z-index: 5;
          padding: 10px 6px;
        }
        .outline-ticks {
          display: flex;
          flex-direction: column;
          gap: 4px;
          align-items: flex-end;
          max-height: 55vh;
          overflow: hidden;
        }
        .outline-tick {
          width: 22px;
          height: 10px;
          min-height: 10px;
          padding: 0;
          border: none;
          background: transparent;
          display: flex;
          align-items: center;
          justify-content: flex-end;
        }
        .outline-tick::after {
          content: "";
          width: 16px;
          height: 2px;
          border-radius: 2px;
          background: var(--line-strong);
          transition: background .12s ease, width .12s ease;
        }
        .outline-tick:hover { background: transparent; border: none; transform: none; }
        .outline-tick:hover::after { background: var(--text); width: 22px; }
        .outline-panel {
          position: absolute;
          right: 100%;
          top: 50%;
          transform: translateY(-50%);
          width: 320px;
          max-height: 380px;
          overflow-y: auto;
          background: var(--surface);
          /* Transparent border instead of a margin-gap: the panel must stay
             a hover-descendant of .chat-outline all the way over, or it
             vanishes while the pointer crosses the gap. */
          border: 1px solid var(--line-strong);
          border-radius: 10px;
          padding: 4px;
          box-shadow: 0 16px 48px rgba(0,0,0,.45);
          display: none;
          gap: 1px;
        }
        .chat-outline:hover .outline-panel,
        .chat-outline:focus-within .outline-panel { display: grid; }
        .outline-item {
          border: none;
          background: transparent;
          text-align: left;
          font-size: 12px;
          color: var(--muted);
          padding: 6px 9px;
          border-radius: 6px;
          min-height: 0;
          white-space: nowrap;
          overflow: hidden;
          text-overflow: ellipsis;
          width: 100%;
          display: block;
        }
        .outline-item:hover { background: var(--surface-2); color: var(--text); border: none; }
        /* Small per-reply action row (Claude Code-style): appears under an
           assistant turn on hover. */
        .msg-actions {
          display: flex;
          align-items: center;
          gap: 2px;
          margin-top: 2px;
          opacity: 0;
          transition: opacity .12s ease;
        }
        .message:hover .msg-actions { opacity: 1; }
        .msg-action {
          border: none;
          background: transparent;
          color: var(--muted);
          width: 24px;
          height: 22px;
          min-height: 22px;
          padding: 0;
          display: inline-flex;
          align-items: center;
          justify-content: center;
          border-radius: 5px;
        }
        .msg-action:hover { background: var(--surface-2); color: var(--text); border: none; }
        .msg-action.copied { color: var(--ok); }
        .msg-action-time { font-size: 11px; color: var(--muted); margin-left: 4px; }
        /* Mini-terminal: the exact message a Worker received, shown inside
           its history entry. */
        .mini-term {
          font-family: "Cascadia Code", ui-monospace, Consolas, monospace;
          font-size: 11.5px;
          background: var(--bg);
          border: 1px solid var(--line);
          border-left: 2px solid var(--accent-2);
          border-radius: 6px;
          padding: 8px 10px;
          margin: 6px 0;
          white-space: pre-wrap;
          color: var(--muted);
        }
        .message-meta {
          display: flex;
          gap: 8px;
          align-items: center;
          color: var(--muted);
          font-size: 11px;
          margin-bottom: 2px;
        }
        .composer {
          border-top: 1px solid var(--line);
          padding: 12px 14px 10px;
          display: grid;
          gap: 8px;
          background: transparent;
        }
        textarea {
          width: 100%;
          min-height: 92px;
          max-height: 220px;
          resize: vertical;
          padding: 11px;
        }
        /* Hermes/Claude-Code-style input card: everything (text, attach,
           mode selectors, send) lives inside one rounded bordered box, and
           the box - not the bare textarea - carries the focus ring. */
        .composer-box {
          border: 1px solid var(--line-strong);
          border-radius: 14px;
          background: var(--surface);
          padding: 12px 12px 10px;
          display: grid;
          gap: 6px;
          transition: border-color .12s ease, box-shadow .12s ease;
        }
        .composer-box:focus-within {
          border-color: var(--accent-2);
          box-shadow: 0 0 0 3px color-mix(in srgb, var(--accent) 8%, transparent);
        }
        .composer-box textarea {
          border: none;
          background: transparent;
          padding: 2px 4px;
          min-height: 72px;
          max-height: 280px;
          resize: none;
          font-size: 13.5px;
        }
        .composer-tools select.mode-danger { color: var(--bad); font-weight: 640; }
        .composer-box textarea:focus { border: none; box-shadow: none; }
        .composer-toolbar {
          display: flex;
          align-items: center;
          justify-content: space-between;
          gap: 8px;
          flex-wrap: wrap;
        }
        .info-box {
          background: rgba(88,166,255,.12);
          border: 1px solid rgba(88,166,255,.4);
          border-radius: 8px;
          padding: 10px 12px;
          margin-bottom: 8px;
        }
        .info-box.hidden { display: none; }
        .info-box-head { font-weight: 600; margin-bottom: 6px; color: #58a6ff; }
        .info-questions { margin: 0 0 8px 18px; padding: 0; }
        .info-questions li { margin-bottom: 4px; }
        .info-answer-row { display: flex; gap: 8px; margin-top: 6px; }
        .info-answer-row input {
          flex: 1; min-height: 30px; padding: 0 10px;
          border-radius: 6px; border: 1px solid rgba(255,255,255,.15);
          background: rgba(0,0,0,.25); color: inherit; font-size: 13px;
        }
        .composer-tools { display: flex; align-items: center; gap: 6px; flex-wrap: wrap; }
        /* Verktygsraden i tre semantiska grupper (åtkomst/modell | körning |
           lägen) - en tunn avdelare i stället för chips/boxar, och gruppens
           label+fält radbryts som EN enhet i stället för att slitas isär. */
        .tool-group { display: flex; align-items: center; gap: 4px; flex-wrap: nowrap; }
        .tool-group + .tool-group { padding-left: 10px; border-left: 1px solid var(--line); }
        .composer-tools .check-field { min-height: 28px; gap: 6px; }
        .composer-tools select {
          min-height: 28px;
          font-size: 12px;
          padding: 0 24px 0 6px;
          border-color: transparent;
          background-color: transparent;
          background-position: right 6px center;
          color: var(--muted);
          max-width: 250px;
        }
        .composer-tools select:hover { background-color: var(--surface-2); color: var(--text); }
        .composer-tools .icon { border-color: transparent; background: transparent; color: var(--muted); }
        .composer-tools .icon:hover { background: var(--surface-2); color: var(--text); border-color: transparent; }
        .composer-tools .small { font-size: 11.5px; }
        .composer-tools input[type="number"] {
          width: 52px;
          min-height: 28px;
          font-size: 12px;
          padding: 0 6px;
          border-color: transparent;
          background: transparent;
          color: var(--muted);
        }
        .composer-tools input[type="number"]:hover,
        .composer-tools input[type="number"]:focus { background: var(--surface-2); color: var(--text); box-shadow: none; }
        .composer-hint { font-size: 11px; color: var(--muted); opacity: .8; }
        .menu-hidden-select { display: none !important; }
        .tool-menu-wrap { position: relative; display: inline-flex; }
        .tool-menu-btn {
          display: inline-flex;
          align-items: center;
          gap: 6px;
          min-height: 28px;
          padding: 0 8px;
          font-size: 12px;
          border: 1px solid transparent;
          background: transparent;
          color: var(--muted);
          border-radius: 6px;
        }
        .tool-menu-btn:hover { background: var(--surface-2); color: var(--text); border-color: transparent; }
        .tool-menu-btn.mode-danger { color: var(--bad); font-weight: 640; }
        .tool-menu {
          position: absolute;
          bottom: calc(100% + 8px);
          left: 0;
          min-width: 240px;
          background: var(--surface);
          border: 1px solid var(--line-strong);
          border-radius: 10px;
          padding: 4px;
          box-shadow: 0 16px 48px rgba(0,0,0,.45);
          z-index: 60;
          display: grid;
          gap: 1px;
        }
        .tool-menu.hidden { display: none; }
        .tool-menu-item {
          display: flex;
          align-items: center;
          gap: 8px;
          border: none;
          background: transparent;
          min-height: 30px;
          padding: 0 10px 0 6px;
          border-radius: 6px;
          font-size: 12.5px;
          color: var(--text);
          text-align: left;
          width: 100%;
        }
        .tool-menu-item:hover { background: var(--surface-2); border: none; }
        .tool-menu-item.disabled { opacity: .45; cursor: default; }
        .tool-menu-check { width: 14px; display: inline-flex; flex: 0 0 auto; }
        .send-btn {
          width: 30px;
          height: 30px;
          min-height: 30px;
          border-radius: 50%;
          padding: 0;
          display: inline-flex;
          align-items: center;
          justify-content: center;
          background: var(--accent);
          border-color: var(--accent);
          color: var(--accent-fg);
          flex: 0 0 auto;
        }
        .send-btn:hover { background: var(--accent); opacity: .88; border-color: var(--accent); }
        .send-btn:disabled { opacity: .35; cursor: default; }
        .attach-chips { display: flex; flex-wrap: wrap; gap: 6px; }
        .attach-chips:empty { display: none; }
        .attach-chip {
          display: inline-flex;
          align-items: center;
          gap: 6px;
          padding: 3px 9px;
          border: 1px solid var(--line);
          border-radius: 999px;
          font-size: 11.5px;
          color: var(--muted);
          background: var(--surface-2);
        }
        .attach-chip button { border: none; background: none; min-height: 0; padding: 0; color: var(--muted); display: inline-flex; }
        .attach-chip button:hover { color: var(--text); background: none; border: none; }
        .composer-actions {
          display: flex;
          align-items: center;
          justify-content: space-between;
          gap: 10px;
          flex-wrap: wrap;
        }
        .inline-fields { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
        .inline-fields input { width: 124px; min-height: 34px; padding: 0 9px; }
        .model-tier-grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 8px; margin-top: 4px; }
        .model-tier-grid .tier-field { display: flex; flex-direction: column; gap: 2px; }
        .model-tier-grid .tier-field input { width: 100%; min-height: 34px; padding: 0 9px; }
        .model-picker { margin-top: 8px; display: flex; flex-direction: column; gap: 8px; }
        .mp-bar { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
        .mp-bar button, .mp-bar select { min-height: 32px; }
        .mp-search { flex: 1; min-width: 130px; min-height: 32px; padding: 0 9px; }
        .mp-chips { display: flex; flex-wrap: wrap; gap: 6px; }
        .mp-chip { border: 1px solid rgba(128,128,128,.28); border-radius: var(--radius); padding: 2px 8px; font-size: 12px; }
        .mp-chip b { color: var(--accent); }
        .mp-ban { border: 1px solid var(--bad-border); color: var(--bad); background: var(--bad-bg); border-radius: var(--radius); padding: 2px 8px; font-size: 12px; cursor: pointer; }
        .mp-wrap { max-height: 320px; overflow: auto; border: 1px solid rgba(128,128,128,.22); border-radius: 8px; }
        .mp-table { width: 100%; border-collapse: collapse; font-size: 12.5px; }
        .mp-table th, .mp-table td { padding: 5px 8px; text-align: left; border-bottom: 1px solid rgba(128,128,128,.14); white-space: nowrap; }
        .mp-table th { position: sticky; top: 0; background: var(--bg); z-index: 1; }
        .mp-table td.num, .mp-table th.num { text-align: right; font-variant-numeric: tabular-nums; }
        .mp-table tr.mp-off { opacity: .4; }
        .mp-table .mp-name { max-width: 240px; overflow: hidden; text-overflow: ellipsis; }
        .mp-table .mp-assign { min-height: 26px; font-size: 12px; }
        .mp-code { font-weight: 600; }
        .mp-hint { color: var(--muted); }
        .provider-list { display: grid; gap: 8px; }
        .provider-row {
          display: grid;
          grid-template-columns: auto 1fr auto auto;
          gap: 8px;
          align-items: center;
          border: 1px solid var(--line);
          border-radius: 7px;
          padding: 9px;
          background: var(--surface-soft);
        }
        .provider-name { font-weight: 700; }
        .provider-id { color: var(--muted); font-size: 12px; }
        .toggle { width: 18px; height: 18px; }
        .host-field {
          display: grid;
          gap: 6px;
          padding: 12px;
          border: 1px solid var(--line);
          border-radius: 7px;
          margin-bottom: 12px;
          background: var(--surface-soft);
        }
        .host-field input { min-height: 34px; padding: 0 9px; width: 100%; }
        .launch-result { margin-top: 8px; color: var(--muted); min-height: 18px; }
        .empty {
          border: 1px dashed var(--line-strong);
          border-radius: 8px;
          color: var(--muted);
          padding: 16px;
          background: var(--surface-soft);
        }
        .cluster-summary {
          display: grid;
          grid-template-columns: repeat(3, 1fr);
          gap: 6px;
          margin-bottom: 10px;
        }
        .metric {
          padding: 8px;
          border-bottom: 2px solid var(--line);
        }
        .metric-value { font-size: 18px; font-weight: 760; }
        .detail-section {
          padding: 4px 0 14px;
          margin-bottom: 14px;
          border-bottom: 1px solid var(--line);
        }
        .detail-section:last-child { border-bottom: 0; }
        .detail-actions { display: flex; gap: 8px; flex-wrap: wrap; margin-top: 10px; }
        .history-list {
          display: grid;
          gap: 6px;
          max-height: 340px;
          overflow-y: auto;
          /* Scrolling to the top/bottom of this list must not "chain" into
             scrolling the panel behind it - the list should absorb the wheel
             input on its own. */
          overscroll-behavior: contain;
        }
        /* Scroll stays fully functional everywhere (wheel, trackpad, drag,
           keyboard) - only the visible track+thumb is hidden, on every
           scrollable region in the app. */
        .content, .messages, .history-list, .topology-scroll, #tasks, .dialog-body {
          scrollbar-width: none;
          -ms-overflow-style: none;
        }
        .content::-webkit-scrollbar, .messages::-webkit-scrollbar, .history-list::-webkit-scrollbar,
        .topology-scroll::-webkit-scrollbar, #tasks::-webkit-scrollbar, .dialog-body::-webkit-scrollbar {
          display: none;
        }
        .hist { border: 1px solid var(--line); border-radius: 7px; background: var(--surface-soft); }
        .hist > summary {
          cursor: pointer;
          padding: 8px 10px;
          display: flex;
          gap: 8px;
          align-items: center;
          justify-content: space-between;
          list-style: none;
        }
        .hist > summary::-webkit-details-marker { display: none; }
        .hist-title { flex: 1 1 auto; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
        .hist-body { padding: 8px 10px 10px; border-top: 1px solid var(--line); white-space: pre-wrap; }
        .hist-body .small { margin-bottom: 6px; }
        .chain { display: flex; gap: 5px; flex-wrap: wrap; margin-top: 7px; }
        .pairing-card {
          border: 1px solid var(--accent);
          border-radius: 7px;
          padding: 10px 12px;
          margin-top: 10px;
          background: var(--surface-active);
          display: flex;
          align-items: center;
          justify-content: space-between;
          gap: 12px;
          flex-wrap: wrap;
        }
        .pairing-card .detail-actions { margin-top: 0; }
        .notice {
          min-height: 38px;
          padding: 9px 10px;
          border: 1px solid var(--ok-border);
          border-radius: 7px;
          background: var(--ok-bg);
          color: var(--ok-text);
          margin-bottom: 10px;
          display: none;
          white-space: pre-wrap;
        }
        .notice.bad { display: block; border-color: var(--bad-border); background: var(--bad-bg); color: var(--bad); }
        .notice.show { display: block; }
        /* Info banners (first-run guidance, update available) are neutral
           cards with an accent rail - the green "success alert" look made
           the very first thing a new user sees feel like a warning. */
        #firstRunBanner.show, #updateBanner.show {
          background: var(--surface-2);
          border-color: var(--line-strong);
          color: var(--text);
          border-left: 3px solid var(--accent);
        }
        .notice.banner-flex { display: flex; align-items: center; gap: 12px; }
        dialog {
          width: clamp(320px, 86vw, 1080px);
          height: clamp(420px, 82vh, 820px);
          border: 1px solid var(--line-strong);
          border-radius: 8px;
          padding: 0;
          color: var(--text);
          background: var(--surface);
          box-shadow: 0 28px 80px rgba(23,32,41,.25);
        }
        /* display is intentionally untouched here - dialog:not([open]) {
           display:none } is the browser's own default and this doesn't
           override it, unlike an earlier version of this rule that set
           display:grid unconditionally and fought the UA's :not([open])
           rule via transition-behavior:allow-discrete + @starting-style.
           That combination looked right on paper (opening worked) but
           left CLOSE genuinely broken: the dialog never actually reached
           display:none, so it stayed in the layout, invisible (opacity 0)
           but still eating pointer-events - the exact "window is still
           there after closing" bug the user hit. JS-driven close (see
           closeSettingsDialog: add .dialog-closing, wait out the
           transition, THEN call .close()) doesn't need the browser to
           discretely animate `display` at all, so there's nothing to get
           stuck. */
        dialog[open] {
          display: grid;
          grid-template-rows: auto 1fr auto;
          opacity: 1;
          transform: scale(1);
          transition: opacity .16s ease, transform .16s ease;
        }
        @starting-style {
          dialog[open] { opacity: 0; transform: scale(.97); }
        }
        dialog[open].dialog-closing { opacity: 0; transform: scale(.97); }
        dialog::backdrop { transition: background-color .16s ease; }
        dialog[open]::backdrop { background-color: rgba(23,32,41,.42); }
        dialog[open].dialog-closing::backdrop { background-color: rgba(23,32,41,0); }
        @starting-style {
          dialog[open]::backdrop { background-color: rgba(23,32,41,0); }
        }
        .dialog-head, .dialog-foot {
          min-height: 58px;
          padding: 10px 16px;
          display: flex;
          align-items: center;
          justify-content: space-between;
          gap: 12px;
          border-bottom: 1px solid var(--line);
        }
        .dialog-foot { border-top: 1px solid var(--line); border-bottom: 0; justify-content: flex-end; }
        .dialog-body { padding: 16px; overflow: auto; min-height: 0; }
        .settings-body {
          display: grid;
          grid-template-columns: 216px 1fr;
          grid-template-rows: auto 1fr;
          padding: 0;
          overflow: hidden;
        }
        .settings-nav {
          grid-row: 2;
          padding: 12px 10px;
          display: grid;
          gap: 3px;
          align-content: start;
          border-right: 1px solid var(--line);
          overflow-y: auto;
          min-height: 0;
        }
        .settings-nav .view-tab { white-space: nowrap; font-size: 12.5px; }
        .settings-content { grid-row: 2; padding: 18px; overflow-y: auto; min-height: 0; }
        .settings-pane .form-subtitle:first-child { margin-top: 0; border-top: none; padding-top: 0; }
        .form-subtitle { font-weight: 640; font-size: 13px; margin: 14px 0 8px; color: var(--muted); border-top: 1px solid var(--line); padding-top: 12px; }
        .form-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 12px; }
        .field { display: grid; gap: 5px; min-width: 0; }
        .field.wide { grid-column: 1 / -1; }
        .field input, .field select { min-height: 38px; padding: 0 10px; width: 100%; min-width: 0; }
        .check-field { display: flex; gap: 8px; align-items: center; min-height: 38px; }
        .settings-provider-list { display: grid; gap: 7px; }
        .settings-provider {
          display: grid;
          grid-template-columns: auto 1fr auto auto;
          align-items: center;
          gap: 8px;
          min-height: 44px;
          padding: 6px 8px;
          border: 1px solid var(--line);
          border-radius: 7px;
        }
        .key-state { color: var(--ok); font-size: 12px; min-height: 17px; }
        .mini-btn { font-size: 12px; padding: 4px 10px; margin-top: 6px; align-self: flex-start; }
        a.mini-btn { display: inline-block; text-decoration: none; color: var(--text); border: 1px solid var(--line); border-radius: 6px; background: var(--surface); transition: background .15s ease, border-color .15s ease; }
        a.mini-btn:hover { background: var(--surface-2); border-color: var(--accent); }
        .msg-preview { margin-top: 8px; display: flex; gap: 8px; flex-wrap: wrap; }
        .node-version-stale { color: var(--bad); font-weight: 600; }
        .contract-card {
          border: 1px solid var(--line); border-radius: 10px;
          padding: 10px 12px; margin: 6px 0; display: grid; gap: 5px;
          background: var(--surface);
        }
        .contract-title { font-size: 11.5px; color: var(--muted); }
        .contract-row { display: flex; align-items: center; gap: 8px; font-size: 12.5px; }
        .contract-row .icon-svg { flex: 0 0 auto; color: var(--muted); }
        .contract-row.met .icon-svg { color: var(--accent); }
        .contract-row.met span { opacity: .75; }
        .contract-row.unmet .icon-svg { color: var(--bad); }
        #benchmarkStatus { white-space: pre-line; color: var(--muted); }
        .bench-table { width: 100%; font-size: 12px; border-collapse: collapse; margin-top: 4px; }
        .bench-table td, .bench-table th { padding: 4px 8px; text-align: left; border-bottom: 1px solid var(--kv-line); }
        .projects-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(320px, 1fr)); gap: 12px; padding: 12px; }
        .project-card { border: 1px solid var(--line); border-radius: 8px; padding: 12px; background: var(--surface); display: flex; flex-direction: column; gap: 6px; transition: border-color .15s ease; }
        .project-card:hover { border-color: var(--accent); }
        .project-card-head { display: flex; justify-content: space-between; align-items: center; gap: 8px; }
        .project-versions { border-top: 1px solid var(--kv-line); margin-top: 4px; padding-top: 6px; display: flex; flex-direction: column; gap: 4px; }
        .project-version-row { display: flex; justify-content: space-between; align-items: center; gap: 8px; }
        .project-iterate { border-top: 1px solid var(--kv-line); margin-top: 4px; padding-top: 8px; display: flex; flex-direction: column; gap: 8px; }
        .project-iterate .mini-btn { margin-top: 0; align-self: auto; }
        .iterate-presets { display: flex; flex-wrap: wrap; gap: 6px; }
        .iterate-row { display: flex; gap: 6px; }
        .iterate-input { flex: 1; min-width: 0; font-size: 12px; padding: 5px 8px; border: 1px solid var(--line); border-radius: 6px; background: var(--surface); color: var(--text); transition: border-color .15s ease; }
        .iterate-input:focus { outline: none; border-color: var(--accent); }
        .iterate-hint { color: var(--muted); }
        .milestone-card { border: 1px solid var(--accent); border-radius: 8px; padding: 10px 12px; margin: 8px 0; background: var(--surface-soft); display: flex; flex-direction: column; gap: 6px; }
        .model-select { width: 100%; margin-top: 6px; font-size: 13px; padding: 6px; }
        .token-row { display: flex; gap: 8px; align-items: center; }
        .token-row input { flex: 1 1 auto; min-width: 0; }
        .token-hint { color: var(--muted); font-size: 12px; margin-top: 6px; }
        /* Mellanläget: ett halvt skärmfönster (981-1280px) är desktop-grid
           men trångt - smalare sidkolumner och tätare luft i stället för att
           innehållskolumnen kläms ihop. Fullscreen ska aldrig vara ett krav. */
        @media (min-width: 981px) and (max-width: 1280px) {
          .shell { grid-template-columns: 210px 1fr; }
          .workspace { grid-template-columns: 250px 1fr; gap: 10px; padding: 10px; }
          .topology-workspace, .schedules-workspace { gap: 10px; padding: 10px; }
          .messages { padding: 10px 12px 6px; }
          .composer { padding: 10px 10px 8px; }
          .composer-tools select { max-width: 190px; }
          .composer-hint { display: none; }
          .topbar { gap: 10px; }
        }
        @media (max-width: 980px) {
          .topbar { height: auto; padding: 10px; align-items: stretch; flex-wrap: wrap; }
          .brand { width: auto; }
          .role-strip { order: 3; width: 100%; max-width: none; grid-template-columns: repeat(3, minmax(0, 1fr)); }
          .role-btn { padding: 0 8px; }
          .status-line { min-width: 0; width: 100%; justify-content: flex-start; }
          .workspace { grid-template-columns: minmax(0, 1fr); padding: 8px; gap: 8px; }
          .topology-workspace { grid-template-columns: minmax(0, 1fr); padding: 8px; gap: 8px; }
          .schedules-workspace { grid-template-columns: minmax(0, 1fr); padding: 8px; gap: 8px; }
          .shell { grid-template-columns: minmax(0, 1fr); }
          .sidebar { border-right: none; border-bottom: 1px solid var(--line); max-height: 46vh; }
          /* Stacked layout has no second column to collapse - hide instead. */
          .shell.sidebar-collapsed .sidebar { display: none; }
          .chat-only-workspace { padding: 8px; }
          .topology-scroll { min-height: 620px; }
          .topology-detail { min-height: 320px; }
          .side, .inspector { min-height: 280px; }
          .messages { padding: 12px; }
          .message { max-width: 100%; }
          .composer { padding: 10px; }
          .inline-fields { width: 100%; }
          .inline-fields input { width: min(140px, 100%); }
          .form-grid { grid-template-columns: minmax(0, 1fr); }
          .field.wide { grid-column: auto; }
        }

        /* Git awareness bar + file-write approval modal */
        .git-bar { display: flex; align-items: center; gap: 10px; padding: 6px 12px;
          border-bottom: 1px solid var(--line); font-size: 12px; flex-wrap: wrap; }
        .git-branch { font-weight: 640; }
        .git-status { color: var(--muted); }
        .git-spacer { flex: 1; }
        .git-commit-input { min-height: 30px; padding: 0 8px; min-width: 180px; }
        .link-btn { background: none; border: none; color: var(--accent); cursor: pointer; padding: 4px; }
        .link-btn:hover { text-decoration: underline; }

        .modal-overlay { position: fixed; inset: 0; background: rgba(0,0,0,.55);
          display: flex; align-items: center; justify-content: center; z-index: 100; padding: 24px; }
        .modal { background: var(--surface); border: 1px solid var(--line); border-radius: var(--radius);
          width: min(820px, 100%); max-height: 86vh; display: flex; flex-direction: column; box-shadow: var(--shadow); }
        .modal-head { display: flex; align-items: flex-start; justify-content: space-between;
          padding: 14px 16px; border-bottom: 1px solid var(--line); }
        .modal-title { font-weight: 700; font-size: 15px; }
        .modal-foot { display: flex; justify-content: flex-end; gap: 10px; padding: 12px 16px; border-top: 1px solid var(--line); }
        .diff-view { margin: 0; padding: 14px 16px; overflow: auto; flex: 1;
          font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; font-size: 12.5px;
          white-space: pre; line-height: 1.5; background: var(--surface-2); }
        .diff-view .add, .diff-view .del { display: block; }

        .update-progress { margin-top: 8px; height: 8px; background: var(--surface-2);
          border: 1px solid var(--line); border-radius: 4px; overflow: hidden; }
        .update-progress-bar { height: 100%; width: 0%; background: var(--accent);
          transition: width .25s ease; }
        /* Vy-byte: diskret in-toning (aterspelas per byte via switchView). */
        .view-enter { animation: viewIn .18s ease; }
        @keyframes viewIn { from { opacity: 0; transform: translateY(4px); } to { opacity: 1; transform: none; } }

        .cost-providers { margin-top: 4px; padding-top: 6px; border-top: 1px solid var(--line); }
        .cost-provider-row { display: flex; justify-content: space-between; font-size: 12px;
          color: var(--muted); padding: 2px 0; }
      </style>
    </head>
    <body>
      <div class="app">
        <header class="topbar">
          <button class="icon ghost" id="sidebarToggleBtn" data-icon="panel-left" title="Visa/dölj sidopanel" style="border-color:transparent"></button>
          <div class="brand">
            <div class="mark">AI</div>
            <div>
              <div class="brand-title">AiLocal</div>
              <div class="small" id="nodeName">starting</div>
            </div>
          </div>
          <div class="role-strip" aria-label="Role selection">
            <button class="role-btn" data-role="Host" data-role-value="1">Host</button>
            <button class="role-btn" data-role="Worker" data-role-value="2">Worker</button>
            <button class="role-btn" data-role="Overseer" data-role-value="3">Overseer</button>
          </div>
          <div class="status-line">
            <span class="dot" id="hostDot"></span>
            <span id="hostStatus">checking host</span>
            <button class="icon" id="authBtn" data-icon="key" title="Anslutningsnyckel (för fjärråtkomst utanför den här datorn)"></button>
            <button class="icon" id="themeBtn" title="Ljust / mörkt läge"></button>
            <button id="settingsBtn">Inställningar</button>
          </div>
          <div class="win-controls">
            <button class="win-btn" data-win="minimize" data-icon="minus" data-icon-size="14" title="Minimera"></button>
            <button class="win-btn" data-win="maximize" data-icon="square" data-icon-size="12" title="Maximera / återställ"></button>
            <button class="win-btn win-close" data-win="close" data-icon="x" data-icon-size="14" title="Stäng"></button>
          </div>
        </header>

        <div style="padding:0 14px">
          <div class="notice" id="globalNotice" style="margin-top:10px"></div>
          <div id="pairingRequests"></div>
          <div id="localNodesBanner"></div>
          <div class="notice" id="firstRunBanner" style="margin-top:10px"></div>
          <div class="notice" id="updateBanner" style="margin-top:10px"></div>
        </div>

        <div class="shell" id="appShell">
          <aside class="sidebar" id="appSidebar">
            <div class="sidebar-top">
              <button class="new-session-btn" id="newSessionTopBtn">
                <span style="display:inline-flex;align-items:center;gap:8px"><span data-icon="plus"></span> Ny session</span>
                <span class="kbd">Ctrl N</span>
              </button>
            </div>
            <nav class="sidebar-nav" aria-label="Vyer">
              <button class="view-tab active" data-view="work"><span data-icon="monitor"></span> Kluster</button>
              <button class="view-tab" data-view="network"><span data-icon="globe"></span> Nätverk</button>
              <button class="view-tab" data-view="schedules"><span data-icon="clock"></span> Schema</button>
              <button class="view-tab" data-view="delegate" id="delegateNavBtn"><span data-icon="send"></span> Delegera till kluster</button>
              <button class="view-tab" data-view="office"><span data-icon="users"></span> Kontorsvy</button>
              <button class="view-tab" data-view="studio"><span data-icon="code"></span> Studio</button>
              <button class="view-tab" data-view="projects"><span data-icon="folder"></span> Projekt</button>
            </nav>
            <div class="sidebar-section">
              <div class="sidebar-section-head">
                <div class="panel-title">Sessioner</div>
                <button class="icon" id="newSessionToggleBtn" data-icon="plus" title="Ny session"></button>
              </div>
              <div class="new-session-form hidden" id="newSessionForm">
                <div class="notice" id="newSessionNotice"></div>
                <select id="sessionHostSelect" class="hidden" title="Vilken Host sessionerna skapas och körs på - mappsökvägen nedan gäller DEN datorn"></select>
                <div class="token-row">
                  <input id="newSessionFolderPath" placeholder="Mappsökväg, t.ex. C:\projekt\mitt-projekt">
                  <button id="browseNewSessionFolder" type="button" data-icon="folder" title="Bläddra..."></button>
                </div>
                <input id="newSessionTitle" placeholder="Namn (valfritt)">
                <div class="composer-actions">
                  <button id="newSessionCancelBtn">Avbryt</button>
                  <button class="primary" id="newSessionCreateBtn">Skapa</button>
                </div>
              </div>
            </div>
            <div class="sidebar-section">
              <input id="sessionSearchInput" placeholder="Sök sessioner...">
            </div>
            <div class="sessions-list" id="sessionsList"></div>
          </aside>
          <div class="views">
        <main class="workspace" id="workView">
          <aside class="panel side">
            <div class="panel-head">
              <div>
                <div class="panel-title">Cluster</div>
                <div class="small" id="nodeCount">0 nodes</div>
              </div>
              <button class="icon" id="updateAllNodesBtn" data-icon="arrow-up" style="display:none"
                title="Uppdatera alla noder - varje worker laddar ner senaste releasen från GitHub och startar om. Noder som bygger just nu hoppas över."></button>
              <button class="icon" id="refreshBtn" data-icon="refresh" title="Refresh"></button>
            </div>
            <div class="content">
              <div class="cluster-summary">
                <div class="metric"><div class="metric-value" id="onlineCount">0</div><div class="small">Online</div></div>
                <div class="metric"><div class="metric-value" id="busyCount">0</div><div class="small">Upptagna</div></div>
                <div class="metric"><div class="metric-value" id="offlineCount">0</div><div class="small">Offline</div></div>
              </div>
              <div class="host-field">
                <label class="small" for="hostInput">Host endpoint</label>
                <input id="hostInput" value="http://127.0.0.1:5080">
                <label class="small" for="launchClusterToken">Klusternyckel (för Worker/Overseer på annan dator)</label>
                <input id="launchClusterToken" placeholder="Klistra in nyckeln från Host-inställningarna">
                <button class="primary" id="quickstartBtn" style="display:none">Starta kluster (Host + Worker)</button>
                <div class="launch-result" id="launchResult"></div>
              </div>
              <div class="node-list" id="nodes"></div>
              <div class="detail-section" id="discoveredWorkersSection" style="display:none;margin-top:12px">
                <div class="panel-title" style="margin-bottom:8px">Upptäckta enheter (ej anslutna)</div>
                <div class="node-list" id="discoveredWorkers"></div>
              </div>
            </div>
          </aside>

          <aside class="panel inspector">
            <div class="panel-head">
              <div>
                <div class="panel-title" id="inspectorTitle">Den här noden</div>
                <div class="small" id="inspectorSub">Lokal konfiguration</div>
              </div>
              <button id="configureNode">Ändra</button>
            </div>
            <div class="content">
              <div id="workerDetail"></div>
              <div id="localDetail">
                <section class="detail-section">
                  <div class="panel-title" style="margin-bottom:8px">Providerordning</div>
                  <div class="provider-list" id="providers"></div>
                  <div class="detail-actions"><button id="saveProviders">Spara ordning</button></div>
                </section>
                <section class="detail-section">
                  <div class="panel-title" style="margin-bottom:8px">Runtime</div>
                <div class="kv" id="runtime">
                  <div class="kv-row"><span>Roll</span><span id="roleLabel">Launcher</span></div>
                  <div class="kv-row"><span>Endpoint</span><span class="mono" id="endpointLabel"></span></div>
                  <div class="kv-row"><span>Jobb</span><span id="taskCount">0</span></div>
                  <div class="kv-row" id="queueRow" style="display:none"><span>Kö / pågår</span><span id="queueLabel">0 / 0</span></div>
                  <div class="kv-row" id="costRow" style="display:none"><span>Kostnad idag</span><span id="costLabel">$0.00</span></div>
                  <div class="kv-row" id="costTotalRow" style="display:none"><span>Kostnad totalt</span><span id="costTotalLabel">$0.00</span></div>
                  <div class="kv-row" id="tokenRow" style="display:none"><span>Tokens idag (in/ut)</span><span class="mono" id="tokenLabel">0 / 0</span></div>
                  <div id="costByProvider" class="cost-providers" style="display:none"></div>
                </div>
                </section>
                <section class="detail-section">
                  <div class="panel-title" style="margin-bottom:8px">Jobb</div>
                  <div class="task-list" id="tasks"></div>
                </section>
                <section class="detail-section">
                  <div class="panel-title" style="margin-bottom:8px;display:flex;justify-content:space-between;align-items:center">
                    <span>Uppgifts-branches</span>
                    <button class="btn ghost sm" id="refreshIsolationBtn" type="button">Uppdatera</button>
                  </div>
                  <div class="small" style="margin-bottom:6px">Varje agent-uppgift med Git-isolering får en egen branch. Granska diffen, merga eller kasta (ångra).</div>
                  <div class="task-list" id="isolationList"><div class="small" style="opacity:.6">Inga aktiva isolerade uppgifter.</div></div>
                </section>
                <section class="detail-section">
                  <div class="panel-title" style="margin-bottom:8px">Team (roller)</div>
                  <div class="small" style="margin-bottom:6px">Varje uppgift körs som en roll: systemprompt + modellval. "Anställda" lämnar över kontext på en delad anteckningsyta.</div>
                  <div class="task-list" id="rolesList"><div class="small" style="opacity:.6">Laddar roller...</div></div>
                </section>
                <section class="detail-section">
                  <div class="panel-title" style="margin-bottom:8px;display:flex;justify-content:space-between;align-items:center">
                    <span>Notiser</span>
                    <span>
                      <label class="check-field" style="margin-right:8px"><input type="checkbox" id="noticeSound"> ljud</label>
                      <button class="btn ghost sm" id="clearNoticesBtn" type="button">Rensa</button>
                    </span>
                  </div>
                  <div class="task-list" id="noticesList"><div class="small" style="opacity:.6">Inga notiser.</div></div>
                </section>
                <section class="panel inspector-card">
                  <div class="panel-title" style="display:flex;align-items:center;justify-content:space-between">
                    <span>Kö (backlog) <span class="small" id="backlogCount" style="opacity:.6"></span></span>
                    <span class="small" style="opacity:.6">startar automatiskt när en worker är ledig</span>
                  </div>
                  <div class="task-list" id="backlogList"><div class="small" style="opacity:.6">Inga köade mål.</div></div>
                </section>
              </div>
            </div>
          </aside>
        </main>

        <main class="chat-only-workspace hidden" id="delegateView">
          <section class="panel chat-panel">
            <div class="chat-head">
              <div>
                <div class="chat-title">Meddelanden</div>
                <div class="small" id="chatSub">Host och Overseer kan skicka mål här.</div>
              </div>
              <span class="pill" id="providerSummary">Local</span>
            </div>
            <div class="messages" id="messages"></div>
            <div class="chat-outline" id="delegateOutline" style="display:none"></div>
            <div class="composer" id="composer">
              <div class="notice" id="composerNotice"></div>
              <div class="composer-box">
                <div class="attach-chips" id="delegateAttachChips"></div>
                <textarea id="prompt" placeholder="Skriv vad du vill att klustret ska göra"></textarea>
                <div class="composer-toolbar">
                  <div class="composer-tools">
                    <div class="tool-group">
                      <button class="icon" id="delegateAttachBtn" data-icon="plus" title="Bifoga filer (förhandsvisning - skickas inte till klustret ännu)"></button>
                      <select id="delegateModeSelect" class="mode-select" title="Agentens behörighet på den här datorn - ändras direkt, gäller alla sessioner på noden">
                        <option value="Off">Behörighet: av (endast chatt)</option>
                        <option value="Sandboxed">Begränsad arbetsyta</option>
                        <option value="Full">Full åtkomst (bypass)</option>
                      </select>
                      <select id="modelSelect" title="Vilken modell Hosten använder - 'Auto' väljer efter uppgiftens komplexitet så du inte alltid betalar för den dyraste. Standard är billiga OpenRouter-modeller (DeepSeek/Kimi/Tencent), inte dyra Claude.">
                        <option value="">Auto (efter komplexitet - billiga modeller)</option>
                        <option value="deepseek/deepseek-chat">DeepSeek V3 (billig, generellt)</option>
                        <option value="deepseek/deepseek-coder">DeepSeek Coder (billig, kod)</option>
                        <option value="moonshotai/kimi-k2">Kimi K2 (billig, stark)</option>
                        <option value="tencent/hy3:free">Tencent Hunyuan (gratis)</option>
                        <option value="anthropic/claude-haiku-4-5">Claude Haiku 4.5 (betald)</option>
                        <option value="anthropic/claude-sonnet-5">Claude Sonnet 5 (betald)</option>
                        <option value="anthropic/claude-opus-4-8">Claude Opus 4.8 (betald, dyr)</option>
                      </select>
                    </div>
                    <div class="tool-group">
                      <label class="small" for="parallelism" title="Hur många deluppgifter som får köras samtidigt">Parallellitet</label>
                      <input id="parallelism" type="number" min="1" max="32" value="4">
                      <label class="small" for="workerSelect" title="Vilken Worker uppdraget körs på - filerna hamnar på DEN datorn. Auto låter Hosten välja.">Worker</label>
                      <select id="workerSelect"><option value="">Auto</option></select>
                    </div>
                    <div class="tool-group">
                      <label class="check-field small" title="PÅ = uppgiften skickas till en Worker i klustret (annan dator) som bygger filer/kör kommandon tills den är klar. AV = vanligt chattsvar som körs lokalt på den här datorn. För att inget ska hända på den här (Overseer-)datorn: kör som Overseer och använd Agentläge.">
                        <input id="assignmentMode" type="checkbox"> Agentläge (kör på Worker)
                      </label>
                      <label class="check-field small" title="PÅ = uppdraget byggs av ett team: en arkitekt delar upp arbetet i oberoende spår, parallella utvecklaragenter bygger i varsin git-worktree, och grenarna mergas ihop. Parallellitet-fältet styr teamstorleken (2-4). Kräver Agentläge.">
                        <input id="teamMode" type="checkbox"> Team-läge
                      </label>
                    </div>
                  </div>
                  <div class="composer-tools">
                    <span class="composer-hint">Enter skickar · Shift+Enter ny rad</span>
                    <button class="send-btn" id="sendBtn" data-icon="arrow-up" title="Skicka"></button>
                  </div>
                </div>
              </div>
              <input type="file" id="delegateFileInput" multiple style="display:none">
            </div>
          </section>
        </main>

        <main class="chat-only-workspace hidden" id="officeView">
          <section class="panel chat-panel">
            <div class="chat-head">
              <div class="chat-title">Kontorsvy - vad alla agenter gor just nu</div>
              <div class="small" style="opacity:.6">Varje ansluten worker som en "skrivbordsrad". Pagaende uppgift, roll och status visas live.</div>
            </div>
            <div class="office-grid" id="officeWorkers"></div>
            <div class="panel-title" style="margin:14px 0 8px">Mal (pagaende forst)</div>
            <div class="task-list" id="officeGoals"></div>
          </section>
        </main>

        <main class="studio-workspace hidden" id="studioView">
          <aside class="panel side studio-tree-panel">
            <div class="panel-head">
              <div class="studio-tabs">
                <button class="studio-tab active" id="studioTabFiles">Filer</button>
                <button class="studio-tab" id="studioTabBranches">Branches</button>
                <button class="studio-tab" id="studioTabNewGame">Nytt spel</button>
                <button class="studio-tab" id="studioTabScreen">Skärm</button>
              </div>
            </div>
            <div class="content">
              <div id="studioFilesPane">
                <div class="studio-root-row">
                  <span class="small" id="studioRoot">ingen mapp vald</span>
                  <button class="icon" id="studioPickRoot" data-icon="folder" title="Välj mapp"></button>
                </div>
                <div class="file-tree" id="fileTree"></div>
              </div>
              <div id="studioBranchesPane" style="display:none">
                <div class="studio-root-row">
                  <span class="small" id="studioAutoMergeState">Auto-merge: av</span>
                  <button class="btn ghost sm" id="studioAutoMergeBtn" title="Kör CI-grind + merge på alla klara branches">Auto-merge alla</button>
                </div>
                <div class="small" style="margin-bottom:6px">Agenternas isolerade branches - granska och merga. När Auto-merge är på (Inställningar) mergas gröna CI-branches automatiskt; röda kastas.</div>
                <div id="studioBranches"></div>
              </div>
            </div>
          </aside>
          <section class="panel studio-editor-panel">
            <div class="panel-head">
              <div>
                <div class="panel-title" id="studioFileName">Ingen fil öppen</div>
                <div class="small mono" id="studioFilePath"></div>
              </div>
              <div style="display:flex;gap:8px">
                <button class="primary sm" id="studioSaveBtn" disabled>Spara</button>
              </div>
            </div>
            <div class="content studio-editor-content">
              <div class="studio-tabs" id="studioTabs"></div>
              <div class="studio-editor-wrap">
                <div class="studio-editor" id="studioEditorView" contenteditable="true" spellcheck="false" placeholder="Välj en fil i trädet till vänster för att öppna den här."></div>
              </div>
              <div class="studio-runbar">
                <button class="btn ghost sm" id="studioBuildBtn" title="Bygg projektet i arbetsmappen">Bygg</button>
                <button class="btn ghost sm" id="studioRunBtn" title="Kör projektet">Kör</button>
                <button class="btn ghost sm" id="studioTestBtn" title="Kör projektets tester">Test</button>
                <button class="btn ghost sm" id="studioGameBtn" title="Bygg spelskivan (Unity/Godot headless)">Bygg spel</button>
                <span class="small mono" id="studioRunState"></span>
              </div>
              <pre class="studio-output" id="studioOutput" style="display:none"></pre>
              <div class="notice" id="studioEditorNotice"></div>
            </div>
          </section>
          <aside class="panel side studio-term-panel">
            <div class="panel-head">
              <div>
                <div class="panel-title">Terminal</div>
                <div class="small" id="studioTermStatus">inte startad</div>
              </div>
              <button class="icon" id="studioTermStart" data-icon="play" title="Starta terminal"></button>
            </div>
            <div class="content studio-term-content">
              <pre id="studioTermOut"></pre>
              <div class="term-input-row">
                <span class="mono" id="studioTermPrompt">$</span>
                <input id="studioTermInput" placeholder="kommando..." autocomplete="off">
              </div>
            </div>
          </aside>
          <aside class="panel side studio-newgame-panel hidden" id="newGamePanel">
            <div class="panel-head">
              <div class="panel-title">Skapa nytt spel</div>
            </div>
            <div class="content">
              <p class="small">Agenten genererar ett komplett spel från din prompt. <b>HTML5</b> = en färdig, spelbar 2D-plattformare (ingen installation, öppna index.html). Unity/Godot = projekt du bygger i motorn.</p>
                <label class="field"><span>Motor</span>
                  <select id="newGameEngine">
                    <option value="html5">HTML5 (spelbar direkt, rekommenderas)</option>
                    <option value="unity">Unity</option>
                    <option value="godot">Godot</option>
                  </select>
                </label>
              <label class="field"><span>Mapp</span>
                <input id="newGameRoot" placeholder="D:\\spel\\MittSpel" />
              </label>
              <label class="field"><span>Prompt</span>
                <textarea id="newGamePrompt" rows="4" placeholder="En 2D-plattformare med hopp och fiender..."></textarea>
              </label>
              <button class="primary sm" id="newGameCreateBtn">Skapa projekt</button>
              <pre class="studio-output" id="newGameOut" style="display:none"></pre>
            </div>
          </aside>
          <aside class="panel side studio-screen-panel hidden" id="screenPanel">
            <div class="panel-head">
              <div class="panel-title">Skärm</div>
              <div style="display:flex;gap:8px">
                <button class="icon" id="screenRefreshBtn" data-icon="refresh" title="Ta ny skärmdump"></button>
              </div>
            </div>
            <div class="content">
              <p class="small" id="screenHint">Slå på "Tillåt skärmkontroll" i Inställningar för att agenten ska kunna se och styra skärmen.</p>
              <div class="screen-shot-wrap">
                <img id="screenImg" alt="skärmdump" style="display:none;width:100%;border-radius:6px" />
              </div>
              <p class="small mono" id="screenState"></p>
              <p class="small">Klicka i bilden för att låta agenten klicka där (kräver skärmkontroll på).</p>
            </div>
          </aside>
        </main>

        <main class="chat-only-workspace hidden" id="sessionView">
          <section class="panel chat-panel">
            <div class="chat-head">
              <div>
                <div class="chat-title" id="sessionTitle">Session</div>
                <div class="small mono" id="sessionFolderPath"></div>
              </div>
              <div style="display:flex;align-items:center;gap:8px">
                <span class="pill" id="sessionAgePill"></span>
                <span class="pill" id="sessionCountPill"></span>
                <span class="pill" id="sessionUsagePill"></span>
                <button class="icon" id="sessionPinBtn" data-icon="pin" title="Nåla"></button>
                <button class="icon" id="sessionDeleteBtn" data-icon="trash" title="Ta bort session"></button>
              </div>
            </div>
            <div class="git-bar" id="gitBar" style="display:none">
              <span class="git-branch" id="gitBranch"></span>
              <span class="git-status" id="gitStatus"></span>
              <button class="link-btn" id="gitDiffBtn">Visa diff</button>
              <span class="git-spacer"></span>
              <input class="git-commit-input" id="gitCommitMsg" placeholder="Commit-meddelande...">
              <button id="gitCommitBtn">Commit</button>
            </div>
            <div class="messages" id="sessionMessages"></div>
            <div class="chat-outline" id="sessionOutline" style="display:none"></div>
            <div class="composer" id="sessionComposer">
            <div class="notice" id="sessionNotice"></div>
            <div class="info-box hidden" id="sessionInfoBox">
              <div class="info-box-head">Agenten behöver info för att fortsätta</div>
              <ul class="info-questions" id="sessionInfoQuestions"></ul>
              <span class="small" id="sessionInfoNote"></span>
              <div class="info-answer-row">
                <input id="sessionInfoAnswer" placeholder="Skriv ditt svar här...">
                <button class="primary" id="sessionInfoSend">Skicka svar</button>
              </div>
            </div>
              <div class="notice warn" id="sessionAgentOffNotice" style="display:none">⚠ Agentläge är avstängt på den här datorn. Meddelanden besvaras som ren text – inget byggs eller skrivs till disk. Sätt "Behörighet" till minst Begränsad för att appen ska kunna skapa filer/spel.</div>
              <div class="composer-box">
                <div class="attach-chips" id="sessionAttachChips"></div>
                <textarea id="sessionPrompt" placeholder="Skriv ett meddelande till agenten i den här mappen"></textarea>
                <div class="composer-toolbar">
                  <div class="composer-tools">
                    <button class="icon" id="sessionAttachBtn" data-icon="plus" title="Bifoga filer (förhandsvisning - skickas inte till agenten ännu)"></button>
                    <select id="sessionModeSelect" class="mode-select" title="Agentens behörighet på den här datorn - ändras direkt, gäller alla sessioner på noden">
                      <option value="Off">Behörighet: av (endast chatt)</option>
                      <option value="Sandboxed">Begränsad arbetsyta</option>
                      <option value="Full">Full åtkomst (bypass)</option>
                    </select>
                  </div>
                  <div class="composer-tools">
                    <span class="small" id="sessionRunningIndicator"></span>
                    <span class="composer-hint">Enter skickar · Shift+Enter ny rad</span>
                    <button id="sessionCancelBtn" class="icon" data-icon="stop" style="display:none" title="Avbryt körningen"></button>
                    <button class="send-btn" id="sessionSendBtn" data-icon="arrow-up" title="Skicka"></button>
                  </div>
                </div>
              </div>
              <input type="file" id="sessionFileInput" multiple style="display:none">
            </div>
          </section>
        </main>

        <main class="topology-workspace hidden" id="networkView">
          <section class="panel topology-panel">
            <div class="panel-head">
              <div>
                <div class="panel-title">Klusternätverk</div>
                <div class="small" id="topologySummary">0 noder</div>
              </div>
              <button id="topologyRefresh">Uppdatera</button>
            </div>
            <div class="topology-scroll">
              <div class="topology-canvas" id="topologyCanvas">
                <svg class="topology-lines" id="topologyLines" aria-hidden="true"></svg>
                <div id="topologyNodes"></div>
              </div>
            </div>
          </section>
          <aside class="panel topology-detail">
            <div class="panel-head">
              <div>
                <div class="panel-title" id="topologyDetailTitle">Nodinformation</div>
                <div class="small" id="topologyDetailSub">Välj en nod i nätverket</div>
              </div>
            </div>
            <div class="content" id="topologyDetail">
              <div class="empty">Ingen nod vald.</div>
            </div>
          </aside>
        </main>

        <main class="chat-only-workspace hidden" id="projectsView">
          <section class="panel">
            <div class="panel-head">
              <div>
                <h2>Projekt</h2>
                <p class="small">Allt som byggts i den här nodens arbetsyta - spela, fortsätt, packa eller rulla tillbaka.</p>
              </div>
              <button class="mini-btn" id="projectsRefreshBtn">Uppdatera</button>
            </div>
            <div class="notice" id="projectsNotice"></div>
            <div id="projectsGrid" class="projects-grid"></div>
          </section>
        </main>

        <main class="schedules-workspace hidden" id="schedulesView">
          <section class="panel side">
            <div class="panel-head">
              <div>
                <div class="panel-title">Schemalagda mål</div>
                <div class="small" id="scheduleSummary">0 scheman</div>
              </div>
              <button id="scheduleRefresh">Uppdatera</button>
            </div>
            <div class="content">
              <div class="empty" id="scheduleHostOnly" style="display:none">
                Schemaläggning styrs av Host-noden. Anslut till en Host för att hantera scheman härifrån.
              </div>
              <div class="node-list" id="schedules"></div>
            </div>
          </section>
          <aside class="panel inspector">
            <div class="panel-head">
              <div>
                <div class="panel-title">Nytt schema</div>
                <div class="small">Körs automatiskt av Host</div>
              </div>
            </div>
            <div class="content">
              <div class="notice" id="scheduleNotice"></div>
              <div class="field wide" style="margin-bottom:10px">
                <span class="small">Namn</span>
                <input id="scheduleName" placeholder="T.ex. Morgonrapport">
              </div>
              <div class="field wide" style="margin-bottom:10px">
                <span class="small">Mål / prompt</span>
                <textarea id="schedulePrompt" style="min-height:70px"></textarea>
              </div>
              <div class="field wide" style="margin-bottom:10px">
                <span class="small">Intervall (minuter, ignoreras om klockslag anges)</span>
                <input id="scheduleInterval" type="number" min="1" value="60">
              </div>
              <div class="field wide" style="margin-bottom:10px">
                <span class="small">Klockslag (UTC, "HH:mm", valfritt)</span>
                <input id="scheduleAtTime" placeholder="t.ex. 07:00">
              </div>
              <div class="detail-actions">
                <button class="primary" id="scheduleCreate">Skapa schema</button>
              </div>
            </div>
          </aside>
        </main>
          </div>
        </div>
        <footer class="statusbar">
          <div class="statusbar-left">
            <span class="dot" id="statusGatewayDot"></span>
            <span id="statusGatewayText">Ansluter...</span>
            <span class="statusbar-sep"></span>
            <span id="statusProjectName" class="small"></span>
            <span class="statusbar-sep"></span>
            <span id="statusAgentsCount"></span>
            <span class="statusbar-sep"></span>
            <span id="statusCronCount"></span>
          </div>
          <div class="statusbar-right">
            <span id="statusSessionTokens" class="mono"></span>
            <span id="statusSessionTokensSep" class="statusbar-sep"></span>
            <span id="statusCost" class="mono"></span>
            <span class="statusbar-sep"></span>
            <span id="statusVersion" class="mono"></span>
          </div>
        </footer>
      </div>

      <dialog id="settingsDialog">
        <div class="dialog-head">
          <div>
            <div class="chat-title" id="settingsTitle">Nodinställningar</div>
            <div class="small" id="settingsSubtitle"></div>
          </div>
          <button class="icon" id="closeSettings" data-icon="x" title="Stäng"></button>
        </div>
        <div class="dialog-body settings-body">
          <div class="notice" id="settingsNotice" style="grid-column:1 / -1"></div>
          <nav class="settings-nav" aria-label="Inställningskategorier">
            <button class="view-tab active" data-settings-cat="general"><span data-icon="monitor"></span> Allmänt</button>
            <button class="view-tab" data-settings-cat="appearance"><span data-icon="sun"></span> Utseende</button>
            <button class="view-tab" data-settings-cat="agent"><span data-icon="folder"></span> Agent &amp; arbetsyta</button>
            <button class="view-tab" data-settings-cat="models"><span data-icon="globe"></span> Modeller &amp; providers</button>
            <button class="view-tab" data-settings-cat="ollama"><span data-icon="monitor"></span> Ollama</button>
            <button class="view-tab" data-settings-cat="memory"><span data-icon="folder"></span> Minne &amp; kontext</button>
            <button class="view-tab" data-settings-cat="security"><span data-icon="key"></span> Säkerhet</button>
            <button class="view-tab" data-settings-cat="notifications"><span data-icon="alert-triangle"></span> Notiser</button>
            <button class="view-tab" data-settings-cat="advanced"><span data-icon="wrench"></span> Avancerat</button>
            <button class="view-tab" data-settings-cat="update"><span data-icon="refresh"></span> Uppdatering</button>
            <button class="view-tab" data-settings-cat="about"><span data-icon="shield"></span> Om</button>
          </nav>
          <div class="settings-content">
            <section class="settings-pane" data-settings-pane="general">
              <div class="form-grid">
                <label class="field"><span class="small">Namn</span><input id="settingNodeName" maxlength="80"></label>
                <label class="field"><span class="small">Host endpoint</span><input id="settingHostEndpoint" placeholder="http://192.168.1.10:5080"></label>
                <label class="check-field"><input id="settingDiscovery" type="checkbox"> Automatisk LAN-upptäckt</label>
                <label class="check-field" id="settingAutoStartRow"><input id="settingAutoStart" type="checkbox"> Starta automatiskt vid inloggning</label>
              </div>
            </section>
            <section class="settings-pane hidden" data-settings-pane="agent">
              <div class="form-grid">
                <label class="field wide">
                  <span class="small">Specialiteter</span>
                  <input id="settingSkills" placeholder="coding, research, writing, data, vision">
                </label>
                <label class="field">
                  <span class="small">Max samtidiga jobb</span>
                  <input id="settingMaxConcurrentTasks" type="number" min="1" max="32">
                </label>
                <label class="field wide">
                  <span class="small">Agentläge (fil- och kommandoåtkomst för "assignment"-uppgifter)</span>
                  <select id="settingAgentAccess">
                    <option value="Off">Av (standard) - den här Workern kör bara vanliga chatt-/målsvar, ingen fil- eller kommandoåtkomst</option>
                    <option value="Sandboxed">Begränsad arbetsyta - läser/skriver filer i en egen mapp på den här datorn, inga terminalkommandon</option>
                    <option value="Full">Full åtkomst - som Claude Code: läser/skriver filer och kör kommandon var som helst på den här datorn</option>
                  </select>
                  <span class="small" style="display:block;margin-top:4px">
                    Avgör bara vad den här datorn tillåter - måste sättas här, en Host kan inte slå på det åt dig.
                  </span>
                </label>
                <label class="field wide">
                  <span class="small">Arbetsmapp (var agenten jobbar)</span>
                  <div class="token-row">
                    <input id="settingWorkspacePath" placeholder="Lämna tom = AgentLocal/agent-workspace">
                    <button id="browseWorkspacePath" type="button" data-icon="folder" title="Bläddra..."></button>
                  </div>
                  <span class="small" style="display:block;margin-top:4px">
                    Sandbox: agenten kan bara läsa/skriva här. Full: även kommandons startmapp. Endast den här datorns ägare sätter detta.
                  </span>
                </label>
                <label class="check-field wide">
                  <input id="settingAiReviewWrites" type="checkbox"> AI-granskning av filändringar
                </label>
                <span class="small" style="display:block;margin-top:-6px">
                  Hostens starkaste modell godkänner varje filskrivning innan den landar på disk under kluster-assignments.
                  Avslag skickas tillbaka till agenten som rättningsinstruktion - byggt för svagare lokala modeller.
                </span>
                <label class="check-field wide">
                  <input id="settingMilestoneApproval" type="checkbox"> Milstolpsgodkännande före bygget
                </label>
                <span class="small" style="display:block;margin-top:-6px">
                  Lokala byggen pausar efter regissörens leveranskontrakt så att du kan godkänna eller styra om med en mening
                  innan agenten bygger. Auto-godkänns efter 10 minuter; klusterkörningar pausar aldrig.
                </span>
                <label class="check-field wide">
                  <input id="settingAllowInternet" type="checkbox"> Internetåtkomst för agenten
                </label>
                <span class="small" style="display:block;margin-top:-6px">
                  Ger agenten ett fetch_url-verktyg så den kan hämta webbsidor (http/https) och läsa dem som text - oberoende av agentlägets filåtkomst.
                </span>
                <label class="check-field wide">
                  <input id="settingUseGitIsolation" type="checkbox"> Git-isolering per uppgift
                </label>
                <span class="small" style="display:block;margin-top:-6px">
                  Varje assignment får sin egen git-worktree + branch i Arbetsmappen (kräver att den är ett git-repo). Flera "anställda" skriver inte över varandra,
                  och när agenten är klar granskas diffen som en PR innan merge. Kräver att Arbetsmapp är ett git-repo - annars körs det vanligt.
                </span>
                <label class="check-field wide">
                  <input id="settingAutoMergeIsolatedTasks" type="checkbox"> Auto-merge av isolerade uppgifter
                </label>
                <span class="small" style="display:block;margin-top:-6px">
                  När CI-grinden (bygg + test i worktreen) passerar mergas uppgiften automatiskt. Misslyckas CI kastas branchen (skillnaden försvinner) och du får en notis. Kräver Git-isolering.
                </span>
                <div class="field wide" style="margin-top:8px">
                  <span class="small">Daglig budget (USD) - A4</span>
                  <input id="settingBudgetLimitUsd" class="input" type="number" min="0" step="0.01" style="max-width:160px">
                </div>
                <span class="small" style="display:block;margin-top:-6px">
                  När dagens kostnad når taket dirigeras nytt arbete till lokal Ollama istället för betalda API:er. 0 = ingen gräns.
                </span>
                <div class="field wide" style="margin-top:8px">
                  <span class="small">Kommando-skydd (run_command)</span>
                  <select id="settingCommandGuard" class="input">
                    <option value="Block">Blockera farliga (rm -rf-klassen)</option>
                    <option value="Warn">Varna men kör ändå</option>
                    <option value="Off">Av (ingen filtrering)</option>
                  </select>
                  <span class="small" style="display:block;margin-top:2px">
                    Små/instabila modeller kan lockas till "rm -rf" av prompt-injektion - skyddet är på (Blockera) som standard, även i fullt filåtkomstläge.
                  </span>
                </div>
                <div class="field wide" style="margin-top:4px">
                  <span class="small">Extra blockerade mönster (regex, nyrad per mönster)</span>
                  <textarea id="settingBlockedCommands" class="input" rows="2" placeholder="t.ex. node reset-db&#10;drop table"></textarea>
                </div>
                <label class="check-field wide" style="margin-top:8px">
                  <input id="settingProjectMemory" type="checkbox"> Projektminne (recall/remember + kodindex)
                </label>
                <span class="small" style="display:block;margin-top:-6px">
                  Ger agenten verktygen recall/remember och bygger ett kodindex + minnesfil (.ailocal-memory.md) i arbetsmappen. "Anställda" bygger upp och återanvänder projektkunskap mellan sessioner.
                </span>
                <label class="check-field wide" style="margin-top:8px">
                  <input id="settingAllowDesktopControl" type="checkbox"> Tillåt skärmkontroll (agenten ser + styr skärmen)
                </label>
                <span class="small" style="display:block;margin-top:-6px">
                  När detta är påtänt kan agenten ta skärmdumpar och klicka/skriva på DIN skärm via Studio-fliken "Skärm" - så den kan se hur ett byggt spel ser ut och styra det. Potent: lämna avstängt om du inte medvetet vill låta agenten styra datorn.
                </span>
                <div class="field wide" style="margin-top:4px">
                  <span class="small">Modell per komplexitet (Hosten väljer, slipper alltid den dyraste)</span>
                  <div class="model-tier-grid">
                    <label class="tier-field"><span class="small">Enkel (1-2)</span>
                      <input id="settingTierSimple" placeholder="deepseek/deepseek-v4-flash"></label>
                    <label class="tier-field"><span class="small">Medel (3-4)</span>
                      <input id="settingTierMedium" placeholder="deepseek/deepseek-v4-pro"></label>
                    <label class="tier-field"><span class="small">Komplex (5)</span>
                      <input id="settingTierComplex" placeholder="z-ai/glm-5.2"></label>
                  </div>
                </div>
                <div class="field wide" style="margin-top:10px">
                  <span class="small">Modellkatalog (OpenRouter) - sortera pa kod-index/kostnad, tilldela tier/fardighet, banna oonskade</span>
                  <div class="model-picker">
                    <div class="mp-bar">
                      <button type="button" id="mpLoad">Hamta katalogen</button>
                      <input id="mpSearch" class="mp-search" placeholder="Sok modell (glm, deepseek, qwen...)">
                      <select id="mpSort">
                        <option value="coding">Sortera: kod-index (bast forst)</option>
                        <option value="in">Sortera: pris in (billigast)</option>
                        <option value="out">Sortera: pris ut (billigast)</option>
                        <option value="context">Sortera: kontext (storst)</option>
                      </select>
                    </div>
                    <div id="mpNote" class="small" style="color:var(--bad)"></div>
                    <div id="mpAssignments" class="mp-chips"></div>
                    <div id="mpBanned" class="mp-chips"></div>
                    <div id="mpWrap" class="mp-wrap"><div class="small mp-hint" style="padding:10px">Tryck "Hamta katalogen" for OpenRouters modeller med kod-index och pris. Klicka Tilldela for att satta tier/fardighet, Banna for att utesluta.</div></div>
                  </div>
                </div>
              </div>
            </section>
            <section class="settings-pane hidden" data-settings-pane="models">
              <div class="form-grid">
                <label class="field"><span class="small">Claude-modell</span><input id="settingAnthropicModel"></label>
                <label class="field"><span class="small">Gemini-modell</span><input id="settingGeminiModel"></label>
                <label class="field"><span class="small">OpenRouter-modell</span>
                  <input id="settingOpenRouterModel" placeholder="openrouter/auto (automatisk)">
                  <button type="button" class="mini-btn" id="fetchOpenRouterModels">Hämta modeller</button>
                  <select id="openRouterModelList" class="model-select hidden"><option value="">— välj från listan —</option></select>
                  <span class="key-state" id="openRouterModelsState"></span>
                </label>
                <label class="field"><span class="small">ChatGPT-modell (OpenAI)</span><input id="settingOpenAIModel" placeholder="t.ex. gpt-4o"></label>
                <label class="field"><span class="small">Lokal Ollama-modell</span><input id="settingOllamaModel" placeholder="Använd rekommenderad"></label>
                <label class="field"><span class="small">Max tokens</span><input id="settingMaxTokens" type="number" min="128" max="131072"></label>
                <label class="field wide"><span class="small">Ollama endpoint</span><input id="settingOllamaEndpoint"></label>
                <label class="check-field wide"><input id="settingAutoPull" type="checkbox"> Hämta vald lokal modell automatiskt</label>
              </div>
              <div class="form-subtitle">Benchmark (självmätning)</div>
              <div class="form-grid">
                <label class="field"><span class="small">Omfång</span>
                  <select id="benchmarkCount">
                    <option value="1">1 prompt (snabb)</option>
                    <option value="3" selected>3 promptar</option>
                    <option value="5">5 promptar (full)</option>
                  </select>
                </label>
                <label class="field"><span class="small">Standardpromptar körs genom nodens egen motor och poängsätts av kvalitetsgrinden - jämför poängen mellan versioner.</span>
                  <button type="button" class="mini-btn" id="benchmarkRunBtn">Kör benchmark</button>
                </label>
                <div class="wide small" id="benchmarkStatus"></div>
                <div class="wide" id="benchmarkHistory"></div>
              </div>
              <div class="form-subtitle">API-nycklar</div>
              <div class="form-grid">
                <label class="field">
                  <span class="small">Claude API-nyckel</span>
                  <input id="settingAnthropicKey" type="password" autocomplete="off" placeholder="Lämna tom för att behålla">
                  <span class="key-state" id="anthropicKeyState"></span>
                </label>
                <label class="field">
                  <span class="small">Gemini API-nyckel</span>
                  <input id="settingGeminiKey" type="password" autocomplete="off" placeholder="Lämna tom för att behålla">
                  <span class="key-state" id="geminiKeyState"></span>
                </label>
                <label class="field">
                  <span class="small">OpenRouter API-nyckel</span>
                  <input id="settingOpenRouterKey" type="password" autocomplete="off" placeholder="Lämna tom för att behålla">
                  <span class="key-state" id="openRouterKeyState"></span>
                </label>
                <label class="field">
                  <span class="small">OpenAI API-nyckel (ChatGPT)</span>
                  <input id="settingOpenAIKey" type="password" autocomplete="off" placeholder="Lämna tom för att behålla">
                  <span class="key-state" id="openAIKeyState"></span>
                </label>
                <label class="check-field"><input id="clearAnthropicKey" type="checkbox"> Ta bort Claude-nyckel</label>
                <label class="check-field"><input id="clearGeminiKey" type="checkbox"> Ta bort Gemini-nyckel</label>
                <label class="check-field"><input id="clearOpenRouterKey" type="checkbox"> Ta bort OpenRouter-nyckel</label>
                <label class="check-field"><input id="clearOpenAIKey" type="checkbox"> Ta bort OpenAI-nyckel</label>
              </div>
              <div class="form-subtitle">Providerordning (fallback-kedja)</div>
              <div class="settings-provider-list" id="settingsProviders"></div>
            </section>
            <section class="settings-pane hidden" data-settings-pane="security">
              <div class="form-grid">
                <div class="field wide">
                  <span class="small">Nuvarande klusternyckel</span>
                  <div class="token-row">
                    <input id="currentClusterToken" class="mono" type="password" readonly>
                    <button class="icon" id="toggleTokenVisibility" data-icon="eye" type="button" title="Visa/dölj"></button>
                    <button id="copyClusterToken" type="button">Kopiera</button>
                    <button id="regenerateClusterToken" type="button">Generera ny</button>
                  </div>
                  <div class="token-hint">
                    Workers och Overseers måste ha samma nyckel för att gå med i klustret.
                    Kopiera den härifrån och klistra in den i deras inställningar (fältet nedan),
                    eller i fältet "Klusternyckel" när du startar en nod manuellt.
                  </div>
                </div>
                <label class="field wide">
                  <span class="small">Klistra in en nyckel för att para ihop den här noden</span>
                  <input id="settingClusterToken" class="mono" type="password" autocomplete="off" placeholder="Lämna tom för att behålla">
                  <span class="key-state" id="clusterTokenState"></span>
                </label>
                <label class="check-field wide"><input id="clearClusterToken" type="checkbox"> Ta bort klusternyckel (öppnar klustret för hela LAN:et)</label>
                <div class="field wide">
                  <span class="small">Operatörsnyckel (begränsad åtkomst: mål, chatt, avbryt - ej nodhantering/inställningar)</span>
                  <div class="token-row">
                    <input id="currentOperatorToken" class="mono" type="password" readonly>
                    <button class="icon" id="toggleOperatorTokenVisibility" data-icon="eye" type="button" title="Visa/dölj"></button>
                    <button id="copyOperatorToken" type="button">Kopiera</button>
                    <button id="regenerateOperatorToken" type="button">Generera ny</button>
                  </div>
                </div>
                <label class="field wide">
                  <span class="small">Klistra in en operatörsnyckel</span>
                  <input id="settingOperatorToken" class="mono" type="password" autocomplete="off" placeholder="Lämna tom för att behålla">
                </label>
                <label class="check-field wide"><input id="clearOperatorToken" type="checkbox"> Ta bort operatörsnyckel</label>
              </div>
            </section>
            <section class="settings-pane hidden" data-settings-pane="update">
              <div id="updateSection">
                <div class="field wide">
                  <span class="small" id="updateStatus">Nuvarande version: ...</span>
                  <div class="token-row">
                    <button id="checkUpdateBtn" type="button">Sök efter uppdatering</button>
                    <button class="primary" id="applyUpdateBtn" type="button" style="display:none">Uppdatera och starta om</button>
                    <a id="updateManualLink" href="#" target="_blank" rel="noopener" style="display:none">Hämta manuellt</a>
                  </div>
                  <div class="update-progress" id="updateProgress" style="display:none">
                    <div class="update-progress-bar" id="updateProgressBar"></div>
                  </div>
                </div>
              </div>
            </section>
            <!-- Panes below are the DESIGN SHELL for the finished app: every
                 planned-but-unbuilt capability gets a visible, honest
                 placeholder (disabled control + "Kommer"-chip) so the full
                 product can be reviewed as a whole before each part is
                 implemented for real. Theme switching in Utseende is live. -->
            <section class="settings-pane hidden" data-settings-pane="appearance">
              <div class="form-grid">
                <label class="field">
                  <span class="small">Tema</span>
                  <select id="settingThemeSelect">
                    <option value="dark">Mörkt</option>
                    <option value="light">Ljust</option>
                    <option value="system" disabled>Följ systemet (kommer)</option>
                  </select>
                </label>
                <label class="field">
                  <span class="small">Accentfärg <span class="soon">Kommer</span></span>
                  <select disabled><option>Mono (grå)</option><option>Nous-blå</option><option>Egen...</option></select>
                </label>
                <label class="field">
                  <span class="small">Täthet <span class="soon">Kommer</span></span>
                  <select disabled><option>Kompakt</option><option>Normal</option><option>Luftig</option></select>
                </label>
                <label class="field">
                  <span class="small">Typsnitt för kod <span class="soon">Kommer</span></span>
                  <select disabled><option>Cascadia Code</option><option>JetBrains Mono</option><option>Consolas</option></select>
                </label>
              </div>
            </section>
            <section class="settings-pane hidden" data-settings-pane="ollama">
              <div class="form-grid">
                <div class="field wide">
                  <span class="small">Installerade modeller <span class="soon">Kommer</span></span>
                  <div class="empty">Listan över lokalt installerade Ollama-modeller visas här, med storlek och möjlighet att ta bort.</div>
                </div>
                <label class="field wide">
                  <span class="small">Hämta ny modell <span class="soon">Kommer</span></span>
                  <div class="token-row">
                    <input disabled placeholder="t.ex. llama3.1:8b, qwen2.5-coder:14b">
                    <button disabled type="button">Hämta</button>
                  </div>
                  <span class="small" style="display:block;margin-top:4px">Rekommendationer utifrån din hårdvara (VRAM) visas här.</span>
                </label>
              </div>
            </section>
            <section class="settings-pane hidden" data-settings-pane="memory">
              <div class="form-grid">
                <div class="field wide">
                  <span class="small">Projektinstruktioner (AILOCAL.md)</span>
                  <span class="small" style="display:block">Lägg en <span class="mono">AILOCAL.md</span> i en sessions mapp så läses den automatiskt in som instruktioner till agenten - fungerar redan idag.</span>
                  <div class="token-row" style="margin-top:6px">
                    <button disabled type="button">Öppna för aktiv session <span class="soon">Kommer</span></button>
                  </div>
                </div>
                <label class="field">
                  <span class="small">Historikgräns per session <span class="soon">Kommer</span></span>
                  <input disabled value="500 meddelanden">
                </label>
                <label class="field">
                  <span class="small">Globalt minne mellan sessioner <span class="soon">Kommer</span></span>
                  <select disabled><option>Av</option><option>På</option></select>
                </label>
              </div>
            </section>
            <section class="settings-pane hidden" data-settings-pane="notifications">
              <div class="form-grid">
                <div class="small">Notiser visas live i panelen <strong>Notiser</strong> (till höger i översikten): mål klart, mål misslyckades, behöver dig (t.ex. pausat utan workers), och worker nere. Ljud-toggle finns i den panelen.</div>
                <label class="check-field wide"><input type="checkbox" disabled> Notis när en filändring väntar på godkännande <span class="soon">Kommer</span></label>
              </div>
            </section>
            <section class="settings-pane hidden" data-settings-pane="advanced">
              <div class="form-grid">
                <div class="field wide">
                  <span class="small">Datamapp</span>
                  <input class="mono" id="settingDataPath" readonly>
                </div>
                <div class="field wide">
                  <div class="token-row">
                    <button disabled type="button">Öppna loggmapp <span class="soon">Kommer</span></button>
                    <button disabled type="button">Exportera sessioner <span class="soon">Kommer</span></button>
                    <button disabled type="button">Rensa historik <span class="soon">Kommer</span></button>
                  </div>
                </div>
                <label class="field">
                  <span class="small">Max tokens per svar</span>
                  <span class="small" style="display:block">Flyttad hit från Modeller i nästa steg <span class="soon">Kommer</span></span>
                </label>
              </div>
            </section>
            <section class="settings-pane hidden" data-settings-pane="about">
              <div class="form-grid">
                <div class="field wide">
                  <span class="small">Version</span>
                  <span class="mono" id="aboutVersion">...</span>
                </div>
                <div class="field wide">
                  <span class="small">Projekt</span>
                  <a href="https://github.com/HappyHamster135/ailocal" target="_blank" rel="noopener">github.com/HappyHamster135/ailocal</a>
                </div>
                <div class="field wide">
                  <span class="small">Vad är AiLocal?</span>
                  <span class="small" style="display:block">Ett privat AI-kluster för hemmalabbet: dina datorer samarbetar mot samma mål med lokala Ollama-modeller och moln-API:er (Claude, ChatGPT, Gemini, OpenRouter), med sessioner per mapp, delegering över nätverket och schemalagda jobb.</span>
                </div>
              </div>
            </section>
          </div>
        </div>
        <div class="dialog-foot">
          <button id="cancelSettings">Avbryt</button>
          <button class="primary" id="saveSettings">Spara</button>
        </div>
      </dialog>

      <!-- File-write approval modal. Must sit BEFORE the script: the script
           executes synchronously at parse time and wires these ids at top
           level, so markup placed after it doesn't exist yet - that exact
           mistake (modal after the script) made the wiring throw and killed
           the whole dashboard script on load. -->
      <div class="modal-overlay" id="diffModal" style="display:none">
        <div class="modal diff-modal">
          <div class="modal-head">
            <div>
              <div class="modal-title">Agenten vill <span id="diffModalKind">skriva en fil</span></div>
              <div class="small mono" id="diffModalPath"></div>
            </div>
            <button class="icon" id="diffModalClose" data-icon="x" title="Stäng"></button>
          </div>
          <pre class="diff-view" id="diffModalBody"></pre>
          <div class="modal-foot">
            <button id="diffRejectBtn">Avvisa</button>
            <button class="primary" id="diffApproveBtn">Godkänn &amp; skriv</button>
          </div>
        </div>
      </div>

      <script>
        const AUTH_HEADER = 'X-AiLocal-Token';
        const stateName = ['Pending','Dispatched','Running','Completed','Failed','Queued','Cancelled','Paused'];
        const cancellableStates = ['Pending','Dispatched','Running','Queued','Paused'];
        const roleNames = {}; // id -> display name, loaded from /api/roles
        const roleName = id => roleNames[id] || id;
        const fmtUsd = value => (value == null) ? '' : (value < 0.01 && value > 0 ? '<$0.01' : `$${value.toFixed(2)}`);
        const fmtTokens = n => (n == null ? '0' : n >= 1000 ? `${(n / 1000).toFixed(1)}k` : `${n}`);
        function renderCostBreakdown(stats) {
          if (!stats) return;
          const today = stats.today || {};
          const all = stats.allTime || {};
          $('costTotalRow').style.display = 'flex';
          $('costTotalLabel').textContent = fmtUsd(all.costUsd) || '$0.00';
          $('tokenRow').style.display = 'flex';
          $('tokenLabel').textContent = `${fmtTokens(today.inputTokens)} / ${fmtTokens(today.outputTokens)}`;
          const rows = (all.byProvider || []).filter(p => (p.costUsd || 0) > 0 || p.tasks > 0);
          const box = $('costByProvider');
          if (rows.length === 0) { box.style.display = 'none'; return; }
          box.style.display = 'block';
          box.innerHTML = rows.map(p =>
            `<div class="cost-provider-row"><span>${providerLabels[p.provider] || p.provider}</span>`
            + `<span class="mono">${fmtUsd(p.costUsd) || '$0.00'} · ${p.tasks} jobb</span></div>`
          ).join('');
        }
        const providerLabels = { anthropic: 'Claude', openai: 'ChatGPT', gemini: 'Gemini', openrouter: 'OpenRouter', ollama: 'Local' };
        const providerIds = ['anthropic', 'openai', 'gemini', 'openrouter', 'ollama'];
        const state = {
          local: null,
          host: null,
          nodes: [],
          topology: { nodes: [], edges: [] },
          tasks: [],
          messages: [],
          assignmentMessages: [],
          nextPlanId: 1,
          workerTasks: [],
          providerOrder: ['anthropic', 'gemini', 'ollama'],
          enabled: { anthropic: true, gemini: true, ollama: true },
          selectedNodeId: null,
          settingsTarget: null,
          settingsOrder: ['anthropic', 'gemini', 'ollama'],
          settingsEnabled: { anthropic: true, gemini: true, ollama: true },
          nodeAction: null,
          activeView: 'work',
          selectedTopologyId: null,
          refreshing: false,
          stats: null,
          queue: null,
          schedules: [],
          streamingTaskId: null,
          streamSource: null,
          streamBuffer: null,
          streamUnavailable: new Set(),
          updateInfo: null,
          firstRunDismissed: false,
          authToken: null,
          discoveredWorkers: [],
          pairingInbound: [],
          pairingConnecting: new Set(),
          pairingErrors: {},
          pairingResponding: new Set(),
          pairingRequestErrors: {},
          openHistoryIds: new Set(),
          // renderInspector() rebuilds these buttons from scratch via
          // innerHTML on every ~3s refresh, which used to silently re-enable
          // them mid-install even while btn.disabled was set directly on the
          // (about-to-be-discarded) DOM node - inviting a duplicate click on
          // an operation the UI already says can "take several minutes".
          nodeBusyAction: null,
          sessions: [],
          activeSessionId: null,
          activeSession: null,
          // Optimistic user turn + live-updating assistant turn for the
          // session currently running - cleared and replaced by the real
          // persisted history (a fresh GET) once the run succeeds, same
          // "don't trust the client's own guess once the server truth is
          // available" approach renderMessages() uses for streaming chat.
          sessionLiveMessages: [],
          sessionSearch: '',
          newSessionFormOpen: false,
          activeSessionRuns: 0,
          // Sant medan DEN HÄR fliken äger en pågående uppdrags-SSE - då har
          // fliken färskare data än den persisterade loggen och rehydrering
          // får inte skriva över den optimistiska vyn (se loadAssignmentLog).
          assignmentStreamLive: false
        };

        const $ = id => document.getElementById(id);
        // Single hand-authored stroke-icon set (no external font/CDN - the
        // app is offline-first, single-file) replacing every emoji in the
        // UI. Every shape uses only safe SVG primitives (line/circle/rect/
        // ellipse/polyline/polygon, at most a single relative arc for
        // "refresh") - no hand-rolled bezier paths that could silently fail
        // to render. currentColor means every icon inherits the button/text
        // color automatically, light or dark theme, no separate icon
        // palette to maintain.
        const ICONS = {
          key: '<circle cx="7" cy="7" r="4"/><line x1="10" y1="10" x2="21" y2="21"/><line x1="14" y1="14" x2="17" y2="11"/><line x1="17" y1="17" x2="20" y2="14"/>',
          file: '<path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/>',
          cog: '<circle cx="12" cy="12" r="3"/><path d="M12 2v3M12 19v3M4.9 4.9l2.1 2.1M17 17l2.1 2.1M2 12h3M19 12h3M4.9 19.1 7 17M17 7l2.1-2.1"/>',
          monitor: '<rect x="2" y="4" width="20" height="14" rx="2"/><line x1="8" y1="21" x2="16" y2="21"/><line x1="12" y1="18" x2="12" y2="21"/>',
          globe: '<circle cx="12" cy="12" r="9"/><ellipse cx="12" cy="12" rx="4" ry="9"/><line x1="3" y1="12" x2="21" y2="12"/>',
          clock: '<circle cx="12" cy="12" r="9"/><line x1="12" y1="7" x2="12" y2="12"/><line x1="12" y1="12" x2="15" y2="15"/>',
          send: '<polygon points="22 2 15 22 11 13 2 9 22 2"/>',
          pin: '<circle cx="12" cy="9" r="6"/><polygon points="7.5 13.5 16.5 13.5 12 21"/>',
          'pin-filled': '<circle cx="12" cy="9" r="6" fill="currentColor"/><polygon points="7.5 13.5 16.5 13.5 12 21" fill="currentColor"/>',
          trash: '<line x1="4" y1="7" x2="20" y2="7"/><line x1="10" y1="11" x2="10" y2="17"/><line x1="14" y1="11" x2="14" y2="17"/><rect x="6" y="7" width="12" height="14" rx="1"/><rect x="9" y="3" width="6" height="4" rx="1"/>',
          eye: '<ellipse cx="12" cy="12" rx="10" ry="6"/><circle cx="12" cy="12" r="3"/>',
          'eye-off': '<ellipse cx="12" cy="12" rx="10" ry="6"/><circle cx="12" cy="12" r="3"/><line x1="2" y1="2" x2="22" y2="22"/>',
          x: '<line x1="6" y1="6" x2="18" y2="18"/><line x1="18" y1="6" x2="6" y2="18"/>',
          wrench: '<line x1="5" y1="19" x2="19" y2="5"/><circle cx="6" cy="18" r="3"/><circle cx="18" cy="6" r="3"/>',
          check: '<polyline points="4 12 9 17 20 6"/>',
          'check-circle': '<circle cx="12" cy="12" r="9"/><polyline points="7.5 12.5 10.5 15.5 16.5 9"/>',
          'alert-triangle': '<polygon points="12 3 22 21 2 21"/><line x1="12" y1="9" x2="12" y2="14"/><circle cx="12" cy="17" r="1" fill="currentColor" stroke="none"/>',
          'x-circle': '<circle cx="12" cy="12" r="9"/><line x1="9" y1="9" x2="15" y2="15"/><line x1="15" y1="9" x2="9" y2="15"/>',
          sun: '<circle cx="12" cy="12" r="4"/><line x1="12" y1="2" x2="12" y2="5"/><line x1="12" y1="19" x2="12" y2="22"/><line x1="2" y1="12" x2="5" y2="12"/><line x1="19" y1="12" x2="22" y2="12"/><line x1="4.9" y1="4.9" x2="7" y2="7"/><line x1="17" y1="17" x2="19.1" y2="19.1"/><line x1="4.9" y1="19.1" x2="7" y2="17"/><line x1="17" y1="7" x2="19.1" y2="4.9"/>',
          moon: '<circle cx="12" cy="13" r="7" fill="currentColor" stroke="none"/><circle cx="17" cy="6" r="1" fill="currentColor" stroke="none"/><circle cx="20" cy="10" r="0.7" fill="currentColor" stroke="none"/>',
          folder: '<polygon points="3 6 9 6 11 8 21 8 21 19 3 19"/>',
          refresh: '<path d="M4 12a8 8 0 1 1 2.5 5.8"/><polyline points="4 17 4 12 9 12"/>',
          plus: '<line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>',
          'chevron-up': '<polyline points="6 15 12 9 18 15"/>',
          'chevron-down': '<polyline points="6 9 12 15 18 9"/>',
          'arrow-up': '<line x1="12" y1="19" x2="12" y2="5"/><polyline points="5 12 12 5 19 12"/>',
          stop: '<rect x="6" y="6" width="12" height="12" rx="2" fill="currentColor" stroke="none"/>',
          minus: '<line x1="5" y1="12" x2="19" y2="12"/>',
          square: '<rect x="6" y="6" width="12" height="12" rx="1"/>',
          copy: '<rect x="9" y="9" width="11" height="11" rx="2"/><rect x="4" y="4" width="11" height="11" rx="2"/>',
          'panel-left': '<rect x="3" y="4" width="18" height="16" rx="2"/><line x1="9" y1="4" x2="9" y2="20"/>',
          shield: '<polygon points="12 3 20 6 20 12 12 21 4 12 4 6"/>',
          users: '<circle cx="9" cy="8" r="3.2"/><path d="M3.5 20a5.5 5.5 0 0 1 11 0"/><circle cx="17" cy="9" r="2.6"/><path d="M15 14.5a5 5 0 0 1 6 5.5"/>',
          code: '<polyline points="8 6 3 12 8 18"/><polyline points="16 6 21 12 16 18"/><line x1="13.5" y1="5" x2="10.5" y2="19"/>',
          play: '<polygon points="8 5 19 12 8 19"/>'
        };
        const icon = (name, size = 16) =>
          `<svg class="icon-svg" width="${size}" height="${size}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${ICONS[name] ?? ''}</svg>`;
        function initIcons() {
          document.querySelectorAll('[data-icon]').forEach(el => {
            el.innerHTML = icon(el.dataset.icon, Number(el.dataset.iconSize) || 16);
          });
        }
        // Must cover quotes too, not just <>& - every call site below embeds
        // this inside double-quoted HTML attributes (data-node-id="${esc(...)}"
        // etc), fed by network input nobody has authenticated yet (a raw LAN
        // discovery beacon's NodeId/Name, or the body of the deliberately-public
        // POST /pairing/request). An unescaped `"` breaks out of the attribute
        // and injects a live event handler - this is exploitable script
        // execution in the dashboard's own origin, not just malformed markup.
        const esc = s => (s ?? '').toString().replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
        const trunc = (s, n) => { s = s ?? ''; return s.length > n ? s.slice(0, n) + '...' : s; };
        const statusText = value => typeof value === 'number' ? ['Idle','Busy','Offline','Error'][value] : value;
        const statusClass = value => {
          const text = (statusText(value) ?? '').toLowerCase();
          return text === 'idle' ? 'ok' : text === 'busy' ? '' : 'bad';
        };
        const ago = value => {
          if (!value) return '';
          const seconds = Math.max(0, Math.round((Date.now() - new Date(value).getTime()) / 1000));
          return seconds < 3 ? 'nu' : seconds < 60 ? `${seconds}s` : `${Math.floor(seconds / 60)}m`;
        };

        // Per-message age label (e.g. "3m sedan"), shown in session history
        // so the operator can see the cadence + how long ago each turn landed.
        const msgAge = m => (m && m.createdAt) ? `<span class="small">${ago(m.createdAt)} sedan</span>` : '';

        // Millisecond -> compact elapsed clock for LIVE run timers
        // (e.g. "12s", "3m 04s", "1h 02m"). Distinct from `ago` (coarse, past).
        const formatDuration = ms => {
          const s = Math.max(0, Math.floor(ms / 1000));
          if (s < 60) return `${s}s`;
          const m = Math.floor(s / 60), r = s % 60;
          if (m < 60) return `${m}m ${String(r).padStart(2, '0')}s`;
          const h = Math.floor(m / 60);
          return `${h}h ${String(m % 60).padStart(2, '0')}m`;
        };

        // Tick all live elapsed-timers in the DOM without a full re-render
        // (keeps scroll position + avoids re-parsing the whole transcript).
        function tickElapsedTimers() {
          document.querySelectorAll('.elapsed-timer[data-started]').forEach(el => {
            const start = Number(el.dataset.started);
            if (start) el.textContent = formatDuration(Date.now() - start);
          });
        }
        setInterval(tickElapsedTimers, 1000);

        async function fetchJson(url, options) {
          const opts = { ...(options || {}) };
          if (state.authToken) {
            opts.headers = { ...(opts.headers || {}), [AUTH_HEADER]: state.authToken };
          }
          const res = await fetch(url, opts);
          const text = await res.text();
          let data = null;
          if (text) {
            try { data = JSON.parse(text); } catch { data = { error: text }; }
          }
          if (!res.ok) {
            throw new Error(data?.detail || data?.error || data?.title || `HTTP ${res.status}`);
          }
          return data;
        }

        async function fetchJsonWithRetry(url, options) {
          let firstError = null;
          for (let attempt = 0; attempt < 4; attempt++) {
            try {
              return await fetchJson(url, options);
            } catch (error) {
              firstError ??= error;
              if (attempt < 3)
                await new Promise(resolve => setTimeout(resolve, 350 * (attempt + 1)));
            }
          }
          throw firstError;
        }

        function activeProviderOrder() {
          return state.providerOrder.filter(id => state.enabled[id]);
        }

        function renderProviders() {
          const order = [...state.providerOrder, ...providerIds.filter(id => !state.providerOrder.includes(id))];
          $('providers').innerHTML = order.map((id, index) => `
            <div class="provider-row">
              <input class="toggle" type="checkbox" ${state.enabled[id] ? 'checked' : ''} data-provider-toggle="${id}">
              <div>
                <div class="provider-name">${providerLabels[id] ?? id}</div>
                <div class="provider-id">${id}</div>
              </div>
              <button class="icon" title="Move up" data-provider-up="${id}" ${index === 0 ? 'disabled' : ''}>${icon('chevron-up', 14)}</button>
              <button class="icon" title="Move down" data-provider-down="${id}" ${index === order.length - 1 ? 'disabled' : ''}>${icon('chevron-down', 14)}</button>
            </div>`).join('');

          $('providerSummary').textContent = activeProviderOrder().map(id => providerLabels[id] ?? id).join(' -> ') || 'Local';

          document.querySelectorAll('[data-provider-toggle]').forEach(input => {
            input.onchange = () => {
              state.enabled[input.dataset.providerToggle] = input.checked;
              renderProviders();
            };
          });
          document.querySelectorAll('[data-provider-up]').forEach(button => {
            button.onclick = () => moveProvider(button.dataset.providerUp, -1);
          });
          document.querySelectorAll('[data-provider-down]').forEach(button => {
            button.onclick = () => moveProvider(button.dataset.providerDown, 1);
          });
        }

        function moveProvider(id, delta) {
          const order = state.providerOrder;
          const index = order.indexOf(id);
          const next = index + delta;
          if (index < 0 || next < 0 || next >= order.length) return;
          [order[index], order[next]] = [order[next], order[index]];
          renderProviders();
        }

        function renderLocal() {
          const local = state.local;
          if (!local) return;
          $('nodeName').textContent = `${local.role} | ${local.name}`;
          $('roleLabel').textContent = local.role;
          $('endpointLabel').textContent = local.endpoint ?? '';
          document.querySelectorAll('.role-btn').forEach(btn => {
            btn.classList.toggle('active', btn.dataset.role === local.role);
            btn.disabled = btn.dataset.role === local.role;
          });
          $('chatSub').textContent = local.role === 'Launcher'
            ? 'Starta Host eller Overseer för att skicka mål.'
            : local.role === 'Worker'
              ? 'Workers tar emot jobb från en Host - mål skickas från Host eller Overseer.'
              : `${local.role}`;
          $('quickstartBtn').style.display = local.role === 'Launcher' ? 'block' : 'none';
          // A Worker has no /api/chat endpoint - it receives dispatched work
          // from a Host, it doesn't accept goals directly. Sessions are
          // unaffected (see #sessionComposer) - /api/sessions is local-only
          // and every role has it.
          $('composer').style.display = local.role === 'Worker' ? 'none' : 'grid';
          $('delegateNavBtn').style.display = local.role === 'Worker' ? 'none' : 'flex';
          if (local.role === 'Worker' && state.activeView === 'delegate') switchView('work');
          // En Overseer kör sessioner PÅ HOST-DATORN (hela /api/sessions
          // proxas dit) - den lokala mappväljaren skulle bläddra på fel
          // maskin, så göm den och förtydliga att sökvägen gäller Hosten.
          const sessionsOnHost = local.role === 'Overseer';
          const browseBtn = $('browseNewSessionFolder');
          if (browseBtn) browseBtn.style.display = sessionsOnHost ? 'none' : '';
          const folderInput = $('newSessionFolderPath');
          if (folderInput) folderInput.placeholder = sessionsOnHost
            ? 'Mappsökväg på HOST-datorn, t.ex. C:\\projekt\\mitt-projekt'
            : 'Mappsökväg, t.ex. C:\\projekt\\mitt-projekt';
          renderInspector();
        }

        function renderHost() {
          // A Worker has no /api/host route at all (it doesn't proxy to
          // anything, unlike Host/Overseer) - state.host stays permanently
          // null there, which used to pin this indicator on "host not
          // connected" forever, even seconds after a fully successful
          // pairing. RegistrationStatus (surfaced on /api/local as
          // local.pairing) is the actual truth for a Worker - it reflects
          // its real ~15s register-heartbeat to the Host, not just whether
          // some endpoint string is saved - so use that instead, including
          // the real reason (bad token, unreachable, ...) when it's not connected.
          if (state.local?.role === 'Worker') {
            const pairing = state.local.pairing;
            const connected = pairing?.state === 'Connected';
            $('hostDot').className = `dot ${connected ? 'ok' : 'bad'}`;
            $('hostStatus').textContent = connected
              ? (state.local.hostEndpoint ?? 'connected')
              : (pairing?.detail || 'host not connected');
            if (state.local.hostEndpoint) $('hostInput').value = state.local.hostEndpoint;
            return;
          }

          const hasHost = !!state.host;
          $('hostDot').className = `dot ${hasHost ? 'ok' : 'bad'}`;
          $('hostStatus').textContent = hasHost ? state.host : 'host not connected';
          if (hasHost) $('hostInput').value = state.host;
        }

        // Mirrors the topbar's own connection dot (renderHost() already did
        // the Worker-vs-Host branching) rather than recomputing it a second
        // time here.
        function renderStatusBar() {
          const topDot = $('hostDot');
          const ok = topDot?.classList.contains('ok');
          $('statusGatewayDot').className = `dot ${ok ? 'ok' : 'bad'}`;
          $('statusGatewayText').textContent = ok ? 'Gateway ready' : ($('hostStatus')?.textContent || 'connecting...');

          $('statusProjectName').textContent = state.local?.name ?? '';

          // Both mechanisms count as "agents doing work right now" - a
          // cluster-dispatched assignment (Host/Overseer "Delegera till
          // kluster") and a session run are different plumbing for the same
          // underlying AgentLoop, and hiding either one would make this
          // figure lie about what's actually running.
          const activeClusterTasks = state.tasks.filter(t => {
            const s = typeof t.state === 'number' ? stateName[t.state] : t.state;
            return cancellableStates.includes(s);
          }).length;
          $('statusAgentsCount').textContent = `${activeClusterTasks + state.activeSessionRuns} agents`;

          const activeCron = state.schedules.filter(s => s.enabled).length;
          $('statusCronCount').textContent = `${activeCron} cron`;

          const activeTokens = state.activeSession?.totalUsage
            ? (state.activeSession.totalUsage.inputTokens || 0) + (state.activeSession.totalUsage.outputTokens || 0)
            : 0;
          $('statusSessionTokens').textContent = activeTokens ? `${activeTokens.toLocaleString('sv-SE')} tok` : '';
          $('statusSessionTokensSep').style.display = activeTokens ? 'inline-block' : 'none';
          $('statusCost').textContent = state.stats ? (fmtUsd(state.stats.today.costUsd) || '$0.00') : '';
          $('statusVersion').textContent = state.updateInfo?.currentVersion ? `v${state.updateInfo.currentVersion}` : '';
        }

        function renderNodes() {
          // Moln-API-rader (id "cloud:*") är visningsrader, inte maskiner -
          // de räknas aldrig in i worker-/online-siffrorna.
          const isCloud = n => String(n.id).startsWith('cloud:');
          const physical = state.nodes.filter(n => !isCloud(n));
          const cloudRows = state.nodes.filter(isCloud);
          const online = physical.filter(n => statusText(n.status) !== 'Offline');
          const busy = physical.filter(n => statusText(n.status) === 'Busy');
          const offline = physical.filter(n => statusText(n.status) === 'Offline');
          $('nodeCount').textContent = `${physical.length} workers` + (cloudRows.length ? ` · ${cloudRows.length} moln` : '');
          $('onlineCount').textContent = online.length;
          $('busyCount').textContent = busy.length;
          $('offlineCount').textContent = offline.length;

          // Flottuppdateringsknappen visas bara när någon nod släpar efter
          // Hostens version - annars finns inget att göra.
          const hostV = state.updateInfo?.currentVersion || null;
          const anyStale = hostV && physical.some(n => n.version && n.version !== hostV);
          const updateAllBtn = $('updateAllNodesBtn');
          if (updateAllBtn) updateAllBtn.style.display = anyStale ? '' : 'none';

          if (state.selectedNodeId && !state.nodes.some(n => n.id === state.selectedNodeId))
            state.selectedNodeId = null;

          // Diff guard: the node list only changes shape/contents on refresh
          // when the data actually changed. Rebuilding innerHTML every 3s (and
          // rebinding every onclick) causes flicker and clobbers any in-flight
          // hover state, so skip the rewrite when the serialized markup is
          // identical. Selection highlight is handled separately below.
          const listHtml = state.nodes.length ? state.nodes.map(n => {
            const cloud = isCloud(n);
            const role = cloud ? 'Moln-API' : (roleName[n.role] ?? n.role);
            const status = statusText(n.status);
            const hardware = n.hardware ? (n.hardware.gpu || n.hardware.cpu) : 'Okänd hårdvara';
            const providers = n.providerPriority?.map(id => providerLabels[id] ?? id).join(' -> ') || 'Ingen provider';
            const skills = (n.skills || ['general']).join(', ');
            const hostVersion = state.updateInfo?.currentVersion || null;
            // Noder < v1.40 saknar /execute/self-update och kan inte flottuppdateras.
            const vParts = String(n.version || '').split('.').map(x => parseInt(x, 10) || 0);
            const tooOldForRemote = !cloud && n.version && (vParts[0] < 1 || (vParts[0] === 1 && vParts[1] < 40));
            const stale = !cloud && n.version && hostVersion && n.version !== hostVersion;
            const versionTitle = tooOldForRemote
              ? 'För gammal för fjärruppdatering (< v1.40) - uppdatera manuellt en gång, sedan går pilknappen'
              : stale
                ? 'Äldre än Hostens v' + esc(hostVersion) + ' - uppdatera via pilknappen ovanför listan'
                : 'Samma version som Hosten';
            const versionSuffix = tooOldForRemote ? ' (för gammal)' : (stale ? ' (äldre)' : '');
            const versionTag = !cloud && n.version
              ? ` | <span class="${stale || tooOldForRemote ? 'node-version-stale' : ''}" title="${versionTitle}">v${esc(n.version)}${versionSuffix}</span>`
              : '';
            // ActiveTasks är rått på Hosten (lokalt startade byggen ligger i
            // selfReportedActive); RunningCount räknar även köade, så bygger = running - köade.
            const running = Math.max(n.activeTasks || 0, n.selfReportedActive || 0);
            const queued = !cloud ? (n.queuedCount || 0) : 0;
            const building = Math.max(running - queued, 0);
            const meta = cloud
              ? 'Redo | uppdrag routas via Hostens API-nyckel'
              : `${esc(status)} | ${building} aktiva${queued > 0 ? ` +${queued} i kö` : ''} | ${esc(ago(n.lastSeen))}${versionTag}`;
            return `<div class="node ${state.selectedNodeId === n.id ? 'selected' : ''}" data-node-id="${esc(n.id)}">
              <div class="node-main">
                <div class="node-status"><span class="dot ${statusClass(n.status)}"></span><strong>${esc(n.name)}</strong></div>
                <span class="pill">${esc(role)}</span>
              </div>
              <div class="small">${meta}</div>
              <div class="small">${esc(trunc(hardware, 48))}</div>
              <div class="small">${esc(trunc(skills, 52))}</div>
              <div class="small">${esc(trunc(providers, 52))}</div>
            </div>`;
          }).join('') : '<div class="empty">Inga Workers har registrerats ännu.</div>';

          // Worker-väljaren i composern speglar agentkapabla online-workers.
          // Egen diff-guard så ~3s-refreshen inte nollställer ett pågående val.
          const workerSel = $('workerSelect');
          if (workerSel) {
            const optionsHtml = '<option value="">Auto</option>' + state.nodes
              .filter(n => statusText(n.status) !== 'Offline' && n.agentAccess && n.agentAccess !== 'Off')
              .map(n => `<option value="${esc(n.id)}">${esc(n.name)}</option>`).join('');
            if (workerSel._html !== optionsHtml) {
              const previous = workerSel.value;
              workerSel.innerHTML = optionsHtml;
              workerSel._html = optionsHtml;
              workerSel.value = previous;
            }
          }

          const listEl = $('nodes');
          if (listEl._html !== listHtml) {
            listEl.innerHTML = listHtml;
            listEl._html = listHtml;
            document.querySelectorAll('[data-node-id]').forEach(card => {
              card.onclick = () => {
                state.selectedNodeId = card.dataset.nodeId;
                state.workerTasks = [];
                renderNodes();
                renderInspector();
                loadWorkerTasks(card.dataset.nodeId);
              };
            });
          } else {
            // Only the selection highlight may have changed - toggle it without
            // a full rebuild.
            document.querySelectorAll('[data-node-id]').forEach(card => {
              card.classList.toggle('selected', card.dataset.nodeId === state.selectedNodeId);
            });
          }
          renderInspector();
        }

        // Click-to-pair, no typing: a Host sees Workers on the LAN before
        // they're registered (via beacon) and can request to connect with one
        // click; the Worker's own operator must still accept it on their side
        // (see renderPairingRequests) before any credential is exchanged.
        function renderDiscoveredWorkers() {
          const section = $('discoveredWorkersSection');
          if (state.local?.role !== 'Host' || !state.discoveredWorkers.length) {
            section.style.display = 'none';
            return;
          }
          section.style.display = 'block';
          $('discoveredWorkers').innerHTML = state.discoveredWorkers.map(w => {
            const connecting = state.pairingConnecting.has(w.id);
            const error = state.pairingErrors[w.id];
            return `<div class="node" style="cursor:default">
              <div class="node-main"><strong>${esc(w.name)}</strong><span class="pill">Worker</span></div>
              <div class="small">${esc(w.endpoint)}</div>
              ${error ? `<div class="small" style="color:var(--bad)">${esc(error)}</div>` : ''}
              <div class="detail-actions">
                <button class="primary" data-connect-worker="${esc(w.id)}" ${connecting ? 'disabled' : ''}>
                  ${connecting ? 'Väntar på godkännande...' : (error ? 'Försök igen' : 'Anslut')}
                </button>
              </div>
            </div>`;
          }).join('');

          document.querySelectorAll('[data-connect-worker]').forEach(button => {
            button.onclick = () => connectToDiscoveredWorker(button.dataset.connectWorker);
          });
        }

        async function connectToDiscoveredWorker(id) {
          delete state.pairingErrors[id];
          state.pairingConnecting.add(id);
          renderDiscoveredWorkers();
          try {
            const result = await fetchJson(`/api/discovered-workers/${id}/connect`, { method: 'POST' });
            if (!result?.requested) {
              state.pairingConnecting.delete(id);
              state.pairingErrors[id] = 'Kunde inte skicka anslutningsförfrågan.';
              renderDiscoveredWorkers();
            }
          } catch (error) {
            state.pairingConnecting.delete(id);
            state.pairingErrors[id] = error.message;
            renderDiscoveredWorkers();
          }
        }

        // The other half of the same handshake: this node received a connect
        // request from a Host (see /pairing/request) and its own operator must
        // explicitly accept or reject it before anything is trusted.
        function renderPairingRequests() {
          const box = $('pairingRequests');
          if (!state.pairingInbound.length) {
            box.innerHTML = '';
            return;
          }
          box.innerHTML = state.pairingInbound.map(r => {
            const busy = state.pairingResponding.has(r.requesterId);
            const error = state.pairingRequestErrors[r.requesterId];
            return `
            <div class="pairing-card">
              <div>
                <strong>${esc(r.requesterName)}</strong> vill ansluta den här datorn till sitt kluster.
                <div class="small mono">${esc(r.requesterEndpoint)}</div>
                ${error ? `<div class="small" style="color:var(--bad)">${esc(error)}</div>` : ''}
              </div>
              <div class="detail-actions">
                <button class="primary" data-accept-pairing="${esc(r.requesterId)}" ${busy ? 'disabled' : ''}>
                  ${busy ? 'Ansluter...' : (error ? 'Försök igen' : 'Anslut')}
                </button>
                <button data-reject-pairing="${esc(r.requesterId)}" ${busy ? 'disabled' : ''}>Avvisa</button>
              </div>
            </div>`;
          }).join('');

          document.querySelectorAll('[data-accept-pairing]').forEach(button => {
            button.onclick = () => respondToPairingRequest(button.dataset.acceptPairing, true);
          });
          document.querySelectorAll('[data-reject-pairing]').forEach(button => {
            button.onclick = () => respondToPairingRequest(button.dataset.rejectPairing, false);
          });
        }

        async function respondToPairingRequest(hostId, accept) {
          const path = accept ? 'accept' : 'reject';
          delete state.pairingRequestErrors[hostId];
          state.pairingResponding.add(hostId);
          renderPairingRequests();
          try {
            await fetchJson(`/pairing/pending/${hostId}/${path}`, { method: 'POST' });
            state.pairingResponding.delete(hostId);
            state.pairingInbound = state.pairingInbound.filter(r => r.requesterId !== hostId);
            renderPairingRequests();
            if (accept) await refresh();
          } catch (error) {
            // Surfaced right on the request card, not the chat composer's
            // notice - that's hidden entirely on a Worker's dashboard (no
            // /api/chat here), so an error shown there would be invisible.
            state.pairingResponding.delete(hostId);
            state.pairingRequestErrors[hostId] = error.message;
            renderPairingRequests();
          }
        }

        // Starting a Worker deliberately doesn't navigate this page away from
        // wherever the operator already is (see launchRole) - so a pending
        // click-to-pair request on that Worker could otherwise go completely
        // unnoticed. This surfaces it right where the operator already is,
        // with a one-click link to the Worker's own dashboard to act on it.
        function renderLocalNodes(nodes) {
          const box = $('localNodesBanner');
          const withRequests = (nodes || []).filter(n => n.pendingPairingRequests > 0);
          if (!withRequests.length) {
            box.innerHTML = '';
            return;
          }
          box.innerHTML = withRequests.map(n => `
            <div class="pairing-card">
              <div>
                <strong>${esc(n.role)}</strong> (${esc(n.endpoint)}) har
                ${n.pendingPairingRequests} väntande anslutningsförfrågan${n.pendingPairingRequests > 1 ? 'ar' : ''}.
              </div>
              <div class="detail-actions">
                <button class="primary" data-open-local-node="${esc(n.endpoint)}">Öppna</button>
              </div>
            </div>`).join('');

          document.querySelectorAll('[data-open-local-node]').forEach(button => {
            button.onclick = () => { window.location.href = withCurrentTheme(button.dataset.openLocalNode); };
          });
        }

        // ---- Sessions (folder-bound, resumable agent conversations) ----
        // Local-only by design (see SessionApi's doc comment) - every call
        // here hits THIS node's own /api/sessions, never a Host-mediated
        // proxy, so there's no WorkerId to pick: whichever node's dashboard
        // you're looking at is where the session runs.

        function folderBaseName(path) {
          const trimmed = (path || '').replace(/[\\/]+$/, '');
          const parts = trimmed.split(/[\\/]/);
          return parts[parts.length - 1] || path || '';
        }

        async function loadSessions() {
          try {
            state.sessions = await fetchJson('/api/sessions') ?? [];
          } catch {
            state.sessions = [];
          }
          renderSessions();
        }

        function sessionItemHtml(s) {
          const active = state.activeSessionId === s.id ? 'active' : '';
          return `
          <div class="session-item ${active}">
            <div class="session-item-row">
              <button class="icon-mini" data-session-pin="${esc(s.id)}" title="${s.pinned ? 'Ta bort nål' : 'Nåla'}">${icon(s.pinned ? 'pin-filled' : 'pin', 13)}</button>
              <div class="session-item-title" data-session-open="${esc(s.id)}" title="${esc(s.title)}">${esc(s.title)}</div>
              <button class="icon-mini" data-session-delete="${esc(s.id)}" title="Ta bort">${icon('x', 13)}</button>
            </div>
            <div class="session-item-meta" data-session-open="${esc(s.id)}">${esc(folderBaseName(s.folderPath))} · ${ago(s.lastActiveAt)}</div>
          </div>`;
        }

        function renderSessions() {
          const q = state.sessionSearch.trim().toLowerCase();
          const filtered = !q ? state.sessions : state.sessions.filter(s =>
            s.title.toLowerCase().includes(q) || s.folderPath.toLowerCase().includes(q));

          const pinned = filtered.filter(s => s.pinned);
          const rest = filtered.filter(s => !s.pinned);
          const groups = new Map();
          for (const s of rest) {
            const key = folderBaseName(s.folderPath);
            if (!groups.has(key)) groups.set(key, []);
            groups.get(key).push(s);
          }

          let html = '';
          if (pinned.length) html += `<div class="session-group-label">Nålade</div>${pinned.map(sessionItemHtml).join('')}`;
          for (const [group, items] of groups)
            html += `<div class="session-group-label">${esc(group)}</div>${items.map(sessionItemHtml).join('')}`;

          $('sessionsList').innerHTML = html || `<div class="empty" style="padding:14px">
            ${q ? 'Inga sessioner matchar sökningen.' : 'Inga sessioner ännu - klicka + för att skapa en.'}</div>`;

          document.querySelectorAll('[data-session-open]').forEach(el => {
            el.onclick = () => openSession(el.dataset.sessionOpen);
          });
          document.querySelectorAll('[data-session-pin]').forEach(btn => {
            btn.onclick = () => toggleSessionPin(btn.dataset.sessionPin);
          });
          document.querySelectorAll('[data-session-delete]').forEach(btn => {
            btn.onclick = () => deleteSession(btn.dataset.sessionDelete);
          });
        }

        async function openSession(id) {
          try {
            const session = await fetchJson(`/api/sessions/${id}`);
            state.activeSessionId = id;
            state.activeSession = session;
            state.sessionLiveMessages = [];
            switchView('session');
            renderSessions();
            renderSessionView();
          } catch (error) {
            showGlobalNotice(error.message, true);
          }
        }

        function renderSessionView() {
          const s = state.activeSession;
          if (!s) return;
          $('sessionTitle').textContent = s.title;
          $('sessionFolderPath').textContent = s.folderPath;
          $('sessionPinBtn').innerHTML = icon(s.pinned ? 'pin-filled' : 'pin');
          $('sessionPinBtn').title = s.pinned ? 'Ta bort nål' : 'Nåla';
          const usage = s.totalUsage;
          const totalTokens = usage ? (usage.inputTokens || 0) + (usage.outputTokens || 0) : 0;
          $('sessionUsagePill').textContent = totalTokens ? `${totalTokens.toLocaleString('sv-SE')} tokens` : '';
          // Session context polish: age since created + message count, next
          // to the token pill. Age uses data-started so the 1s tick keeps it
          // live without a full re-render.
          const ageEl = $('sessionAgePill');
          if (ageEl) {
            if (s.createdAt) {
              const start = new Date(s.createdAt).getTime();
              ageEl.dataset.started = String(start);
              ageEl.className = 'pill elapsed-timer';
              ageEl.textContent = formatDuration(Date.now() - start) + ' sedan';
            } else { ageEl.textContent = ''; }
          }
          const countEl = $('sessionCountPill');
          if (countEl) countEl.textContent = `${(s.messages || []).length} meddelanden`;
          renderSessionMessages();
          wireGitBar();
          loadGitStatus();
        }

        // Hopprailen till höger: ett streck per användar-tur. Hela railen är
        // en hover-yta - så fort pekaren är över den fälls en panel ut med
        // ALLA skickade meddelanden i klartext, klickbara för att hoppa.
        // Byggs om efter varje render eftersom transkriptet ersätts via
        // innerHTML.
        function renderChatOutline(containerId, outlineId) {
          const container = $(containerId);
          const outline = $(outlineId);
          if (!container || !outline) return;
          const userTurns = [...container.querySelectorAll('.message.user')];
          if (userTurns.length === 0) {
            outline.innerHTML = '';
            outline.style.display = 'none';
            return;
          }
          outline.style.display = 'block';
          const texts = userTurns.map(el => (el.querySelector('.msg-text')?.textContent ?? '').trim());
          outline.innerHTML = `
            <div class="outline-ticks">
              ${userTurns.map((_, i) => `<button class="outline-tick" data-outline-idx="${i}"></button>`).join('')}
            </div>
            <div class="outline-panel">
              ${texts.map((text, i) => `<button class="outline-item" data-outline-idx="${i}" title="${esc(trunc(text, 200))}">${esc(trunc(text, 90))}</button>`).join('')}
            </div>`;
          outline.querySelectorAll('[data-outline-idx]').forEach(el => {
            el.onclick = () => {
              const target = container.querySelectorAll('.message.user')[Number(el.dataset.outlineIdx)];
              if (target) target.scrollIntoView({ behavior: 'smooth', block: 'center' });
            };
          });
        }

        // Kopiera-knappen i svarsraden: texten hämtas från meddelandets egen
        // DOM (samma innehåll oavsett vilket transkript den sitter i).
        function wireMessageActions(containerId) {
          $(containerId)?.querySelectorAll('.msg-action[data-copy]').forEach(btn => {
            btn.onclick = async () => {
              // Hela transkriptet, inte bara slutraden: ett uppdrag bär sitt
              // verkliga innehåll i stegvisningen (rapport: kopiera gav bara
              // "✗ network error" när slutsvaret var kort/fel).
              const article = btn.closest('.message');
              const stepLines = [...(article?.querySelectorAll('.step-flow > *, .contract-card') ?? [])]
                .map(el => el.textContent.replace(/\s+/g, ' ').trim())
                .filter(Boolean);
              const finalText = article?.querySelector('.msg-text')?.textContent ?? '';
              const text = [...stepLines, finalText].filter(Boolean).join('\n');
              try {
                await navigator.clipboard.writeText(text);
                btn.classList.add('copied');
                setTimeout(() => btn.classList.remove('copied'), 900);
              } catch { /* clipboard kan nekas - knappen gör då inget */ }
            };
          });
        }
        const msgActionsHtml = `
          <div class="msg-actions">
            <button class="msg-action" data-copy title="Kopiera svaret">${icon('copy', 12)}</button>
          </div>`;

        function persistedSessionMessageHtml(m) {
          const ta = formatToolActivity(m);
          if (ta) return ta;
          if (m.role === 'user')
            return `<article class="message user"><div class="message-meta"><strong>Du</strong>${msgAge(m)}</div><div class="msg-text">${esc(m.content)}</div></article>`;
          if (m.role === 'tool')
            return `<article class="message assistant">
              <div class="message-meta"><span class="pill">✓ ${esc(m.toolName ?? 'verktyg')}</span>${msgAge(m)}</div>
              <div class="mono small msg-text">${esc(trunc(m.content ?? '', 2000))}</div>
            </article>`;
          const toolLines = (m.toolCalls ?? []).map(tc => `> ${tc.name}(${trunc(tc.argumentsJson ?? '', 160)})`).join('\n');
          const body = [toolLines, m.content].filter(Boolean).join('\n');
          return `<article class="message assistant">
            <div class="message-meta"><strong>AiLocal</strong>${msgAge(m)}</div>
            <div class="msg-text">${body ? esc(body) : '<span class="small">...</span>'}</div>
            ${body ? msgActionsHtml : ''}
          </article>`;
        }

        function liveSessionBubbleHtml(m) {
          const ta = formatToolActivity(m);
          if (ta) return ta;
          if (m.role === 'user')
            return `<article class="message user"><div class="message-meta"><strong>Du</strong></div><div class="msg-text">${esc(m.content)}</div></article>`;
          // Claude Code-stil: agentens resonemang och verktygsanrop strömmar
          // in som ett steg-flöde (löpande text + kompakta verktygsrader);
          // slutsvaret läggs som meddelandets egen text under. Innan första
          // steget visar flödet självt ett skimrande "startar…".
          const running = m.state === 'Running';
          const steps = stepRowsHtml(m.steps, running && !m.content);
          const body = m.content ? `<div class="msg-text">${esc(m.content)}</div>` : '';
          return `<article class="message assistant">
            <div class="message-meta"><strong>AiLocal</strong>${m.state && m.state !== 'Running' ? `<span class="small">${esc(m.state === 'Completed' ? '' : m.state)}</span>` : ''}</div>
            ${steps}
            ${body}
          </article>`;
        }

        function renderSessionMessages() {
          const box = $('sessionMessages');
          const persisted = state.activeSession?.messages ?? [];
          const all = [...persisted.map(m => persistedSessionMessageHtml(m)), ...state.sessionLiveMessages.map(m => liveSessionBubbleHtml(m))];
          if (!all.length) {
            box.innerHTML = `<div class="empty empty-hero">
              <div class="hero-mark">AI</div>
              <div class="hero-title">Vad ska agenten göra i den här mappen?</div>
              <div class="small">Den läser, skriver och kör kommandon i mappens kontext - historiken sparas och går att återuppta.</div>
            </div>`;
            renderChatOutline('sessionMessages', 'sessionOutline');
            return;
          }
          const wasNearBottom = box.scrollHeight - box.scrollTop - box.clientHeight < 80;
          const previousScrollTop = box.scrollTop;
          box.innerHTML = all.join('');
          box.scrollTop = wasNearBottom ? box.scrollHeight : previousScrollTop;
          renderChatOutline('sessionMessages', 'sessionOutline');
          wireMessageActions('sessionMessages');
        }

        // --- Git awareness (session folder) ---
        // These lived NESTED inside renderSessionMessages (a stray brace
        // during that feature's merge) - function declarations inside a
        // function are scoped to it, so renderSessionView's wireGitBar()/
        // loadGitStatus() calls and the run-loop's handleApprovalStep()
        // threw ReferenceError: opening a session crashed its render and
        // the git bar + approval modal never worked in the shipped build.
        function wireGitBar() {
          $('gitDiffBtn').onclick = () => showGitDiff();
          $('gitCommitBtn').onclick = () => doGitCommit();
        }

        async function loadGitStatus() {
          const id = state.activeSessionId;
          const bar = $('gitBar');
          if (!id) { bar.style.display = 'none'; return; }
          try {
            const status = await fetchJson(`/api/sessions/${id}/git/status`);
            if (!status || !status.isRepo) { bar.style.display = 'none'; return; }
            bar.style.display = 'flex';
            $('gitBranch').textContent = status.branch ? `⎇ ${status.branch}` : '(ingen branch)';
            const parts = [];
            if (status.ahead) parts.push(`↑${status.ahead}`);
            if (status.behind) parts.push(`↓${status.behind}`);
            const counts = [
              status.staged?.length && `${status.staged.length} staged`,
              status.unstaged?.length && `${status.unstaged.length} ändrad`,
              status.untracked?.length && `${status.untracked.length} ny`
            ].filter(Boolean);
            $('gitStatus').textContent = [...parts, ...counts].join(' · ') || 'rent';
          } catch {
            bar.style.display = 'none';
          }
        }

        async function showGitDiff() {
          const id = state.activeSessionId;
          if (!id) return;
          const { diff } = await fetchJson(`/api/sessions/${id}/git/diff`) ?? { diff: '' };
          openDiffModal('Arbetskopia', '(git diff)', diff);
        }

        async function doGitCommit() {
          const id = state.activeSessionId;
          const msg = $('gitCommitMsg').value.trim();
          if (!id || !msg) { showSessionNotice('Skriv ett commit-meddelande.', true); return; }
          try {
            await fetchJson(`/api/sessions/${id}/git/commit`, {
              method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ message: msg })
            });
            $('gitCommitMsg').value = '';
            showSessionNotice('Commit skapad.');
            await loadGitStatus();
          } catch (error) {
            showSessionNotice(error.message, true);
          }
        }

        // Turn a raw tool-call (name + JSON args/parameters) into one
        // readable line instead of dumping the JSON into the history.
        function describeToolCall(name, argsJson) {
          let n = (name || '').trim();
          let a = null;
          try { a = argsJson ? JSON.parse(argsJson) : null; } catch { a = null; }
          if (a && typeof a === 'object' && a.name && a.parameters) { n = n || a.name; a = a.parameters; }
          const arg = k => a && a[k] != null ? String(a[k]) : '';
          const map = {
            scaffold: () => `scaffold: ${arg('engine') || 'spel'} i ${arg('rootFolder') || 'mapp'}`,
            write_file: () => `skriver fil: ${arg('path') || arg('file_path') || '?'}`,
            create_file: () => `skapar fil: ${arg('path') || '?'}`,
            read_file: () => `läser fil: ${arg('path') || '?'}`,
            list_files: () => `listar: ${arg('path') || 'mapp'}`,
            verify: () => `verifierar ändring`,
            run_command: () => `kör kommando: ${arg('command') || arg('cmd') || '?'}`
          };
          const made = map[n];
          if (made) return made();
          const compact = a && typeof a === 'object' ? JSON.stringify(a).slice(0, 120) : '';
          return compact ? `${n || 'verktyg'}: ${compact}` : (n || 'verktyg');
        }

        // Render any tool activity (tool role, assistant toolCalls, or an
        // assistant message whose content is a raw tool-call JSON blob) as a
        // single readable line. Returns HTML or null if not a tool activity.
        function formatToolActivity(m) {
          if (m.role === 'tool') {
            return `<article class="message assistant"><div class="message-meta"><span class="pill">✓ ${esc(describeToolCall(m.toolName, m.content))}</span></div></article>`;
          }
          const calls = m.toolCalls;
          if (calls && calls.length) {
            const lines = calls.map(tc => `> ${esc(describeToolCall(tc.name, tc.argumentsJson))}`).join('\n');
            return `<article class="message assistant"><div class="message-meta"><strong>AiLocal</strong></div><div class="msg-text mono small">${lines}</div></article>`;
          }
          if (m.content) {
            const c = m.content.trim();
            if (c.startsWith('{') && c.includes('"name"')) {
              const txt = describeToolCall(null, c);
              return `<article class="message assistant"><div class="message-meta"><strong>AiLocal</strong></div><div class="msg-text mono small">> ${esc(txt)}</div></article>`;
            }
          }
          return null;
        }

        // --- File-write approval (preview before save) ---
        function openDiffModal(kindLabel, path, body) {
          $('diffModalKind').textContent = kindLabel;
          $('diffModalPath').textContent = path || '(okänd fil)';
          $('diffModalBody').textContent = body || '(tom fil / ingen skillnad att visa)';
          $('diffModal').style.display = 'flex';
        }
        function closeDiffModal() { $('diffModal').style.display = 'none'; }

        async function approvePendingChange(approve, reason) {
          const id = state.activeSessionId;
          if (!id) return;
          try {
            await fetchJson(`/api/sessions/${id}/approve-change`, {
              method: 'POST', headers: { 'content-type': 'application/json' },
              body: JSON.stringify({ approve, reason: reason || null })
            });
          } catch (error) {
            showSessionNotice(error.message, true);
          } finally {
            closeDiffModal();
          }
        }

        $('diffModalClose').onclick = closeDiffModal;
        $('diffApproveBtn').onclick = () => approvePendingChange(true);
        $('diffRejectBtn').onclick = () => {
          const reason = window.prompt('Varför avvisas ändringen? (valfritt)');
          approvePendingChange(false, reason || undefined);
        };
        $('diffModal').addEventListener('click', e => { if (e.target === $('diffModal')) closeDiffModal(); });

        // Called from the run-loop when a step of kind "awaiting_approval"
        // arrives - surfaces the change for the operator to approve/reject.
        // For a brand-new file we show the full content (there is no diff);
        // for an edit we show the line diff.
        function handleApprovalStep(detailJson) {
          try {
            const d = JSON.parse(detailJson);
            const isNew = !!d.isNew;
            const body = isNew
              ? (d.newContent || d.diff || '')
              : (d.diff || (d.newContent ? d.newContent : ''));
            openDiffModal(isNew ? 'skapa en ny fil' : 'ändra en fil', d.path || '', body);
          } catch {
            openDiffModal('skriva en fil', '', '');
          }
        }

        // Called from the run-loop when a step of kind "awaiting_info" arrives -
        // the agent paused to ask the operator real questions. We surface them
        // and block on an answer (POST /api/sessions/{id}/answer-info) before
        // the run can continue.
        async function handleInfoStep(detailJson, sessionId) {
          let questions = [];
          let blocking = false;
          try {
            const d = JSON.parse(detailJson);
            questions = Array.isArray(d.questions) ? d.questions : [];
            blocking = !!d.blocking;
          } catch { /* malformed request - show nothing extra */ }

          const box = $('sessionInfoBox');
          if (!box) return;
          box.style.display = 'block';
          const list = $('sessionInfoQuestions');
          list.innerHTML = '';
          questions.forEach((q, i) => {
            const li = document.createElement('li');
            li.textContent = (i + 1) + '. ' + q;
            list.appendChild(li);
          });
          if (blocking) {
            const note = $('sessionInfoNote');
            if (note) note.textContent = 'Agenten kan inte fortsätta utan dina svar.';
          }
          const answerEl = $('sessionInfoAnswer');
          if (answerEl) answerEl.focus();
          const sendBtn = $('sessionInfoSend');
          if (sendBtn) {
            sendBtn.onclick = async () => {
              const answer = (answerEl?.value || '').trim();
              if (!answer) { showSessionNotice('Skriv ett svar först.', true); return; }
              try {
                await fetchJson(`/api/sessions/${sessionId}/answer-info`, {
                  method: 'POST',
                  headers: { 'content-type': 'application/json' },
                  body: JSON.stringify({ answer })
                });
                box.style.display = 'none';
                if (answerEl) answerEl.value = '';
              } catch (e) {
                showSessionNotice('Kunde inte skicka svaret: ' + (e.message || e), true);
              }
            };
          }
        }

        function showSessionNotice(message, isError = false) {
          const box = $('sessionNotice');
          box.textContent = message;
          box.className = `notice show ${isError ? 'bad' : ''}`;
        }

        async function sendSessionMessage() {
          const id = state.activeSessionId;
          const text = $('sessionPrompt').value.trim();
          if (!text || !id) return;
          $('sessionPrompt').value = '';
          $('sessionSendBtn').disabled = true;
          $('sessionCancelBtn').style.display = 'inline-flex';
          $('sessionRunningIndicator').textContent = 'Kör...';
          $('sessionNotice').className = 'notice';

          const liveAssistant = { role: 'assistant', content: '', state: 'Running', steps: [] };
          state.sessionLiveMessages = [{ role: 'user', content: text }, liveAssistant];
          renderSessionMessages();

          // Strukturerade steg (Claude Code-stil) i stället for en radlogg:
          // resonemang blir lopande text, verktygsanrop kompakta rader.
          const addStep = step => {
            liveAssistant.steps.push(step);
            renderSessionMessages();
          };

          try {
            const headers = { 'content-type': 'application/json' };
            if (state.authToken) headers[AUTH_HEADER] = state.authToken;
            const response = await fetch(`/api/sessions/${id}/run`, {
              method: 'POST',
              headers,
              body: JSON.stringify({ message: text })
            });

            if (!response.ok || !response.body) {
              let detail = `HTTP ${response.status}`;
              try {
                const errBody = await response.json();
                detail = errBody?.detail || errBody?.error || errBody?.title || detail;
              } catch { /* body wasn't JSON - keep the status-code message */ }
              throw new Error(detail);
            }

            const reader = response.body.getReader();
            const decoder = new TextDecoder();
            let buffer = '';
            let success = false;
            for (;;) {
              const { done, value } = await reader.read();
              if (done) break;
              buffer += decoder.decode(value, { stream: true });
              let sepIndex;
              while ((sepIndex = buffer.indexOf('\n\n')) !== -1) {
                const frame = buffer.slice(0, sepIndex);
                buffer = buffer.slice(sepIndex + 2);
                const dataLine = frame.split('\n').find(l => l.startsWith('data:'));
                if (!dataLine) continue; // keepalive-pingar (": ping") saknar data-rad
                let payload;
                try { payload = JSON.parse(dataLine.slice(5).trim()); }
                catch { continue; } // trasig frame får inte fälla hela strömmen
                if (!payload) continue;
                if (payload.step) {
                  if (payload.step.Kind === 'awaiting_approval') handleApprovalStep(payload.step.Detail);
                  if (payload.step.Kind === 'awaiting_info') await handleInfoStep(payload.step.Detail, id);
                  addStep(payload.step);
                } else if (payload.final) {
                  success = !!payload.final.Success;
                  liveAssistant.state = success ? 'Completed' : 'Failed';
                  liveAssistant.content = payload.final.FinalAnswer
                    || (success ? '(inget svar)' : '(misslyckades)');
                  renderSessionMessages();
                }
              }
            }

            if (success) {
              // Only a successful run is persisted server-side (see
              // SessionApi) - refetch rather than trust the client's own
              // optimistic copy, same reasoning as the plan-subtask flow.
              state.activeSession = await fetchJson(`/api/sessions/${id}`);
              state.sessionLiveMessages = [];
              renderSessionView();
              await loadSessions();
            } else {
              showSessionNotice('Körningen misslyckades - se meddelandet ovan.', true);
            }
          } catch (error) {
            liveAssistant.state = 'Failed';
            addStep({ Kind: 'error', Detail: error.message });
            showSessionNotice(error.message, true);
          } finally {
            $('sessionSendBtn').disabled = false;
            $('sessionCancelBtn').style.display = 'none';
            $('sessionRunningIndicator').textContent = '';
          }
        }

        async function cancelSessionRun() {
          if (!state.activeSessionId) return;
          try {
            await fetchJson(`/api/sessions/${state.activeSessionId}/cancel`, { method: 'POST' });
          } catch (error) {
            showSessionNotice(error.message, true);
          }
        }

        async function toggleSessionPin(id) {
          const s = state.sessions.find(x => x.id === id);
          if (!s) return;
          try {
            await fetchJson(`/api/sessions/${id}`, {
              method: 'PUT',
              headers: { 'content-type': 'application/json' },
              body: JSON.stringify({ pinned: !s.pinned })
            });
            await loadSessions();
            if (state.activeSessionId === id) {
              state.activeSession = await fetchJson(`/api/sessions/${id}`);
              renderSessionView();
            }
          } catch (error) {
            showGlobalNotice(error.message, true);
          }
        }

        async function deleteSession(id) {
          const s = state.sessions.find(x => x.id === id);
          if (!window.confirm(`Ta bort sessionen "${s ? s.title : id}"? Historiken går inte att återställa (mappen och dess filer påverkas inte).`))
            return;
          try {
            await fetchJson(`/api/sessions/${id}`, { method: 'DELETE' });
            if (state.activeSessionId === id) {
              state.activeSessionId = null;
              state.activeSession = null;
              switchView('work');
            }
            await loadSessions();
          } catch (error) {
            showGlobalNotice(error.message, true);
          }
        }

        // Native Windows folder picker (see DialogsApi/NativeDialogs) - falls
        // back to the plain text input with no error dialog on non-Windows
        // or an older WebView2 runtime, since typing a path always still
        // works regardless of whether this endpoint is available.
        async function pickFolder(inputId, noticeElId) {
          const input = $(inputId);
          try {
            const result = await fetchJson('/api/dialogs/pick-folder', {
              method: 'POST',
              headers: { 'content-type': 'application/json' },
              body: JSON.stringify({ initialDirectory: input.value.trim() || null })
            });
            if (result?.path) input.value = result.path;
          } catch (error) {
            const box = noticeElId ? $(noticeElId) : null;
            if (box) {
              box.textContent = error.message;
              box.className = 'notice show bad';
            }
          }
        }

        function toggleNewSessionForm(show) {
          state.newSessionFormOpen = show ?? !state.newSessionFormOpen;
          $('newSessionForm').classList.toggle('hidden', !state.newSessionFormOpen);
          $('newSessionNotice').className = 'notice';
          if (state.newSessionFormOpen) $('newSessionFolderPath').focus();
        }

        async function createSession() {
          const folderPath = $('newSessionFolderPath').value.trim();
          const title = $('newSessionTitle').value.trim();
          if (!folderPath) {
            $('newSessionNotice').textContent = 'Ange en mappsökväg.';
            $('newSessionNotice').className = 'notice show bad';
            return;
          }
          $('newSessionCreateBtn').disabled = true;
          try {
            const session = await fetchJson('/api/sessions', {
              method: 'POST',
              headers: { 'content-type': 'application/json' },
              body: JSON.stringify({ folderPath, title: title || null })
            });
            $('newSessionFolderPath').value = '';
            $('newSessionTitle').value = '';
            toggleNewSessionForm(false);
            state.activeSessionId = session.id;
            state.activeSession = session;
            state.sessionLiveMessages = [];
            switchView('session');
            await loadSessions();
            renderSessionView();
          } catch (error) {
            $('newSessionNotice').textContent = error.message;
            $('newSessionNotice').className = 'notice show bad';
          } finally {
            $('newSessionCreateBtn').disabled = false;
          }
        }

        const knownViews = ['work', 'network', 'schedules', 'delegate', 'session', 'office', 'studio', 'projects'];
        function switchView(view) {
          const previous = state.activeView;
          state.activeView = knownViews.includes(view) ? view : 'work';
          $('workView').classList.toggle('hidden', state.activeView !== 'work');
          $('networkView').classList.toggle('hidden', state.activeView !== 'network');
          $('schedulesView').classList.toggle('hidden', state.activeView !== 'schedules');
          $('delegateView').classList.toggle('hidden', state.activeView !== 'delegate');
          $('sessionView').classList.toggle('hidden', state.activeView !== 'session');
          $('officeView').classList.toggle('hidden', state.activeView !== 'office');
          $('studioView').classList.toggle('hidden', state.activeView !== 'studio');
          $('projectsView').classList.toggle('hidden', state.activeView !== 'projects');
          if (state.activeView === 'projects') loadProjects();
          // Mjuk in-tonings-animation pa den vy som just visades - klassen
          // tas bort och laggs tillbaka sa animationen spelas om vid varje
          // byte (inte bara forsta gangen).
          if (previous !== state.activeView) {
            const shown = $(state.activeView + 'View');
            if (shown) {
              shown.classList.remove('view-enter');
              void shown.offsetWidth;
              shown.classList.add('view-enter');
            }
          }
          document.querySelectorAll('[data-view]').forEach(button => {
            button.classList.toggle('active', button.dataset.view === state.activeView);
          });
          if (state.activeView === 'network') {
            renderTopology();
            renderTopologyDetail();
          }
          if (state.activeView === 'schedules') {
            loadSchedules();
          }
          if (state.activeView === 'office') {
            renderOffice();
          }
          if (state.activeView === 'studio') {
            renderStudio();
          }
        }

        function renderTopology() {
          const canvas = $('topologyCanvas');
          const nodeLayer = $('topologyNodes');
          const lineLayer = $('topologyLines');
          if (!canvas || !nodeLayer || !lineLayer) return;

          const graphNodes = (state.topology?.nodes ?? []).map(node => ({ ...node }));
          const graphEdges = state.topology?.edges ?? [];
          if (state.local?.role === 'Overseer') {
            const root = graphNodes.find(node => node.role === 'Overseer');
            if (root) root.name = state.local.name;
          }

          $('topologySummary').textContent = `${graphNodes.length} noder`;
          if (!graphNodes.length) {
            nodeLayer.innerHTML = '<div class="topology-empty">Ingen topologi tillgänglig.</div>';
            lineLayer.innerHTML = '';
            renderTopologyDetail();
            return;
          }

          const width = Math.max(canvas.clientWidth || 0, 320);
          const compact = width < 620;
          const positions = new Map();
          const overseers = graphNodes.filter(node => node.role === 'Overseer');
          const hosts = graphNodes.filter(node => node.role === 'Host');
          const workers = graphNodes.filter(node => node.role === 'Worker');
          let height;

          if (compact) {
            const ordered = [...overseers];
            const assignedWorkers = new Set();
            hosts.forEach(host => {
              ordered.push(host);
              graphEdges
                .filter(edge => edge.source === host.id)
                .forEach(edge => {
                  const worker = workers.find(item => item.id === edge.target);
                  if (worker && !assignedWorkers.has(worker.id)) {
                    ordered.push(worker);
                    assignedWorkers.add(worker.id);
                  }
                });
            });
            workers
              .filter(worker => !assignedWorkers.has(worker.id))
              .forEach(worker => ordered.push(worker));

            ordered.forEach((node, index) =>
              positions.set(node.id, { x: width / 2, y: 72 + index * 112 }));
            height = Math.max(560, 172 + Math.max(0, ordered.length - 1) * 112);
          } else {
            distribute(overseers, 72, Math.max(1, overseers.length), width, positions);
            distribute(hosts, 210, Math.max(1, hosts.length), width, positions);

            const workerColumns = Math.min(5, Math.max(1, workers.length));
            workers.forEach((node, index) => {
              const column = index % workerColumns;
              const row = Math.floor(index / workerColumns);
              const x = width * (column + 1) / (workerColumns + 1);
              positions.set(node.id, { x, y: 370 + row * 112 });
            });

            const workerRows = Math.max(1, Math.ceil(workers.length / workerColumns));
            height = Math.max(560, 430 + workerRows * 112);
          }

          canvas.style.height = `${height}px`;
          lineLayer.setAttribute('viewBox', `0 0 ${width} ${height}`);
          lineLayer.innerHTML = graphEdges.map(edge => {
            const source = positions.get(edge.source);
            const target = positions.get(edge.target);
            return source && target
              ? `<line x1="${source.x}" y1="${source.y}" x2="${target.x}" y2="${target.y}"></line>`
              : '';
          }).join('');

          nodeLayer.innerHTML = graphNodes.map(node => {
            const position = positions.get(node.id) ?? { x: width / 2, y: height / 2 };
            const role = (node.role ?? 'Worker').toLowerCase();
            const offline = (node.status ?? '').toLowerCase() === 'offline';
            return `<button class="topology-node ${esc(role)} ${offline ? 'offline' : ''} ${state.selectedTopologyId === node.id ? 'selected' : ''}"
                style="left:${position.x}px;top:${position.y}px"
                data-topology-node="${esc(node.id)}">
              <span class="topology-node-name">${esc(node.name)}</span>
              <span class="topology-role">${esc(node.role)} | ${esc(node.status)}</span>
              ${node.skills?.length ? `<span class="small">${esc(trunc(node.skills.join(', '), 38))}</span>` : ''}
            </button>`;
          }).join('');

          document.querySelectorAll('[data-topology-node]').forEach(button => {
            button.onclick = () => selectTopologyNode(button.dataset.topologyNode);
          });
        }

        function distribute(nodes, y, columns, width, positions) {
          nodes.forEach((node, index) => {
            const x = width * (index + 1) / (columns + 1);
            positions.set(node.id, { x, y });
          });
        }

        function selectTopologyNode(id) {
          state.selectedTopologyId = id;
          const worker = state.nodes.find(node => node.id === id);
          if (worker) {
            state.selectedNodeId = id;
            state.workerTasks = [];
            loadWorkerTasks(id);
          } else {
            state.selectedNodeId = null;
            state.workerTasks = [];
          }
          renderTopology();
          renderTopologyDetail();
        }

        function renderTopologyDetail() {
          const graphNode = (state.topology?.nodes ?? [])
            .find(node => node.id === state.selectedTopologyId);
          if (!graphNode) {
            $('topologyDetailTitle').textContent = 'Nodinformation';
            $('topologyDetailSub').textContent = 'Välj en nod i nätverket';
            $('topologyDetail').innerHTML = '<div class="empty">Ingen nod vald.</div>';
            return;
          }

          $('topologyDetailTitle').textContent = graphNode.name;
          $('topologyDetailSub').textContent = `${graphNode.role} | ${graphNode.status}`;
          const worker = state.nodes.find(node => node.id === graphNode.id);
          if (!worker) {
            // A Host that's been reconfigured or torn down during testing
            // stays listed forever otherwise (no automatic expiry, since a
            // merely-offline Host should keep showing up once it's back) -
            // give the operator a way to forget it.
            const canForget = graphNode.role === 'Host';
            $('topologyDetail').innerHTML = `
              <section class="detail-section">
                <div class="kv">
                  <div class="kv-row"><span>Roll</span><span>${esc(graphNode.role)}</span></div>
                  <div class="kv-row"><span>Status</span><span>${esc(graphNode.status)}</span></div>
                  <div class="kv-row"><span>Endpoint</span><span class="mono">${esc(graphNode.endpoint || '-')}</span></div>
                </div>
                ${canForget ? `<div class="detail-actions"><button id="topologyForgetHostBtn">Glöm den här Host</button></div>` : ''}
              </section>`;
            if (canForget) {
              $('topologyForgetHostBtn').onclick = () => forgetHost(graphNode.id, graphNode.name);
            }
            return;
          }

          const hardware = worker.hardware;
          // Rebuilding innerHTML resets this element's own scrollTop to 0,
          // same as it used to wipe an expanded <details>' open state - the
          // fix for that (data-hist-id + wireHistoryToggles) didn't cover
          // scroll position, so scrolling down to read older entries here
          // got silently reset to the top on the next ~3s refresh.
          const topologyHistoryScroll = $('topologyHistory')?.scrollTop ?? 0;
          $('topologyDetail').innerHTML = `
            <section class="detail-section">
              <div class="kv">
                <div class="kv-row"><span>Status</span><span>${esc(statusText(worker.status))}</span></div>
                <div class="kv-row"><span>Kapacitet</span><span>${worker.activeTasks ?? 0}/${worker.maxConcurrentTasks ?? 1}</span></div>
                <div class="kv-row"><span>Specialiteter</span><span>${esc((worker.skills || ['general']).join(', '))}</span></div>
                <div class="kv-row"><span>GPU</span><span>${esc(hardware?.gpu || 'Ingen')}</span></div>
                <div class="kv-row"><span>RAM</span><span>${hardware?.systemMemoryGb ?? '-'} GB</span></div>
                <div class="kv-row"><span>Endpoint</span><span class="mono">${esc(worker.endpoint)}</span></div>
              </div>
              <div class="detail-actions">
                <button id="topologyConfigureBtn">Konfigurera</button>
                <button id="topologyRemoveBtn">Ta bort från gruppen</button>
              </div>
            </section>
            <section class="detail-section">
              <div class="panel-title" style="margin-bottom:8px">Historik</div>
              <div class="history-list" id="topologyHistory">${workerHistoryHtml()}</div>
            </section>`;
          $('topologyHistory').scrollTop = topologyHistoryScroll;

          $('topologyConfigureBtn').onclick = () => openSettings(worker.id);
          $('topologyRemoveBtn').onclick = () => removeNodeFromCluster(worker.id);
          wireHistoryToggles();
        }

        function renderInspector() {
          const node = state.nodes.find(n => n.id === state.selectedNodeId);
          if (!node) {
            $('workerDetail').innerHTML = '';
            $('workerDetail').style.display = 'none';
            $('localDetail').style.display = 'block';
            $('inspectorTitle').textContent = 'Den här noden';
            $('inspectorSub').textContent = state.local ? `${state.local.role} | ${state.local.name}` : 'Lokal konfiguration';
            $('configureNode').textContent = 'Ändra';
            return;
          }

          $('localDetail').style.display = 'none';
          $('workerDetail').style.display = 'block';
          $('inspectorTitle').textContent = node.name;
          $('inspectorSub').textContent = `${statusText(node.status)} | ${node.endpoint}`;
          $('configureNode').textContent = 'Konfigurera';

          const h = node.hardware;
          const providerPills = (node.providerPriority ?? []).map(id =>
            `<span class="pill">${esc(providerLabels[id] ?? id)}</span>`).join('');
          // See the matching comment in renderTopologyDetail() - rebuilding
          // innerHTML resets this element's own scrollTop to 0 on every
          // ~3s refresh, silently undoing a scroll down to read older entries.
          const workerHistoryScroll = $('workerHistory')?.scrollTop ?? 0;
          $('workerDetail').innerHTML = `
            <section class="detail-section">
              <div class="panel-title">Status</div>
              <div class="kv">
                <div class="kv-row"><span>Tillstånd</span><span class="node-status"><span class="dot ${statusClass(node.status)}"></span>${esc(statusText(node.status))}</span></div>
                <div class="kv-row"><span>Aktiva jobb</span><span>${node.activeTasks ?? 0}</span></div>
                <div class="kv-row"><span>Senast sedd</span><span>${esc(ago(node.lastSeen))}</span></div>
                <div class="kv-row"><span>Endpoint</span><span class="mono">${esc(node.endpoint)}</span></div>
              </div>
            </section>
            <section class="detail-section">
              <div class="panel-title">Hårdvara</div>
              <div class="kv">
                <div class="kv-row"><span>CPU</span><span>${esc(trunc(h?.cpu || 'Okänd', 34))}</span></div>
                <div class="kv-row"><span>Kärnor</span><span>${h?.logicalCores ?? '-'}</span></div>
                <div class="kv-row"><span>RAM</span><span>${h ? `${h.systemMemoryGb} GB` : '-'}</span></div>
                <div class="kv-row"><span>GPU</span><span>${esc(trunc(h?.gpu || 'Ingen', 34))}</span></div>
                <div class="kv-row"><span>VRAM</span><span>${h?.gpuMemoryGb ? `${h.gpuMemoryGb} GB` : '-'}</span></div>
              </div>
            </section>
            <section class="detail-section">
              <div class="panel-title">AI-konfiguration</div>
              <div class="chain">${providerPills || '<span class="small">Ingen provider</span>'}</div>
              <div class="kv" style="margin-top:8px">
                <div class="kv-row"><span>Specialiteter</span><span>${esc((node.skills || ['general']).join(', '))}</span></div>
                <div class="kv-row"><span>Kapacitet</span><span>${node.activeTasks ?? 0}/${node.maxConcurrentTasks ?? 1} jobb</span></div>
                <div class="kv-row"><span>Lokal modell</span><span>${esc(node.localModel || '-')}</span></div>
                <div class="kv-row"><span>Rekommenderad</span><span>${esc(node.recommendedModel || '-')}</span></div>
                <div class="kv-row"><span>Version</span><span>${esc(node.version || '-')}</span></div>
              </div>
              <div class="notice" id="nodeActionStatus"></div>
              <div class="detail-actions">
                <button class="primary" id="setupAiBtn" ${state.nodeBusyAction ? 'disabled' : ''}>Installera lokal AI</button>
                <button id="inspectRuntimeBtn">Kontrollera Ollama</button>
                <button id="pullModelBtn" ${state.nodeBusyAction ? 'disabled' : ''}>Hämta lokal modell</button>
                <button id="removeNodeBtn">Ta bort från gruppen</button>
              </div>
            </section>
            <section class="detail-section">
              <div class="panel-title" style="margin-bottom:8px">Historik</div>
              <div class="history-list" id="workerHistory">${workerHistoryHtml()}</div>
            </section>`;
          $('workerHistory').scrollTop = workerHistoryScroll;

          $('setupAiBtn').onclick = setupWorkerAi;
          $('inspectRuntimeBtn').onclick = inspectWorkerRuntime;
          $('pullModelBtn').onclick = pullWorkerModel;
          $('removeNodeBtn').onclick = () => removeNodeFromCluster(node.id);
          if (state.nodeAction?.id === node.id) {
            const box = $('nodeActionStatus');
            box.textContent = state.nodeAction.message;
            box.className = `notice show ${state.nodeAction.isError ? 'bad' : ''}`;
          }
          wireHistoryToggles();
        }

        function showNodeAction(message, isError = false) {
          state.nodeAction = { id: state.selectedNodeId, message, isError };
          const box = $('nodeActionStatus');
          if (!box) return;
          box.textContent = message;
          box.className = `notice show ${isError ? 'bad' : ''}`;
        }

        async function inspectWorkerRuntime() {
          if (!state.selectedNodeId) return;
          showNodeAction('Kontrollerar Ollama...');
          try {
            const runtime = await fetchJson(`/api/nodes/${state.selectedNodeId}/runtime`);
            const stateText = runtime.ollamaEndpointReachable ? 'Ollama är online' : 'Ollama svarar inte';
            const modelText = runtime.recommendedModelInstalled
              ? `${runtime.recommendedModel} är installerad`
              : `${runtime.recommendedModel} saknas`;
            showNodeAction(`${stateText}. ${modelText}.`, !runtime.ollamaEndpointReachable);
          } catch (error) {
            showNodeAction(error.message, true);
          }
        }

        async function pullWorkerModel() {
          if (!state.selectedNodeId) return;
          state.nodeBusyAction = 'pull';
          showNodeAction('Hämtar modellen. Det kan ta en stund...');
          renderInspector();
          try {
            const result = await fetchJson(`/api/nodes/${state.selectedNodeId}/runtime/pull`, { method: 'POST' });
            showNodeAction(result.success ? `${result.model} är installerad.` : result.output, !result.success);
          } catch (error) {
            showNodeAction(error.message, true);
          } finally {
            state.nodeBusyAction = null;
            renderInspector();
          }
        }

        async function removeNodeFromCluster(id) {
          const node = state.nodes.find(item => item.id === id);
          if (!node) return;
          if (!window.confirm(`Ta bort ${node.name} från gruppen? Workern kan inte registrera sig igen förrän den återställs.`))
            return;
          try {
            await fetchJson(`/api/nodes/${id}`, { method: 'DELETE' });
            state.selectedNodeId = null;
            state.selectedTopologyId = null;
            state.workerTasks = [];
            await refresh();
          } catch (error) {
            showNodeAction(error.message, true);
          }
        }

        async function forgetHost(id, name) {
          if (!window.confirm(`Glöm ${name}? Den dyker upp igen om den hörs av på nätverket.`))
            return;
          try {
            await fetchJson(`/api/hosts/${id}`, { method: 'DELETE' });
            state.selectedTopologyId = null;
            await refresh();
          } catch (error) {
            // Not topologyDetailSub - that's plain unstyled text and the next
            // 3s refresh() poll rebuilds it back to normal status, silently
            // erasing the error within seconds either way.
            showGlobalNotice(error.message, true);
          }
        }

        function workerHistoryHtml() {
          const items = state.workerTasks;
          if (!items || !items.length) return '<div class="empty">Inga jobb ännu.</div>';
          return items.map(t => {
            const status = typeof t.state === 'number' ? stateName[t.state] : t.state;
            const meta = [
              t.provider ? `${t.provider}/${t.model ?? ''}` : '',
              t.requiredSkill ? t.requiredSkill : '',
              t.complexity ? 'nivå ' + t.complexity : '',
              ago(t.completedAt || t.createdAt)
            ].filter(Boolean).join(' | ');
            const body = t.result || t.error || '(inget resultat)';
            const assignment = t.assignmentReason
              ? `<div class="small" style="margin-top:6px">${esc(t.assignmentReason)}</div>`
              : '';
            // Mini-terminal med exakt vad workern FICK - "Goal" som rubrik
            // sa ingenting om vilket meddelande som faktiskt skickades.
            const promptBlock = t.prompt
              ? `<div class="mini-term">&rsaquo; ${esc(trunc(t.prompt, 600))}</div>`
              : '';
            const summaryTitle = t.prompt ? trunc(t.prompt, 64) : (t.title || '(okänt)');
            // The 3s refresh cycle rebuilds this whole list from scratch, which
            // would otherwise silently re-collapse an entry the operator just
            // opened to read - re-apply the "open" attribute from tracked state.
            const isOpen = state.openHistoryIds.has(t.id);
            return `<details class="hist" data-hist-id="${esc(t.id)}" ${isOpen ? 'open' : ''}>
              <summary><span class="hist-title">${esc(summaryTitle)}</span><span class="pill">${esc(status)}</span></summary>
              <div class="hist-body"><div class="small">${esc(meta)}</div>${promptBlock}${assignment}<div>${esc(trunc(body, 1200))}</div></div>
            </details>`;
          }).join('');
        }

        function wireHistoryToggles() {
          document.querySelectorAll('[data-hist-id]').forEach(details => {
            details.addEventListener('toggle', () => {
              const id = details.dataset.histId;
              if (details.open) state.openHistoryIds.add(id);
              else state.openHistoryIds.delete(id);
            });
          });
        }

        async function loadWorkerTasks(id) {
          try {
            state.workerTasks = await fetchJson(`/api/nodes/${id}/tasks`) ?? [];
          } catch {
            state.workerTasks = [];
          }
          if (state.selectedNodeId === id) {
            renderInspector();
            renderTopologyDetail();
          }
        }

        async function setupWorkerAi() {
          if (!state.selectedNodeId) return;
          state.nodeBusyAction = 'setup';
          showNodeAction('Installerar Ollama och hämtar modellen. Det kan ta flera minuter...');
          renderInspector();
          try {
            const result = await fetchJson(`/api/nodes/${state.selectedNodeId}/runtime/setup`, { method: 'POST' });
            const lines = (result.steps || []).map(s => `${s.ok ? 'OK' : 'FEL'}  ${s.step}: ${s.detail}`).join('\n');
            showNodeAction((result.success ? 'Klart.' : 'Delvis klart.') + '\n' + lines, !result.success);
          } catch (error) {
            showNodeAction(error.message, true);
          } finally {
            state.nodeBusyAction = null;
            renderInspector();
          }
        }

        function renderTasks() {
          $('taskCount').textContent = state.tasks.length;
          const tasksHtml = state.tasks.length ? state.tasks.slice(0, 8).map(t => {
            const status = typeof t.state === 'number' ? stateName[t.state] : t.state;
            const roleBadge = t.roleId ? `<span class="pill role">${esc(roleName(t.roleId))}</span>` : '';
            const delegation = [
              t.workerName ? '-> ' + t.workerName : '',
              t.workerTier ? t.workerTier : '',
              t.complexity ? 'niva ' + t.complexity : ''
            ].filter(Boolean).join(' | ');
            const cost = fmtUsd(t.estimatedCostUsd);
            const cancellable = cancellableStates.includes(status);
            const notes = (!t.parentId && t.notes) ? `<pre class="iso-diff" style="max-height:160px;overflow:auto;margin-top:4px">${esc(t.notes)}</pre>` : '';
            return `<div class="node">
              <div class="node-main"><span class="mono">${esc(t.id)}</span><span class="pill">${esc(status)}</span>${roleBadge}</div>
              <div class="small">${esc(trunc(t.title || t.prompt, 64))}</div>
              <div class="small">${esc(delegation)}</div>
              <div class="small">${esc(t.provider ? `${t.provider}/${t.model ?? ''}` : '')}${cost ? ' | ' + esc(cost) : ''}</div>
              ${notes}
              ${cancellable ? `<div class="detail-actions"><button class="icon" title="Avbryt" data-cancel-task="${esc(t.id)}">${icon('x', 14)}</button></div>` : ''}
            </div>`;
          }).join('') : '<div class="empty">Inga jobb ännu.</div>';

          // Diff guard: only rewrite when the task list actually changed, so the
          // 3s refresh doesn't rebuild the DOM (flicker) or drop in-flight hover.
          const tasksEl = $('tasks');
          if (tasksEl._html !== tasksHtml) {
            tasksEl.innerHTML = tasksHtml;
            tasksEl._html = tasksHtml;
            document.querySelectorAll('[data-cancel-task]').forEach(button => {
              button.onclick = () => cancelTask(button.dataset.cancelTask);
            });
          }
        }

        async function renderRoles() {
          try {
            const roles = await fetchJson('/api/roles');
            for (const r of roles) roleNames[r.id] = r.name;
            const el = $('rolesList');
            if (!el) return;
            el.innerHTML = roles.map(r => `<div class="node">
              <div class="node-main"><span class="mono">${esc(r.name)}</span><span class="pill">${esc(r.requiredSkill)}</span>${r.complexityBias ? `<span class="pill">+${r.complexityBias} modell</span>` : ''}</div>
              <div class="small">${esc(trunc(r.systemPrompt, 90))}</div>
            </div>`).join('');
          } catch { /* roles panel is best-effort */ }
        }

        const noticeLabels = { TaskDone: 'Klart', TaskFailed: 'Misslyckades', NeedsYou: 'Behöver dig', WorkerDown: 'Worker nere' };
        let lastNoticeAt = null;
        async function renderNotices() {
          try {
            const notices = await fetchJson('/api/notices');
            const el = $('noticesList');
            if (!el) return;
            if (!notices.length) { el.innerHTML = '<div class="small" style="opacity:.6">Inga notiser.</div>'; return; }
            // Beep once when a brand-new notice arrives (and sound is enabled).
            const newest = notices.length ? notices[0].at : null;
            if (newest && lastNoticeAt && newest > lastNoticeAt && $('noticeSound').checked) playNoticeSound();
            if (newest) lastNoticeAt = newest;
            el.innerHTML = notices.slice(0, 30).map(n => {
              const label = noticeLabels[n.type] || n.type;
              const when = n.at ? new Date(n.at).toLocaleString() : '';
              return `<div class="node">
                <div class="node-main"><span class="pill ${n.type === 'WorkerDown' || n.type === 'TaskFailed' ? 'bad' : 'good'}">${esc(label)}</span>${n.refId ? `<span class="mono small">${esc(n.refId)}</span>` : ''}</div>
                <div class="small">${esc(n.message)}</div>
                <div class="small" style="opacity:.5">${esc(when)}</div>
              </div>`;
            }).join('');
          } catch { /* notices panel is best-effort */ }
        }

        function playNoticeSound() {
          try {
            const ctx = new (window.AudioContext || window.webkitAudioContext)();
            const osc = ctx.createOscillator();
            const gain = ctx.createGain();
            osc.connect(gain); gain.connect(ctx.destination);
            osc.frequency.value = 660; osc.type = 'sine';
            gain.gain.setValueAtTime(0.001, ctx.currentTime);
            gain.gain.exponentialRampToValueAtTime(0.15, ctx.currentTime + 0.02);
            gain.gain.exponentialRampToValueAtTime(0.001, ctx.currentTime + 0.25);
            osc.start(); osc.stop(ctx.currentTime + 0.26);
          } catch { /* audio is best-effort */ }
        }

        async function renderBacklog() {
          try {
            const items = await fetchJson('/api/backlog');
            const el = $('backlogList');
            if (!el) return;
            $('backlogCount').textContent = items.length ? '(' + items.length + ')' : '';
            if (!items.length) { el.innerHTML = '<div class="small" style="opacity:.6">Inga köade mål.</div>'; return; }
            el.innerHTML = items.map(it => `<div class="node">
              <div class="node-main"><span class="mono">${esc(trunc(it.prompt, 70))}</span></div>
              <div class="small" style="opacity:.6">köad ${new Date(it.submittedAt).toLocaleTimeString()}</div>
              <div style="display:flex;gap:6px;margin-top:4px">
                <button class="btn ghost sm" data-start="${esc(it.id)}">Starta nu</button>
                <button class="btn ghost sm" data-remove="${esc(it.id)}">Ta bort</button>
              </div>
            </div>`).join('');
            el.querySelectorAll('[data-start]').forEach(b => b.onclick = async () => {
              await fetch('/api/backlog/' + b.dataset.start + '/start', { method: 'POST', headers: authHeaders() });
              await renderBacklog();
            });
            el.querySelectorAll('[data-remove]').forEach(b => b.onclick = async () => {
              await fetch('/api/backlog/' + b.dataset.remove, { method: 'DELETE', headers: authHeaders() });
              await renderBacklog();
            });
          } catch { /* backlog panel is best-effort */ }
        }

        async function renderOffice() {
          try {
            const data = await fetchJson('/api/office');
            const wEl = $('officeWorkers');
            if (!wEl) return;
            if (!data.workers.length) {
              wEl.innerHTML = '<div class="small" style="opacity:.6">Inga workers anslutna.</div>';
            } else {
              wEl.innerHTML = data.workers.map(w => {
                const statusClass = w.status === 'Offline' ? 'bad' : (w.status === 'Busy' ? 'good' : '');
                const cur = w.current;
                const curHtml = cur
                  ? `<div class="small" style="margin-top:4px">${esc(trunc(cur.title, 70))}</div>
                     <div class="small" style="opacity:.6">${esc(roleName(cur.role) || cur.role || 'agent')} | ${esc(stateNameFromStr(cur.state))}${cur.complexity ? ' | niva ' + cur.complexity : ''}</div>`
                  : `<div class="small" style="opacity:.6;margin-top:4px">${w.status === 'Offline' ? 'frånkopplad' : 'ledig'}</div>`;
                const acc = w.agentAccess || 'Off';
                const accPill = acc === 'Full' ? 'good' : (acc === 'Off' ? 'bad' : '');
                const wsShort = w.workspacePath ? esc(trunc(w.workspacePath, 40)) : '<span style="opacity:.5">standard-mapp</span>';
                const opt = (v, label) => `<option value="${v}"${acc === v ? ' selected' : ''}>${label}</option>`;
                return `<div class="office-card ${w.status === 'Offline' ? 'offline' : ''}">
                  <div class="node-main"><span class="mono">${esc(w.name)}</span><span class="pill ${statusClass}">${esc(w.status)}</span><span class="pill ${accPill}">${esc(acc)}</span>${w.activeTasks ? `<span class="small">${w.activeTasks} aktiva</span>` : ''}</div>
                  ${curHtml}
                  <div class="small" style="opacity:.6;margin-top:4px">${icon('folder')} ${wsShort}</div>
                  <button class="btn-mini" data-wcfg="${esc(w.id)}" style="margin-top:6px">${icon('cog')} Inställningar</button>
                  <div class="worker-cfg" id="wcfg-${esc(w.id)}" style="display:none;margin-top:8px;padding:8px;border:1px solid var(--border);border-radius:8px">
                    <label class="small" style="display:block;margin-bottom:2px">Arbetsmapp på workerns dator</label>
                    <input class="inp" id="wcfg-ws-${esc(w.id)}" placeholder="t.ex. C:\\Users\\namn\\Desktop\\TEST" value="${w.workspacePath ? esc(w.workspacePath) : ''}" style="width:100%;margin-bottom:6px">
                    <label class="small" style="display:block;margin-bottom:2px">Åtkomstnivå</label>
                    <select class="inp" id="wcfg-acc-${esc(w.id)}" style="width:100%;margin-bottom:6px">
                      ${opt('Off', 'Av (ingen agent)')}${opt('Sandboxed', 'Begränsad (endast arbetsmappen)')}${opt('Full', 'Full åtkomst (hela datorn)')}
                    </select>
                    <label class="small" style="display:flex;align-items:center;gap:6px;margin-bottom:8px">
                      <input type="checkbox" id="wcfg-net-${esc(w.id)}"> Tillåt internet (krävs för auto-nedladdning av verktyg)
                    </label>
                    <button class="btn" data-wsave="${esc(w.id)}">Spara till workern</button>
                    <span class="small" id="wcfg-msg-${esc(w.id)}" style="margin-left:8px"></span>
                  </div>
                </div>`;
              }).join('');
              wEl.querySelectorAll('[data-wcfg]').forEach(b => b.onclick = () => {
                const p = $('wcfg-' + b.dataset.wcfg);
                if (p) p.style.display = p.style.display === 'none' ? 'block' : 'none';
              });
              wEl.querySelectorAll('[data-wsave]').forEach(b => b.onclick = () => saveWorkerSettings(b.dataset.wsave));
            }
            const gEl = $('officeGoals');
            if (gEl) gEl.innerHTML = (data.goals.length ? data.goals : []).map(g => `<div class="node">
              <div class="node-main"><span class="mono">${esc(trunc(g.title, 60))}</span><span class="pill">${esc(g.state)}</span>${g.role ? `<span class="pill">${esc(roleName(g.role) || g.role)}</span>` : ''}</div>
              <div class="small" style="opacity:.6">${g.children} deluppgifter</div>
            </div>`).join('') || '<div class="small" style="opacity:.6">Inga mål.</div>';
          } catch { /* office view is best-effort */ }
        }

        // B1: push per-worker workspace + access-level to a specific Worker via
        // the Host proxy (PUT /api/nodes/{id}/settings -> that worker's own
        // /api/settings). The Overseer decides where each Worker saves/works
        // and how much of its machine the agent may touch.
        async function saveWorkerSettings(id) {
          const msg = $('wcfg-msg-' + id);
          const ws = ($('wcfg-ws-' + id)?.value || '').trim();
          const acc = $('wcfg-acc-' + id)?.value || 'Off';
          const net = !!$('wcfg-net-' + id)?.checked;
          const body = { agentAccess: acc, allowInternet: net };
          if (ws) body.workspacePath = ws;
          if (msg) { msg.textContent = 'Sparar...'; msg.style.color = ''; }
          try {
            await fetchJson('/api/nodes/' + encodeURIComponent(id) + '/settings', {
              method: 'PUT',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify(body)
            });
            if (msg) { msg.textContent = '✓ sparat'; msg.style.color = 'var(--good)'; }
            setTimeout(renderOffice, 1200);
          } catch (e) {
            if (msg) { msg.textContent = 'Fel: ' + e.message; msg.style.color = 'var(--bad)'; }
          }
        }

        const stateNameFromStr = s => s; // state already a readable string from /api/office

        async function cancelTask(id) {
          if (!window.confirm('Avbryt den här uppgiften?')) return;
          try {
            await fetchJson(`/api/tasks/${id}/cancel`, { method: 'POST' });
            await refresh();
          } catch (error) {
            showGlobalNotice(error.message, true);
          }
        }

        // --- Git task-isolation panel -------------------------------------------
        // Lists the Worker's currently-isolated agent tasks (one worktree+branch
        // each). Merge folds the branch into its base; Discard throws it away
        // (the free undo button); "Visa diff" opens the change inline.
        async function refreshIsolation() {
          const el = $('isolationList');
          let tasks;
          try {
            tasks = await fetchJson('/execute/isolation/list');
          } catch {
            el.innerHTML = '<div class="small" style="opacity:.6">Kunde inte läsa isolerade uppgifter (agentläge/worktree saknas).</div>';
            return;
          }
          if (!tasks || !tasks.length) {
            el.innerHTML = '<div class="small" style="opacity:.6">Inga aktiva isolerade uppgifter.</div>';
            return;
          }
          el.innerHTML = tasks.map(t => `<div class="node">
            <div class="node-main"><span class="mono">${esc(t.branch)}</span><span class="pill">${esc(t.baseBranch)}</span></div>
            <div class="small">${esc(trunc(t.title || t.taskId, 64))}</div>
            <div class="detail-actions">
              <button class="btn ghost sm" data-iso-merge="${esc(t.taskId)}">Merge</button>
              <button class="btn ghost sm" data-iso-discard="${esc(t.taskId)}">Kasta</button>
              <button class="btn ghost sm" data-iso-diff="${esc(t.taskId)}">Visa diff</button>
            </div>
            <pre class="iso-diff" id="iso-diff-${esc(t.taskId)}" style="display:none;max-height:240px;overflow:auto"></pre>
          </div>`).join('');
          el.querySelectorAll('[data-iso-merge]').forEach(b => b.onclick = () => isoMerge(b.dataset.isoMerge));
          el.querySelectorAll('[data-iso-discard]').forEach(b => b.onclick = () => isoDiscard(b.dataset.isoDiscard));
          el.querySelectorAll('[data-iso-diff]').forEach(b => b.onclick = () => isoDiff(b.dataset.isoDiff));
        }

        async function isoMerge(taskId) {
          if (!window.confirm('Merga den här uppgifts-branchen in i sin bas?')) return;
          try {
            const r = await fetchJson('/execute/isolation/merge', { method: 'POST', body: JSON.stringify({ taskId }) });
            showGlobalNotice(r.success ? 'Mergad: ' + (r.output || 'OK') : 'Merge misslyckades: ' + (r.output || ''), !r.success);
            await refreshIsolation();
          } catch (e) { showGlobalNotice(e.message, true); }
        }

        async function isoDiscard(taskId) {
          if (!window.confirm('Kasta den här uppgifts-branchen? Ändringarna försvinner (ångra).')) return;
          try {
            await fetchJson('/execute/isolation/discard', { method: 'POST', body: JSON.stringify({ taskId }) });
            showGlobalNotice('Uppgifts-branch kastad.');
            await refreshIsolation();
          } catch (e) { showGlobalNotice(e.message, true); }
        }

        async function isoDiff(taskId) {
          const pre = $('iso-diff-' + taskId);
          if (!pre) return;
          if (pre.style.display !== 'none') { pre.style.display = 'none'; return; }
          try {
            const r = await fetchJson('/execute/isolation/diff', { method: 'POST', body: JSON.stringify({ taskId }) });
            pre.textContent = r.diff || '(ingen diff)';
            pre.style.display = 'block';
          } catch (e) { showGlobalNotice(e.message, true); }
        }

        function renderMessages() {
          const box = $('messages');
          // Assignments (agent mode) bypass the Host's task/chat board
          // entirely (see /api/assignment) - they're not something the
          // server knows about or persists, so they're tracked client-side
          // only and merged in here purely for display. They don't survive
          // a page reload; that's a known, intentional limit of this first
          // pass, not an oversight.
          const allMessages = [...state.messages, ...state.assignmentMessages];
          syncComposerLock();
          if (!allMessages.length) {
            box._html = null; // hero-läget får aldrig fastna bakom diff-guarden
            box.innerHTML = `<div class="empty empty-hero">
              <div class="hero-mark">AI</div>
              <div class="hero-title">Vad vill du få gjort?</div>
              <div class="small">Beskriv ett mål så bryter Hosten ner det och delegerar till klustrets Workers.</div>
            </div>`;
            return;
          }
          // The refresh loop calls this every ~3s (more often while a reply
          // is streaming), and used to force-scroll to the bottom every
          // single time, unconditionally - scrolling up to reread an earlier
          // message got yanked back down within seconds. Only follow new
          // messages automatically if the operator was already at (or near)
          // the bottom before this render; otherwise leave their scroll
          // position alone. Restore the exact previous position explicitly
          // rather than relying on whatever the browser does by default after
          // an innerHTML replacement (not guaranteed to leave scrollTop where
          // it was - same class of state loss the history list had, just for
          // scroll position instead of an expanded <details>' open attribute).
          const wasNearBottom = box.scrollHeight - box.scrollTop - box.clientHeight < 80;
          const previousScrollTop = box.scrollTop;
          const messagesHtml = allMessages.map(m => {
            if (m.isPlan) return renderPlanBubble(m);
            if (m.isAssignment) return assignmentBubbleHtml(m);
            const inFlight = m.role === 'assistant' && m.taskId && cancellableStates.includes(m.state);
            const live = inFlight && state.streamBuffer && state.streamBuffer.taskId === m.taskId;
            const content = live ? state.streamBuffer.text : m.content;
            // Väntar svaret fortfarande (inga tokens ännu): visa ett skimrande
            // statusord istället för en tom rad, så det syns att den arbetar.
            const waitingWord = { Pending: 'Köar...', Queued: 'Köar...', Dispatched: 'Skickar till worker...', Running: 'Tänker...' }[m.state] ?? 'Arbetar...';
            const showShimmer = inFlight && !(content && content.trim());
            return `
            <article class="message ${m.role === 'user' ? 'user' : 'assistant'}">
              <div class="message-meta">
                <strong>${m.role === 'user' ? 'Du' : 'AiLocal'}</strong>
                ${m.isAssignment ? '<span class="pill">Assignment</span>' : ''}
                ${m.subtaskTitle ? `<span class="pill">${esc(m.subtaskTitle)}</span>` : ''}
                ${m.workerName ? `<span class="small">${esc(m.workerName)}</span>` : ''}
                ${m.state ? `<span>${esc(m.state)}</span>` : ''}
                ${m.provider ? `<span>${esc(m.provider)}/${esc(m.model ?? '')}</span>` : ''}
                ${inFlight ? `<button class="icon" title="Avbryt" data-cancel-task="${esc(m.taskId)}">${icon('x', 14)}</button>` : ''}
              </div>
              ${showShimmer
                ? `<div class="msg-text"><span class="thinking-shimmer">${esc(waitingWord)}</span></div>`
                : `<div class="msg-text">${esc(content)}</div>`}
              ${m.role !== 'user' && !inFlight && content ? msgActionsHtml : ''}
            </article>`;
          }).join('');
          // Diff-guard: 3s-pollen renderade om identiskt innehåll och rörde
          // då scrollpositionen ("vyn hoppar högst upp hela tiden") - vid
          // oförändrad HTML lämnas DOM och scroll helt orörda.
          if (box._html === messagesHtml) return;
          box._html = messagesHtml;
          box.innerHTML = messagesHtml;
          box.scrollTop = wasNearBottom ? box.scrollHeight : previousScrollTop;

          document.querySelectorAll('#messages [data-cancel-task]').forEach(button => {
            button.onclick = () => cancelTask(button.dataset.cancelTask);
          });
          wirePlanBubbles();

          manageStreaming();
          renderChatOutline('messages', 'delegateOutline');
          wireMessageActions('messages');
        }

        const planStateLabels = {
          planning: 'Planerar...',
          reviewing: 'Granska planen',
          running: 'Kör',
          done: 'Klart',
          stopped: 'Stoppad (ett steg misslyckades)',
          cancelled: 'Avbruten',
          failed: 'Planering misslyckades'
        };
        const subtaskStatusLabels = { pending: '', running: 'Kör...', done: '✓', failed: '✗ Misslyckades', skipped: 'Hoppades över' };

        function renderPlanBubble(m) {
          const statusLabel = planStateLabels[m.planState] ?? m.planState;
          if (m.planState === 'planning' || m.planState === 'failed') {
            return `
            <article class="message assistant">
              <div class="message-meta"><strong>AiLocal</strong><span class="pill">${esc(statusLabel)}</span></div>
              <div>${m.planState === 'failed' ? `✗ ${esc(m.error)}` : 'Bryter ner målet i deluppgifter...'}</div>
            </article>`;
          }

          const rows = m.subtasks.map((s, i) => `
            <div style="margin:8px 0">
              <label class="check-field" style="align-items:flex-start">
                <input type="checkbox" ${s.included ? 'checked' : ''}
                  ${m.planState !== 'reviewing' ? 'disabled' : ''}
                  data-plan-toggle="${m.id}:${i}">
                <span>
                  <strong>${esc(s.title)}</strong>
                  ${s.independent ? '<span class="pill" title="Kan köras parallellt på en annan dator">Parallell</span>' : ''}
                  ${subtaskStatusLabels[s.status] ? `<span class="pill">${esc(subtaskStatusLabels[s.status])}</span>` : ''}
                  <div class="small">${esc(s.description)}</div>
                </span>
              </label>
            </div>`).join('');

          return `
          <article class="message assistant">
            <div class="message-meta"><strong>AiLocal</strong><span class="pill">${esc(statusLabel)}</span></div>
            ${m.planNote ? `<div class="small" style="opacity:.75;margin-bottom:6px">${esc(m.planNote)}</div>` : ''}
            <div>${rows}</div>
            ${m.planState === 'reviewing' ? `
              <div class="detail-actions">
                <button class="primary" data-plan-run="${m.id}">Kör planen</button>
                <button data-plan-cancel="${m.id}">Avbryt</button>
              </div>` : ''}
          </article>`;
        }

        function wirePlanBubbles() {
          const plans = [...state.messages, ...state.assignmentMessages].filter(m => m.isPlan);
          const findPlan = id => plans.find(p => String(p.id) === id);

          document.querySelectorAll('#messages [data-plan-toggle]').forEach(input => {
            const [planId, index] = input.dataset.planToggle.split(':');
            input.onchange = () => {
              const plan = findPlan(planId);
              if (plan) plan.subtasks[Number(index)].included = input.checked;
            };
          });
          document.querySelectorAll('#messages [data-plan-run]').forEach(button => {
            button.onclick = () => {
              const plan = findPlan(button.dataset.planRun);
              if (plan) runPlan(plan);
            };
          });
          document.querySelectorAll('#messages [data-plan-cancel]').forEach(button => {
            button.onclick = () => {
              const plan = findPlan(button.dataset.planCancel);
              if (plan) cancelPlan(plan);
            };
          });
        }

        /// Opens a live SSE stream for the newest in-flight chat reply so tokens
        /// appear as they're generated instead of waiting for the next 3s poll.
        /// Fan-out (multi-worker) goals have no single stream to subscribe to -
        /// the server replies with an immediate non-terminal "done" frame, which
        /// is remembered per task so we stop retrying and fall back to polling.
        function manageStreaming() {
          const live = [...state.messages].reverse().find(m =>
            m.role === 'assistant' && m.taskId && cancellableStates.includes(m.state) &&
            !state.streamUnavailable.has(m.taskId));

          if (!live) {
            stopStreaming();
            return;
          }
          if (state.streamingTaskId === live.taskId) return;

          stopStreaming();
          const taskId = live.taskId;
          state.streamingTaskId = taskId;
          state.streamBuffer = { taskId, text: '' };
          const streamUrl = `/api/tasks/${taskId}/stream` +
            (state.authToken ? `?token=${encodeURIComponent(state.authToken)}` : '');
          const source = new EventSource(streamUrl);
          state.streamSource = source;
          source.onmessage = event => {
            let payload;
            try { payload = JSON.parse(event.data); } catch { return; }
            if (payload.done) {
              const terminal = ['Completed', 'Failed', 'Cancelled', 'Paused'].includes(payload.state);
              const hadDeltas = (state.streamBuffer?.text?.length ?? 0) > 0;
              stopStreaming();
              if (!terminal) state.streamUnavailable.add(taskId);
              if (terminal || hadDeltas) refresh();
              return;
            }
            if (typeof payload.delta === 'string' && state.streamBuffer) {
              state.streamBuffer.text += payload.delta;
              renderMessages();
            }
          };
          source.onerror = () => {
            stopStreaming();
            state.streamUnavailable.add(taskId);
          };
        }

        function stopStreaming() {
          if (state.streamSource) {
            state.streamSource.close();
            state.streamSource = null;
          }
          state.streamingTaskId = null;
          state.streamBuffer = null;
        }

        async function loadSchedules() {
          const hostOnly = $('scheduleHostOnly');
          try {
            state.schedules = await fetchJson('/api/schedules') ?? [];
            hostOnly.style.display = 'none';
          } catch {
            state.schedules = [];
            hostOnly.style.display = state.local?.role === 'Host' ? 'none' : 'block';
          }
          renderSchedules();
        }

        function renderSchedules() {
          $('scheduleSummary').textContent = `${state.schedules.length} scheman`;
          $('schedules').innerHTML = state.schedules.length ? state.schedules.map(s => {
            const cadence = s.atTimeOfDay ? `dagligen ${s.atTimeOfDay} UTC` : `var ${s.intervalMinutes}:e minut`;
            const last = s.lastRunAt ? `senast körd ${ago(s.lastRunAt)} sedan` : 'aldrig körd';
            return `<div class="node">
              <div class="node-main"><strong>${esc(s.name)}</strong><span class="pill">${s.enabled ? 'Aktiv' : 'Pausad'}</span></div>
              <div class="small">${esc(trunc(s.prompt, 72))}</div>
              <div class="small">${esc(cadence)} | ${esc(last)}</div>
              <div class="detail-actions">
                <button data-run-schedule="${esc(s.id)}">Kör nu</button>
                <button data-toggle-schedule="${esc(s.id)}">${s.enabled ? 'Pausa' : 'Aktivera'}</button>
                <button data-delete-schedule="${esc(s.id)}">Ta bort</button>
              </div>
            </div>`;
          }).join('') : '<div class="empty">Inga schemalagda mål ännu.</div>';

          document.querySelectorAll('[data-run-schedule]').forEach(button => {
            button.onclick = () => runScheduleNow(button.dataset.runSchedule);
          });
          document.querySelectorAll('[data-toggle-schedule]').forEach(button => {
            button.onclick = () => toggleSchedule(button.dataset.toggleSchedule);
          });
          document.querySelectorAll('[data-delete-schedule]').forEach(button => {
            button.onclick = () => deleteSchedule(button.dataset.deleteSchedule);
          });
        }

        function showScheduleNotice(message, isError = false) {
          const box = $('scheduleNotice');
          box.textContent = message;
          box.className = `notice show ${isError ? 'bad' : ''}`;
        }

        async function createSchedule() {
          const name = $('scheduleName').value.trim();
          const prompt = $('schedulePrompt').value.trim();
          if (!name || !prompt) {
            showScheduleNotice('Namn och mål/prompt krävs.', true);
            return;
          }
          const button = $('scheduleCreate');
          button.disabled = true;
          try {
            await fetchJson('/api/schedules', {
              method: 'POST',
              headers: { 'content-type': 'application/json' },
              body: JSON.stringify({
                name,
                prompt,
                intervalMinutes: Number($('scheduleInterval').value) || 60,
                atTimeOfDay: $('scheduleAtTime').value.trim() || null
              })
            });
            $('scheduleName').value = '';
            $('schedulePrompt').value = '';
            $('scheduleAtTime').value = '';
            showScheduleNotice('Schema skapat.');
            await loadSchedules();
          } catch (error) {
            showScheduleNotice(error.message, true);
          } finally {
            button.disabled = false;
          }
        }

        async function runScheduleNow(id) {
          try {
            await fetchJson(`/api/schedules/${id}/run`, { method: 'POST' });
            showScheduleNotice('Schemat kördes.');
            await loadSchedules();
          } catch (error) {
            showScheduleNotice(error.message, true);
          }
        }

        async function toggleSchedule(id) {
          const schedule = state.schedules.find(s => s.id === id);
          if (!schedule) return;
          try {
            await fetchJson(`/api/schedules/${id}`, {
              method: 'PUT',
              headers: { 'content-type': 'application/json' },
              body: JSON.stringify({ enabled: !schedule.enabled })
            });
            await loadSchedules();
          } catch (error) {
            showScheduleNotice(error.message, true);
          }
        }

        async function deleteSchedule(id) {
          if (!confirm('Ta bort schemat?')) return;
          try {
            await fetchJson(`/api/schedules/${id}`, { method: 'DELETE' });
            await loadSchedules();
          } catch (error) {
            showScheduleNotice(error.message, true);
          }
        }

        // Flottuppdatering: EN knapp uppdaterar alla workers (varje nod
        // laddar ner senaste releasen från GitHub själv och startar om).
        // Noderna försvinner ur listan under omstarten och registrerar sig
        // igen på nya versionen - Hosten uppdateras separat via sin egen
        // uppdateringsknapp, sist, så den här dashboarden lever under tiden.
        async function updateAllNodes() {
          if (!confirm('Uppdatera alla workers till senaste releasen? Varje nod laddar ner nya versionen och startar om (noder som bygger just nu hoppas över).')) return;
          const btn = $('updateAllNodesBtn');
          btn.disabled = true;
          try {
            const res = await fetchJson('/api/nodes/update-all', { method: 'POST' });
            const started = (res.triggered || []).filter(t => t.ok).map(t => t.node);
            const failed = (res.triggered || []).filter(t => !t.ok).map(t => `${t.node} (${t.detail || 'fel'})`);
            const parts = [];
            if (started.length) parts.push(`Uppdatering startad: ${started.join(', ')}`);
            if (res.skippedBusy?.length) parts.push(`Hoppade över (bygger just nu): ${res.skippedBusy.join(', ')}`);
            if (res.skippedOffline?.length) parts.push(`Offline: ${res.skippedOffline.join(', ')}`);
            if (failed.length) parts.push(`Misslyckades: ${failed.join(', ')}`);
            alert(parts.join('\n') || 'Inga noder att uppdatera.');
          } catch (err) {
            alert('Flottuppdateringen misslyckades: ' + (err?.message || err));
          } finally {
            btn.disabled = false;
            refresh();
          }
        }

        async function refresh() {
          if (state.refreshing || $('settingsDialog')?.open) return;
          state.refreshing = true;
          try {
            try {
              state.local = await fetchJson('/api/local');
              renderLocal();
            } catch {}

            try {
              const h = await fetchJson('/api/host');
              state.host = h?.host ?? null;
              state.overseerHosts = h?.hosts || [];
              renderHost();
              renderSessionHostSelect();
            } catch {
              renderHost();
            }

            const [nodesResult, topologyResult, tasksResult, messagesResult, schedulesResult, sessionsResult] =
              await Promise.allSettled([
                fetchJson('/api/nodes'),
                fetchJson('/api/topology'),
                fetchJson('/api/tasks'),
                fetchJson('/api/chat'),
                // Fetched here too (not just loadSchedules(), which only runs
                // once the Schema tab is opened) so the status bar's cron
                // count is accurate even if that tab is never visited.
                fetchJson('/api/schedules'),
                // Same reasoning as schedules above - the sidebar list (and
                // the pin/lastActive state of whichever item is active) needs
                // to stay live regardless of which view is open.
                fetchJson('/api/sessions')
              ]);

            if (nodesResult.status === 'fulfilled')
              state.nodes = nodesResult.value ?? [];
            renderNodes();

            if (topologyResult.status === 'fulfilled')
              state.topology = topologyResult.value ?? state.topology;
            if (state.activeView === 'network') {
              renderTopology();
              renderTopologyDetail();
            }

            if (tasksResult.status === 'fulfilled')
              state.tasks = tasksResult.value ?? [];
            renderTasks();

            if (messagesResult.status === 'fulfilled')
              state.messages = messagesResult.value ?? [];
            // The render-only-the-active-view optimization checked for a view
            // named 'chat' - which doesn't exist (the view is 'delegate'), so
            // incoming chat replies were NEVER rendered after a refresh: the
            // Worker answered, /api/chat had the reply, the transcript stayed
            // empty. Streaming (manageStreaming) is also started from inside
            // renderMessages, so live token streaming silently died too.
            if (state.activeView === 'delegate') renderMessages();

            if (schedulesResult.status === 'fulfilled')
              state.schedules = schedulesResult.value ?? [];

            if (sessionsResult.status === 'fulfilled')
              state.sessions = sessionsResult.value ?? [];
            renderSessions();

            try {
              const active = await fetchJson('/api/sessions/active-count');
              state.activeSessionRuns = active?.count ?? 0;
            } catch { state.activeSessionRuns = 0; }

            if (state.selectedNodeId) await loadWorkerTasks(state.selectedNodeId);

            try {
              state.stats = await fetchJson('/api/stats');
              state.queue = await fetchJson('/api/queue');
              $('queueRow').style.display = 'flex';
              $('costRow').style.display = 'flex';
              $('queueLabel').textContent = `${state.queue.queued} / ${state.queue.inFlight}`;
              $('costLabel').textContent = fmtUsd(state.stats.today.costUsd) || '$0.00';
              renderCostBreakdown(state.stats);
            } catch {
              $('queueRow').style.display = 'none';
              $('costRow').style.display = 'none';
              $('costTotalRow').style.display = 'none';
              $('tokenRow').style.display = 'none';
              $('costByProvider').style.display = 'none';
            }

            if (state.local?.role === 'Host') {
              try {
                state.discoveredWorkers = await fetchJson('/api/discovered-workers') ?? [];
                const pending = await fetchJson('/api/pairing-status') ?? [];
                // Once the outbound request is gone - either the Worker
                // connected (it'll also vanish from discoveredWorkers) or the
                // request expired after 5 minutes without a reply - drop the
                // "waiting" state so a stuck/failed attempt can be retried
                // instead of leaving the button disabled forever.
                for (const id of [...state.pairingConnecting]) {
                  if (!pending.some(p => p.peerId === id))
                    state.pairingConnecting.delete(id);
                }
              } catch { state.discoveredWorkers = []; }
              renderDiscoveredWorkers();
            }

            if (state.local?.role === 'Worker') {
              try {
                state.pairingInbound = await fetchJson('/pairing/pending') ?? [];
              } catch { state.pairingInbound = []; }
              renderPairingRequests();
            }

            try {
              renderLocalNodes(await fetchJson('/api/local-nodes'));
            } catch { renderLocalNodes([]); }

            renderFirstRunBanner();
            checkForUpdate();
            renderStatusBar();
          } finally {
            state.refreshing = false;
          }
        }

        function renderFirstRunBanner() {
          const box = $('firstRunBanner');
          if (state.firstRunDismissed || !state.local) {
            box.className = 'notice';
            return;
          }

          const bannerText = state.local.role === 'Launcher'
            ? 'Nytt kluster? Klicka <strong>"Starta kluster (Host + Worker)"</strong> till vänster för att komma igång på den här datorn, ' +
              'eller välj Host/Worker/Overseer ovan. Kopiera klusternyckeln från Host-inställningarna till andra datorer som ska gå med.'
            : state.local.role === 'Host' && !state.nodes.length
              ? 'Ingen Worker ansluten ännu. Starta en Worker på den här eller en annan dator på samma nätverk - den dyker upp under ' +
                '"Upptäckta enheter" här nedanför, redo att anslutas med ett klick. Ser du den inte? Klistra in klusternyckeln ' +
                '(Inställningar -> Klustersäkerhet) på Worker-datorn istället.'
              : state.local.role === 'Worker' && !state.local.hostEndpoint
                ? 'Den här Workern är inte ansluten till någon Host än. Öppna Host-datorns instrumentpanel - den ser den här ' +
                  'datorn automatiskt under "Upptäckta enheter" och kan ansluta med ett klick. Väntar du på en bekräftelse ' +
                  'istället? Kolla om det ligger en väntande förfrågan högst upp på den här sidan.'
                : null;

          if (bannerText === null) {
            box.className = 'notice';
            return;
          }

          // Flexrad med texten i en egen span - kryssknappen var tidigare
          // float:right rakt i textflödet och hamnade aldrig vertikalt
          // centrerad mot innehållet.
          box.innerHTML = `<span style="flex:1">${bannerText}</span>
            <button class="icon" id="dismissFirstRun" title="Stäng">${icon('x')}</button>`;
          box.className = 'notice show banner-flex';
          $('dismissFirstRun').onclick = () => { state.firstRunDismissed = true; renderFirstRunBanner(); };
        }

        let updateChecked = false;
        async function checkForUpdate() {
          if (updateChecked) return;
          updateChecked = true;
          try {
            const info = await fetchJson('/api/update-check');
            state.updateInfo = info;
            const box = $('updateBanner');
            if (info?.updateAvailable) {
              // Enklicks-uppdatering direkt fran bannern nar noden kan
              // sjalvuppdatera - annars manuell hamtningslank som forut.
              const action = info.canSelfUpdate
                ? `<button class="mini-btn" id="updateNowBtn">Uppdatera nu</button>`
                : (info.downloadUrl ? ` <a href="${esc(info.downloadUrl)}" target="_blank" rel="noopener">Hämta</a>` : '');
              box.innerHTML = `<span style="flex:1">En ny version är tillgänglig: <strong>${esc(info.latestVersion)}</strong> ` +
                `(nuvarande ${esc(info.currentVersion)}).` +
                (info.notes ? ` - ${esc(info.notes)}` : '') + `</span>${action}`;
              box.className = 'notice show banner-flex';
              const btn = $('updateNowBtn');
              if (btn) btn.onclick = async () => {
                // openSettings utan argument = DENNA nods installningar -
                // ett argument tolkas som ett Worker-id och gommer
                // uppdateringssektionen helt.
                await openSettings();
                await checkUpdateInSettings();
                applyUpdate();
              };
            } else {
              box.className = 'notice';
            }
          } catch {
            updateChecked = false;
          }
        }

        async function loadProviders() {
          try {
            const data = await fetchJson('/api/providers');
            const priority = data?.priority?.length ? data.priority : ['anthropic','gemini','ollama'];
            state.providerOrder = [...priority, ...providerIds.filter(id => !priority.includes(id))];
            state.enabled = Object.fromEntries(providerIds.map(id => [id, priority.includes(id)]));
          } catch {}
          renderProviders();
        }

        async function saveProviders() {
          const priority = activeProviderOrder();
          try {
            const data = await fetchJson('/api/providers', {
              method: 'PUT',
              headers: { 'content-type': 'application/json' },
              body: JSON.stringify({ priority })
            });
            if (data?.priority) {
              state.providerOrder = [...data.priority, ...providerIds.filter(id => !data.priority.includes(id))];
              state.enabled = Object.fromEntries(providerIds.map(id => [id, data.priority.includes(id)]));
            }
            renderProviders();
          } catch (error) {
            showGlobalNotice(error.message, true);
          }
        }

        function activeSettingsProviderOrder() {
          return state.settingsOrder.filter(id => state.settingsEnabled[id]);
        }

        function renderSettingsProviders() {
          const order = [...state.settingsOrder, ...providerIds.filter(id => !state.settingsOrder.includes(id))];
          $('settingsProviders').innerHTML = order.map((id, index) => `
            <div class="settings-provider">
              <input type="checkbox" ${state.settingsEnabled[id] ? 'checked' : ''} data-settings-toggle="${id}">
              <div><strong>${providerLabels[id] ?? id}</strong><div class="small">${id}</div></div>
              <button class="icon" title="Flytta upp" data-settings-up="${id}" ${index === 0 ? 'disabled' : ''}>${icon('chevron-up', 14)}</button>
              <button class="icon" title="Flytta ner" data-settings-down="${id}" ${index === order.length - 1 ? 'disabled' : ''}>${icon('chevron-down', 14)}</button>
            </div>`).join('');

          document.querySelectorAll('[data-settings-toggle]').forEach(input => {
            input.onchange = () => {
              state.settingsEnabled[input.dataset.settingsToggle] = input.checked;
              renderSettingsProviders();
            };
          });
          document.querySelectorAll('[data-settings-up]').forEach(button => {
            button.onclick = () => moveSettingsProvider(button.dataset.settingsUp, -1);
          });
          document.querySelectorAll('[data-settings-down]').forEach(button => {
            button.onclick = () => moveSettingsProvider(button.dataset.settingsDown, 1);
          });
        }

        function moveSettingsProvider(id, delta) {
          const index = state.settingsOrder.indexOf(id);
          const next = index + delta;
          if (index < 0 || next < 0 || next >= state.settingsOrder.length) return;
          [state.settingsOrder[index], state.settingsOrder[next]] =
            [state.settingsOrder[next], state.settingsOrder[index]];
          renderSettingsProviders();
        }

        // ---- Modellvaljare: OpenRouter-katalog -> tiers/skill-routes + ban ----
        const ModelPicker = (() => {
          let catalog = [], routes = [], banned = [], loaded = false, hasConfig = false;
          const SLOTS = [
            { key: 'simple', label: 'Billig', tier: 'settingTierSimple' },
            { key: 'medium', label: 'Mellan', tier: 'settingTierMedium' },
            { key: 'complex', label: 'Stark', tier: 'settingTierComplex' },
            { key: 'coding1', label: 'Kod latt', skill: 'coding', minC: 1 },
            { key: 'coding4', label: 'Kod tung', skill: 'coding', minC: 4 },
            { key: 'research3', label: 'Research', skill: 'research', minC: 3 },
            { key: 'vision1', label: 'Vision', skill: 'vision', minC: 1, mm: true }
          ];
          const el = id => document.getElementById(id);
          const routeOf = s => routes.find(r => r.skill === s.skill && r.minComplexity === s.minC);
          const currentModel = s => s.tier ? (el(s.tier) ? el(s.tier).value.trim() : '') : (routeOf(s) ? routeOf(s).model : '');
          const note = msg => { const n = el('mpNote'); if (n) n.textContent = msg || ''; };
          function assign(slotKey, modelId) {
            const s = SLOTS.find(x => x.key === slotKey); if (!s || !modelId) return;
            const m = catalog.find(x => x.id === modelId);
            if (s.mm && m && !m.multimodal) { note('Vision behover en multimodal modell - ' + modelId + ' ar text-only.'); return; }
            note('');
            if (s.tier) { const i = el(s.tier); if (i) i.value = modelId; }
            else { const r = routeOf(s); if (r) r.model = modelId; else routes.push({ skill: s.skill, provider: 'openrouter', model: modelId, minComplexity: s.minC }); }
            render();
          }
          function toggleBan(modelId) {
            const i = banned.findIndex(b => b.toLowerCase() === modelId.toLowerCase());
            if (i >= 0) banned.splice(i, 1); else banned.push(modelId);
            render();
          }
          function setConfig(mt) {
            routes = (mt && Array.isArray(mt.routes)) ? JSON.parse(JSON.stringify(mt.routes)) : [];
            banned = (mt && Array.isArray(mt.bannedModels)) ? mt.bannedModels.slice() : [];
            hasConfig = true; if (loaded) render();
          }
          async function load() {
            const w = el('mpWrap'); if (w) w.innerHTML = '<div class="small mp-hint" style="padding:10px">Hamtar katalogen...</div>';
            try { catalog = (await fetchJson('/api/models/catalog')) || []; loaded = true; render(); }
            catch (e) { if (w) w.innerHTML = '<div class="small mp-hint" style="padding:10px">Kunde inte hamta katalogen (natverk/OpenRouter).</div>'; }
          }
          const money = n => n ? ('$' + (n < 1 ? n.toFixed(3) : n.toFixed(2))) : '-';
          const codeOf = m => (m.codingIndex == null ? -1 : m.codingIndex);
          function render() {
            const a = el('mpAssignments');
            if (a) a.innerHTML = SLOTS.map(s => '<span class="mp-chip">' + esc(s.label) + ': <b>' + esc(currentModel(s) || '-') + '</b></span>').join('');
            const b = el('mpBanned');
            if (b) b.innerHTML = banned.length ? ('<span class="small mp-hint">Bannade:</span> ' + banned.map(x => '<span class="mp-ban" data-unban="' + esc(x) + '">' + esc(x) + ' x</span>').join(' ')) : '';
            const w = el('mpWrap'); if (!w || !loaded) return;
            const q = (el('mpSearch') ? el('mpSearch').value : '').toLowerCase();
            const sort = el('mpSort') ? el('mpSort').value : 'coding';
            let rows = catalog.filter(m => !q || m.id.toLowerCase().includes(q) || (m.name || '').toLowerCase().includes(q));
            rows.sort((x, y) => sort === 'in' ? (x.inputPerMillion - y.inputPerMillion)
              : sort === 'out' ? (x.outputPerMillion - y.outputPerMillion)
              : sort === 'context' ? (y.contextLength - x.contextLength)
              : (codeOf(y) - codeOf(x)));
            rows = rows.slice(0, 200);
            const opts = SLOTS.map(s => '<option value="' + s.key + '">' + esc(s.label) + '</option>').join('');
            w.innerHTML = '<table class="mp-table"><thead><tr><th>Modell</th><th class="num">Kontext</th><th class="num">$in/M</th><th class="num">$ut/M</th><th class="num">Kod</th><th>Tilldela</th><th></th></tr></thead><tbody>'
              + rows.map(m => {
                const off = banned.some(x => x.toLowerCase() === m.id.toLowerCase());
                return '<tr class="' + (off ? 'mp-off' : '') + '">'
                  + '<td class="mp-name" title="' + esc(m.id) + '">' + esc(m.name || m.id) + (m.multimodal ? ' <span class="mp-hint">(bild)</span>' : '') + '<br><span class="mp-hint">' + esc(m.id) + '</span></td>'
                  + '<td class="num">' + (m.contextLength ? (Math.round(m.contextLength / 1000) + 'k') : '-') + '</td>'
                  + '<td class="num">' + money(m.inputPerMillion) + '</td>'
                  + '<td class="num">' + money(m.outputPerMillion) + '</td>'
                  + '<td class="num mp-code">' + (m.codingIndex == null ? '-' : m.codingIndex) + '</td>'
                  + '<td><select class="mp-assign" data-assign="' + esc(m.id) + '"><option value="">Tilldela...</option>' + opts + '</select></td>'
                  + '<td><button type="button" class="mp-ban" data-ban="' + esc(m.id) + '">' + (off ? 'Tillat' : 'Banna') + '</button></td>'
                  + '</tr>';
              }).join('') + '</tbody></table>';
          }
          return { load, setConfig, render, routes: () => routes, banned: () => banned, hasConfig: () => hasConfig, _assign: assign, _ban: toggleBan };
        })();
        (function () {
          const on = (id, ev, fn) => { const e = document.getElementById(id); if (e) e.addEventListener(ev, fn); };
          on('mpLoad', 'click', () => ModelPicker.load());
          on('mpSearch', 'input', () => ModelPicker.render());
          on('mpSort', 'change', () => ModelPicker.render());
          on('mpWrap', 'change', e => { const t = e.target; if (t && t.dataset && t.dataset.assign) ModelPicker._assign(t.value, t.dataset.assign); });
          on('mpWrap', 'click', e => { const t = e.target; if (t && t.dataset && t.dataset.ban) ModelPicker._ban(t.dataset.ban); });
          on('mpBanned', 'click', e => { const t = e.target; if (t && t.dataset && t.dataset.unban) ModelPicker._ban(t.dataset.unban); });
        })();

        function applySettingsData(data) {
          $('settingNodeName').value = data.nodeName ?? '';
          $('settingHostEndpoint').value = data.hostEndpoint ?? '';
          $('settingDiscovery').checked = data.discoveryEnabled ?? true;
          $('settingSkills').value = (data.skills ?? ['general']).join(', ');
          $('settingMaxConcurrentTasks').value = data.maxConcurrentTasks ?? 1;
          $('settingAgentAccess').value = data.agentAccess ?? 'Off';
          $('settingWorkspacePath').value = data.workspacePath ?? '';
          $('settingAiReviewWrites').checked = data.aiReviewWrites ?? false;
          $('settingMilestoneApproval').checked = data.milestoneApproval ?? false;
          $('settingAllowInternet').checked = data.allowInternet ?? false;
          $('settingUseGitIsolation').checked = data.useGitIsolation ?? false;
          $('settingAutoMergeIsolatedTasks').checked = data.autoMergeIsolatedTasks ?? false;
          $('settingBudgetLimitUsd').value = data.budgetLimitUsd != null ? data.budgetLimitUsd : 0;
          $('settingCommandGuard').value = data.commandGuard ?? 'Block';
          $('settingBlockedCommands').value = (data.blockedCommands ?? []).join('\n');
          $('settingProjectMemory').checked = data.projectMemoryEnabled ?? false;
          $('settingAllowDesktopControl').checked = data.allowDesktopControl ?? false;
          $('settingTierSimple').value = data.modelTiers?.simple ?? '';
          $('settingTierMedium').value = data.modelTiers?.medium ?? '';
          $('settingTierComplex').value = data.modelTiers?.complex ?? '';
          ModelPicker.setConfig(data.modelTiers || {});
          $('settingClusterToken').value = '';
          $('clearClusterToken').checked = false;
          $('clusterTokenState').textContent = data.clusterTokenConfigured
            ? 'Klusternyckel konfigurerad'
            : 'Ingen klusternyckel';
          $('currentClusterToken').value = data.clusterToken ?? '';
          $('currentClusterToken').type = 'password';
          $('toggleTokenVisibility').innerHTML = icon('eye');
          $('settingOperatorToken').value = '';
          $('clearOperatorToken').checked = false;
          $('currentOperatorToken').value = data.operatorToken ?? '';
          $('currentOperatorToken').type = 'password';
          $('toggleOperatorTokenVisibility').innerHTML = icon('eye');
          $('settingAutoStartRow').style.display = data.startWithWindowsSupported === false ? 'none' : 'flex';
          $('settingAutoStart').checked = data.startWithWindows ?? false;
          $('settingDataPath').value = data.settingsPath ?? '';
          reflectAgentAccess(data.agentAccess ?? 'Off');
          $('settingAnthropicModel').value = data.anthropicModel ?? '';
          $('settingGeminiModel').value = data.geminiModel ?? '';
          $('settingOpenRouterModel').value = data.openRouterModel ?? '';
          $('settingOpenAIModel').value = data.openAIModel ?? '';
          $('settingOllamaModel').value = data.ollamaModel ?? '';
          $('settingOllamaEndpoint').value = data.ollamaEndpoint ?? 'http://localhost:11434';
          $('settingMaxTokens').value = data.maxTokens ?? 4096;
          $('settingAutoPull').checked = data.autoPullOllamaModel ?? false;
          $('settingAnthropicKey').value = '';
          $('settingGeminiKey').value = '';
          $('settingOpenRouterKey').value = '';
          $('settingOpenAIKey').value = '';
          $('clearAnthropicKey').checked = false;
          $('clearGeminiKey').checked = false;
          $('clearOpenRouterKey').checked = false;
          $('clearOpenAIKey').checked = false;
          $('anthropicKeyState').textContent = data.anthropicKeyConfigured ? 'Nyckel konfigurerad' : 'Ingen nyckel';
          $('geminiKeyState').textContent = data.geminiKeyConfigured ? 'Nyckel konfigurerad' : 'Ingen nyckel';
          $('openRouterKeyState').textContent = data.openRouterKeyConfigured ? 'Nyckel konfigurerad' : 'Ingen nyckel';
          $('openAIKeyState').textContent = data.openAIKeyConfigured ? 'Nyckel konfigurerad' : 'Ingen nyckel';

          const priority = data.providerPriority?.length ? data.providerPriority : ['ollama'];
          state.settingsOrder = [...priority, ...providerIds.filter(id => !priority.includes(id))];
          state.settingsEnabled = Object.fromEntries(providerIds.map(id => [id, priority.includes(id)]));
          renderSettingsProviders();
        }

        function toggleTokenVisibility() {
          const field = $('currentClusterToken');
          const hidden = field.type === 'password';
          field.type = hidden ? 'text' : 'password';
          $('toggleTokenVisibility').innerHTML = icon(hidden ? 'eye-off' : 'eye');
        }

        function toggleOperatorTokenVisibility() {
          const field = $('currentOperatorToken');
          const hidden = field.type === 'password';
          field.type = hidden ? 'text' : 'password';
          $('toggleOperatorTokenVisibility').innerHTML = icon(hidden ? 'eye-off' : 'eye');
        }

        async function copyOperatorToken() {
          const value = $('currentOperatorToken').value;
          if (!value) {
            showSettingsNotice('Ingen operatörsnyckel att kopiera. Generera en först.', true);
            return;
          }
          try {
            await navigator.clipboard.writeText(value);
            showSettingsNotice('Operatörsnyckeln är kopierad.');
          } catch {
            $('currentOperatorToken').type = 'text';
            $('currentOperatorToken').select();
            showSettingsNotice('Kunde inte kopiera automatiskt - markerad för manuell kopiering.', true);
          }
        }

        async function regenerateOperatorToken() {
          if (!confirm('Generera en ny operatörsnyckel? Alla som använder den nuvarande nyckeln tappar åtkomst tills de får den nya.'))
            return;
          const button = $('regenerateOperatorToken');
          button.disabled = true;
          try {
            const url = state.settingsTarget
              ? `/api/nodes/${state.settingsTarget}/settings`
              : '/api/settings';
            const data = await fetchJson(url, {
              method: 'PUT',
              headers: { 'content-type': 'application/json' },
              body: JSON.stringify({ regenerateOperatorToken: true })
            });
            applySettingsData(data);
            $('currentOperatorToken').type = 'text';
            $('toggleOperatorTokenVisibility').innerHTML = icon('eye-off');
            showSettingsNotice('Ny operatörsnyckel genererad.');
          } catch (error) {
            showSettingsNotice(error.message, true);
          } finally {
            button.disabled = false;
          }
        }

        async function copyClusterToken() {
          const value = $('currentClusterToken').value;
          if (!value) {
            showSettingsNotice('Ingen klusternyckel att kopiera. Generera en först.', true);
            return;
          }
          try {
            await navigator.clipboard.writeText(value);
            showSettingsNotice('Klusternyckeln är kopierad.');
          } catch {
            $('currentClusterToken').type = 'text';
            $('currentClusterToken').select();
            showSettingsNotice('Kunde inte kopiera automatiskt - markerad för manuell kopiering.', true);
          }
        }

        async function regenerateClusterToken() {
          if (!confirm('Generera en ny klusternyckel? Alla parkopplade Workers/Overseers måste få den nya nyckeln.'))
            return;
          const button = $('regenerateClusterToken');
          button.disabled = true;
          try {
            const url = state.settingsTarget
              ? `/api/nodes/${state.settingsTarget}/settings`
              : '/api/settings';
            const data = await fetchJson(url, {
              method: 'PUT',
              headers: { 'content-type': 'application/json' },
              body: JSON.stringify({ regenerateClusterToken: true })
            });
            applySettingsData(data);
            $('currentClusterToken').type = 'text';
            $('toggleTokenVisibility').innerHTML = icon('eye-off');
            showSettingsNotice('Ny klusternyckel genererad. Uppdatera övriga noder med den.');
          } catch (error) {
            showSettingsNotice(error.message, true);
          } finally {
            button.disabled = false;
          }
        }

        function showSettingsNotice(message, isError = false) {
          const box = $('settingsNotice');
          box.textContent = message;
          box.className = `notice show ${isError ? 'bad' : ''}`;
        }

        function switchSettingsCategory(category) {
          document.querySelectorAll('[data-settings-pane]').forEach(pane => {
            pane.classList.toggle('hidden', pane.dataset.settingsPane !== category);
          });
          document.querySelectorAll('[data-settings-cat]').forEach(button => {
            button.classList.toggle('active', button.dataset.settingsCat === category);
          });
        }

        async function openSettings(targetId = null) {
          state.settingsTarget = targetId;
          switchSettingsCategory('general');
          $('settingsNotice').className = 'notice';
          $('saveSettings').disabled = true;
          $('settingThemeSelect').value = document.documentElement.getAttribute('data-theme') || 'dark';
          $('aboutVersion').textContent = state.updateInfo?.currentVersion ? `v${state.updateInfo.currentVersion}` : '...';
          $('settingsTitle').textContent = targetId ? 'Konfigurera Worker' : 'Nodinställningar';
          const node = state.nodes.find(n => n.id === targetId);
          $('settingsSubtitle').textContent = node ? `${node.name} | ${node.endpoint}` : (state.local?.name ?? '');
          // Self-update only ever touches the exe of the process answering the
          // request - there's no remote-update proxy (a Host pushing updates
          // to a Worker is a much bigger blast radius than the settings it
          // already proxies), so the section only makes sense for this node.
          $('updateSection').style.display = targetId ? 'none' : '';
          $('settingsDialog').showModal();
          try {
            await waitForRefreshIdle();
            const url = targetId ? `/api/nodes/${targetId}/settings` : '/api/settings';
            applySettingsData(await fetchJsonWithRetry(url));
            $('saveSettings').disabled = false;
            if (!targetId) checkUpdateInSettings();
          } catch (error) {
            showSettingsNotice(error.message, true);
          }
        }

        async function checkUpdateInSettings() {
          const status = $('updateStatus');
          const applyBtn = $('applyUpdateBtn');
          const manualLink = $('updateManualLink');
          status.textContent = 'Söker efter uppdatering...';
          applyBtn.style.display = 'none';
          manualLink.style.display = 'none';
          try {
            const info = await fetchJson('/api/update-check');
            state.updateInfo = info;
            if (info.error) {
              status.textContent = `Nuvarande version: ${info.currentVersion}. Kunde inte söka efter uppdatering: ${info.error}`;
            } else if (info.updateAvailable) {
              status.textContent = `Ny version tillgänglig: ${info.latestVersion} (nuvarande ${info.currentVersion}).` +
                (info.notes ? ` ${info.notes}` : '');
              if (info.canSelfUpdate) {
                applyBtn.style.display = '';
              } else if (info.downloadUrl) {
                manualLink.href = info.downloadUrl;
                manualLink.style.display = '';
              }
            } else {
              status.textContent = `Du kör senaste versionen (${info.currentVersion}).`;
            }
          } catch (error) {
            status.textContent = `Kunde inte söka efter uppdatering: ${error.message}`;
          }
        }

        async function applyUpdate() {
          if (!confirm('Ladda ner och installera den nya versionen nu? Programmet startar om automatiskt om några sekunder.'))
            return;
          const applyBtn = $('applyUpdateBtn');
          const checkBtn = $('checkUpdateBtn');
          const status = $('updateStatus');
          const progress = $('updateProgress');
          const bar = $('updateProgressBar');
          const manualLink = $('updateManualLink');
          applyBtn.disabled = true;
          checkBtn.disabled = true;
          manualLink.style.display = 'none';
          progress.style.display = 'block';
          bar.style.width = '0%';
          status.textContent = 'Söker efter uppdatering...';

          // Poll progress while the server downloads + swaps the exe in place.
          let done = false;
          const poll = setInterval(async () => {
            try {
              const p = await fetchJson('/api/update-progress');
              if (p.phase === 'downloading') {
                const pct = Math.round((p.fraction ?? 0) * 100);
                bar.style.width = pct + '%';
                const mb = n => (n / 1048576).toFixed(1);
                status.textContent = `Laddar ner ${mb(p.downloaded)} / ${mb(p.total)} MB (${pct}%)...`;
              } else if (p.phase === 'installing') {
                bar.style.width = '100%';
                status.textContent = 'Installerar...';
              } else if (p.phase === 'restarting') {
                status.textContent = 'Startar om...';
                done = true;
              } else if (p.phase === 'error') {
                status.textContent = `Uppdateringen misslyckades: ${p.error ?? 'okänt fel'}`;
                clearInterval(poll);
                applyBtn.disabled = false;
                checkBtn.disabled = false;
                progress.style.display = 'none';
              }
            } catch { /* ignore transient poll errors */ }
          }, 500);

          try {
            await fetchJson('/api/update-apply', { method: 'POST' });
            // Wait for the process to come back up as the new version, then reload.
            waitForRestart(() => { clearInterval(poll); });
          } catch (error) {
            clearInterval(poll);
            status.textContent = `Uppdateringen misslyckades: ${error.message}`;
            applyBtn.disabled = false;
            checkBtn.disabled = false;
            progress.style.display = 'none';
          }
        }

        async function waitForRestart(onDone) {
          // The process that just answered /api/update-apply is about to
          // exit and come back up as the new version on the same port -
          // poll until something answers again, then reload to pick it up.
          for (let attempt = 0; attempt < 60; attempt++) {
            await new Promise(resolve => setTimeout(resolve, 2000));
            try {
              await fetchJson('/api/version');
              if (onDone) onDone();
              location.reload();
              return;
            } catch { /* still restarting */ }
          }
          if (onDone) onDone();
          $('updateStatus').textContent = 'Startade om men svarar inte än - ladda om sidan manuellt om det dröjer längre.';
        }

        async function waitForRefreshIdle() {
          for (let attempt = 0; attempt < 80 && state.refreshing; attempt++)
            await new Promise(resolve => setTimeout(resolve, 100));
        }

        // Animates the fade-out, THEN calls the real .close() - doing it the
        // other way around (close() first, animate after) is what the
        // previous CSS-only attempt tried via transition-behavior:
        // allow-discrete + @starting-style, and it left the dialog stuck:
        // invisible but still display:grid and still eating pointer-events,
        // because the browser never actually finished the discrete display
        // swap. This way .close() (and its real, immediate display:none)
        // only runs once the fade has already visually completed, so there
        // is nothing left to get stuck.
        function closeSettingsDialog() {
          const dialog = $('settingsDialog');
          if (!dialog.open || dialog.classList.contains('dialog-closing')) return;
          dialog.classList.add('dialog-closing');
          setTimeout(() => {
            dialog.classList.remove('dialog-closing');
            dialog.close();
          }, 160);
        }

        async function saveSettings() {
          const button = $('saveSettings');
          button.disabled = true;
          const body = {
            nodeName: $('settingNodeName').value,
            hostEndpoint: $('settingHostEndpoint').value,
            discoveryEnabled: $('settingDiscovery').checked,
            startWithWindows: $('settingAutoStart').checked,
            skills: $('settingSkills').value.split(',').map(value => value.trim()).filter(Boolean),
            maxConcurrentTasks: Number($('settingMaxConcurrentTasks').value),
            agentAccess: $('settingAgentAccess').value,
            workspacePath: $('settingWorkspacePath').value.trim() || null,
            aiReviewWrites: $('settingAiReviewWrites').checked,
            milestoneApproval: $('settingMilestoneApproval').checked,
            allowInternet: $('settingAllowInternet').checked,
            useGitIsolation: $('settingUseGitIsolation').checked,
            autoMergeIsolatedTasks: $('settingAutoMergeIsolatedTasks').checked,
            budgetLimitUsd: parseFloat($('settingBudgetLimitUsd').value) || 0,
            commandGuard: $('settingCommandGuard').value,
            blockedCommands: $('settingBlockedCommands').value.split('\n').map(value => value.trim()).filter(Boolean),
            projectMemoryEnabled: $('settingProjectMemory').checked,
            allowDesktopControl: $('settingAllowDesktopControl').checked,
            modelTiers: {
              simple: $('settingTierSimple').value.trim() || 'deepseek/deepseek-v4-flash',
              medium: $('settingTierMedium').value.trim() || 'deepseek/deepseek-v4-pro',
              complex: $('settingTierComplex').value.trim() || 'z-ai/glm-5.2'
            },
            modelRoutes: ModelPicker.hasConfig() ? ModelPicker.routes() : undefined,
            bannedModels: ModelPicker.hasConfig() ? ModelPicker.banned() : undefined,
            clusterToken: $('settingClusterToken').value || null,
            clearClusterToken: $('clearClusterToken').checked,
            operatorToken: $('settingOperatorToken').value || null,
            clearOperatorToken: $('clearOperatorToken').checked,
            providerPriority: activeSettingsProviderOrder(),
            anthropicModel: $('settingAnthropicModel').value,
            geminiModel: $('settingGeminiModel').value,
            openRouterModel: $('settingOpenRouterModel').value,
            openAIModel: $('settingOpenAIModel').value,
            ollamaModel: $('settingOllamaModel').value,
            ollamaEndpoint: $('settingOllamaEndpoint').value,
            maxTokens: Number($('settingMaxTokens').value),
            autoPullOllamaModel: $('settingAutoPull').checked,
            anthropicApiKey: $('settingAnthropicKey').value || null,
            geminiApiKey: $('settingGeminiKey').value || null,
            openRouterApiKey: $('settingOpenRouterKey').value || null,
            openAIApiKey: $('settingOpenAIKey').value || null,
            clearAnthropicApiKey: $('clearAnthropicKey').checked,
            clearGeminiApiKey: $('clearGeminiKey').checked,
            clearOpenRouterApiKey: $('clearOpenRouterKey').checked,
            clearOpenAIApiKey: $('clearOpenAIKey').checked,
          };
          try {
            const url = state.settingsTarget
              ? `/api/nodes/${state.settingsTarget}/settings`
              : '/api/settings';
            const data = await fetchJson(url, {
              method: 'PUT',
              headers: { 'content-type': 'application/json' },
              body: JSON.stringify(body)
            });
            applySettingsData(data);
            showSettingsNotice('Inställningarna är sparade.');
            if (!state.settingsTarget) await loadProviders();
            await refresh();
            closeSettingsDialog();
          } catch (error) {
            showSettingsNotice(error.message, true);
          } finally {
            button.disabled = false;
          }
        }

        async function fetchOpenRouterModels() {
          const btn = $('fetchOpenRouterModels');
          const list = $('openRouterModelList');
          const state2 = $('openRouterModelsState');
          btn.disabled = true;
          state2.textContent = 'hämtar…';
          state2.className = 'key-state';
          try {
            const data = await fetchJson('/api/openrouter/models');
            const models = (data.models || []).filter(m => m.id);
            list.innerHTML = '<option value="">— välj från listan —</option>' +
              models.map(m => `<option value="${m.id}">${m.id}</option>`).join('');
            list.classList.remove('hidden');
            state2.textContent = `${models.length} modeller`;
            list.onchange = () => { if (list.value) $('settingOpenRouterModel').value = list.value; };
          } catch (error) {
            state2.textContent = error.message;
            state2.className = 'key-state bad';
          } finally {
            btn.disabled = false;
          }
        }

        function showComposerNotice(message, isError = false) {
          const box = $('composerNotice');
          box.textContent = message;
          box.className = `notice show ${isError ? 'bad' : ''}`;
        }

        // For errors from actions that exist regardless of role (cancel a
        // task, reorder providers, forget a host) - #composerNotice lives
        // inside #composer, which is hidden entirely on a Worker's dashboard
        // (no /api/chat there), so those errors would otherwise be invisible
        // exactly like the pairing-request errors were before that got fixed.
        // #globalNotice sits in the page header instead, outside any
        // role-conditional element, and auto-hides after a few seconds so a
        // stale error doesn't linger forever once the operator's moved on.
        let globalNoticeTimer = null;
        function showGlobalNotice(message, isError = false) {
          const box = $('globalNotice');
          box.textContent = message;
          box.className = `notice show ${isError ? 'bad' : ''}`;
          clearTimeout(globalNoticeTimer);
          globalNoticeTimer = setTimeout(() => { box.className = 'notice'; }, 8000);
        }

        // En körning i taget i delegera-vyn: att kunna avfyra en andra
        // ---- Persistent uppdragshistorik (rehydrering + poll) ----
        // Stegvisningen levde tidigare BARA i flikens JS-minne: en omladdning
        // (t.ex. musknapp bak/fram) eller en appomstart visade på sin höjd
        // slutsvaret, och en pågående körning såg ut som ingenting. Nu byggs
        // delegera-vyn upp från nodens egen logg (GET /api/assignment-log)
        // och pollas var 3:e sekund så länge något inlägg är Running.
        function assignmentLogToMessages(entries) {
          const msgs = [];
          [...entries].reverse().forEach(e => {
            msgs.push({ role: 'user', content: e.Prompt, isAssignment: true, fromLog: true });
            msgs.push({
              role: 'assistant', isAssignment: true, fromLog: true,
              state: e.State,
              workerName: e.WorkerName || '',
              steps: e.Steps || [],
              content: e.FinalAnswer || '',
              previewPath: e.PreviewPath || null,
              artifactPath: e.ArtifactPath || null,
              startedAt: e.State === 'Running' && e.StartedAt ? new Date(e.StartedAt).getTime() : null
            });
          });
          return msgs;
        }

        // Fliken äger vyn när en egen ström pågår ELLER när den innehåller
        // lokalt skapade bubblor (planer, nyss körda uppdrag) - loggen får
        // aldrig skriva över dem; den tar bara över i en "tom" flik.
        function assignmentViewOwnedLocally() {
          // adoptFromLog = strömmen tappades men körningen lever på noden;
          // en sådan bubbla ÄGER inte vyn - loggen får ta över och visa
          // sanningen (inklusive slutresultatet).
          return state.assignmentStreamLive
            || state.assignmentMessages.some(m => m.role !== 'user' && !m.fromLog && !m.adoptFromLog);
        }

        // ---- Projekt-vyn: portfölj + rollback ----
        // Endpointsen finns bara där assignment-motorn kör lokalt (Worker/
        // Launcher) - andra roller får en ärlig notis i stället för en tom vy.
        function showProjectsNotice(message, isError = false) {
          const box = $('projectsNotice');
          box.textContent = message;
          box.className = message ? `notice show ${isError ? 'bad' : ''}` : 'notice';
        }

        async function loadProjects() {
          const grid = $('projectsGrid');
          if (!grid) return;
          let projects;
          try {
            projects = await fetchJson('/api/projects') ?? [];
          } catch {
            grid.innerHTML = '';
            showProjectsNotice('Projektvyn finns på noder som bygger lokalt (Worker eller skrivbordsappen).', true);
            return;
          }
          showProjectsNotice('');
          if (!projects.length) {
            grid.innerHTML = '<div class="empty">Inga projekt ännu - bygg något i Delegera-vyn så dyker det upp här.</div>';
            return;
          }
          grid.innerHTML = projects.map(p => `
            <article class="project-card" data-project="${esc(p.Rel)}">
              <div class="project-card-head">
                <strong>${esc(p.Name)}</strong>
                <span class="pill">${esc(p.Engine !== 'unknown' ? p.Engine : p.Kind)}</span>
              </div>
              <div class="small">${p.Files} filer · ${esc(new Date(p.LastModified).toLocaleString('sv-SE'))}</div>
              <div class="small">${p.LatestClean === true ? 'Senaste kvalitet: godkänd' : p.LatestClean === false ? 'Senaste kvalitet: anmärkningar' : 'Ingen kvalitetsdata ännu'}${p.Snapshots ? ` · ${p.Snapshots} versioner` : ''}</div>
              <div class="detail-actions">
                ${p.Playable ? `<a class="mini-btn" href="/api/preview/${p.Rel === '.' ? '' : esc(p.Rel) + '/'}index.html" target="_blank" rel="noopener">Spela</a>` : ''}
                <button class="mini-btn" data-proj-iterate="${esc(p.Rel)}">Vidareutveckla</button>
                <button class="mini-btn" data-proj-package="${esc(p.Rel)}">Packa</button>
                <button class="mini-btn" data-proj-folder="${esc(p.Rel)}">Mapp</button>
                ${p.Snapshots ? `<button class="mini-btn" data-proj-versions="${esc(p.Rel)}">Versioner</button>` : ''}
                ${p.Rel !== '.' ? `<button class="mini-btn" data-proj-delete="${esc(p.Rel)}">Radera</button>` : ''}
              </div>
              <div class="project-iterate hidden" data-iterate-for="${esc(p.Rel)}">
                <div class="iterate-presets">
                  <button class="mini-btn" data-iterate-preset="Gör spelet svårare: snabbare tempo, fler och tuffare fiender, och hårdare mål">Svårare</button>
                  <button class="mini-btn" data-iterate-preset="Lägg till en ny bana eller nivå med ny utmaning">Ny bana</button>
                  <button class="mini-btn" data-iterate-preset="Lägg till ljudeffekter och bakgrundsmusik som passar spelet">Ljud &amp; musik</button>
                  <button class="mini-btn" data-iterate-preset="Snygga till grafiken med bättre sprites, animationer och partikeleffekter">Snyggare grafik</button>
                </div>
                <div class="iterate-row">
                  <input type="text" class="iterate-input" data-iterate-input="${esc(p.Rel)}" placeholder="...eller beskriv en ändring själv">
                  <button class="mini-btn" data-iterate-run="${esc(p.Rel)}" data-iterate-name="${esc(p.Name)}">Kör</button>
                </div>
                <div class="small iterate-hint">Startar ett nytt bygge som fortsätter på DET HÄR projektet (kontinuitetsbrief). Kräver Agentläge på noden.</div>
              </div>
              <div class="project-versions hidden" data-versions-for="${esc(p.Rel)}"></div>
            </article>`).join('');
          wireProjectCards();
        }

        // B2: vidareutveckla-knappen kör ett uppföljningsuppdrag mot ETT
        // specifikt projekt. projectRel skickas till /api/assignment som
        // resolvar den till projektmappen -> kontinuitetsbriefen kör mot RÄTT
        // projekt (inte bara det senast aktiva). Strömmen visas i Delegera-vyn
        // via samma runPlanSubtask som planer/team använder.
        async function iterateProject(rel, tweak) {
          tweak = (tweak || '').trim();
          if (!rel || !tweak) return;
          const card = document.querySelector(`.project-card[data-project="${CSS.escape(rel)}"]`);
          const name = card?.querySelector('strong')?.textContent || rel;
          const box = document.querySelector(`[data-iterate-for="${CSS.escape(rel)}"]`);
          if (box) box.classList.add('hidden');
          switchView('delegate');
          const subtask = { title: `Vidareutveckla: ${name}`, description: tweak };
          await runPlanSubtask(subtask, tweak, $('workerSelect')?.value || null, null, rel);
        }

        function wireProjectCards() {
          document.querySelectorAll('[data-proj-iterate]').forEach(btn => btn.onclick = () => {
            const box = document.querySelector(`[data-iterate-for="${CSS.escape(btn.dataset.projIterate)}"]`);
            if (box) box.classList.toggle('hidden');
          });
          // Snabbval FYLLER faltet (fokus) - ett bygge startar aldrig pa ett
          // rakt klick, bara nar operatoren bekraftar med Kor/Enter (bygget
          // kostar tokens). Fritexten later en beskriva andringen sjalv.
          document.querySelectorAll('[data-iterate-preset]').forEach(btn => btn.onclick = () => {
            const rel = btn.closest('.project-card')?.dataset.project;
            const input = document.querySelector(`[data-iterate-input="${CSS.escape(rel)}"]`);
            if (input) { input.value = btn.dataset.iteratePreset; input.focus(); }
          });
          document.querySelectorAll('[data-iterate-run]').forEach(btn => btn.onclick = () => {
            const input = document.querySelector(`[data-iterate-input="${CSS.escape(btn.dataset.iterateRun)}"]`);
            iterateProject(btn.dataset.iterateRun, input?.value || '');
          });
          document.querySelectorAll('[data-iterate-input]').forEach(inp => inp.onkeydown = e => {
            if (e.key === 'Enter') { e.preventDefault(); iterateProject(inp.dataset.iterateInput, inp.value || ''); }
          });
          document.querySelectorAll('[data-proj-package]').forEach(btn => btn.onclick = async () => {
            btn.disabled = true;
            try {
              const r = await fetchJson('/api/projects/package', {
                method: 'POST', headers: { 'content-type': 'application/json' },
                body: JSON.stringify({ rel: btn.dataset.projPackage })
              });
              showProjectsNotice(r.Success ? `Paketerat: ${r.PackagePath || r.Output}` : r.Output, !r.Success);
            } catch (error) { showProjectsNotice(error.message, true); }
            finally { btn.disabled = false; }
          });
          document.querySelectorAll('[data-proj-folder]').forEach(btn => btn.onclick = async () => {
            try {
              await fetchJson('/api/projects/open-folder', {
                method: 'POST', headers: { 'content-type': 'application/json' },
                body: JSON.stringify({ rel: btn.dataset.projFolder })
              });
            } catch (error) { showProjectsNotice(error.message, true); }
          });
          document.querySelectorAll('[data-proj-delete]').forEach(btn => btn.onclick = async () => {
            if (!window.confirm('Radera projektet permanent? Versionssnapshots blir kvar men projektmappen tas bort.')) return;
            try {
              await fetchJson('/api/projects/delete', {
                method: 'POST', headers: { 'content-type': 'application/json' },
                body: JSON.stringify({ rel: btn.dataset.projDelete })
              });
              loadProjects();
            } catch (error) { showProjectsNotice(error.message, true); }
          });
          document.querySelectorAll('[data-proj-versions]').forEach(btn => btn.onclick = async () => {
            const rel = btn.dataset.projVersions;
            const box = document.querySelector(`[data-versions-for="${CSS.escape(rel)}"]`);
            if (!box) return;
            if (!box.classList.contains('hidden')) { box.classList.add('hidden'); return; }
            try {
              const versions = await fetchJson(`/api/projects/snapshots?rel=${encodeURIComponent(rel)}`) ?? [];
              box.innerHTML = versions.map(v => `
                <div class="project-version-row">
                  <span class="small">${esc(new Date(v.TakenAt).toLocaleString('sv-SE'))} · ${esc(v.Label)}${v.Clean ? ' · godkänd' : ''}</span>
                  <button class="mini-btn" data-restore-rel="${esc(rel)}" data-restore-file="${esc(v.File)}">Återställ</button>
                </div>`).join('') || '<span class="small">Inga versioner.</span>';
              box.classList.remove('hidden');
              box.querySelectorAll('[data-restore-rel]').forEach(rb => rb.onclick = async () => {
                if (!window.confirm('Återställa projektet till den här versionen? Nuvarande filer ersätts.')) return;
                try {
                  const r = await fetchJson('/api/projects/restore', {
                    method: 'POST', headers: { 'content-type': 'application/json' },
                    body: JSON.stringify({ rel: rb.dataset.restoreRel, file: rb.dataset.restoreFile })
                  });
                  showProjectsNotice(r.output || 'Återställt.');
                  loadProjects();
                } catch (error) { showProjectsNotice(error.message, true); }
              });
            } catch (error) { showProjectsNotice(error.message, true); }
          });
        }

        // ---- Benchmark (självmätning i Inställningar) ----
        // Endpointen finns bara där assignment-motorn kör lokalt (Worker/
        // Launcher) - andra roller får ett tyst 404 och sektionen står tom.
        let benchmarkTimer = null;
        async function loadBenchmark() {
          const box = $('benchmarkHistory');
          if (!box) return;
          let data;
          try { data = await fetchJson('/api/benchmark'); } catch { return; }
          const btn = $('benchmarkRunBtn');
          if (btn) btn.disabled = !!data.Running;
          $('benchmarkStatus').textContent = data.Running
            ? (data.Progress || []).slice(-3).join('\n')
            : ((data.Progress || []).slice(-1)[0] || '');
          const rows = (data.History || []).slice(-8).reverse().map(r =>
            `<tr><td>v${esc(r.Version)}</td><td>${esc(new Date(r.StartedAt).toLocaleDateString('sv-SE'))}</td><td>${(r.Results || []).length} promptar</td><td><strong>${r.TotalScore}</strong>/100</td></tr>`).join('');
          box.innerHTML = rows
            ? `<table class="bench-table"><tr><th>Version</th><th>Datum</th><th>Omfång</th><th>Poäng</th></tr>${rows}</table>`
            : '<span class="small">Inga benchmarkkörningar ännu.</span>';
          clearTimeout(benchmarkTimer);
          if (data.Running) benchmarkTimer = setTimeout(loadBenchmark, 5000);
        }

        async function runBenchmark() {
          try {
            await fetchJson('/api/benchmark/run', {
              method: 'POST',
              headers: { 'content-type': 'application/json' },
              body: JSON.stringify({ count: Number($('benchmarkCount').value) || 3 })
            });
            loadBenchmark();
          } catch (error) {
            $('benchmarkStatus').textContent = error.message;
          }
        }

        let assignmentLogTimer = null;
        async function loadAssignmentLog() {
          if (assignmentViewOwnedLocally()) return;
          let entries;
          try { entries = await fetchJson('/api/assignment-log') ?? []; }
          catch { return; /* äldre nod utan endpointen - visa som förut */ }
          state.assignmentMessages = assignmentLogToMessages(entries);
          renderMessages();
          clearTimeout(assignmentLogTimer);
          if (entries.some(e => e.State === 'Running'))
            assignmentLogTimer = setTimeout(loadAssignmentLog, 3000);
        }

        // chatt/uppdrag ovanpå en pågående (utan kö eller varning) gjorde
        // bara läget förvirrande. Avbryt (✕) eller invänta klart för nästa.
        function delegateBusy() {
          const chatLive = (state.messages || []).some(m =>
            ['Running', 'Dispatched', 'Queued', 'Pending'].includes(m.state));
          const localLive = state.assignmentMessages.some(m =>
            (m.isPlan && ['planning', 'running'].includes(m.planState)) ||
            (!m.isPlan && m.state === 'Running'));
          return chatLive || localLive;
        }

        // Overseer-växeln: när flera Hosts är announcade väljer operatören
        // här vilken Host nya sessioner skapas och körs på. Valet sparas som
        // cookie och läses av sessionsproxyn på servern - alla befintliga
        // sessionsanrop följer med automatiskt, utan fler JS-ändringar.
        function renderSessionHostSelect() {
          const sel = $('sessionHostSelect');
          if (!sel) return;
          const hosts = state.overseerHosts || [];
          if (hosts.length < 2) { sel.classList.add('hidden'); return; }
          const current = decodeURIComponent((document.cookie.match(/(?:^|; )ailocalSessionHost=([^;]*)/) || [])[1] || '');
          const html = hosts.map(h => {
            const ep = h.endpoint || h.Endpoint || '';
            const name = h.name || h.Name || ep;
            return `<option value="${esc(ep)}" ${current === ep ? 'selected' : ''}>Sessioner körs på: ${esc(name)}</option>`;
          }).join('');
          if (sel._html !== html) { sel.innerHTML = html; sel._html = html; }
          sel.classList.remove('hidden');
          sel.onchange = () => {
            document.cookie = 'ailocalSessionHost=' + encodeURIComponent(sel.value) + '; path=/; max-age=31536000';
            refresh();
          };
        }

        function syncComposerLock() {
          const btn = $('sendBtn');
          if (!btn) return;
          // Agentläges-uppdrag KÖAS på noden sedan uppdragskön kom - då är
          // det fritt att skicka fler ("ställ tre spel på kö"). Chatt- och
          // plankörningar saknar kö och behåller spärren.
          const busy = delegateBusy() && !$('assignmentMode')?.checked;
          btn.disabled = busy;
          btn.title = busy ? 'En körning pågår - avbryt den (✕), vänta, eller slå på Agentläge (köas)' : 'Skicka';
        }

        async function sendMessage() {
          const prompt = $('prompt').value.trim();
          if (!prompt) return;
          if (delegateBusy() && !$('assignmentMode').checked) {
            showComposerNotice('En körning pågår redan - avbryt den (✕), vänta, eller slå på Agentläge så köas uppdraget på noden.', true);
            return;
          }
          if ($('assignmentMode').checked) {
            // Team-läge kör målet som ETT uppdrag där NODEN gör uppdelningen
            // (arkitekt -> parallella worktree-agenter -> merge) - planeraren
            // hoppas över, den skulle bara duplicera arkitektens jobb.
            if ($('teamMode')?.checked) {
              await runTeamAssignment(prompt);
              return;
            }
            await planAndRunGoal(prompt);
            return;
          }
          const providerOrder = activeProviderOrder();
          const parallelism = Number($('parallelism').value) || 1;
          const model = $('modelSelect').value || null;
          $('sendBtn').disabled = true;
          $('composerNotice').className = 'notice';
          try {
            await fetchJson('/api/chat', {
              method: 'POST',
              headers: { 'content-type': 'application/json' },
              body: JSON.stringify({ prompt, parallelism, providerOrder, modelHint: model })
            });
            $('prompt').value = '';
            await refresh();
          } catch (error) {
            showComposerNotice(error.message, true);
          } finally {
            syncComposerLock();
          }
        }

        // Maps an AgentStep's Kind to a short icon prefix for the transcript.
        // Returns PLAIN text - renderMessages() already runs esc() once over
        // the whole message body, so escaping here too would double-encode
        // entities (e.g. turn a literal "&amp;" from a tool's file contents
        // into "&amp;amp;").
        function stepLine(step) {
          // Plain-text symbols, not the SVG icon() helper - this returns
          // plain text (see the doc comment above), not HTML, so it can't
          // embed markup without breaking the single esc() pass renderMessages()
          // already does over the whole message body.
          const stepMarkers = {
            thinking: '…',
            tool_call: '>',
            tool_result: '✓',
            tool_error: '!',
            plan: '▸',
            awaiting_info: '?',
            done: '✓',
            error: '✗',
            cancelled: '×'
          };
          const marker = stepMarkers[step.Kind] ?? '·';
          return `${marker} ${step.Detail}`;
        }

        function firstLine(s) {
          const i = (s || '').indexOf('\n');
          return i < 0 ? (s || '') : s.slice(0, i);
        }

        // "verktyg({...json...})" -> { name, args } för kompakta verktygsrader.
        function parseToolCall(detail) {
          const i = (detail || '').indexOf('(');
          if (i <= 0) return { name: (detail || '').trim(), args: '' };
          let args = detail.slice(i + 1).trim();
          if (args.endsWith(')')) args = args.slice(0, -1);
          return { name: detail.slice(0, i).trim(), args };
        }

        // Agentens steg i Claude Code/Hermes-stil: resonemang som löpande
        // text, verktygsanrop som kompakta rader (namn + trunkerade
        // argument), resultat som dämpade enradare. 'done' hoppas över -
        // slutsvaret visas som meddelandets egen text, inte som ett steg.
        function stepRowsHtml(steps, running) {
          const rows = (steps || []).map(s => {
            const kind = s.Kind, d = s.Detail || '';
            if (kind === 'thinking' || kind === 'plan')
              return d.trim() ? `<div class="step-think">${esc(d)}</div>` : '';
            if (kind === 'done' || kind === 'awaiting_info' || kind === 'awaiting_approval')
              return '';
            if (kind === 'tool_call') {
              const t = parseToolCall(d);
              return `<div class="step-row"><span class="k">›</span><span class="step-tool-name">${esc(t.name)}</span>${t.args ? `<span class="step-tool-args">${esc(trunc(t.args, 140))}</span>` : ''}</div>`;
            }
            if (kind === 'tool_error')
              return `<div class="step-row step-error"><span class="k">!</span><span class="step-tool-args">${esc(trunc(firstLine(d), 160))}</span></div>`;
            if (kind === 'error' || kind === 'cancelled')
              return `<div class="step-row step-error"><span class="k">✗</span><span>${esc(trunc(d, 220))}</span></div>`;
            if (kind === 'contract' || kind === 'contract_status') {
              // Regissörens leveranskontrakt som checklista: före bygget som
              // "att leverera", efter uppföljningen med avbockade punkter.
              try {
                const c = JSON.parse(d);
                const unmet = new Set(c.unmet || []);
                const judged = kind === 'contract_status';
                const items = (c.criteria || []).map(item => {
                  const met = judged && !unmet.has(item);
                  const cls = judged ? (met ? 'met' : 'unmet') : '';
                  const mark = judged ? icon(met ? 'check' : 'x', 12) : icon('square', 12);
                  return `<div class="contract-row ${cls}">${mark}<span>${esc(item)}</span></div>`;
                }).join('');
                const title = judged ? 'Leveranskontraktet - regissörens avbockning' : 'Leveranskontraktet (att leverera)';
                return `<div class="contract-card"><div class="contract-title">${esc(title)}</div>${items}</div>`;
              } catch { return ''; }
            }
            return `<div class="step-result">${esc(trunc(firstLine(d), 150))}</div>`;
          }).join('');
          const tail = running
            ? `<div class="step-row"><span class="thinking-shimmer">${rows ? 'arbetar…' : 'startar…'}</span></div>`
            : '';
          return (rows || tail) ? `<div class="step-flow">${rows}${tail}</div>` : '';
        }

        // Renders an assignment (agent-mode) bubble with live context: a
        // ticking elapsed timer, which Worker it landed on, how many steps
        // have run, and the step-log kept separate from the final answer.
        // Fixes the old "bara working" experience where the only visible
        // signal was the static state word.
        function assignmentBubbleHtml(m) {
          const running = m.state === 'Running';
          const timer = m.startedAt
            ? `<span class="elapsed-timer" data-started="${m.startedAt}">${formatDuration(Date.now() - m.startedAt)}</span>`
            : '';
          const workerPill = m.workerName
            ? `<span class="pill">${icon('monitor')} ${esc(m.workerName)}</span>`
            : (running ? `<span class="small">väljer worker…</span>` : '');
          const stepCount = (m.steps || []).length;
          const stepMeta = running && stepCount
            ? `<span class="small">${stepCount} steg</span>`
            : (stepCount ? `<span class="small">${stepCount} steg</span>` : '');
          const statePill = m.state && m.state !== 'Running'
            ? `<span class="${m.state === 'Completed' ? 'good' : 'bad'}">${esc(m.state === 'Completed' ? 'Klar' : m.state === 'Failed' ? 'Misslyckades' : m.state)}</span>`
            : `<span class="small">${running ? 'arbetar…' : ''}</span>`;

          const stepsHtml = stepRowsHtml(m.steps, running);

          const answer = (!running && m.content && m.content.trim())
            ? `<div class="msg-text">${esc(m.content)}</div>`
            : '';

          const playLink = m.previewPath
            ? `<a class="mini-btn" href="${esc(m.previewPath)}" target="_blank" rel="noopener">Öppna resultatet i webbläsaren</a>` : '';
          const artifactLink = m.artifactPath
            ? `<a class="mini-btn" href="${esc(m.artifactPath)}">Ladda ner spelet (exe)</a>` : '';
          const preview = (!running && (playLink || artifactLink))
            ? `<div class="msg-preview">${playLink}${artifactLink}</div>`
            : '';

          const milestone = (running && m.milestone && m.milestone.id)
            ? `<div class="milestone-card">
                <strong>Milstolpe - regissörens leveranskontrakt</strong>
                <div class="msg-text">${esc(m.milestone.contract || '')}</div>
                <div class="detail-actions">
                  <button class="mini-btn" data-milestone-approve="${esc(m.milestone.id)}">Godkänn och bygg</button>
                  <button class="mini-btn" data-milestone-adjust="${esc(m.milestone.id)}">Justera…</button>
                </div>
              </div>`
            : '';

          return `
          <article class="message assistant assignment">
            <div class="message-meta">
              <strong>AiLocal</strong>
              <span class="pill">Assignment</span>
              ${m.subtaskTitle ? `<span class="pill">${esc(m.subtaskTitle)}</span>` : ''}
              ${workerPill}
              ${timer}
              ${stepMeta}
              ${statePill}
            </div>
            ${stepsHtml}
            ${milestone}
            ${answer}
            ${preview}
            ${!running && m.content ? msgActionsHtml : ''}
          </article>`;
        }

        // Team-läge: hela målet skickas som ETT uppdrag med teamSize satt -
        // noden kör arkitekt -> parallella worktree-agenter -> merge själv.
        async function runTeamAssignment(goalText) {
          $('prompt').value = '';
          $('composerNotice').className = 'notice';
          const teamSize = Math.min(4, Math.max(2, Number($('parallelism').value) || 2));
          state.assignmentMessages.push({ role: 'user', content: goalText, isAssignment: true });
          try {
            await runPlanSubtask({ title: `Team-bygge (${teamSize} agenter)` }, goalText, $('workerSelect')?.value || null, teamSize);
          } finally {
            renderMessages();
            syncComposerLock();
          }
        }

        // Assignment mode plans before it runs: the goal gets broken into a
        // reviewable list of subtasks (POST /api/goal-plan) instead of being
        // handed straight to one Worker as one big vague instruction. The
        // operator sees the plan and can drop steps before anything executes,
        // but there's no per-step approval after that "Kör planen" click - it
        // runs to completion (or the first failure) on its own.
        async function planAndRunGoal(goalText) {
          $('sendBtn').disabled = true;
          $('composerNotice').className = 'notice';
          $('prompt').value = '';

          const planMsg = { id: state.nextPlanId++, role: 'assistant', isPlan: true, planState: 'planning', subtasks: [] };
          state.assignmentMessages.push({ role: 'user', content: goalText, isAssignment: true }, planMsg);
          renderMessages();

          try {
            const result = await fetchJson('/api/goal-plan', {
              method: 'POST',
              headers: { 'content-type': 'application/json' },
              body: JSON.stringify({ goal: goalText })
            });
            planMsg.planState = 'reviewing';
            planMsg.subtasks = result.subtasks.map(s => ({ ...s, included: true, status: 'pending' }));
          } catch (error) {
            // Planeringen kan fallera (t.ex. en svag lokal modell som inte
            // klarar strikt JSON, eller ingen provider) - fall tillbaka till
            // att köra målet som ETT uppdrag i stället för att stanna helt.
            // Agenten planerar ändå själv (DESIGN.md-steget i systemprompten).
            planMsg.planState = 'reviewing';
            planMsg.planNote = 'Planeringen misslyckades (' + error.message + ') - målet körs som ett enda uppdrag i stället.';
            planMsg.subtasks = [{ title: 'Uppdrag', description: goalText, independent: false, included: true, status: 'pending' }];
          } finally {
            renderMessages();
            syncComposerLock();
          }
        }

        async function cancelPlan(planMsg) {
          planMsg.planState = 'cancelled';
          renderMessages();
        }

        async function runPlan(planMsg) {
          planMsg.planState = 'running';
          renderMessages();

          const included = planMsg.subtasks.filter(s => s.included);
          // independent=false (the default) shares a workspace with earlier
          // steps, so it must run after them on the SAME Worker - independent
          // ones don't need to see anyone else's files, so they're safe to
          // fire off concurrently on whichever Worker is free. See GoalPlanner.
          const sequential = included.filter(s => !s.independent);
          const independent = included.filter(s => s.independent);

          // Operatören kan välja worker i composern - då hamnar FILERNA på
          // den datorn (rapporterat: "filerna hamnade inte på host-datorn").
          // Auto (tomt) låter Hosten välja som förut.
          let pinnedWorkerId = $('workerSelect')?.value || null;
          let stoppedEarly = false;
          const completedSummaries = [];

          for (const subtask of sequential) {
            if (stoppedEarly) { subtask.status = 'skipped'; continue; }
            subtask.status = 'running';
            renderMessages();

            const contextPrefix = completedSummaries.length
              ? `Context - already completed by an earlier step on this same computer:\n${completedSummaries.join('\n')}\n\nYour task now:\n`
              : '';
            const outcome = await runPlanSubtask(subtask, contextPrefix + subtask.description, pinnedWorkerId);
            pinnedWorkerId = outcome.workerId ?? pinnedWorkerId;
            subtask.status = outcome.success ? 'done' : 'failed';
            if (outcome.success) {
              completedSummaries.push(`- ${subtask.title}: ${trunc(outcome.summary, 200)}`);
            } else {
              stoppedEarly = true;
            }
            renderMessages();
          }

          if (independent.length) {
            independent.forEach(s => { s.status = 'running'; });
            renderMessages();
            await Promise.all(independent.map(async subtask => {
              const outcome = await runPlanSubtask(subtask, subtask.description, $('workerSelect')?.value || null);
              subtask.status = outcome.success ? 'done' : 'failed';
              renderMessages();
            }));
          }

          planMsg.planState = stoppedEarly ? 'stopped' : 'done';
          renderMessages();
        }

        // One subtask's execution: same SSE-by-hand approach as before
        // (EventSource can't POST a body), plus reading the new leading
        // "worker" frame /api/assignment now sends first, so a sequential
        // group's later steps can pin to the same Worker the first one landed
        // on (see HostRole's /api/assignment WorkerId handling).
        async function runPlanSubtask(subtask, assignmentText, workerId, teamSize, projectRel) {
          const stepMsg = { role: 'assistant', content: '', state: 'Running', isAssignment: true, subtaskTitle: subtask.title, startedAt: Date.now(), steps: [] };
          state.assignmentMessages.push(stepMsg);
          state.assignmentStreamLive = true;
          renderMessages();

          let workerIdUsed = workerId;
          let success = false;
          let summary = '';

          const addStep = step => { stepMsg.milestone = null; stepMsg.steps.push(step); renderMessages(); };

          try {
            const headers = { 'content-type': 'application/json' };
            if (state.authToken) headers[AUTH_HEADER] = state.authToken;
            const response = await fetch('/api/assignment', {
              method: 'POST',
              headers,
              body: JSON.stringify({ assignment: assignmentText, workerId, teamSize: teamSize || null, projectRel: projectRel || null })
            });

            if (!response.ok || !response.body) {
              let detail = `HTTP ${response.status}`;
              try {
                const errBody = await response.json();
                detail = errBody?.detail || errBody?.error || errBody?.title || detail;
              } catch { /* body wasn't JSON - keep the status-code message */ }
              throw new Error(detail);
            }

            const reader = response.body.getReader();
            const decoder = new TextDecoder();
            let buffer = '';
            for (;;) {
              const { done, value } = await reader.read();
              if (done) break;
              buffer += decoder.decode(value, { stream: true });
              let sepIndex;
              while ((sepIndex = buffer.indexOf('\n\n')) !== -1) {
                const frame = buffer.slice(0, sepIndex);
                buffer = buffer.slice(sepIndex + 2);
                const dataLine = frame.split('\n').find(l => l.startsWith('data:'));
                if (!dataLine) continue; // keepalive-pingar (": ping") saknar data-rad
                let payload;
                try { payload = JSON.parse(dataLine.slice(5).trim()); }
                catch { continue; } // trasig frame får inte fälla hela strömmen
                if (!payload) continue;
                if (payload.worker) {
                  workerIdUsed = payload.worker.id;
                  stepMsg.workerName = payload.worker.name;
                  renderMessages();
                } else if (payload.step) {
                  // Milstolpen renderas som ett godkännandekort i bubblan i
                  // stället för en rå JSON-stegrad; nästa riktiga steg (efter
                  // beslutet) rensar kortet via addStep.
                  if (payload.step.Kind === 'awaiting_milestone') {
                    try { stepMsg.milestone = JSON.parse(payload.step.Detail); } catch { stepMsg.milestone = null; }
                    renderMessages();
                  } else {
                    addStep(payload.step);
                  }
                } else if (payload.final) {
                  success = !!payload.final.Success;
                  summary = payload.final.FinalAnswer || '';
                  // Förhandsvisnings-/artefaktvägar följer med final-framen -
                  // för klusterkörningar redan omskrivna av Hosten till dess
                  // proxy (/api/nodes/{id}/...), så länkarna fungerar överallt.
                  stepMsg.previewPath = payload.previewPath || null;
                  stepMsg.artifactPath = payload.artifactPath || null;
                  stepMsg.state = success ? 'Completed' : 'Failed';
                  // The final answer lives in content; the running step-log
                  // stays in steps[]. If the run produced no steps at all
                  // (a plain chat reply), surface the answer as the body.
                  stepMsg.content = summary || (success ? '(inget svar)' : '(misslyckades)');
                  renderMessages();
                }
              }
            }
          } catch (error) {
            if (error?.name === 'AbortError') {
              stepMsg.state = 'Failed';
              stepMsg.content = `✗ ${error.message}`;
              summary = error.message;
            } else {
              // Ett nätverksglapp dödar bara STRÖMMEN - bygget kör vidare på
              // noden (rapport: "network error" trots att uppdraget levde).
              // Släpp ägarskapet till uppdragsloggen: pollen tar över vyn och
              // slutresultatet kommer därifrån i stället för ett falskt fel.
              stepMsg.adoptFromLog = true;
              stepMsg.steps.push({ Kind: 'thinking', Detail: 'Anslutningen tappades - körningen fortsätter på noden och följs via uppdragsloggen i stället.' });
              summary = 'anslutningen tappades - körningen följs via uppdragsloggen';
            }
            renderMessages();
          } finally {
            state.assignmentStreamLive = false;
            if (stepMsg.adoptFromLog) loadAssignmentLog();
          }

          return { success, summary, workerId: workerIdUsed };
        }

        async function quickstart() {
          const btn = $('quickstartBtn');
          btn.disabled = true;
          $('launchResult').textContent = 'Startar Host + Worker...';
          try {
            const data = await fetchJson('/api/quickstart', { method: 'POST' });
            if (!data.started) {
              $('launchResult').textContent = data.error || 'Kunde inte starta klustret.';
              return;
            }
            $('launchResult').innerHTML =
              `Startade Host på <span class="mono">${esc(data.hostEndpoint)}</span>${data.worker ? ' + Worker' : ''}`;
            window.location.href = withCurrentTheme(data.hostEndpoint);
          } catch (error) {
            $('launchResult').textContent = error.message;
          } finally {
            btn.disabled = false;
          }
        }

        async function launchRole(role, roleValue) {
          const hostEndpoint = $('hostInput').value.trim();
          const clusterToken = $('launchClusterToken').value.trim() || null;
          $('launchResult').textContent = `Startar ${role}...`;
          try {
            const data = await fetchJson('/api/launch', {
              method: 'POST',
              headers: { 'content-type': 'application/json' },
              body: JSON.stringify({ role: Number(roleValue), hostEndpoint, clusterToken })
            });
            if (!data?.started) {
              $('launchResult').textContent = data?.error || `Kunde inte starta ${role}.`;
              return;
            }
            const verb = data.reused ? 'Kör redan' : 'Startade';
            $('launchResult').innerHTML = `${verb} ${role} på <span class="mono">${esc(data.endpoint)}</span>`;
            if (role !== 'Worker') window.location.href = withCurrentTheme(data.endpoint);
          } catch (error) {
            $('launchResult').textContent = error.message;
          }
        }

        document.querySelectorAll('.role-btn').forEach(btn => {
          btn.onclick = () => launchRole(btn.dataset.role, btn.dataset.roleValue);
        });
        document.querySelectorAll('[data-view]').forEach(button => {
          button.onclick = () => switchView(button.dataset.view);
        });
        $('sendBtn').onclick = sendMessage;
        $('quickstartBtn').onclick = quickstart;
        $('saveProviders').onclick = saveProviders;
        $('refreshBtn').onclick = refresh;
        $('updateAllNodesBtn').onclick = updateAllNodes;
        $('topologyRefresh').onclick = refresh;
        $('settingsBtn').onclick = () => openSettings(null);
        $('configureNode').onclick = () => openSettings(state.selectedNodeId);
        $('saveSettings').onclick = saveSettings;
        $('fetchOpenRouterModels').onclick = fetchOpenRouterModels;
        $('toggleTokenVisibility').onclick = toggleTokenVisibility;
        $('copyClusterToken').onclick = copyClusterToken;
        $('regenerateClusterToken').onclick = regenerateClusterToken;
        $('toggleOperatorTokenVisibility').onclick = toggleOperatorTokenVisibility;
        $('copyOperatorToken').onclick = copyOperatorToken;
        $('regenerateOperatorToken').onclick = regenerateOperatorToken;
        $('scheduleRefresh').onclick = refresh;
        $('scheduleCreate').onclick = createSchedule;
        $('closeSettings').onclick = closeSettingsDialog;
        $('cancelSettings').onclick = closeSettingsDialog;
        $('checkUpdateBtn').onclick = checkUpdateInSettings;
        $('applyUpdateBtn').onclick = applyUpdate;
        document.querySelectorAll('[data-settings-cat]').forEach(button => {
          button.onclick = () => switchSettingsCategory(button.dataset.settingsCat);
        });
        $('settingThemeSelect').onchange = () => {
          const value = $('settingThemeSelect').value;
          if (value === 'dark' || value === 'light') applyTheme(value);
        };
        $('settingsDialog').addEventListener('click', event => {
          if (event.target === $('settingsDialog')) closeSettingsDialog();
        });
        $('settingsDialog').addEventListener('close', refresh);
        // Enter skickar, Shift+Enter radbryter - samma tangentmodell som
        // Hermes/Claude Code. Ctrl+Enter fungerar fortfarande av gammal vana.
        $('prompt').addEventListener('keydown', event => {
          if (event.key === 'Enter' && !event.shiftKey) { event.preventDefault(); sendMessage(); }
        });
        window.addEventListener('resize', () => {
          if (state.activeView === 'network') renderTopology();
        });

        $('newSessionToggleBtn').onclick = () => toggleNewSessionForm();
        $('newSessionCancelBtn').onclick = () => toggleNewSessionForm(false);
        $('newSessionCreateBtn').onclick = createSession;
        $('browseNewSessionFolder').onclick = () => pickFolder('newSessionFolderPath', 'newSessionNotice');
        $('browseWorkspacePath').onclick = () => pickFolder('settingWorkspacePath', 'settingsNotice');
        $('newSessionFolderPath').addEventListener('keydown', event => {
          if (event.key === 'Enter') createSession();
        });
        $('newSessionTitle').addEventListener('keydown', event => {
          if (event.key === 'Enter') createSession();
        });
        $('sessionSearchInput').addEventListener('input', event => {
          state.sessionSearch = event.target.value;
          renderSessions();
        });
        $('sessionSendBtn').onclick = sendSessionMessage;
        $('sessionCancelBtn').onclick = cancelSessionRun;
        $('sessionPinBtn').onclick = () => { if (state.activeSessionId) toggleSessionPin(state.activeSessionId); };
        $('sessionDeleteBtn').onclick = () => { if (state.activeSessionId) deleteSession(state.activeSessionId); };
        $('sessionPrompt').addEventListener('keydown', event => {
          if (event.key === 'Enter' && !event.shiftKey) { event.preventDefault(); sendSessionMessage(); }
        });

        // I WebView2-skalet: aktivera fönsterknapparna och markera dokumentet
        // så CSS:en visar dem (vanlig webbläsare har egna fönsterkontroller).
        if (window.chrome?.webview) {
          document.body.classList.add('desktop-shell');
          document.querySelectorAll('[data-win]').forEach(btn => {
            btn.onclick = () => window.chrome.webview.postMessage('win:' + btn.dataset.win);
          });
        }

        $('newSessionTopBtn').onclick = () => toggleNewSessionForm(true);
        window.addEventListener('keydown', event => {
          if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'n') {
            event.preventDefault();
            toggleNewSessionForm(true);
          }
        });

        // Sidopanels-toggle (Hermes-stil): läget överlever omladdning.
        function applySidebarCollapsed(collapsed) {
          $('appShell').classList.toggle('sidebar-collapsed', collapsed);
          try { localStorage.setItem('ailocal-sidebar-collapsed', collapsed ? '1' : ''); } catch {}
        }
        $('sidebarToggleBtn').onclick = () => {
          applySidebarCollapsed(!$('appShell').classList.contains('sidebar-collapsed'));
        };
        try {
          if (localStorage.getItem('ailocal-sidebar-collapsed') === '1') applySidebarCollapsed(true);
        } catch {}

        // Filbilagor (skal): valda filer visas som chips i composern men
        // skickas inte med ännu - flödet finns så designen kan utvärderas
        // innan uppladdning/inbäddning byggs på riktigt.
        const attachments = { delegate: [], session: [] };
        function renderAttachChips(kind, chipsElId) {
          $(chipsElId).innerHTML = attachments[kind].map((f, i) => `
            <span class="attach-chip">${icon('folder', 12)} ${esc(f.name)}
              <button type="button" data-detach="${kind}:${i}" title="Ta bort">${icon('x', 11)}</button>
            </span>`).join('');
          document.querySelectorAll(`#${chipsElId} [data-detach]`).forEach(btn => {
            btn.onclick = () => {
              const [k, index] = btn.dataset.detach.split(':');
              attachments[k].splice(Number(index), 1);
              renderAttachChips(k, chipsElId);
            };
          });
        }
        function wireAttach(kind, btnId, inputId, chipsElId) {
          $(btnId).onclick = () => $(inputId).click();
          $(inputId).addEventListener('change', () => {
            attachments[kind].push(...[...$(inputId).files]);
            $(inputId).value = '';
            renderAttachChips(kind, chipsElId);
          });
        }
        wireAttach('delegate', 'delegateAttachBtn', 'delegateFileInput', 'delegateAttachChips');
        wireAttach('session', 'sessionAttachBtn', 'sessionFileInput', 'sessionAttachChips');

        // Custom dropdown för composer-verktygen: den STÄNGDA selecten gick
        // att tema, men den ÖPPNA popuplistan ritas av OS:et och förblir
        // Windows-grå oavsett CSS. Den nativa selecten göms och styrs från
        // en egen knapp+meny - selecten blir kvar i DOM som sanningskälla så
        // all befintlig värde-/change-wiring fortsätter fungera orörd.
        const toolMenuSyncs = [];
        function syncToolMenus() { toolMenuSyncs.forEach(sync => sync()); }
        function attachToolMenu(selectId) {
          const select = $(selectId);
          if (!select) return;
          select.classList.add('menu-hidden-select');

          const wrap = document.createElement('span');
          wrap.className = 'tool-menu-wrap';
          select.insertAdjacentElement('afterend', wrap);
          const btn = document.createElement('button');
          btn.type = 'button';
          btn.className = 'tool-menu-btn';
          btn.title = select.title;
          const menu = document.createElement('div');
          menu.className = 'tool-menu hidden';
          wrap.append(btn, menu);

          const syncLabel = () => {
            const opt = select.options[select.selectedIndex];
            btn.innerHTML = `<span>${esc(opt ? opt.textContent : '')}</span>${icon('chevron-down', 11)}`;
            btn.classList.toggle('mode-danger', select.classList.contains('mode-danger'));
          };
          const close = () => menu.classList.add('hidden');
          btn.onclick = event => {
            event.stopPropagation();
            document.querySelectorAll('.tool-menu').forEach(m => { if (m !== menu) m.classList.add('hidden'); });
            menu.innerHTML = [...select.options].map((opt, i) => `
              <button type="button" class="tool-menu-item ${opt.disabled ? 'disabled' : ''}" data-index="${i}" ${opt.disabled ? 'disabled' : ''}>
                <span class="tool-menu-check">${i === select.selectedIndex ? icon('check', 12) : ''}</span>${esc(opt.textContent)}
              </button>`).join('');
            menu.querySelectorAll('.tool-menu-item:not(.disabled)').forEach(item => {
              item.onclick = () => {
                select.selectedIndex = Number(item.dataset.index);
                select.dispatchEvent(new Event('change'));
                close();
                syncLabel();
              };
            });
            menu.classList.toggle('hidden');
          };
          document.addEventListener('click', close);
          window.addEventListener('keydown', event => { if (event.key === 'Escape') close(); });
          select.addEventListener('change', syncLabel);
          toolMenuSyncs.push(syncLabel);
          syncLabel();
        }

        // Behörighetsval vid terminalen (Claude-Code-stil "bypass permissions"):
        // speglar och ÄNDRAR nodens verkliga agentläge via PUT /api/settings -
        // Full åtkomst markeras rött, precis som bypass-läget i förlagan.
        function reflectAgentAccess(value) {
          ['sessionModeSelect', 'delegateModeSelect'].forEach(id => {
            const el = $(id);
            if (!el) return;
            el.value = value;
            el.classList.toggle('mode-danger', value === 'Full');
          });
          // Warn up front when building is impossible: a session with agent
          // access Off can only answer with text - it can never write files
          // or scaffold a project, so "bygg ett spel" would just return code.
          const off = $('sessionAgentOffNotice');
          if (off) off.style.display = (value === 'Off') ? 'block' : 'none';
          syncToolMenus();
        }
        async function loadAgentAccess() {
          try {
            const s = await fetchJson('/api/settings');
            reflectAgentAccess(s.agentAccess ?? 'Off');
          } catch { /* operatörsnyckel utan admin - selecten behåller default */ }
        }
        async function changeAgentAccess(value) {
          try {
            const data = await fetchJson('/api/settings', {
              method: 'PUT',
              headers: { 'content-type': 'application/json' },
              body: JSON.stringify({ agentAccess: value })
            });
            reflectAgentAccess(data.agentAccess ?? value);
          } catch (error) {
            showGlobalNotice(error.message, true);
            loadAgentAccess();
          }
        }
        ['sessionModeSelect', 'delegateModeSelect'].forEach(id => {
          const el = $(id);
          if (el) el.onchange = () => changeAgentAccess(el.value);
        });
        attachToolMenu('sessionModeSelect');
        attachToolMenu('delegateModeSelect');
        attachToolMenu('modelSelect');
        loadAgentAccess();

        function applyTheme(theme) {
          document.documentElement.setAttribute('data-theme', theme);
          try { localStorage.setItem('ailocal-theme', theme); } catch {}
          const btn = $('themeBtn');
          if (btn) btn.innerHTML = icon(theme === 'dark' ? 'sun' : 'moon');
        }
        function initTheme() {
          const requested = new URLSearchParams(window.location.search).get('theme');
          let theme = requested;
          try { theme = localStorage.getItem('ailocal-theme'); } catch {}
          if (requested === 'dark' || requested === 'light') theme = requested;
          // Dark by default now (no more OS-preference fallback) - light is
          // still one click away via themeBtn, same as before.
          if (theme !== 'dark' && theme !== 'light') theme = 'dark';
          applyTheme(theme);
        }
        function withCurrentTheme(endpoint) {
          const url = new URL(endpoint, window.location.href);
          url.searchParams.set('theme', document.documentElement.getAttribute('data-theme') || 'light');
          if (state.authToken) url.searchParams.set('token', state.authToken);
          return url.toString();
        }
        $('themeBtn').onclick = () => {
          const dark = document.documentElement.getAttribute('data-theme') === 'dark';
          applyTheme(dark ? 'light' : 'dark');
        };
        initTheme();

        function initAuth() {
          const fromQuery = new URLSearchParams(window.location.search).get('token');
          if (fromQuery) {
            try { localStorage.setItem('ailocal-token', fromQuery); } catch {}
          }
          try { state.authToken = localStorage.getItem('ailocal-token') || null; } catch { state.authToken = null; }
        }
        $('authBtn').onclick = () => {
          const value = prompt(
            'Anslutningsnyckel för fjärråtkomst (klustrets nyckel, eller en operatörsnyckel med begränsad åtkomst).\n' +
            'Behövs bara när du öppnar den här sidan från en annan dator - inte för lokal åtkomst.',
            state.authToken || '');
          if (value === null) return;
          try {
            if (value.trim()) localStorage.setItem('ailocal-token', value.trim());
            else localStorage.removeItem('ailocal-token');
          } catch {}
          state.authToken = value.trim() || null;
          refresh();
        };
        initAuth();
        initIcons();
        const refreshIsolationBtn = $('refreshIsolationBtn');
        if (refreshIsolationBtn) refreshIsolationBtn.onclick = () => refreshIsolation();
        const clearNoticesBtn = $('clearNoticesBtn');
        if (clearNoticesBtn) clearNoticesBtn.onclick = async () => {
          try { await fetch('/api/notices', { method: 'DELETE', headers: authHeaders() }); await renderNotices(); }
          catch (e) { showGlobalNotice(e.message, true); }
        };

        // Musknapp 4/5 (och Alt+pil) navigerade bak/fram: dashboarden är EN
        // sida utan historik, så "bakåt" laddade om appen och kastade bort
        // hela den levande vyn medan agenten fortfarande jobbade (rapporterat).
        // History-fällan äter navigeringen; preventDefault stoppar X-knapparna.
        history.pushState(null, '', location.href);
        window.addEventListener('popstate', () => history.pushState(null, '', location.href));
        window.addEventListener('mousedown', e => { if (e.button === 3 || e.button === 4) e.preventDefault(); });
        window.addEventListener('mouseup', e => { if (e.button === 3 || e.button === 4) e.preventDefault(); });

        const benchmarkRunBtn = $('benchmarkRunBtn');
        if (benchmarkRunBtn) benchmarkRunBtn.onclick = runBenchmark;
        const projectsRefreshBtn = $('projectsRefreshBtn');
        if (projectsRefreshBtn) projectsRefreshBtn.onclick = loadProjects;

        // Milstolpsknapparna byggs om vid varje render - eventdelegering på
        // dokumentet i stället för omkoppling per render.
        document.addEventListener('click', async e => {
          const approve = e.target.closest('[data-milestone-approve]');
          const adjust = e.target.closest('[data-milestone-adjust]');
          if (!approve && !adjust) return;
          const id = approve ? approve.dataset.milestoneApprove : adjust.dataset.milestoneAdjust;
          let note = null;
          if (adjust) {
            note = window.prompt('Hur vill du justera inriktningen? (en mening - bygget fortsätter med den)');
            if (note === null) return;
          }
          try {
            await fetchJson('/api/assignment/milestone', {
              method: 'POST',
              headers: { 'content-type': 'application/json' },
              body: JSON.stringify({ id, approve: !!approve, note })
            });
          } catch (error) {
            showComposerNotice(error.message, true);
          }
        });

        loadProviders();
        refresh();
        setInterval(refresh, 3000);
        loadAssignmentLog();
        loadBenchmark();
        refreshIsolation();
        renderRoles();
        renderNotices();
        renderBacklog();
        setInterval(renderNotices, 5000);
        setInterval(renderBacklog, 5000);

        // ---- Studio view: file explorer + code editor + terminal ----
        const studio = { root: null, openPath: null, termId: null, termTimer: null };

        function initStudio() {
          const pick = $('studioPickRoot');
          if (pick) pick.onclick = async () => {
            try {
              const data = await fetchJson('/api/dialogs/pick-folder', { method: 'POST', headers: { 'content-type': 'application/json' }, body: '{}' });
              if (data && data.path) { studio.root = data.path; renderStudio(); }
            } catch (e) { showGlobalNotice(e.message, true); }
          };
          const save = $('studioSaveBtn');
          if (save) save.onclick = () => saveStudioFile();
          const termStart = $('studioTermStart');
          if (termStart) termStart.onclick = () => startStudioTerminal();
          const termInput = $('studioTermInput');
          if (termInput) termInput.onkeydown = (e) => {
            if (e.key === 'Enter') {
              e.preventDefault();
              sendStudioTerminalInput(termInput.value);
              termInput.value = '';
            }
          };
          const tabFiles = $('studioTabFiles');
          if (tabFiles) tabFiles.onclick = () => studioTabSwitch('files');
          const tabBranches = $('studioTabBranches');
          if (tabBranches) tabBranches.onclick = () => studioTabSwitch('branches');
          const autoMergeBtn = $('studioAutoMergeBtn');
          if (autoMergeBtn) autoMergeBtn.onclick = async () => {
            try {
              await fetchJson('/api/isolation/auto-merge-all', { method: 'POST', body: '{}' });
              showGlobalNotice('Auto-merge kördes på alla aktiva branches.');
              await renderStudioBranches();
            } catch (e) { showGlobalNotice(e.message, true); }
          };
          const buildBtn = $('studioBuildBtn');
          if (buildBtn) buildBtn.onclick = () => runStudioCommand('build');
          const runBtn = $('studioRunBtn');
          if (runBtn) runBtn.onclick = () => runStudioCommand('run');
          const testBtn = $('studioTestBtn');
          if (testBtn) testBtn.onclick = () => runStudioCommand('test');
          const gameBtn = $('studioGameBtn');
          if (gameBtn) gameBtn.onclick = () => buildGame();

          const tabNewGame = $('studioTabNewGame');
          if (tabNewGame) tabNewGame.onclick = () => studioTabSwitch('newgame');
          const tabScreen = $('studioTabScreen');
          if (tabScreen) tabScreen.onclick = () => studioTabSwitch('screen');

          const newGameCreate = $('newGameCreateBtn');
          if (newGameCreate) newGameCreate.onclick = () => createNewGame();
          const screenRefresh = $('screenRefreshBtn');
          if (screenRefresh) screenRefresh.onclick = () => captureScreen();
          const screenImg = $('screenImg');
          if (screenImg) screenImg.onclick = (e) => clickOnScreen(e, screenImg);
        }

        function studioTabSwitch(tab) {
          ['files', 'branches', 'newgame', 'screen'].forEach(t => {
            const el = $('studioTab' + t.charAt(0).toUpperCase() + t.slice(1));
            if (el) el.classList.toggle('active', t === tab);
          });
          const filesPane = $('studioFilesPane');
          const branchesPane = $('studioBranchesPane');
          const ng = $('newGamePanel');
          const sp = $('screenPanel');
          if (filesPane) filesPane.parentElement.style.display = tab === 'files' ? '' : 'none';
          if (branchesPane) branchesPane.parentElement.style.display = tab === 'branches' ? '' : 'none';
          if (ng) ng.classList.toggle('hidden', tab !== 'newgame');
          if (sp) sp.classList.toggle('hidden', tab !== 'screen');
          if (tab === 'screen') captureScreen();
        }

        async function createNewGame() {
          const out = $('newGameOut');
          const engine = $('newGameEngine').value;
          const root = $('newGameRoot').value.trim();
          const prompt = $('newGamePrompt').value.trim();
          if (!root) { showGlobalNotice('Välj en mapp för projektet.', true); return; }
          try {
            out.style.display = 'block';
            out.textContent = 'Skapar ' + engine + '-projekt...';
            const data = await fetchJson('/api/game/scaffold', {
              method: 'POST',
              headers: { 'content-type': 'application/json' },
              body: JSON.stringify({ engine, prompt, root })
            });
            out.textContent = (data.output || 'Klart') + '\n\nFiler:\n' + (data.files || []).join('\n');
            showGlobalNotice('Spelprojekt skapat i ' + data.path + '. Växla till Filer och tryck "Bygg spel".');
          } catch (e) {
            out.style.display = 'block';
            out.textContent = 'Fel: ' + (e.message || e);
          }
        }

        async function captureScreen() {
          const img = $('screenImg');
          const stateEl = $('screenState');
          const hint = $('screenHint');
          try {
            const res = await fetch('/api/desktop/screenshot', { headers: authHeaders() });
            if (res.status === 403) {
              if (hint) hint.textContent = 'Skärmkontroll är avstängd. Slå på "Tillåt skärmkontroll" i Inställningar.';
              if (stateEl) stateEl.textContent = 'avstängd';
              return;
            }
            if (!res.ok) { if (stateEl) stateEl.textContent = 'kunnde inte ta skärmdump (' + res.status + ')'; return; }
            const blob = await res.blob();
            const url = URL.createObjectURL(blob);
            if (img) { img.src = url; img.style.display = 'block'; }
            if (hint) hint.textContent = 'Live-skärm. Agenten ser detta när skärmkontroll är på.';
            if (stateEl) stateEl.textContent = 'live';
          } catch (e) {
            if (stateEl) stateEl.textContent = 'fel: ' + (e.message || e);
          }
        }

        async function clickOnScreen(e, img) {
          const rect = img.getBoundingClientRect();
          const scaleX = img.naturalWidth / rect.width;
          const scaleY = img.naturalHeight / rect.height;
          const x = Math.round((e.clientX - rect.left) * scaleX);
          const y = Math.round((e.clientY - rect.top) * scaleY);
          try {
            await fetchJson('/api/desktop/click', {
              method: 'POST',
              headers: { 'content-type': 'application/json' },
              body: JSON.stringify({ x, y })
            });
            showGlobalNotice('Klickade på skärmen (' + x + ', ' + y + ').');
          } catch (err) {
            showGlobalNotice('Kunde inte klicka: ' + (err.message || err), true);
          }
        }

        async function renderStudio() {
          if (state.activeView !== 'studio') return;
          if (!studio.root) {
            // try the worker workspace from settings
            try {
              const s = await fetchJson('/api/settings');
              studio.root = s.workspacePath || null;
            } catch (_) {}
          }
          $('studioRoot').textContent = studio.root || 'ingen mapp vald';
          if (studio.root) loadFileTree(studio.root, '');
        }

        async function loadFileTree(root, rel) {
          const tree = $('fileTree');
          if (!tree) return;
          tree.innerHTML = '<div class="small">Laddar...</div>';
          try {
            const items = await fetchJson('/api/files/tree?root=' + encodeURIComponent(root));
            const sorted = (items || []).slice().sort((a, b) =>
              (a.isDir === b.isDir) ? a.name.localeCompare(b.name) : (a.isDir ? -1 : 1));
            tree.innerHTML = sorted.map(it => {
              const depth = (it.path.split('/').length - 1);
              const indent = ' '.repeat(Math.min(depth, 8) * 2);
              const cls = it.isDir ? 'node-row dir' : 'node-row';
              const entryIcon = icon(it.isDir ? 'folder' : 'file');
              return `<div class="${cls}" data-path="${esc(it.path)}" data-dir="${it.isDir}">${indent}${entryIcon} ${esc(it.name)}</div>`;
            }).join('');
            tree.querySelectorAll('.node-row').forEach(row => {
              row.onclick = () => {
                tree.querySelectorAll('.node-row').forEach(r => r.classList.remove('selected'));
                row.classList.add('selected');
                const p = row.dataset.path;
                if (row.dataset.dir === 'true') loadFileTree(root, p);
                else openStudioFile(p);
              };
            });
          } catch (e) {
            tree.innerHTML = `<div class="notice bad">${esc(e.message)}</div>`;
          }
        }

        // ---- Studio: tabbed + syntax-highlighted editor (P1) ----
        const studioTabs = [];   // { path, name, content, dirty }
        let studioActive = null;

        function langFromPath(p) {
          if (/\.cs$/i.test(p)) return 'cs';
          if (/\.(js|ts|tsx|jsx)$/i.test(p)) return 'js';
          if (/\.json$/i.test(p)) return 'json';
          if (/\.(html|xml)$/i.test(p)) return 'html';
          if (/\.(css|scss)$/i.test(p)) return 'css';
          if (/\.(py|rb|go|rs|cpp|c|h)$/i.test(p)) return 'clike';
          return 'text';
        }

        // Minimal offline syntax highlighter - escapes HTML then wraps tokens.
        // Good enough for "feels like a studio"; not a full parser.
        function highlightCode(code, lang) {
          const esc = s => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
          let h = esc(code);
          // strings (double + single quoted)
          h = h.replace(/(&quot;|"|')(?:\\.|(?!\1).)*\1/g, m => `<span class="tok-str">${m}</span>`);
          // comments: // line and /* block */
          h = h.replace(/(\/\/[^\n]*|\/\*[\s\S]*?\*\/)/g, m => `<span class="tok-com">${m}</span>`);
          // keywords
          const kw = /\b(typeof|var|let|const|function|return|if|else|for|while|class|public|private|protected|internal|static|void|int|string|bool|true|false|null|undefined|new|async|await|import|export|from|interface|enum|struct|namespace|using|get|set|in|of|do|switch|case|break|continue|throw|try|catch|finally|yield|this|def|elif|fi|then)\b/g;
          h = h.replace(kw, m => `<span class="tok-kw">${m}</span>`);
          // numbers
          h = h.replace(/\b\d+(\.\d+)?\b/g, m => `<span class="tok-num">${m}</span>`);
          return h;
        }

        function renderStudioTabs() {
          const c = $('studioTabs');
          if (!c) return;
          c.innerHTML = studioTabs.map(t =>
            `<div class="studio-tab ${t === studioActive ? 'active' : ''} ${t.dirty ? 'dirty' : ''}" data-path="${esc(t.path)}">` +
            `<span class="name">${esc(t.name)}</span>` +
            `<span class="x" data-close="${esc(t.path)}">✕</span></div>`).join('');
          c.querySelectorAll('.studio-tab').forEach(el => {
            el.onclick = (e) => {
              if (e.target.classList.contains('x')) { closeStudioTab(e.target.dataset.close); return; }
              const t = studioTabs.find(x => x.path === el.dataset.path);
              if (t) { studioActive = t; renderStudioTabs(); refreshStudioEditor(); }
            };
          });
        }

        function closeStudioTab(path) {
          const i = studioTabs.findIndex(t => t.path === path);
          if (i < 0) return;
          studioTabs.splice(i, 1);
          if (studioActive && studioActive.path === path)
            studioActive = studioTabs[Math.max(0, i - 1)] || null;
          if (!studioActive) {
            $('studioEditorView').innerHTML = '';
            $('studioFileName').textContent = 'Ingen fil öppen';
            $('studioFilePath').textContent = '';
          } else refreshStudioEditor();
          renderStudioTabs();
        }

        function refreshStudioEditor() {
          const ed = $('studioEditorView');
          if (!ed || !studioActive) return;
          ed.innerHTML = highlightCode(studioActive.content, langFromPath(studioActive.path));
          ed.onblur = () => {
            if (!studioActive) return;
            studioActive.content = ed.innerText;
            ed.innerHTML = highlightCode(studioActive.content, langFromPath(studioActive.path));
          };
          $('studioFileName').textContent = studioActive.name;
          $('studioFilePath').textContent = studioActive.path;
          $('studioEditorNotice').textContent = '';
        }

        async function openStudioFile(rel) {
          let tab = studioTabs.find(t => t.path === rel);
          if (!tab) {
            const data = await fetchJson('/api/files/content?path=' + encodeURIComponent(rel) + '&root=' + encodeURIComponent(studio.root) + '&offset=1&limit=2000');
            tab = { path: rel, name: rel.split('/').pop(), content: (data.lines || []).join('\n'), dirty: false };
            studioTabs.push(tab);
          }
          studioActive = tab;
          renderStudioTabs();
          refreshStudioEditor();
        }

        async function saveStudioFile() {
          if (!studioActive) return;
          const btn = $('studioSaveBtn');
          btn.disabled = true;
          try {
            studioActive.content = $('studioEditorView').innerText;
            await fetchJson('/api/files/write', {
              method: 'POST', headers: { 'content-type': 'application/json' },
              body: JSON.stringify({ path: studioActive.path, root: studio.root, content: studioActive.content })
            });
            studioActive.dirty = false;
            renderStudioTabs();
            $('studioEditorNotice').textContent = 'Sparad.';
            $('studioEditorNotice').className = 'notice';
          } catch (e) {
            $('studioEditorNotice').textContent = e.message;
            $('studioEditorNotice').className = 'notice bad';
          } finally { btn.disabled = false; }
        }

        // ---- Studio: Build / Run / Test (P2) ----
        async function buildGame() {
          if (!studio.root) { showGlobalNotice('Skapa/öppna ett spelprojekt först.', true); return; }
          const out = $('studioOutput'), state = $('studioRunState');
          if (!out) return;
          out.style.display = 'block';
          out.className = 'studio-output';
          out.textContent = 'Bygger .exe (letar upp installerad motor)...';
          state.textContent = 'körs';
          try {
            const r = await fetchJson('/api/game/build', {
              method: 'POST', headers: { 'content-type': 'application/json' },
              body: JSON.stringify({ engine: 'auto', root: studio.root })
            });
            out.textContent = r.output || '(ingen output)';
            if (!r.success) out.className = 'studio-output err';
            state.textContent = r.success ? 'klar: ' + (r.exe || '') : 'misslyckades';
          } catch (e) {
            out.textContent = e.message; out.className = 'studio-output err'; state.textContent = 'fel';
          }
        }

        async function runStudioCommand(kind) { // 'build' | 'run' | 'test'
          if (!studio.root) { showGlobalNotice('Välj en arbetsmapp först.', true); return; }
          const out = $('studioOutput'), state = $('studioRunState');
          if (!out) return;
          out.style.display = 'block';
          out.className = 'studio-output';
          out.textContent = 'Kör ' + kind + '...';
          state.textContent = 'körs';
          try {
            const r = await fetchJson('/api/workspace/' + kind, {
              method: 'POST', headers: { 'content-type': 'application/json' },
              body: JSON.stringify({ root: studio.root })
            });
            out.textContent = r.output || '(ingen output)';
            if (!r.success) out.className = 'studio-output err';
            state.textContent = r.success ? 'klar' : 'misslyckades';
          } catch (e) {
            out.textContent = e.message; out.className = 'studio-output err'; state.textContent = 'fel';
          }
        }

        async function startStudioTerminal() {
          if (studio.termId) return;
          try {
            const data = await fetchJson('/api/terminals', {
              method: 'POST', headers: { 'content-type': 'application/json' },
              body: JSON.stringify({ root: studio.root || '.' })
            });
            studio.termId = data.terminalId;
            $('studioTermStatus').textContent = 'körs';
            pollStudioTerminal();
          } catch (e) {
            $('studioTermStatus').textContent = 'fel: ' + e.message;
          }
        }

        async function sendStudioTerminalInput(data) {
          if (!studio.termId) return;
          try {
            await fetchJson('/api/terminals/' + studio.termId + '/input', {
              method: 'POST', headers: { 'content-type': 'application/json' },
              body: JSON.stringify({ data })
            });
          } catch (e) { $('studioTermStatus').textContent = 'fel: ' + e.message; }
        }

        async function pollStudioTerminal() {
          if (!studio.termId) return;
          try {
            const data = await fetchJson('/api/terminals/' + studio.termId + '/output');
            const out = $('studioTermOut');
            if (data && data.output) {
              out.textContent += data.output;
              out.scrollTop = out.scrollHeight;
            }
          } catch (_) {}
          studio.termTimer = setTimeout(pollStudioTerminal, 700);
        }

        // ---- Studio: branches tab (git isolation review) ----
        function studioTabSwitch(which) {
          const files = which === 'files';
          $('studioTabFiles').classList.toggle('active', files);
          $('studioTabBranches').classList.toggle('active', !files);
          $('studioFilesPane').style.display = files ? '' : 'none';
          $('studioBranchesPane').style.display = files ? 'none' : '';
          if (!files) renderStudioBranches();
        }

        async function renderStudioBranches() {
          const el = $('studioBranches');
          if (!el) return;
          try {
            const s = await fetchJson('/api/settings');
            const on = !!(s && s.autoMergeIsolatedTasks);
            const st = $('studioAutoMergeState');
            if (st) st.textContent = 'Auto-merge: ' + (on ? 'PÅ' : 'av');
          } catch (_) {}
          let tasks;
          try {
            tasks = await fetchJson('/api/isolation/list');
          } catch (e) {
            el.innerHTML = '<div class="small" style="opacity:.6">Kunde inte läsa isolerade uppgifter (agentläge/worktree saknas).</div>';
            return;
          }
          if (!tasks || !tasks.length) {
            el.innerHTML = '<div class="small" style="opacity:.6">Inga aktiva isolerade uppgifter.</div>';
            return;
          }
          el.innerHTML = tasks.map(t => `<div class="branch-row">
            <div class="mono">${esc(t.branch)}</div>
            <div class="small">bas: ${esc(t.baseBranch)} · ${esc(trunc(t.title || t.taskId, 48))}</div>
            <div class="branch-actions">
              <button class="btn ghost sm" data-iso-merge="${esc(t.taskId)}">Merge</button>
              <button class="btn ghost sm" data-iso-discard="${esc(t.taskId)}">Kasta</button>
              <button class="btn ghost sm" data-iso-diff="${esc(t.taskId)}">Visa diff</button>
              <button class="btn ghost sm" data-iso-verify="${esc(t.taskId)}">Verifiera</button>
            </div>
            <pre class="iso-diff" id="iso-diff-${esc(t.taskId)}" style="display:none;max-height:240px;overflow:auto"></pre>
            <div class="iso-diff-side" id="iso-diff-side-${esc(t.taskId)}" style="display:none"></div>
            <div class="iso-verify-out" id="iso-verify-${esc(t.taskId)}" style="display:none"></div>
          </div>`).join('');
          el.querySelectorAll('[data-iso-merge]').forEach(b => b.onclick = () => studioMerge(b.dataset.isoMerge));
          el.querySelectorAll('[data-iso-discard]').forEach(b => b.onclick = () => studioDiscard(b.dataset.isoDiscard));
          el.querySelectorAll('[data-iso-diff]').forEach(b => b.onclick = () => studioDiff(b.dataset.isoDiff));
          el.querySelectorAll('[data-iso-verify]').forEach(b => b.onclick = () => studioVerify(b.dataset.isoVerify));
        }

        async function studioMerge(taskId) {
          if (!window.confirm('Merga den här uppgifts-branchen in i sin bas?')) return;
          try {
            const r = await fetchJson('/api/isolation/merge', { method: 'POST', body: JSON.stringify({ taskId }) });
            showGlobalNotice(r.success ? 'Mergad: ' + (r.output || 'OK') : 'Merge misslyckades: ' + (r.output || ''), !r.success);
            await renderStudioBranches();
          } catch (e) { showGlobalNotice(e.message, true); }
        }

        async function studioDiscard(taskId) {
          if (!window.confirm('Kasta den här uppgifts-branchen? Ändringarna försvinner (ångra).')) return;
          try {
            await fetchJson('/api/isolation/discard', { method: 'POST', body: JSON.stringify({ taskId }) });
            showGlobalNotice('Uppgifts-branch kastad.');
            await renderStudioBranches();
          } catch (e) { showGlobalNotice(e.message, true); }
        }

        async function studioDiff(taskId) {
          const side = $('iso-diff-side-' + taskId);
          if (!side) return;
          if (side.style.display !== 'none') { side.style.display = 'none'; return; }
          try {
            const r = await fetchJson('/api/isolation/diff', { method: 'POST', body: JSON.stringify({ taskId }) });
            side.innerHTML = renderSideBySide(r.diff || '');
            side.style.display = 'block';
          } catch (e) { showGlobalNotice(e.message, true); }
        }

        // P5: parse a unified git diff into a side-by-side (old | new) view
        // with per-line syntax highlighting. Offline, no CDN.
        function renderSideBySide(diff) {
          if (!diff || !diff.trim()) return '<div class="small" style="opacity:.6">Ingen diff.</div>';
          const files = [];
          let cur = null, oldLine = 0, newLine = 0;
          const esc = s => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
          const langOf = p => /\.(cs|js|ts|tsx|jsx|json|html|xml|css|scss|py|rb|go|rs|cpp|c|h)$/i.test(p) ? p : '';
          for (const raw of diff.split('\n')) {
            if (raw.startsWith('diff --git')) {
              if (cur) files.push(cur);
              const m = raw.match(/diff --git a\/(.*) b\/(.*)/);
              cur = { file: m ? m[2] : raw, left: [], right: [] };
            } else if (raw.startsWith('@@')) {
              const m = raw.match(/@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@/);
              oldLine = m ? +m[1] : 0; newLine = m ? +m[2] : 0;
            } else if (raw.startsWith('+') && !raw.startsWith('+++')) {
              cur.right.push({ type: 'add', n: newLine++, text: raw.slice(1) });
            } else if (raw.startsWith('-') && !raw.startsWith('---')) {
              cur.left.push({ type: 'del', n: oldLine++, text: raw.slice(1) });
            } else if (raw.startsWith(' ')) {
              cur.left.push({ type: 'ctx', n: oldLine++, text: raw.slice(1) });
              cur.right.push({ type: 'ctx', n: newLine++, text: raw.slice(1) });
            } else if (raw.startsWith('\\')) {
              // "\ No newline at end of file" - ignore
            }
          }
          if (cur) files.push(cur);
          return files.map(f => {
            const lang = langOf(f.file);
            const left = f.left.map(l => {
              const cls = l.type === 'del' ? 'del' : (l.type === 'ctx' ? 'ctx' : 'ctx');
              const h = l.type === 'del' ? highlightCode(esc(l.text), langOf(lang)) : esc(l.text);
              return `<div class="iso-line ${l.type}"><span class="iso-ln">${l.n || ''}</span><span class="iso-txt">${h}</span></div>`;
            }).join('');
            const right = f.right.map(l => {
              const h = l.type === 'add' ? highlightCode(esc(l.text), langOf(lang)) : esc(l.text);
              return `<div class="iso-line ${l.type}"><span class="iso-ln">${l.n || ''}</span><span class="iso-txt">${h}</span></div>`;
            }).join('');
            return `<div class="iso-file"><div class="iso-file-name mono">${esc(f.file)}</div>
              <div class="iso-cols"><div class="iso-col old">${left}</div><div class="iso-col new">${right}</div></div></div>`;
          }).join('');
        }

        // P6: ask a Worker to actually run the task's app and report the
        // startup output back, so an operator can see it boots before merge.
        async function studioVerify(taskId) {
          const out = $('iso-verify-' + taskId);
          if (!out) return;
          out.style.display = 'block';
          out.className = 'iso-verify-out';
          out.textContent = 'Verifierar (bygger + kör)...';
          try {
            const r = await fetchJson('/api/isolation/verify', { method: 'POST', body: JSON.stringify({ taskId }) });
            out.textContent = r.output || '(ingen output)';
            if (!r.success) out.className = 'iso-verify-out err';
          } catch (e) { out.textContent = e.message; out.className = 'iso-verify-out err'; }
        }
        initStudio();
      </script>
    </body>
    </html>
    """;
}
