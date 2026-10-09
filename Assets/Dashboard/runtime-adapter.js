/* Runtime binding for the unchanged Claude Design standalone export.
   The original component owns markup, colors, dimensions and provider art. */
(() => {
  "use strict";
  let component, snapshot, snapshotId = 0, register;
  const ids = {codex: "codex", "claude-code": "claude", antigravity: "antigravity"};
  const send = message => window.chrome?.webview?.postMessage(message);

  // Hook the export's component registration without rewriting its source.
  Object.defineProperty(window, "__dcRegister", {
    configurable: true,
    get: () => register,
    set: original => {
      register = (name, Component, defaults) => original(name, class extends Component {
        constructor(props) { super(props); component = this; }
        renderVals() {
          const data = snapshot;
          this.props.provider = ids[data?.provider] || "codex";
          this.props.conn = data?.connection || "disconnected";
          this.state.enabled = {
            codex: !!data?.providers.codex,
            claude: !!data?.providers["claude-code"],
            antigravity: !!data?.providers.antigravity
          };
          const values = super.renderVals();
          const ownerId = ids[data?.provider];
          if (!ownerId) {
            this.props.provider = "antigravity";
            const standby = super.renderVals().providers;
            values.providers[0] = standby[0];
            values.providers[1] = standby[1];
            this.props.provider = "codex";
          }
          values.providers = values.providers.map(provider => {
            return {...provider, toggle: () => {}};
          });
          values.owner = {...values.owner,
            name: data?.ownerName || "No active provider",
            project: data?.project || "",
            details: data?.details || "",
            stateLine: data?.activity || "",
            elapsed: data?.elapsed || "",
            large: data?.largeImage || "",
            small: data?.smallImage || ""
          };
          if (!ownerId) values.owner.icon = "";
          values.ownerOn = !!ownerId && !!data?.enabled;
          values.ownerOff = !values.ownerOn;
          const originalBar = values.metrics.find(metric => metric.hasBar)?.barStyle || "";
          values.metrics = (data?.metrics || []).map(metric => ({...metric,
            hasBar: metric.progressPercent != null,
            barStyle: originalBar.replace(/width:[^;]+;/, `width:${Math.max(0, Math.min(100, metric.progressPercent || 0))}%;`)
          }));
          values.hasMetrics = values.metrics.length > 0;
          values.noMetrics = !values.hasMetrics;
          values.emptyNote = data?.usageNote || "No current provider activity.";
          values.cardLabel = `${data?.activityType || "Discord preview"}. ${values.owner.name}. ${values.owner.details}. ${values.owner.stateLine}. ${data?.smallText || ""}`;
          return values;
        }
      }, defaults);
    }
  });

  // Preserve existing image elements and keyboard focus during data refreshes.
  function reconcile(target, source) {
    if (target.nodeType !== source.nodeType || target.nodeName !== source.nodeName) {
      target.replaceWith(source.cloneNode(true)); return;
    }
    if (target.nodeType === Node.TEXT_NODE) {
      if (target.nodeValue !== source.nodeValue) target.nodeValue = source.nodeValue;
      return;
    }
    if (target.nodeType !== Node.ELEMENT_NODE) return;
    for (const attribute of [...target.attributes]) {
      if (!source.hasAttribute(attribute.name)) target.removeAttribute(attribute.name);
    }
    for (const attribute of source.attributes) {
      if (target.getAttribute(attribute.name) !== attribute.value) target.setAttribute(attribute.name, attribute.value);
    }
    if (target instanceof HTMLInputElement) target.checked = source.checked;
    for (let index = 0; index < source.childNodes.length; index++) {
      const oldChild = target.childNodes[index], newChild = source.childNodes[index];
      if (!oldChild) target.appendChild(newChild.cloneNode(true));
      else reconcile(oldChild, newChild);
    }
    while (target.childNodes.length > source.childNodes.length) target.lastChild.remove();
  }

  function decorate(root) {
    const card = root.querySelector("figure");
    if (!card) return;
    card.querySelector("div > span").textContent = snapshot.activityType || "Discord preview";
    const images = card.querySelectorAll("img");
    images[0].hidden = !snapshot.largeImage;
    images[1].hidden = !snapshot.smallImage;
    images[1].title = snapshot.smallText || "";
    images[1].setAttribute("aria-label", snapshot.smallText || "");
    card.style.opacity = snapshot.connection !== "connected" || !snapshot.enabled || !snapshot.hasPublishedPresence ? "0.5" : "1";
    const button = card.lastElementChild;
    button.hidden = !snapshot.buttonLabel;
    button.textContent = snapshot.buttonLabel || "";
    button.setAttribute("role", "button");
    button.tabIndex = snapshot.buttonLabel ? 0 : -1;
    button.setAttribute("aria-hidden", "false");
    // Window controls use the title bar already present in the source export.
    const header = root.querySelector(".kcp > div");
    const controls = [...header.children].slice(-3);
    controls.forEach((control, index) => {
      control.dataset.windowAction = ["minimize", "maximize", "close"][index];
      control.setAttribute("role", "button");
      control.setAttribute("aria-label", ["Minimize", "Maximize", "Close"][index]);
      control.tabIndex = 0;
    });
    header.removeAttribute("aria-hidden");
    header.dataset.titleBar = "true";
    const owner = root.querySelector("section");
    owner.querySelector("img").hidden = !snapshot.provider;
    if (!snapshot.provider) owner.lastElementChild.textContent = "nothing is published";
  }

  document.addEventListener("DOMContentLoaded", () => {
    // Some source elements have inline display:block. Apply runtime visibility
    // without changing any of the original layout or visual declarations.
    const visibility = document.createElement("style");
    visibility.textContent = "#app[hidden], #app [hidden]{display:none!important}";
    document.head.appendChild(visibility);
    const root = document.getElementById("app");
    root.hidden = true; // Fixture values are never displayed to the app user.
    const originalRender = component.__rerender;
    component.__rerender = () => {
      const host = component.__host;
      const savedHost = component.__host;
      component.__host = host.cloneNode(false);
      originalRender();
      decorate(component.__host);
      const generated = component.__host;
      component.__host = savedHost;
      reconcile(host, generated);
    };
    function activate(element) {
      const windowAction = element.closest("[data-window-action]");
      if (windowAction) send({type: "window", action: windowAction.dataset.windowAction});
      else if (element.closest("figure > [role=button]")) send({type: "button"});
    }
    root.addEventListener("change", event => {
      if (event.target instanceof HTMLInputElement) {
        const providerId = ["codex", "claude-code", "antigravity"][[...root.querySelectorAll("input")].indexOf(event.target)];
        send({type: "provider", providerId, enabled: event.target.checked});
      }
    });
    root.addEventListener("click", event => activate(event.target));
    root.addEventListener("keydown", event => {
      if (["Enter", " "].includes(event.key) && event.target.getAttribute("role") === "button") {
        event.preventDefault(); activate(event.target);
      }
    });
    root.addEventListener("pointerdown", event => {
      if (event.button === 0 && event.target.closest("[data-title-bar]") && !event.target.closest("[data-window-action]")) send({type: "window", action: "drag"});
    });
    window.chrome.webview.addEventListener("message", event => {
      if (event.data.type !== "snapshot") return;
      snapshot = event.data.payload; snapshotId = event.data.id;
      component.__rerender(); root.hidden = false;
      const displayedId = snapshotId;
      requestAnimationFrame(() => requestAnimationFrame(() => send({type: "painted", id: displayedId})));
    });
    send({type: "ready"});
  });
})();
