// Browser side of the formula editor (Components/Editor.razor). Blazor renders the textarea and the highlighted
// layer; this module handles what needs the DOM synchronously: the caret, the scroll of the layer, the position
// of the suggestions and the keys that must not reach the textarea while the suggestions are open.

const KEYS = new Set(["ArrowUp", "ArrowDown", "Enter", "Tab", "Escape"]);

/** Connects the textarea with its highlighted layer and with the component (a DotNetObjectReference). */
export function attach(textarea, layer, component) {
  const syncScroll = () => {
    layer.scrollTop = textarea.scrollTop;
    layer.scrollLeft = textarea.scrollLeft;
  };
  const onKeyDown = (event) => {
    // The component decides synchronously whether it uses the key (to choose or insert a suggestion)
    if (KEYS.has(event.key) && component.invokeMethod("HandleKey", event.key)) {
      event.preventDefault();
      return;
    }
    if (event.key === "Tab") {
      event.preventDefault();
      textarea.setRangeText("  ", textarea.selectionStart, textarea.selectionEnd, "end");
      textarea.dispatchEvent(new Event("input", { bubbles: true }));
    }
  };
  textarea.addEventListener("keydown", onKeyDown);
  textarea.addEventListener("scroll", syncScroll);
  textarea.addEventListener("input", syncScroll);
  return {
    detach() {
      textarea.removeEventListener("keydown", onKeyDown);
      textarea.removeEventListener("scroll", syncScroll);
      textarea.removeEventListener("input", syncScroll);
    },
  };
}

export function caret(textarea) {
  return textarea.selectionStart;
}

export function syncScroll(textarea, layer) {
  layer.scrollTop = textarea.scrollTop;
  layer.scrollLeft = textarea.scrollLeft;
}

/** Position of the suggestions: the end of `text` (the formula before the word), measured in a hidden copy. */
export function measure(textarea, mirror, text) {
  mirror.textContent = text;
  const marker = document.createElement("span");
  marker.textContent = "|";
  mirror.appendChild(marker);
  return {
    top: marker.offsetTop - textarea.scrollTop + 22,
    left: Math.min(marker.offsetLeft - textarea.scrollLeft, textarea.clientWidth - 260),
  };
}

export function setCaret(textarea, position) {
  textarea.focus();
  textarea.selectionStart = textarea.selectionEnd = position;
}
