(() => {
  const preview = document.querySelector("[data-rpc-preview]");
  if (!preview) {
    return;
  }

  const elapsed = preview.querySelector("[data-rpc-elapsed]");
  if (!elapsed) {
    return;
  }

  const initialSeconds = Number.parseInt(
    preview.dataset.initialElapsedSeconds ?? "120",
    10);
  const safeInitialSeconds = Number.isFinite(initialSeconds) && initialSeconds >= 0
    ? initialSeconds
    : 120;
  const startedAt = performance.now();

  const formatElapsed = (totalSeconds) => {
    const minutes = Math.floor(totalSeconds / 60);
    const seconds = totalSeconds % 60;
    return `${minutes}:${seconds.toString().padStart(2, "0")}`;
  };

  const updateElapsed = () => {
    const elapsedSeconds = safeInitialSeconds + Math.floor((performance.now() - startedAt) / 1000);
    elapsed.textContent = formatElapsed(elapsedSeconds);
  };

  updateElapsed();
  window.setInterval(updateElapsed, 1000);
})();
