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
          --shadow: 0 14px 40px rgba(23,32,41,.08);
        }
        :root[data-theme="dark"] {
          color-scheme: dark;
          --bg: #0e1116;
          --surface: #161b22;
          --surface-2: #1f2630;
          --surface-soft: #1b222c;
          --surface-active: #1b2b4a;
          --surface-selected: #1d2c4d;
          --topbar-bg: rgba(22,27,34,.86);
          --line: #2a323d;
          --line-strong: #3a4552;
          --kv-line: #232b35;
          --user-bubble-border: #274b86;
          --text: #e6edf3;
          --muted: #8b98a6;
          --accent: #4f8bff;
          --accent-2: #2bb6c4;
          --ok: #4ade80;
          --ok-bg: #12251a;
          --ok-border: #1f4d33;
          --ok-text: #6ee7a0;
          --warn: #f59e0b;
          --bad: #f87171;
          --bad-bg: #2a1516;
          --bad-border: #5b2626;
          --radius: 8px;
          --shadow: 0 14px 40px rgba(0,0,0,.45);
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
        button.primary { background: var(--accent); border-color: var(--accent); color: white; }
        button.ghost { background: transparent; }
        button.icon { width: 30px; min-height: 30px; padding: 0; }
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
          grid-template-rows: auto auto auto 1fr;
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
        .viewbar {
          min-height: 44px;
          padding: 6px 14px;
          display: flex;
          align-items: center;
          gap: 6px;
          border-bottom: 1px solid var(--line);
          background: var(--surface);
        }
        .view-tab {
          min-height: 32px;
          padding: 0 14px;
          font-weight: 680;
          background: transparent;
          border-color: transparent;
        }
        .view-tab.active {
          color: var(--accent);
          background: var(--surface-active);
          border-color: var(--line);
        }
        .hidden { display: none !important; }
        .workspace {
          display: grid;
          grid-template-columns: 280px minmax(360px, 1fr) 320px;
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
          transition: border-color .15s, background .15s;
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
        .history-list { display: grid; gap: 6px; }
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
          width: min(760px, calc(100vw - 28px));
          max-height: calc(100vh - 32px);
          border: 1px solid var(--line-strong);
          border-radius: 8px;
          padding: 0;
          color: var(--text);
          background: var(--surface);
          box-shadow: 0 28px 80px rgba(23,32,41,.25);
        }
        dialog::backdrop { background: rgba(23,32,41,.42); }
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
        .dialog-body { padding: 16px; overflow: auto; max-height: calc(100vh - 170px); }
        .form-section { margin-bottom: 20px; }
        .form-title { font-weight: 740; margin-bottom: 10px; }
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
          .viewbar { padding: 6px 8px; }
          .topology-workspace { grid-template-columns: minmax(0, 1fr); padding: 8px; gap: 8px; }
          .schedules-workspace { grid-template-columns: minmax(0, 1fr); padding: 8px; gap: 8px; }
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
            <button class="icon" id="authBtn" title="Anslutningsnyckel (för fjärråtkomst utanför den här datorn)">🔑</button>
            <button class="icon" id="themeBtn" title="Ljust / mörkt läge"></button>
            <button id="settingsBtn">Inställningar</button>
          </div>
        </header>

        <div style="padding:0 14px">
          <div id="pairingRequests"></div>
          <div class="notice" id="firstRunBanner" style="margin-top:10px"></div>
          <div class="notice" id="updateBanner" style="margin-top:10px"></div>
        </div>

        <nav class="viewbar" aria-label="Vyer">
          <button class="view-tab active" data-view="work">Arbete</button>
          <button class="view-tab" data-view="network">Nätverk</button>
          <button class="view-tab" data-view="schedules">Schema</button>
        </nav>

        <main class="workspace" id="workView">
          <aside class="panel side">
            <div class="panel-head">
              <div>
                <div class="panel-title">Cluster</div>
                <div class="small" id="nodeCount">0 nodes</div>
              </div>
              <button class="icon" id="refreshBtn" title="Refresh">R</button>
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

          <section class="panel chat-panel">
            <div class="chat-head">
              <div>
                <div class="chat-title">Meddelanden</div>
                <div class="small" id="chatSub">Host och Overseer kan skicka mål här.</div>
              </div>
              <span class="pill" id="providerSummary">Local</span>
            </div>
            <div class="messages" id="messages"></div>
            <div class="composer">
              <div class="notice" id="composerNotice"></div>
              <textarea id="prompt" placeholder="Skriv vad du vill att klustret ska göra"></textarea>
              <div class="composer-actions">
                <div class="inline-fields">
                  <label class="small" for="parallelism">Parallellitet</label>
                  <input id="parallelism" type="number" min="1" max="32" value="4">
                </div>
                <button class="primary" id="sendBtn">Skicka</button>
              </div>
            </div>
          </section>

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

      <dialog id="settingsDialog">
        <div class="dialog-head">
          <div>
            <div class="chat-title" id="settingsTitle">Nodinställningar</div>
            <div class="small" id="settingsSubtitle"></div>
          </div>
          <button class="icon" id="closeSettings" title="Stäng">X</button>
        </div>
        <div class="dialog-body">
          <div class="notice" id="settingsNotice"></div>
          <section class="form-section">
            <div class="form-title">Nod och nätverk</div>
            <div class="form-grid">
              <label class="field"><span class="small">Namn</span><input id="settingNodeName" maxlength="80"></label>
              <label class="field"><span class="small">Host endpoint</span><input id="settingHostEndpoint" placeholder="http://192.168.1.10:5080"></label>
              <label class="check-field"><input id="settingDiscovery" type="checkbox"> Automatisk LAN-upptäckt</label>
              <label class="check-field" id="settingAutoStartRow"><input id="settingAutoStart" type="checkbox"> Starta automatiskt vid inloggning</label>
            </div>
          </section>
          <section class="form-section">
            <div class="form-title">Worker-profil</div>
            <div class="form-grid">
              <label class="field wide">
                <span class="small">Specialiteter</span>
                <input id="settingSkills" placeholder="coding, research, writing, data, vision">
              </label>
              <label class="field">
                <span class="small">Max samtidiga jobb</span>
                <input id="settingMaxConcurrentTasks" type="number" min="1" max="32">
              </label>
            </div>
          </section>
          <section class="form-section">
            <div class="form-title">Klustersäkerhet</div>
            <div class="form-grid">
              <div class="field wide">
                <span class="small">Nuvarande klusternyckel</span>
                <div class="token-row">
                  <input id="currentClusterToken" type="password" readonly>
                  <button class="icon" id="toggleTokenVisibility" type="button" title="Visa/dölj">👁</button>
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
                <input id="settingClusterToken" type="password" autocomplete="off" placeholder="Lämna tom för att behålla">
                <span class="key-state" id="clusterTokenState"></span>
              </label>
              <label class="check-field wide"><input id="clearClusterToken" type="checkbox"> Ta bort klusternyckel (öppnar klustret för hela LAN:et)</label>
              <div class="field wide">
                <span class="small">Operatörsnyckel (begränsad åtkomst: mål, chatt, avbryt - ej nodhantering/inställningar)</span>
                <div class="token-row">
                  <input id="currentOperatorToken" type="password" readonly>
                  <button class="icon" id="toggleOperatorTokenVisibility" type="button" title="Visa/dölj">👁</button>
                  <button id="copyOperatorToken" type="button">Kopiera</button>
                  <button id="regenerateOperatorToken" type="button">Generera ny</button>
                </div>
              </div>
              <label class="field wide">
                <span class="small">Klistra in en operatörsnyckel</span>
                <input id="settingOperatorToken" type="password" autocomplete="off" placeholder="Lämna tom för att behålla">
              </label>
              <label class="check-field wide"><input id="clearOperatorToken" type="checkbox"> Ta bort operatörsnyckel</label>
            </div>
          </section>
          <section class="form-section">
            <div class="form-title">Providerordning</div>
            <div class="settings-provider-list" id="settingsProviders"></div>
          </section>
          <section class="form-section">
            <div class="form-title">Modeller och runtime</div>
            <div class="form-grid">
              <label class="field"><span class="small">Claude-modell</span><input id="settingAnthropicModel"></label>
              <label class="field"><span class="small">Gemini-modell</span><input id="settingGeminiModel"></label>
              <label class="field"><span class="small">Lokal Ollama-modell</span><input id="settingOllamaModel" placeholder="Använd rekommenderad"></label>
              <label class="field"><span class="small">Max tokens</span><input id="settingMaxTokens" type="number" min="128" max="131072"></label>
              <label class="field wide"><span class="small">Ollama endpoint</span><input id="settingOllamaEndpoint"></label>
              <label class="check-field wide"><input id="settingAutoPull" type="checkbox"> Hämta vald lokal modell automatiskt</label>
            </div>
          </section>
          <section class="form-section">
            <div class="form-title">API-nycklar</div>
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
              <label class="check-field"><input id="clearAnthropicKey" type="checkbox"> Ta bort Claude-nyckel</label>
              <label class="check-field"><input id="clearGeminiKey" type="checkbox"> Ta bort Gemini-nyckel</label>
            </div>
          </section>
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
        const providerLabels = { anthropic: 'Claude', gemini: 'Gemini', ollama: 'Local' };
        const providerIds = ['anthropic', 'gemini', 'ollama'];
        const state = {
          local: null,
          host: null,
          nodes: [],
          topology: { nodes: [], edges: [] },
          tasks: [],
          messages: [],
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
          pairingConnecting: new Set()
        };

        const $ = id => document.getElementById(id);
        const esc = s => (s ?? '').toString().replace(/[<>&]/g, c => ({'<':'&lt;','>':'&gt;','&':'&amp;'}[c]));
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
              <button class="icon" title="Move up" data-provider-up="${id}" ${index === 0 ? 'disabled' : ''}>^</button>
              <button class="icon" title="Move down" data-provider-down="${id}" ${index === order.length - 1 ? 'disabled' : ''}>v</button>
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
            : `${local.role}`;
          $('quickstartBtn').style.display = local.role === 'Launcher' ? 'block' : 'none';
          renderInspector();
        }

        function renderHost() {
          const hasHost = !!state.host;
          $('hostDot').className = `dot ${hasHost ? 'ok' : 'bad'}`;
          $('hostStatus').textContent = hasHost ? state.host : 'host not connected';
          if (hasHost) $('hostInput').value = state.host;
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

          $('nodes').innerHTML = state.nodes.length ? state.nodes.map(n => {
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

          document.querySelectorAll('[data-node-id]').forEach(card => {
            card.onclick = () => {
              state.selectedNodeId = card.dataset.nodeId;
              state.workerTasks = [];
              renderNodes();
              renderInspector();
              loadWorkerTasks(card.dataset.nodeId);
            };
          });
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
            return `<div class="node" style="cursor:default">
              <div class="node-main"><strong>${esc(w.name)}</strong><span class="pill">Worker</span></div>
              <div class="small">${esc(w.endpoint)}</div>
              <div class="detail-actions">
                <button class="primary" data-connect-worker="${esc(w.id)}" ${connecting ? 'disabled' : ''}>
                  ${connecting ? 'Väntar på godkännande...' : 'Anslut'}
                </button>
              </div>
            </div>`;
          }).join('');

          document.querySelectorAll('[data-connect-worker]').forEach(button => {
            button.onclick = () => connectToDiscoveredWorker(button.dataset.connectWorker);
          });
        }

        async function connectToDiscoveredWorker(id) {
          state.pairingConnecting.add(id);
          renderDiscoveredWorkers();
          try {
            const result = await fetchJson(`/api/discovered-workers/${id}/connect`, { method: 'POST' });
            if (!result?.requested) {
              state.pairingConnecting.delete(id);
              showComposerNotice('Kunde inte skicka anslutningsförfrågan.', true);
              renderDiscoveredWorkers();
            }
          } catch (error) {
            state.pairingConnecting.delete(id);
            showComposerNotice(error.message, true);
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
          box.innerHTML = state.pairingInbound.map(r => `
            <div class="pairing-card">
              <div>
                <strong>${esc(r.requesterName)}</strong> vill ansluta den här datorn till sitt kluster.
                <div class="small mono">${esc(r.requesterEndpoint)}</div>
              </div>
              <div class="detail-actions">
                <button class="primary" data-accept-pairing="${esc(r.requesterId)}">Anslut</button>
                <button data-reject-pairing="${esc(r.requesterId)}">Avvisa</button>
              </div>
            </div>`).join('');

          document.querySelectorAll('[data-accept-pairing]').forEach(button => {
            button.onclick = () => respondToPairingRequest(button.dataset.acceptPairing, true);
          });
          document.querySelectorAll('[data-reject-pairing]').forEach(button => {
            button.onclick = () => respondToPairingRequest(button.dataset.rejectPairing, false);
          });
        }

        async function respondToPairingRequest(hostId, accept) {
          const path = accept ? 'accept' : 'reject';
          try {
            await fetchJson(`/pairing/pending/${hostId}/${path}`, { method: 'POST' });
            state.pairingInbound = state.pairingInbound.filter(r => r.requesterId !== hostId);
            renderPairingRequests();
            if (accept) await refresh();
          } catch (error) {
            showComposerNotice(error.message, true);
          }
        }

        function switchView(view) {
          state.activeView = view === 'network' ? 'network' : view === 'schedules' ? 'schedules' : 'work';
          $('workView').classList.toggle('hidden', state.activeView !== 'work');
          $('networkView').classList.toggle('hidden', state.activeView !== 'network');
          $('schedulesView').classList.toggle('hidden', state.activeView !== 'schedules');
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
            $('topologyDetail').innerHTML = `
              <section class="detail-section">
                <div class="kv">
                  <div class="kv-row"><span>Roll</span><span>${esc(graphNode.role)}</span></div>
                  <div class="kv-row"><span>Status</span><span>${esc(graphNode.status)}</span></div>
                  <div class="kv-row"><span>Endpoint</span><span class="mono">${esc(graphNode.endpoint || '-')}</span></div>
                </div>
              </section>`;
            return;
          }

          const hardware = worker.hardware;
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
              <div class="history-list">${workerHistoryHtml()}</div>
            </section>`;

          $('topologyConfigureBtn').onclick = () => openSettings(worker.id);
          $('topologyRemoveBtn').onclick = () => removeNodeFromCluster(worker.id);
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
                <button class="primary" id="setupAiBtn">Installera lokal AI</button>
                <button id="inspectRuntimeBtn">Kontrollera Ollama</button>
                <button id="pullModelBtn">Hämta lokal modell</button>
                <button id="removeNodeBtn">Ta bort från gruppen</button>
              </div>
            </section>
            <section class="detail-section">
              <div class="panel-title" style="margin-bottom:8px">Historik</div>
              <div class="history-list" id="workerHistory">${workerHistoryHtml()}</div>
            </section>`;

          $('setupAiBtn').onclick = setupWorkerAi;
          $('inspectRuntimeBtn').onclick = inspectWorkerRuntime;
          $('pullModelBtn').onclick = pullWorkerModel;
          $('removeNodeBtn').onclick = () => removeNodeFromCluster(node.id);
          if (state.nodeAction?.id === node.id) {
            const box = $('nodeActionStatus');
            box.textContent = state.nodeAction.message;
            box.className = `notice show ${state.nodeAction.isError ? 'bad' : ''}`;
          }
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
          const button = $('pullModelBtn');
          button.disabled = true;
          showNodeAction('Hämtar modellen. Det kan ta en stund...');
          try {
            const result = await fetchJson(`/api/nodes/${state.selectedNodeId}/runtime/pull`, { method: 'POST' });
            showNodeAction(result.success ? `${result.model} är installerad.` : result.output, !result.success);
          } catch (error) {
            showNodeAction(error.message, true);
          } finally {
            button.disabled = false;
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
            return `<details class="hist">
              <summary><span class="hist-title">${esc(t.title || t.prompt)}</span><span class="pill">${esc(status)}</span></summary>
              <div class="hist-body"><div class="small">${esc(meta)}</div>${assignment}<div>${esc(trunc(body, 1200))}</div></div>
            </details>`;
          }).join('');
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
          const btn = $('setupAiBtn');
          btn.disabled = true;
          showNodeAction('Installerar Ollama och hämtar modellen. Det kan ta flera minuter...');
          try {
            const result = await fetchJson(`/api/nodes/${state.selectedNodeId}/runtime/setup`, { method: 'POST' });
            const lines = (result.steps || []).map(s => `${s.ok ? 'OK' : 'FEL'}  ${s.step}: ${s.detail}`).join('\n');
            showNodeAction((result.success ? 'Klart.' : 'Delvis klart.') + '\n' + lines, !result.success);
          } catch (error) {
            showNodeAction(error.message, true);
          } finally {
            btn.disabled = false;
          }
        }

        function renderTasks() {
          $('taskCount').textContent = state.tasks.length;
          $('tasks').innerHTML = state.tasks.length ? state.tasks.slice(0, 8).map(t => {
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
              ${cancellable ? `<div class="detail-actions"><button class="icon" title="Avbryt" data-cancel-task="${esc(t.id)}">✕</button></div>` : ''}
            </div>`;
          }).join('') : '<div class="empty">Inga jobb ännu.</div>';

          document.querySelectorAll('[data-cancel-task]').forEach(button => {
            button.onclick = () => cancelTask(button.dataset.cancelTask);
          });
        }

        async function cancelTask(id) {
          try {
            await fetchJson(`/api/tasks/${id}/cancel`, { method: 'POST' });
            await refresh();
          } catch (error) {
            showComposerNotice(error.message, true);
          }
        }

        function renderMessages() {
          const box = $('messages');
          if (!state.messages.length) {
            box.innerHTML = `<div class="empty">Inga meddelanden ännu.</div>`;
            return;
          }
          box.innerHTML = state.messages.map(m => {
            const inFlight = m.role === 'assistant' && m.taskId && cancellableStates.includes(m.state);
            const live = inFlight && state.streamBuffer && state.streamBuffer.taskId === m.taskId;
            const content = live ? state.streamBuffer.text : m.content;
            return `
            <article class="message ${m.role === 'user' ? 'user' : 'assistant'}">
              <div class="message-meta">
                <strong>${m.role === 'user' ? 'You' : 'AiLocal'}</strong>
                ${m.state ? `<span>${esc(m.state)}</span>` : ''}
                ${m.provider ? `<span>${esc(m.provider)}/${esc(m.model ?? '')}</span>` : ''}
                ${inFlight ? `<button class="icon" title="Avbryt" data-cancel-task="${esc(m.taskId)}">✕</button>` : ''}
              </div>
              <div>${esc(content)}</div>
            </article>`;
          }).join('');
          box.scrollTop = box.scrollHeight;

          document.querySelectorAll('#messages [data-cancel-task]').forEach(button => {
            button.onclick = () => cancelTask(button.dataset.cancelTask);
          });

          manageStreaming();
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

            const [nodesResult, topologyResult, tasksResult, messagesResult] =
              await Promise.allSettled([
                fetchJson('/api/nodes'),
                fetchJson('/api/topology'),
                fetchJson('/api/tasks'),
                fetchJson('/api/chat')
              ]);

            if (nodesResult.status === 'fulfilled')
              state.nodes = nodesResult.value ?? [];
            renderNodes();

            if (topologyResult.status === 'fulfilled')
              state.topology = topologyResult.value ?? state.topology;
            renderTopology();
            renderTopologyDetail();

            if (tasksResult.status === 'fulfilled')
              state.tasks = tasksResult.value ?? [];
            renderTasks();

            if (messagesResult.status === 'fulfilled')
              state.messages = messagesResult.value ?? [];
            renderMessages();

            if (state.selectedNodeId) await loadWorkerTasks(state.selectedNodeId);

            try {
              state.stats = await fetchJson('/api/stats');
              state.queue = await fetchJson('/api/queue');
              $('queueRow').style.display = 'flex';
              $('costRow').style.display = 'flex';
              $('queueLabel').textContent = `${state.queue.queued} / ${state.queue.inFlight}`;
              $('costLabel').textContent = fmtUsd(state.stats.today.costUsd) || '$0.00';
            } catch {
              $('queueRow').style.display = 'none';
              $('costRow').style.display = 'none';
            }

            if (state.local?.role === 'Host') {
              try {
                state.discoveredWorkers = await fetchJson('/api/discovered-workers') ?? [];
                const pending = await fetchJson('/api/pairing-status') ?? [];
                for (const id of [...state.pairingConnecting]) {
                  if (!pending.some(p => p.peerId === id) && !state.discoveredWorkers.some(w => w.id === id))
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

            renderFirstRunBanner();
            checkForUpdate();
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
              '<button class="icon" id="dismissFirstRun" title="Stäng" style="float:right">✕</button>';
            box.className = 'notice show';
            const dismiss = $('dismissFirstRun');
            if (dismiss) dismiss.onclick = () => { state.firstRunDismissed = true; renderFirstRunBanner(); };
          } else if (state.local.role === 'Host' && !state.nodes.length) {
            box.innerHTML =
              'Ingen Worker ansluten ännu. Starta en Worker på den här eller en annan dator och klistra in klusternyckeln (Inställningar -> Klustersäkerhet) för att para ihop den. ' +
              '<button class="icon" id="dismissFirstRun" title="Stäng" style="float:right">✕</button>';
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
            showComposerNotice(error.message, true);
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
              <button class="icon" title="Flytta upp" data-settings-up="${id}" ${index === 0 ? 'disabled' : ''}>^</button>
              <button class="icon" title="Flytta ner" data-settings-down="${id}" ${index === order.length - 1 ? 'disabled' : ''}>v</button>
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
          $('settingClusterToken').value = '';
          $('clearClusterToken').checked = false;
          $('clusterTokenState').textContent = data.clusterTokenConfigured
            ? 'Klusternyckel konfigurerad'
            : 'Ingen klusternyckel';
          $('currentClusterToken').value = data.clusterToken ?? '';
          $('currentClusterToken').type = 'password';
          $('toggleTokenVisibility').textContent = '👁';
          $('settingOperatorToken').value = '';
          $('clearOperatorToken').checked = false;
          $('currentOperatorToken').value = data.operatorToken ?? '';
          $('currentOperatorToken').type = 'password';
          $('toggleOperatorTokenVisibility').textContent = '👁';
          $('settingAutoStartRow').style.display = data.startWithWindowsSupported === false ? 'none' : 'flex';
          $('settingAutoStart').checked = data.startWithWindows ?? false;
          $('settingAnthropicModel').value = data.anthropicModel ?? '';
          $('settingGeminiModel').value = data.geminiModel ?? '';
          $('settingOllamaModel').value = data.ollamaModel ?? '';
          $('settingOllamaEndpoint').value = data.ollamaEndpoint ?? 'http://localhost:11434';
          $('settingMaxTokens').value = data.maxTokens ?? 4096;
          $('settingAutoPull').checked = data.autoPullOllamaModel ?? false;
          $('settingAnthropicKey').value = '';
          $('settingGeminiKey').value = '';
          $('clearAnthropicKey').checked = false;
          $('clearGeminiKey').checked = false;
          $('anthropicKeyState').textContent = data.anthropicKeyConfigured ? 'Nyckel konfigurerad' : 'Ingen nyckel';
          $('geminiKeyState').textContent = data.geminiKeyConfigured ? 'Nyckel konfigurerad' : 'Ingen nyckel';

          const priority = data.providerPriority?.length ? data.providerPriority : ['ollama'];
          state.settingsOrder = [...priority, ...providerIds.filter(id => !priority.includes(id))];
          state.settingsEnabled = Object.fromEntries(providerIds.map(id => [id, priority.includes(id)]));
          renderSettingsProviders();
        }

        function toggleTokenVisibility() {
          const field = $('currentClusterToken');
          const hidden = field.type === 'password';
          field.type = hidden ? 'text' : 'password';
          $('toggleTokenVisibility').textContent = hidden ? '🙈' : '👁';
        }

        function toggleOperatorTokenVisibility() {
          const field = $('currentOperatorToken');
          const hidden = field.type === 'password';
          field.type = hidden ? 'text' : 'password';
          $('toggleOperatorTokenVisibility').textContent = hidden ? '🙈' : '👁';
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
            $('toggleOperatorTokenVisibility').textContent = '🙈';
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
            $('toggleTokenVisibility').textContent = '🙈';
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

        async function openSettings(targetId = null) {
          state.settingsTarget = targetId;
          $('settingsNotice').className = 'notice';
          $('saveSettings').disabled = true;
          $('settingsTitle').textContent = targetId ? 'Konfigurera Worker' : 'Nodinställningar';
          const node = state.nodes.find(n => n.id === targetId);
          $('settingsSubtitle').textContent = node ? `${node.name} | ${node.endpoint}` : (state.local?.name ?? '');
          $('settingsDialog').showModal();
          try {
            await waitForRefreshIdle();
            const url = targetId ? `/api/nodes/${targetId}/settings` : '/api/settings';
            applySettingsData(await fetchJsonWithRetry(url));
            $('saveSettings').disabled = false;
          } catch (error) {
            showSettingsNotice(error.message, true);
          }
        }

        async function waitForRefreshIdle() {
          for (let attempt = 0; attempt < 80 && state.refreshing; attempt++)
            await new Promise(resolve => setTimeout(resolve, 100));
        }

        function closeSettingsDialog() {
          $('settingsDialog').close();
          refresh();
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
            clusterToken: $('settingClusterToken').value || null,
            clearClusterToken: $('clearClusterToken').checked,
            operatorToken: $('settingOperatorToken').value || null,
            clearOperatorToken: $('clearOperatorToken').checked,
            providerPriority: activeSettingsProviderOrder(),
            anthropicModel: $('settingAnthropicModel').value,
            geminiModel: $('settingGeminiModel').value,
            ollamaModel: $('settingOllamaModel').value,
            ollamaEndpoint: $('settingOllamaEndpoint').value,
            maxTokens: Number($('settingMaxTokens').value),
            autoPullOllamaModel: $('settingAutoPull').checked,
            anthropicApiKey: $('settingAnthropicKey').value || null,
            geminiApiKey: $('settingGeminiKey').value || null,
            clearAnthropicApiKey: $('clearAnthropicKey').checked,
            clearGeminiApiKey: $('clearGeminiKey').checked
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

        async function sendMessage() {
          const prompt = $('prompt').value.trim();
          if (!prompt) return;
          const providerOrder = activeProviderOrder();
          const parallelism = Number($('parallelism').value) || 1;
          $('sendBtn').disabled = true;
          $('composerNotice').className = 'notice';
          try {
            await fetchJson('/api/chat', {
              method: 'POST',
              headers: { 'content-type': 'application/json' },
              body: JSON.stringify({ prompt, parallelism, providerOrder })
            });
            $('prompt').value = '';
            await refresh();
          } catch (error) {
            showComposerNotice(error.message, true);
          } finally {
            $('sendBtn').disabled = false;
          }
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
          $('launchResult').textContent = `Starting ${role}...`;
          try {
            const data = await fetchJson('/api/launch', {
              method: 'POST',
              headers: { 'content-type': 'application/json' },
              body: JSON.stringify({ role: Number(roleValue), hostEndpoint, clusterToken })
            });
            if (!data?.started) {
              $('launchResult').textContent = data?.error || `Could not start ${role}.`;
              return;
            }
            $('launchResult').innerHTML = `Started ${role} on <span class="mono">${esc(data.endpoint)}</span>`;
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

        function applyTheme(theme) {
          document.documentElement.setAttribute('data-theme', theme);
          try { localStorage.setItem('ailocal-theme', theme); } catch {}
          const btn = $('themeBtn');
          if (btn) btn.textContent = theme === 'dark' ? '☀' : '\u{1F319}';
        }
        function initTheme() {
          const requested = new URLSearchParams(window.location.search).get('theme');
          let theme = requested;
          try { theme = localStorage.getItem('ailocal-theme'); } catch {}
          if (requested === 'dark' || requested === 'light') theme = requested;
          if (theme !== 'dark' && theme !== 'light')
            theme = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
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

        loadProviders();
        refresh();
        setInterval(refresh, 3000);
      </script>
    </body>
    </html>
    """;
}
