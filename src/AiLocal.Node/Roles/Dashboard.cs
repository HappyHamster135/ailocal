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
        :root {
          color-scheme: light;
          --bg: #f6f7f9;
          --surface: #ffffff;
          --surface-2: #eef2f5;
          --surface-soft: #fbfcfd;
          --surface-active: #eef4ff;
          --surface-selected: #f4f7ff;
          --topbar-bg: rgba(255,255,255,.86);
          --line: #d9e0e6;
          --line-strong: #bcc8d1;
          --kv-line: #eef1f4;
          --user-bubble-border: #cfe0ff;
          --text: #172029;
          --muted: #667583;
          --accent: #246bfe;
          --accent-2: #087f8c;
          --ok: #15803d;
          --ok-bg: #f0faf3;
          --ok-border: #bad5c2;
          --ok-text: #166534;
          --warn: #b45309;
          --bad: #b91c1c;
          --bad-bg: #fff5f5;
          --bad-border: #efc3c3;
          --radius: 8px;
          --shadow: 0 1px 2px rgba(23,32,41,.05);
        }
        :root[data-theme="dark"] {
          color-scheme: dark;
          --bg: #0a0c10;
          --surface: #14171c;
          --surface-2: #1c2028;
          --surface-soft: #10141a;
          --surface-active: #1b2333;
          --surface-selected: #182030;
          --topbar-bg: rgba(16,19,24,.9);
          --line: #22262e;
          --line-strong: #333944;
          --kv-line: #1c2028;
          --user-bubble-border: #2c4a7c;
          --text: #eef1f5;
          --muted: #8992a1;
          --accent: #5b8cff;
          --accent-2: #22b8c8;
          --ok: #3ddc84;
          --ok-bg: #0f2116;
          --ok-border: #1d4a30;
          --ok-text: #6ee7a0;
          --warn: #f0a020;
          --bad: #ff6b6b;
          --bad-bg: #2a1418;
          --bad-border: #5c2530;
          --radius: 8px;
          --shadow: 0 1px 2px rgba(0,0,0,.3);
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
          font-family: Inter, ui-sans-serif, system-ui, -apple-system, Segoe UI, sans-serif;
          font-size: 14px;
          line-height: 1.4;
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
          min-height: 36px;
          padding: 0 12px;
          border-radius: 7px;
          cursor: pointer;
        }
        button:hover { border-color: var(--line-strong); background: var(--surface-soft); }
        button:active { transform: scale(.97); }
        button.primary { background: var(--accent); border-color: var(--accent); color: white; }
        button.ghost { background: transparent; }
        button.icon { width: 30px; min-height: 30px; padding: 0; display: inline-flex; align-items: center; justify-content: center; }
        .icon-svg { display: block; flex: 0 0 auto; }
        button.active { border-color: var(--accent); color: var(--accent); background: var(--surface-active); }
        input, textarea, select {
          border: 1px solid var(--line);
          background: var(--surface);
          color: var(--text);
          border-radius: 7px;
          outline: none;
        }
        input:focus, textarea:focus, select:focus { border-color: var(--accent); box-shadow: 0 0 0 3px rgba(36,107,254,.12); }
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
          background: var(--surface);
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
          height: 64px;
          padding: 0 18px;
          display: flex;
          align-items: center;
          gap: 16px;
          border-bottom: 1px solid var(--line);
          background: var(--topbar-bg);
          backdrop-filter: blur(12px);
          position: sticky;
          top: 0;
          z-index: 5;
        }
        .brand {
          display: flex;
          align-items: center;
          gap: 10px;
          width: 210px;
          flex: 0 0 auto;
        }
        .mark {
          width: 34px;
          height: 34px;
          border-radius: 8px;
          background: linear-gradient(135deg, var(--accent), var(--accent-2));
          color: white;
          display: grid;
          place-items: center;
          font-weight: 800;
        }
        .brand-title { font-size: 17px; font-weight: 760; letter-spacing: 0; }
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
        }
        .sidebar {
          display: grid;
          grid-template-rows: auto auto auto 1fr;
          min-height: 0;
          overflow: hidden;
          border-right: 1px solid var(--line);
          background: var(--surface);
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
        .chat-only-workspace .chat-panel { width: 100%; max-width: 860px; }
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
        .panel-title { font-weight: 740; }
        .small { font-size: 12px; color: var(--muted); }
        .content { padding: 12px; overflow: auto; }
        .kv {
          display: grid;
          gap: 7px;
          margin-bottom: 12px;
        }
        .kv-row {
          display: flex;
          justify-content: space-between;
          gap: 10px;
          padding: 8px 0;
          border-bottom: 1px solid var(--kv-line);
        }
        .mono { font-family: ui-monospace, SFMono-Regular, Consolas, monospace; font-size: 12px; }
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
        .chat-title { font-size: 16px; font-weight: 760; }
        .messages {
          padding: 18px;
          overflow: auto;
          display: flex;
          flex-direction: column;
          gap: 12px;
        }
        .message {
          width: fit-content;
          max-width: min(78ch, 92%);
          border: 1px solid var(--line);
          background: var(--surface-soft);
          border-radius: 8px;
          padding: 12px 13px;
          white-space: pre-wrap;
        }
        .message.user {
          margin-left: auto;
          background: var(--surface-active);
          border-color: var(--user-bubble-border);
        }
        .message.assistant { margin-right: auto; }
        .message-meta {
          display: flex;
          gap: 8px;
          align-items: center;
          color: var(--muted);
          font-size: 12px;
          margin-bottom: 6px;
        }
        .composer {
          border-top: 1px solid var(--line);
          padding: 12px;
          display: grid;
          gap: 10px;
          background: var(--surface-soft);
        }
        textarea {
          width: 100%;
          min-height: 92px;
          max-height: 220px;
          resize: vertical;
          padding: 11px;
        }
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
          grid-template-columns: 190px 1fr;
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
        .token-row { display: flex; gap: 8px; align-items: center; }
        .token-row input { flex: 1 1 auto; min-width: 0; }
        .token-hint { color: var(--muted); font-size: 12px; margin-top: 6px; }
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

        .cost-providers { margin-top: 4px; padding-top: 6px; border-top: 1px solid var(--line); }
        .cost-provider-row { display: flex; justify-content: space-between; font-size: 12px;
          color: var(--muted); padding: 2px 0; }
      </style>
    </head>
    <body>
      <div class="app">
        <header class="topbar">
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
        </header>

        <div style="padding:0 14px">
          <div class="notice" id="globalNotice" style="margin-top:10px"></div>
          <div id="pairingRequests"></div>
          <div id="localNodesBanner"></div>
          <div class="notice" id="firstRunBanner" style="margin-top:10px"></div>
          <div class="notice" id="updateBanner" style="margin-top:10px"></div>
        </div>

        <div class="shell">
          <aside class="sidebar" id="appSidebar">
            <nav class="sidebar-nav" aria-label="Vyer">
              <button class="view-tab active" data-view="work"><span data-icon="monitor"></span> Kluster</button>
              <button class="view-tab" data-view="network"><span data-icon="globe"></span> Nätverk</button>
              <button class="view-tab" data-view="schedules"><span data-icon="clock"></span> Schema</button>
              <button class="view-tab" data-view="delegate" id="delegateNavBtn"><span data-icon="send"></span> Delegera till kluster</button>
            </nav>
            <div class="sidebar-section">
              <div class="sidebar-section-head">
                <div class="panel-title">Sessioner</div>
                <button class="icon" id="newSessionToggleBtn" data-icon="plus" title="Ny session"></button>
              </div>
              <div class="new-session-form hidden" id="newSessionForm">
                <div class="notice" id="newSessionNotice"></div>
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
            <div class="composer" id="composer">
              <div class="notice" id="composerNotice"></div>
              <textarea id="prompt" placeholder="Skriv vad du vill att klustret ska göra"></textarea>
              <div class="composer-actions">
                <div class="inline-fields">
                  <label class="small" for="parallelism">Parallellitet</label>
                  <input id="parallelism" type="number" min="1" max="32" value="4">
                </div>
                <label class="small" for="modelSelect">Modell</label>
                <select id="modelSelect" title="Vilken modell Hosten anvander - 'Auto' valjer efter uppgiftens komplexitet sa du inte alltid betalar for den dyraste.">
                  <option value="">Auto (efter komplexitet)</option>
                  <option value="claude-haiku-4-5">Claude Haiku 4.5 (enkel)</option>
                  <option value="claude-sonnet-5">Claude Sonnet 5 (medel)</option>
                  <option value="claude-opus-4-8">Claude Opus 4.8 (komplex)</option>
                </select>
                <label class="check-field" title="Skickar till en Worker med agentlage påslaget, som jobbar med filer/kommandon tills den anser uppgiften klar, istallet för ett vanligt engangssvar.">
                  <input id="assignmentMode" type="checkbox"> Assignment (agentlage)
                </label>
                <button class="primary" id="sendBtn">Skicka</button>
              </div>
            </div>
          </section>
        </main>

        <main class="chat-only-workspace hidden" id="sessionView">
          <section class="panel chat-panel">
            <div class="chat-head">
              <div>
                <div class="chat-title" id="sessionTitle">Session</div>
                <div class="small mono" id="sessionFolderPath"></div>
              </div>
              <div style="display:flex;align-items:center;gap:8px">
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
            <div class="composer" id="sessionComposer">
              <div class="notice" id="sessionNotice"></div>
              <textarea id="sessionPrompt" placeholder="Skriv ett meddelande till agenten i den här mappen"></textarea>
              <div class="composer-actions">
                <span class="small" id="sessionRunningIndicator"></span>
                <button id="sessionCancelBtn" style="display:none">Avbryt</button>
                <button class="primary" id="sessionSendBtn">Skicka</button>
              </div>
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
            <button class="view-tab" data-settings-cat="agent"><span data-icon="folder"></span> Agent &amp; arbetsyta</button>
            <button class="view-tab" data-settings-cat="models"><span data-icon="globe"></span> Modeller &amp; providers</button>
            <button class="view-tab" data-settings-cat="security"><span data-icon="key"></span> Säkerhet</button>
            <button class="view-tab" data-settings-cat="update"><span data-icon="refresh"></span> Uppdatering</button>
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
                <div class="field wide" style="margin-top:4px">
                  <span class="small">Modell per komplexitet (Hosten väljer, slipper alltid den dyraste)</span>
                  <div class="model-tier-grid">
                    <label class="tier-field"><span class="small">Enkel (1-2)</span>
                      <input id="settingTierSimple" placeholder="claude-haiku-4-5"></label>
                    <label class="tier-field"><span class="small">Medel (3-4)</span>
                      <input id="settingTierMedium" placeholder="claude-sonnet-5"></label>
                    <label class="tier-field"><span class="small">Komplex (5)</span>
                      <input id="settingTierComplex" placeholder="claude-opus-4-8"></label>
                  </div>
                </div>
              </div>
            </section>
            <section class="settings-pane hidden" data-settings-pane="models">
              <div class="form-grid">
                <label class="field"><span class="small">Claude-modell</span><input id="settingAnthropicModel"></label>
                <label class="field"><span class="small">Gemini-modell</span><input id="settingGeminiModel"></label>
                <label class="field"><span class="small">OpenRouter-modell</span><input id="settingOpenRouterModel" placeholder="t.ex. anthropic/claude-sonnet-4.5"></label>
                <label class="field"><span class="small">ChatGPT-modell (OpenAI)</span><input id="settingOpenAIModel" placeholder="t.ex. gpt-4o"></label>
                <label class="field"><span class="small">Lokal Ollama-modell</span><input id="settingOllamaModel" placeholder="Använd rekommenderad"></label>
                <label class="field"><span class="small">Max tokens</span><input id="settingMaxTokens" type="number" min="128" max="131072"></label>
                <label class="field wide"><span class="small">Ollama endpoint</span><input id="settingOllamaEndpoint"></label>
                <label class="check-field wide"><input id="settingAutoPull" type="checkbox"> Hämta vald lokal modell automatiskt</label>
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
          </div>
        </div>
        <div class="dialog-foot">
          <button id="cancelSettings">Avbryt</button>
          <button class="primary" id="saveSettings">Spara</button>
        </div>
      </dialog>

      <script>
        const AUTH_HEADER = 'X-AiLocal-Token';
        const roleName = ['Launcher','Host','Worker','Overseer'];
        const stateName = ['Pending','Dispatched','Running','Completed','Failed','Queued','Cancelled'];
        const cancellableStates = ['Pending','Dispatched','Running','Queued'];
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
          activeSessionRuns: 0
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
          'chevron-down': '<polyline points="6 9 12 15 18 9"/>'
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
          const online = state.nodes.filter(n => statusText(n.status) !== 'Offline');
          const busy = state.nodes.filter(n => statusText(n.status) === 'Busy');
          const offline = state.nodes.filter(n => statusText(n.status) === 'Offline');
          $('nodeCount').textContent = `${state.nodes.length} workers`;
          $('onlineCount').textContent = online.length;
          $('busyCount').textContent = busy.length;
          $('offlineCount').textContent = offline.length;

          if (state.selectedNodeId && !state.nodes.some(n => n.id === state.selectedNodeId))
            state.selectedNodeId = null;

          // Diff guard: the node list only changes shape/contents on refresh
          // when the data actually changed. Rebuilding innerHTML every 3s (and
          // rebinding every onclick) causes flicker and clobbers any in-flight
          // hover state, so skip the rewrite when the serialized markup is
          // identical. Selection highlight is handled separately below.
          const listHtml = state.nodes.length ? state.nodes.map(n => {
            const role = roleName[n.role] ?? n.role;
            const status = statusText(n.status);
            const hardware = n.hardware ? (n.hardware.gpu || n.hardware.cpu) : 'Okänd hårdvara';
            const providers = n.providerPriority?.map(id => providerLabels[id] ?? id).join(' -> ') || 'Ingen provider';
            const skills = (n.skills || ['general']).join(', ');
            return `<div class="node ${state.selectedNodeId === n.id ? 'selected' : ''}" data-node-id="${esc(n.id)}">
              <div class="node-main">
                <div class="node-status"><span class="dot ${statusClass(n.status)}"></span><strong>${esc(n.name)}</strong></div>
                <span class="pill">${esc(role)}</span>
              </div>
              <div class="small">${esc(status)} | ${n.activeTasks ?? 0} aktiva | ${esc(ago(n.lastSeen))}</div>
              <div class="small">${esc(trunc(hardware, 48))}</div>
              <div class="small">${esc(trunc(skills, 52))}</div>
              <div class="small">${esc(trunc(providers, 52))}</div>
            </div>`;
          }).join('') : '<div class="empty">Inga Workers har registrerats ännu.</div>';

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
          renderSessionMessages();
          wireGitBar();
          loadGitStatus();
        }

        function persistedSessionMessageHtml(m) {
          if (m.role === 'user')
            return `<article class="message user"><div class="message-meta"><strong>Du</strong></div><div>${esc(m.content)}</div></article>`;
          if (m.role === 'tool')
            return `<article class="message assistant">
              <div class="message-meta"><span class="pill">✓ ${esc(m.toolName ?? 'verktyg')}</span></div>
              <div class="mono small" style="white-space:pre-wrap">${esc(trunc(m.content ?? '', 2000))}</div>
            </article>`;
          const toolLines = (m.toolCalls ?? []).map(tc => `> ${tc.name}(${trunc(tc.argumentsJson ?? '', 160)})`).join('\n');
          const body = [toolLines, m.content].filter(Boolean).join('\n');
          return `<article class="message assistant">
            <div class="message-meta"><strong>AiLocal</strong></div>
            <div>${body ? esc(body) : '<span class="small">...</span>'}</div>
          </article>`;
        }

        function liveSessionBubbleHtml(m) {
          if (m.role === 'user')
            return `<article class="message user"><div class="message-meta"><strong>Du</strong></div><div>${esc(m.content)}</div></article>`;
          return `<article class="message assistant">
            <div class="message-meta"><strong>AiLocal</strong>${m.state ? `<span>${esc(m.state)}</span>` : ''}</div>
            <div>${esc(m.content)}</div>
          </article>`;
        }

        function renderSessionMessages() {
          const box = $('sessionMessages');
          const persisted = state.activeSession?.messages ?? [];
          const all = [...persisted.map(m => persistedSessionMessageHtml(m)), ...state.sessionLiveMessages.map(m => liveSessionBubbleHtml(m))];
          if (!all.length) {
            box.innerHTML = `<div class="empty">Inga meddelanden ännu i den här sessionen.</div>`;
            return;
            }

            // --- Git awareness (session folder) ---
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
              const res = await fetchJson(`/api/sessions/${id}/git/commit`, {
                method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ message: msg })
              });
              $('gitCommitMsg').value = '';
              showSessionNotice('Commit skapad.');
              await loadGitStatus();
            } catch (error) {
              showSessionNotice(error.message, true);
            }
            }

            // --- File-write approval (preview before save) ---
            let _pendingDiffPath = '';

            function openDiffModal(title, path, diff) {
            _pendingDiffPath = path;
            $('diffModalPath').textContent = path;
            $('diffModalBody').textContent = diff || '(tom fil / ingen skillnad att visa)';
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
            // arrives - surfaces the diff for the operator to approve/reject.
            function handleApprovalStep(detailJson) {
            try {
              const d = JSON.parse(detailJson);
              openDiffModal('Agenten vill skriva en fil', d.path || '(okänd fil)', d.diff || '');
            } catch {
              openDiffModal('Agenten vill skriva en fil', '', '');
            }
            }
          const wasNearBottom = box.scrollHeight - box.scrollTop - box.clientHeight < 80;
          const previousScrollTop = box.scrollTop;
          box.innerHTML = all.join('');
          box.scrollTop = wasNearBottom ? box.scrollHeight : previousScrollTop;
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

          const liveAssistant = { role: 'assistant', content: '', state: 'Running' };
          state.sessionLiveMessages = [{ role: 'user', content: text }, liveAssistant];
          renderSessionMessages();

          const lines = [];
          const appendLine = line => {
            lines.push(line);
            liveAssistant.content = lines.join('\n');
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
                if (!dataLine) continue;
                const payload = JSON.parse(dataLine.slice(5).trim());
                if (payload.step) {
                  if (payload.step.Kind === 'awaiting_approval') handleApprovalStep(payload.step.Detail);
                  appendLine(stepLine(payload.step));
                } else if (payload.final) {
                  success = !!payload.final.Success;
                  liveAssistant.state = success ? 'Completed' : 'Failed';
                  if (!lines.length) {
                    appendLine(payload.final.FinalAnswer || (success ? '(inget svar)' : '(misslyckades)'));
                  } else {
                    renderSessionMessages();
                  }
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
            appendLine(`✗ ${error.message}`);
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

        const knownViews = ['work', 'network', 'schedules', 'delegate', 'session'];
        function switchView(view) {
          state.activeView = knownViews.includes(view) ? view : 'work';
          $('workView').classList.toggle('hidden', state.activeView !== 'work');
          $('networkView').classList.toggle('hidden', state.activeView !== 'network');
          $('schedulesView').classList.toggle('hidden', state.activeView !== 'schedules');
          $('delegateView').classList.toggle('hidden', state.activeView !== 'delegate');
          $('sessionView').classList.toggle('hidden', state.activeView !== 'session');
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
            // The 3s refresh cycle rebuilds this whole list from scratch, which
            // would otherwise silently re-collapse an entry the operator just
            // opened to read - re-apply the "open" attribute from tracked state.
            const isOpen = state.openHistoryIds.has(t.id);
            return `<details class="hist" data-hist-id="${esc(t.id)}" ${isOpen ? 'open' : ''}>
              <summary><span class="hist-title">${esc(t.title || t.prompt)}</span><span class="pill">${esc(status)}</span></summary>
              <div class="hist-body"><div class="small">${esc(meta)}</div>${assignment}<div>${esc(trunc(body, 1200))}</div></div>
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
            const delegation = [
              t.workerName ? '-> ' + t.workerName : '',
              t.workerTier ? t.workerTier : '',
              t.complexity ? 'niva ' + t.complexity : ''
            ].filter(Boolean).join(' | ');
            const cost = fmtUsd(t.estimatedCostUsd);
            const cancellable = cancellableStates.includes(status);
            return `<div class="node">
              <div class="node-main"><span class="mono">${esc(t.id)}</span><span class="pill">${esc(status)}</span></div>
              <div class="small">${esc(trunc(t.title || t.prompt, 64))}</div>
              <div class="small">${esc(delegation)}</div>
              <div class="small">${esc(t.provider ? `${t.provider}/${t.model ?? ''}` : '')}${cost ? ' | ' + esc(cost) : ''}</div>
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

        async function cancelTask(id) {
          if (!window.confirm('Avbryt den här uppgiften?')) return;
          try {
            await fetchJson(`/api/tasks/${id}/cancel`, { method: 'POST' });
            await refresh();
          } catch (error) {
            showGlobalNotice(error.message, true);
          }
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
          if (!allMessages.length) {
            box.innerHTML = `<div class="empty">Inga meddelanden ännu.</div>`;
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
          box.innerHTML = allMessages.map(m => {
            if (m.isPlan) return renderPlanBubble(m);
            const inFlight = m.role === 'assistant' && m.taskId && cancellableStates.includes(m.state);
            const live = inFlight && state.streamBuffer && state.streamBuffer.taskId === m.taskId;
            const content = live ? state.streamBuffer.text : m.content;
            return `
            <article class="message ${m.role === 'user' ? 'user' : 'assistant'}">
              <div class="message-meta">
                <strong>${m.role === 'user' ? 'You' : 'AiLocal'}</strong>
                ${m.isAssignment ? '<span class="pill">Assignment</span>' : ''}
                ${m.subtaskTitle ? `<span class="pill">${esc(m.subtaskTitle)}</span>` : ''}
                ${m.workerName ? `<span class="small">${esc(m.workerName)}</span>` : ''}
                ${m.state ? `<span>${esc(m.state)}</span>` : ''}
                ${m.provider ? `<span>${esc(m.provider)}/${esc(m.model ?? '')}</span>` : ''}
                ${inFlight ? `<button class="icon" title="Avbryt" data-cancel-task="${esc(m.taskId)}">${icon('x', 14)}</button>` : ''}
              </div>
              <div>${esc(content)}</div>
            </article>`;
          }).join('');
          box.scrollTop = wasNearBottom ? box.scrollHeight : previousScrollTop;

          document.querySelectorAll('#messages [data-cancel-task]').forEach(button => {
            button.onclick = () => cancelTask(button.dataset.cancelTask);
          });
          wirePlanBubbles();

          manageStreaming();
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
              const terminal = ['Completed', 'Failed', 'Cancelled'].includes(payload.state);
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
              renderHost();
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
            if (state.activeView === 'chat') renderMessages();

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
          if (state.local.role === 'Launcher') {
            box.innerHTML =
              'Nytt kluster? Klicka <strong>"Starta kluster (Host + Worker)"</strong> till vänster för att komma igång på den här datorn, ' +
              'eller välj Host/Worker/Overseer ovan. Kopiera klusternyckeln från Host-inställningarna till andra datorer som ska gå med. ' +
              '<button class="icon" id="dismissFirstRun" title="Stäng" style="float:right">' + icon('x') + '</button>';
            box.className = 'notice show';
            const dismiss = $('dismissFirstRun');
            if (dismiss) dismiss.onclick = () => { state.firstRunDismissed = true; renderFirstRunBanner(); };
          } else if (state.local.role === 'Host' && !state.nodes.length) {
            box.innerHTML =
              'Ingen Worker ansluten ännu. Starta en Worker på den här eller en annan dator på samma nätverk - den dyker upp under ' +
              '"Upptäckta enheter" här nedanför, redo att anslutas med ett klick. Ser du den inte? Klistra in klusternyckeln ' +
              '(Inställningar -> Klustersäkerhet) på Worker-datorn istället. ' +
              '<button class="icon" id="dismissFirstRun" title="Stäng" style="float:right">' + icon('x') + '</button>';
            box.className = 'notice show';
            const dismiss = $('dismissFirstRun');
            if (dismiss) dismiss.onclick = () => { state.firstRunDismissed = true; renderFirstRunBanner(); };
          } else if (state.local.role === 'Worker' && !state.local.hostEndpoint) {
            box.innerHTML =
              'Den här Workern är inte ansluten till någon Host än. Öppna Host-datorns instrumentpanel - den ser den här ' +
              'datorn automatiskt under "Upptäckta enheter" och kan ansluta med ett klick. Väntar du på en bekräftelse ' +
              'istället? Kolla om det ligger en väntande förfrågan högst upp på den här sidan. ' +
              '<button class="icon" id="dismissFirstRun" title="Stäng" style="float:right">' + icon('x') + '</button>';
            box.className = 'notice show';
            const dismiss = $('dismissFirstRun');
            if (dismiss) dismiss.onclick = () => { state.firstRunDismissed = true; renderFirstRunBanner(); };
          } else {
            box.className = 'notice';
          }
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
              box.innerHTML = `En ny version är tillgänglig: <strong>${esc(info.latestVersion)}</strong> ` +
                `(nuvarande ${esc(info.currentVersion)}).` +
                (info.downloadUrl ? ` <a href="${esc(info.downloadUrl)}" target="_blank" rel="noopener">Hämta</a>` : '') +
                (info.notes ? ` - ${esc(info.notes)}` : '');
              box.className = 'notice show';
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

        function applySettingsData(data) {
          $('settingNodeName').value = data.nodeName ?? '';
          $('settingHostEndpoint').value = data.hostEndpoint ?? '';
          $('settingDiscovery').checked = data.discoveryEnabled ?? true;
          $('settingSkills').value = (data.skills ?? ['general']).join(', ');
          $('settingMaxConcurrentTasks').value = data.maxConcurrentTasks ?? 1;
          $('settingAgentAccess').value = data.agentAccess ?? 'Off';
          $('settingWorkspacePath').value = data.workspacePath ?? '';
          $('settingTierSimple').value = data.modelTiers?.simple ?? '';
          $('settingTierMedium').value = data.modelTiers?.medium ?? '';
          $('settingTierComplex').value = data.modelTiers?.complex ?? '';
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
            modelTiers: {
              simple: $('settingTierSimple').value.trim() || 'claude-haiku-4-5',
              medium: $('settingTierMedium').value.trim() || 'claude-sonnet-5',
              complex: $('settingTierComplex').value.trim() || 'claude-opus-4-8'
            },
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

        async function sendMessage() {
          const prompt = $('prompt').value.trim();
          if (!prompt) return;
          if ($('assignmentMode').checked) {
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
            $('sendBtn').disabled = false;
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
            done: '✓',
            error: '✗',
            cancelled: '×'
          };
          const marker = stepMarkers[step.Kind] ?? '·';
          return `${marker} ${step.Detail}`;
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
            planMsg.planState = 'failed';
            planMsg.error = error.message;
          } finally {
            renderMessages();
            $('sendBtn').disabled = false;
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

          let pinnedWorkerId = null;
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
              const outcome = await runPlanSubtask(subtask, subtask.description, null);
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
        async function runPlanSubtask(subtask, assignmentText, workerId) {
          const stepMsg = { role: 'assistant', content: '', state: 'Running', isAssignment: true, subtaskTitle: subtask.title };
          state.assignmentMessages.push(stepMsg);
          renderMessages();

          const lines = [];
          const appendLine = line => {
            lines.push(line);
            stepMsg.content = lines.join('\n');
            renderMessages();
          };

          let workerIdUsed = workerId;
          let success = false;
          let summary = '';

          try {
            const headers = { 'content-type': 'application/json' };
            if (state.authToken) headers[AUTH_HEADER] = state.authToken;
            const response = await fetch('/api/assignment', {
              method: 'POST',
              headers,
              body: JSON.stringify({ assignment: assignmentText, workerId })
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
                if (!dataLine) continue;
                const payload = JSON.parse(dataLine.slice(5).trim());
                if (payload.worker) {
                  workerIdUsed = payload.worker.id;
                  stepMsg.workerName = payload.worker.name;
                  renderMessages();
                } else if (payload.step) {
                  appendLine(stepLine(payload.step));
                } else if (payload.final) {
                  success = !!payload.final.Success;
                  summary = payload.final.FinalAnswer || '';
                  stepMsg.state = success ? 'Completed' : 'Failed';
                  // See the equivalent note this replaced: AgentLoop always
                  // emits a step with this same text right before returning.
                  if (!lines.length) {
                    appendLine(summary || (success ? '(inget svar)' : '(misslyckades)'));
                  } else {
                    renderMessages();
                  }
                }
              }
            }
          } catch (error) {
            stepMsg.state = 'Failed';
            appendLine(`✗ ${error.message}`);
            summary = error.message;
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
        $('topologyRefresh').onclick = refresh;
        $('settingsBtn').onclick = () => openSettings(null);
        $('configureNode').onclick = () => openSettings(state.selectedNodeId);
        $('saveSettings').onclick = saveSettings;
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
        $('settingsDialog').addEventListener('click', event => {
          if (event.target === $('settingsDialog')) closeSettingsDialog();
        });
        $('settingsDialog').addEventListener('close', refresh);
        $('prompt').addEventListener('keydown', event => {
          if ((event.ctrlKey || event.metaKey) && event.key === 'Enter') sendMessage();
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
          if ((event.ctrlKey || event.metaKey) && event.key === 'Enter') sendSessionMessage();
        });

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

        loadProviders();
        refresh();
        setInterval(refresh, 3000);
      </script>
    <div class="modal-overlay" id="diffModal" style="display:none">
      <div class="modal diff-modal">
        <div class="modal-head">
          <div>
            <div class="modal-title">Agenten vill skriva en fil</div>
            <div class="small mono" id="diffModalPath"></div>
          </div>
          <button class="icon" id="diffModalClose" title="Stäng">✕</button>
        </div>
        <pre class="diff-view" id="diffModalBody"></pre>
        <div class="modal-foot">
          <button id="diffRejectBtn">Avvisa</button>
          <button class="primary" id="diffApproveBtn">Godkänn &amp; skriv</button>
        </div>
      </div>
    </div>
    </body>
    </html>
    """;
}
