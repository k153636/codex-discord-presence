(() => {
  const preview = document.querySelector("[data-rpc-preview]");
  if (!preview) {
    return;
  }

  const track = preview.querySelector("[data-rpc-preview-track]");
  const initialCard = preview.querySelector("[data-rpc-card]");
  const slideStatus = preview.querySelector("[data-rpc-slide-status]");
  const buttons = [...preview.querySelectorAll("[data-rpc-direction]")];

  if (!track || !initialCard || !slideStatus || buttons.length === 0) {
    return;
  }

  const rpcThemes = [
    {
      details: "MCP chrome-devtools",
      states: [
        { activity: "Reading the live DOM first", image: "assets/rpc_reading.gif", initialElapsedSeconds: 120 },
        { activity: "Comparing rendered bounds", image: "assets/rpc_thinking.gif", initialElapsedSeconds: 124 },
        { activity: "Inspecting the active tab", image: "assets/rpc_coding.gif", initialElapsedSeconds: 128 }
      ]
    },
    {
      details: "Codex thought summary",
      states: [
        { activity: "The frame should stay stable", image: "assets/rpc_thinking.gif", initialElapsedSeconds: 146 },
        { activity: "Tracing the parent width", image: "assets/rpc_coding.gif", initialElapsedSeconds: 150 },
        { activity: "Keeping the signal concise", image: "assets/rpc_reading.gif", initialElapsedSeconds: 154 }
      ]
    },
    {
      details: "Debugging live render",
      states: [
        { activity: "Reproducing the timer drift", image: "assets/rpc_coding.gif", initialElapsedSeconds: 168 },
        { activity: "Tracing the icon squeeze", image: "assets/rpc_reading.gif", initialElapsedSeconds: 172 },
        { activity: "Verifying the elapsed state", image: "assets/rpc_thinking.gif", initialElapsedSeconds: 176 }
      ]
    }
  ];

  const cardPositionClasses = [
    "rpc-preview-card--far-previous",
    "rpc-preview-card--previous",
    "rpc-preview-card--active",
    "rpc-preview-card--next",
    "rpc-preview-card--far-next"
  ];

  const getCardParts = (card) => ({
    image: card.querySelector("[data-rpc-card-image]"),
    details: card.querySelector("[data-rpc-card-details]"),
    state: card.querySelector("[data-rpc-card-state]"),
    elapsed: card.querySelector("[data-rpc-elapsed]")
  });

  const cards = [initialCard];
  while (cards.length < 5) {
    cards.push(initialCard.cloneNode(true));
  }
  cards.slice(1).forEach((card) => track.append(card));

  const partsByCard = new Map();
  for (const card of cards) {
    const parts = getCardParts(card);
    if (Object.values(parts).some((part) => !part)) {
      return;
    }
    partsByCard.set(card, parts);
  }

  const safeSeconds = (value) => Number.isFinite(value) && value >= 0 ? value : 0;

  const formatElapsed = (totalSeconds) => {
    const minutes = Math.floor(totalSeconds / 60);
    const seconds = totalSeconds % 60;
    return `${minutes}:${seconds.toString().padStart(2, "0")}`;
  };

  const themeStateIndexes = rpcThemes.map(() => 0);
  const themeStartedAt = rpcThemes.map(() => performance.now());

  const normalizeThemeIndex = (index) => (index + rpcThemes.length) % rpcThemes.length;

  const getThemeState = (themeIndex) => {
    const theme = rpcThemes[themeIndex];
    return theme.states[themeStateIndexes[themeIndex]];
  };

  const getPositionClass = (offset) => {
    if (offset <= -2) {
      return "rpc-preview-card--far-previous";
    }
    if (offset === -1) {
      return "rpc-preview-card--previous";
    }
    if (offset === 0) {
      return "rpc-preview-card--active";
    }
    if (offset === 1) {
      return "rpc-preview-card--next";
    }
    return "rpc-preview-card--far-next";
  };

  const setCardPosition = (card, offset) => {
    card.classList.remove(...cardPositionClasses);
    card.classList.add(getPositionClass(offset));
    card.setAttribute("aria-hidden", offset === 0 ? "false" : "true");
  };

  const renderCard = (card, parts, themeIndex) => {
    const theme = rpcThemes[themeIndex];
    const state = getThemeState(themeIndex);
    parts.details.textContent = theme.details;
    parts.state.textContent = state.activity;
    parts.state.classList.toggle("rpc-preview-card-state--wrapped", state.activity.includes("\n"));
    parts.image.src = state.image;
    parts.elapsed.textContent = formatElapsed(safeSeconds(state.initialElapsedSeconds));
    card.setAttribute("aria-label", `Discord activity ${themeIndex + 1} of ${rpcThemes.length}: ${theme.details}: ${state.activity.replace(/\s+/g, " ").trim()}`);
  };

  const renderStatus = (themeIndex) => {
    const theme = rpcThemes[themeIndex];
    const state = getThemeState(themeIndex);
    slideStatus.textContent = `${theme.details} · ${state.activity.replace(/\s+/g, " ").trim()}`;
  };

  let activeThemeIndex = 0;
  let cardsByOffset = new Map(cards.map((card, index) => [index - 2, card]));
  let isTransitioning = false;
  const autoAdvanceTimers = rpcThemes.map(() => 0);
  let autoAdvancePaused = preview.matches(":hover");
  const autoAdvanceDelay = 6200;
  const autoAdvanceStagger = 1100;
  const prefersReducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");

  const clearThemeAutoAdvance = (themeIndex) => {
    window.clearTimeout(autoAdvanceTimers[themeIndex]);
    autoAdvanceTimers[themeIndex] = 0;
  };

  const clearAllThemeAutoAdvances = () => {
    rpcThemes.forEach((_, themeIndex) => clearThemeAutoAdvance(themeIndex));
  };

  const scheduleThemeAutoAdvance = (themeIndex, delay = autoAdvanceDelay) => {
    clearThemeAutoAdvance(themeIndex);
    if (autoAdvancePaused || prefersReducedMotion.matches || document.hidden) {
      return;
    }

    autoAdvanceTimers[themeIndex] = window.setTimeout(() => {
      autoAdvanceTimers[themeIndex] = 0;
      if (!document.hidden && !autoAdvancePaused && !prefersReducedMotion.matches) {
        advanceThemeState(themeIndex);
      }
    }, delay);
  };

  const scheduleAllThemeAutoAdvances = (stagger = false) => {
    rpcThemes.forEach((_, themeIndex) => {
      const delay = autoAdvanceDelay + (stagger ? themeIndex * autoAdvanceStagger : 0);
      scheduleThemeAutoAdvance(themeIndex, delay);
    });
  };

  const updateElapsed = () => {
    const now = performance.now();
    for (const [offset, card] of cardsByOffset) {
      const themeIndex = normalizeThemeIndex(activeThemeIndex + offset);
      const state = getThemeState(themeIndex);
      const elapsedSeconds = safeSeconds(state.initialElapsedSeconds)
        + Math.floor((now - themeStartedAt[themeIndex]) / 1000);
      partsByCard.get(card).elapsed.textContent = formatElapsed(elapsedSeconds);
    }
  };

  const updateButtons = () => {
    buttons.forEach((button) => {
      button.disabled = isTransitioning;
    });
  };

  const animateTo = (incomingCard, onFinish) => {
    let hasFinished = false;
    let fallbackTimer = 0;

    const finishTransition = () => {
      if (hasFinished) {
        return;
      }

      hasFinished = true;
      incomingCard.removeEventListener("transitionend", finishOnTransitionEnd);
      window.clearTimeout(fallbackTimer);
      onFinish();
    };

    const finishOnTransitionEnd = (event) => {
      if (event.target === incomingCard && event.propertyName === "transform") {
        finishTransition();
      }
    };

    incomingCard.addEventListener("transitionend", finishOnTransitionEnd);
    fallbackTimer = window.setTimeout(finishTransition, 650);
  };

  const renderInitialCards = () => {
    for (let offset = -2; offset <= 2; offset += 1) {
      const card = cardsByOffset.get(offset);
      const themeIndex = normalizeThemeIndex(activeThemeIndex + offset);
      renderCard(card, partsByCard.get(card), themeIndex);
      setCardPosition(card, offset);
    }
  };

  const renderThemeCards = (themeIndex) => {
    for (const [offset, card] of cardsByOffset) {
      if (normalizeThemeIndex(activeThemeIndex + offset) === themeIndex) {
        renderCard(card, partsByCard.get(card), themeIndex);
      }
    }
  };

  const advanceThemeState = (themeIndex) => {
    const theme = rpcThemes[themeIndex];
    themeStateIndexes[themeIndex] = (themeStateIndexes[themeIndex] + 1) % theme.states.length;
    themeStartedAt[themeIndex] = performance.now();
    renderThemeCards(themeIndex);
    if (themeIndex === activeThemeIndex) {
      renderStatus(themeIndex);
    }
    updateElapsed();
    scheduleThemeAutoAdvance(themeIndex);
  };

  if (typeof prefersReducedMotion.addEventListener === "function") {
    prefersReducedMotion.addEventListener("change", () => scheduleAllThemeAutoAdvances(true));
  }

  const moveTo = (direction) => {
    if (isTransitioning || direction === 0) {
      return;
    }

    const step = direction > 0 ? 1 : -1;
    const nextThemeIndex = normalizeThemeIndex(activeThemeIndex + step);
    const incomingOffset = step > 0 ? 1 : -1;
    const incomingCard = cardsByOffset.get(incomingOffset);
    const incomingParts = partsByCard.get(incomingCard);

    isTransitioning = true;
    updateButtons();
    renderCard(incomingCard, incomingParts, nextThemeIndex);
    renderStatus(nextThemeIndex);

    const nextCardsByOffset = new Map();
    for (const [offset, card] of cardsByOffset) {
      const nextOffset = offset - step;
      setCardPosition(card, nextOffset);
      nextCardsByOffset.set(nextOffset, card);
    }

    animateTo(incomingCard, () => {
      const transitionFarOffset = step > 0 ? -3 : 3;
      const resetOffset = step > 0 ? 2 : -2;
      const farCard = nextCardsByOffset.get(transitionFarOffset);
      const farParts = partsByCard.get(farCard);
      const farThemeIndex = normalizeThemeIndex(nextThemeIndex + resetOffset);

      farCard.style.transition = "none";
      renderCard(farCard, farParts, farThemeIndex);
      setCardPosition(farCard, resetOffset);
      void farCard.offsetWidth;
      farCard.style.transition = "";

      nextCardsByOffset.delete(transitionFarOffset);
      nextCardsByOffset.set(resetOffset, farCard);
      cardsByOffset = nextCardsByOffset;
      activeThemeIndex = nextThemeIndex;
      updateElapsed();
      isTransitioning = false;
      updateButtons();
    });
  };

  buttons.forEach((button) => {
    button.addEventListener("click", () => {
      const direction = Number.parseInt(button.dataset.rpcDirection ?? "0", 10);
      moveTo(Number.isFinite(direction) ? direction : 0);
    });
  });

  preview.addEventListener("mouseenter", () => {
    autoAdvancePaused = true;
    clearAllThemeAutoAdvances();
  });
  preview.addEventListener("mouseleave", () => {
    autoAdvancePaused = false;
    scheduleAllThemeAutoAdvances(true);
  });
  preview.addEventListener("focusin", () => {
    autoAdvancePaused = true;
    clearAllThemeAutoAdvances();
  });
  preview.addEventListener("focusout", (event) => {
    if (!preview.contains(event.relatedTarget)) {
      autoAdvancePaused = false;
      scheduleAllThemeAutoAdvances(true);
    }
  });
  document.addEventListener("visibilitychange", () => {
    if (document.hidden) {
      clearAllThemeAutoAdvances();
    } else {
      scheduleAllThemeAutoAdvances(true);
    }
  });

  renderInitialCards();
  renderStatus(activeThemeIndex);
  updateElapsed();
  updateButtons();
  scheduleAllThemeAutoAdvances(true);
  window.setInterval(updateElapsed, 1000);
})();
