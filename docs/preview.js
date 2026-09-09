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

  const modelLabel = "gpt 6 astra";
  const baseTokenCount = 35_600_000;
  const rpcThemes = [
    { label: "MCP" },
    { label: "Summary" },
    { label: "Debugging" }
  ];

  const sharedStates = [
    { activity: "Reading the live DOM first", image: "assets/rpc_reading.gif", initialElapsedSeconds: 120, tokenRatePerSecond: 64_000, tokenBurstOnEnter: 120_000, effort: "xhigh" },
    { activity: "Comparing rendered bounds", image: "assets/rpc_thinking.gif", initialElapsedSeconds: 124, tokenRatePerSecond: 72_000, tokenBurstOnEnter: 180_000, effort: "high" },
    { activity: "Inspecting the active tab", image: "assets/rpc_coding.gif", initialElapsedSeconds: 128, tokenRatePerSecond: 58_000, tokenBurstOnEnter: 140_000, effort: "max" },
    { activity: "The frame should stay stable", image: "assets/rpc_thinking.gif", initialElapsedSeconds: 146, tokenRatePerSecond: 46_000, tokenBurstOnEnter: 90_000, effort: "high" },
    { activity: "Tracing the parent width", image: "assets/rpc_coding.gif", initialElapsedSeconds: 150, tokenRatePerSecond: 78_000, tokenBurstOnEnter: 240_000, effort: "xhigh" },
    { activity: "Keeping the signal concise", image: "assets/rpc_reading.gif", initialElapsedSeconds: 154, tokenRatePerSecond: 52_000, tokenBurstOnEnter: 130_000, effort: "max" },
    { activity: "Reproducing the timer drift", image: "assets/rpc_coding.gif", initialElapsedSeconds: 168, tokenRatePerSecond: 88_000, tokenBurstOnEnter: 320_000, effort: "max" },
    { activity: "Tracing the icon squeeze", image: "assets/rpc_reading.gif", initialElapsedSeconds: 172, tokenRatePerSecond: 68_000, tokenBurstOnEnter: 220_000, effort: "xhigh" },
    { activity: "Verifying the elapsed state", image: "assets/rpc_thinking.gif", initialElapsedSeconds: 176, tokenRatePerSecond: 42_000, tokenBurstOnEnter: 110_000, effort: "high" }
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

  const selectInitialStateIndexes = () => {
    const availableIndexes = sharedStates.map((_, index) => index);
    return rpcThemes.map(() => {
      const position = Math.floor(Math.random() * availableIndexes.length);
      return availableIndexes.splice(position, 1)[0];
    });
  };

  const themeStateIndexes = selectInitialStateIndexes();
  const themeEfforts = rpcThemes.map(() => "xhigh");
  const initialTimestamp = performance.now();
  const themeStartedAt = rpcThemes.map(() => initialTimestamp);
  const themeTokenCounts = rpcThemes.map(() => baseTokenCount);
  const themeTokenUpdatedAt = rpcThemes.map(() => initialTimestamp);

  const normalizeThemeIndex = (index) => (index + rpcThemes.length) % rpcThemes.length;

  const getThemeState = (themeIndex) => {
    return sharedStates[themeStateIndexes[themeIndex]];
  };

  const formatTokenCount = (tokenCount) => `${(tokenCount / 1_000_000).toFixed(1)}M Token`;

  const getTokenCount = (themeIndex, now) => {
    const state = getThemeState(themeIndex);
    const elapsedSeconds = Math.max(0, Math.floor((now - themeTokenUpdatedAt[themeIndex]) / 1000));
    if (elapsedSeconds > 0) {
      themeTokenCounts[themeIndex] += elapsedSeconds * state.tokenRatePerSecond;
      themeTokenUpdatedAt[themeIndex] += elapsedSeconds * 1000;
    }

    return themeTokenCounts[themeIndex];
  };

  const formatDetails = (themeIndex, now) => `${modelLabel} ${themeEfforts[themeIndex]} 1.5x • ${formatTokenCount(getTokenCount(themeIndex, now))}`;

  const getNextStateIndex = (themeIndex) => {
    const currentStateIndex = themeStateIndexes[themeIndex];
    const occupiedStateIndexes = new Set(
      themeStateIndexes.filter((_, index) => index !== themeIndex)
    );
    const availableIndexes = sharedStates
      .map((_, index) => index)
      .filter((index) => index !== currentStateIndex && !occupiedStateIndexes.has(index));
    const candidates = availableIndexes.length > 0
      ? availableIndexes
      : sharedStates.map((_, index) => index).filter((index) => index !== currentStateIndex);
    return candidates[Math.floor(Math.random() * candidates.length)];
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

  const renderCard = (card, parts, themeIndex, now = performance.now()) => {
    const theme = rpcThemes[themeIndex];
    const state = getThemeState(themeIndex);
    const activity = `${theme.label} · ${state.activity}`;
    const details = formatDetails(themeIndex, now);
    parts.details.textContent = details;
    parts.state.textContent = activity;
    parts.state.classList.toggle("rpc-preview-card-state--wrapped", activity.includes("\n"));
    parts.image.src = state.image;
    parts.elapsed.textContent = formatElapsed(safeSeconds(state.initialElapsedSeconds));
    card.setAttribute("aria-label", `Discord activity ${themeIndex + 1} of ${rpcThemes.length}: ${details}: ${activity.replace(/\s+/g, " ").trim()}`);
  };

  const renderStatus = (themeIndex) => {
    const theme = rpcThemes[themeIndex];
    const state = getThemeState(themeIndex);
    slideStatus.textContent = `${theme.label} · ${state.activity.replace(/\s+/g, " ").trim()}`;
  };

  let activeThemeIndex = 0;
  let cardsByOffset = new Map(cards.map((card, index) => [index - 2, card]));
  let isTransitioning = false;
  const autoAdvanceTimers = rpcThemes.map(() => 0);
  let autoAdvancePaused = preview.matches(":hover");
  const autoAdvanceMinDelay = 5400;
  const autoAdvanceMaxDelay = 8200;
  const prefersReducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");

  const clearThemeAutoAdvance = (themeIndex) => {
    window.clearTimeout(autoAdvanceTimers[themeIndex]);
    autoAdvanceTimers[themeIndex] = 0;
  };

  const clearAllThemeAutoAdvances = () => {
    rpcThemes.forEach((_, themeIndex) => clearThemeAutoAdvance(themeIndex));
  };

  const getAutoAdvanceDelay = () => Math.round(
    autoAdvanceMinDelay + Math.random() * (autoAdvanceMaxDelay - autoAdvanceMinDelay)
  );

  const scheduleThemeAutoAdvance = (themeIndex, delay = getAutoAdvanceDelay()) => {
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

  const scheduleAllThemeAutoAdvances = () => {
    rpcThemes.forEach((_, themeIndex) => scheduleThemeAutoAdvance(themeIndex));
  };

  const updateElapsed = () => {
    const now = performance.now();
    for (const [offset, card] of cardsByOffset) {
      const themeIndex = normalizeThemeIndex(activeThemeIndex + offset);
      const state = getThemeState(themeIndex);
      const parts = partsByCard.get(card);
      const elapsedSeconds = safeSeconds(state.initialElapsedSeconds)
        + Math.floor((now - themeStartedAt[themeIndex]) / 1000);
      parts.details.textContent = formatDetails(themeIndex, now);
      parts.elapsed.textContent = formatElapsed(elapsedSeconds);
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
    const now = performance.now();
    getTokenCount(themeIndex, now);
    themeStateIndexes[themeIndex] = getNextStateIndex(themeIndex);
    const nextState = getThemeState(themeIndex);
    themeTokenCounts[themeIndex] += nextState.tokenBurstOnEnter;
    themeEfforts[themeIndex] = nextState.effort;
    themeStartedAt[themeIndex] = now;
    themeTokenUpdatedAt[themeIndex] = now;
    renderThemeCards(themeIndex);
    if (themeIndex === activeThemeIndex) {
      renderStatus(themeIndex);
    }
    updateElapsed();
    scheduleThemeAutoAdvance(themeIndex);
  };

  if (typeof prefersReducedMotion.addEventListener === "function") {
    prefersReducedMotion.addEventListener("change", scheduleAllThemeAutoAdvances);
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
    scheduleAllThemeAutoAdvances();
  });
  preview.addEventListener("focusin", () => {
    autoAdvancePaused = true;
    clearAllThemeAutoAdvances();
  });
  preview.addEventListener("focusout", (event) => {
    if (!preview.contains(event.relatedTarget)) {
      autoAdvancePaused = false;
      scheduleAllThemeAutoAdvances();
    }
  });
  document.addEventListener("visibilitychange", () => {
    if (document.hidden) {
      clearAllThemeAutoAdvances();
    } else {
      scheduleAllThemeAutoAdvances();
    }
  });

  renderInitialCards();
  renderStatus(activeThemeIndex);
  updateElapsed();
  updateButtons();
  scheduleAllThemeAutoAdvances();
  window.setInterval(updateElapsed, 1000);
})();
