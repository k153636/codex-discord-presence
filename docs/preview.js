(() => {
  const preview = document.querySelector("[data-rpc-preview]");
  if (!preview) {
    return;
  }

  const track = preview.querySelector("[data-rpc-preview-track]");
  const carousel = preview.querySelector(".rpc-preview-carousel");
  const initialCard = preview.querySelector("[data-rpc-card]");
  const slideStatus = preview.querySelector("[data-rpc-slide-status]");
  const buttons = [...preview.querySelectorAll("[data-rpc-direction]")];

  if (!track || !carousel || !initialCard || !slideStatus || buttons.length === 0) {
    return;
  }

  const modelLabel = "gpt 6 astra";
  const baseTokenCount = 35_600_000;
  const rpcThemes = [
    {
      states: [
        { activity: "MCP chrome-devtools", image: "assets/rpc_reading.gif", initialElapsedSeconds: 120, tokenGrowthMinPerSecond: 180_000, tokenGrowthMaxPerSecond: 480_000, tokenBurstMin: 160_000, tokenBurstMax: 360_000, effort: "xhigh" }
      ]
    },
    {
      states: [
        { activity: "Keeping the signal concise", image: "assets/rpc_thinking.gif", initialElapsedSeconds: 154, tokenGrowthMinPerSecond: 160_000, tokenGrowthMaxPerSecond: 420_000, tokenBurstMin: 120_000, tokenBurstMax: 360_000, effort: "max" },
        { activity: "Summarizing the active work", image: "assets/rpc_thinking.gif", initialElapsedSeconds: 146, tokenGrowthMinPerSecond: 120_000, tokenGrowthMaxPerSecond: 320_000, tokenBurstMin: 80_000, tokenBurstMax: 240_000, effort: "high" },
        { activity: "Turning context into one clear line", image: "assets/rpc_thinking.gif", initialElapsedSeconds: 172, tokenGrowthMinPerSecond: 380_000, tokenGrowthMaxPerSecond: 900_000, tokenBurstMin: 320_000, tokenBurstMax: 760_000, effort: "xhigh" }
      ]
    },
    {
      states: [
        { activity: "Comparing rendered bounds", image: "assets/rpc_thinking.gif", initialElapsedSeconds: 124, tokenGrowthMinPerSecond: 260_000, tokenGrowthMaxPerSecond: 720_000, tokenBurstMin: 240_000, tokenBurstMax: 600_000, effort: "high" },
        { activity: "Keeping the frame stable", image: "assets/rpc_coding.gif", initialElapsedSeconds: 150, tokenGrowthMinPerSecond: 420_000, tokenGrowthMaxPerSecond: 980_000, tokenBurstMin: 360_000, tokenBurstMax: 820_000, effort: "xhigh" },
        { activity: "Reproducing the timer drift", image: "assets/rpc_coding.gif", initialElapsedSeconds: 168, tokenGrowthMinPerSecond: 650_000, tokenGrowthMaxPerSecond: 1_400_000, tokenBurstMin: 600_000, tokenBurstMax: 1_400_000, effort: "max" },
        { activity: "Tracing the icon squeeze", image: "assets/rpc_coding.gif", initialElapsedSeconds: 172, tokenGrowthMinPerSecond: 380_000, tokenGrowthMaxPerSecond: 900_000, tokenBurstMin: 320_000, tokenBurstMax: 760_000, effort: "xhigh" }
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

  const selectInitialStateIndexes = () => {
    return rpcThemes.map((theme) => {
      return Math.floor(Math.random() * theme.states.length);
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
    return rpcThemes[themeIndex].states[themeStateIndexes[themeIndex]];
  };

  const formatTokenCount = (tokenCount) => `${(tokenCount / 1_000_000).toFixed(1)}M Token`;

  const getRandomTokenDelta = (minimum, maximum) => Math.round(
    minimum + Math.random() * (maximum - minimum)
  );

  const getTokenCount = (themeIndex, now) => {
    const state = getThemeState(themeIndex);
    const elapsedSeconds = Math.max(0, Math.floor((now - themeTokenUpdatedAt[themeIndex]) / 1000));
    for (let second = 0; second < elapsedSeconds; second += 1) {
      themeTokenCounts[themeIndex] += getRandomTokenDelta(
        state.tokenGrowthMinPerSecond,
        state.tokenGrowthMaxPerSecond
      );
    }
    if (elapsedSeconds > 0) {
      themeTokenUpdatedAt[themeIndex] += elapsedSeconds * 1000;
    }

    return themeTokenCounts[themeIndex];
  };

  const formatDetails = (themeIndex, now) => `${modelLabel} ${themeEfforts[themeIndex]} 1.5x • ${formatTokenCount(getTokenCount(themeIndex, now))}`;

  const getNextStateIndex = (themeIndex) => {
    const currentStateIndex = themeStateIndexes[themeIndex];
    const availableIndexes = rpcThemes[themeIndex].states
      .map((_, index) => index)
      .filter((index) => index !== currentStateIndex);
    return availableIndexes.length > 0
      ? availableIndexes[Math.floor(Math.random() * availableIndexes.length)]
      : currentStateIndex;
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
    const state = getThemeState(themeIndex);
    const activity = state.activity;
    const details = formatDetails(themeIndex, now);
    parts.details.textContent = details;
    parts.state.textContent = activity;
    parts.state.classList.toggle("rpc-preview-card-state--wrapped", activity.includes("\n"));
    parts.image.src = state.image;
    parts.elapsed.textContent = formatElapsed(safeSeconds(state.initialElapsedSeconds));
    card.setAttribute("aria-label", `Discord activity ${themeIndex + 1} of ${rpcThemes.length}: ${details}: ${activity.replace(/\s+/g, " ").trim()}`);
  };

  const renderStatus = (themeIndex) => {
    const state = getThemeState(themeIndex);
    slideStatus.textContent = state.activity.replace(/\s+/g, " ").trim();
  };

  let activeThemeIndex = 0;
  let cardsByOffset = new Map(cards.map((card, index) => [index - 2, card]));
  let isTransitioning = false;
  const autoAdvanceTimers = rpcThemes.map(() => 0);
  let autoAdvancePaused = preview.matches(":hover");
  const autoAdvanceMinDelay = 5400;
  const autoAdvanceMaxDelay = 8200;

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
    if (autoAdvancePaused || document.hidden) {
      return;
    }

    autoAdvanceTimers[themeIndex] = window.setTimeout(() => {
      autoAdvanceTimers[themeIndex] = 0;
      if (!document.hidden && !autoAdvancePaused) {
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
    themeTokenCounts[themeIndex] += getRandomTokenDelta(
      nextState.tokenBurstMin,
      nextState.tokenBurstMax
    );
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

  const swipeThreshold = 42;
  let swipeStart = null;

  const clearSwipe = () => {
    swipeStart = null;
  };

  const capturePointer = (method, pointerId) => {
    try {
      carousel[method]?.(pointerId);
    } catch {
      // Synthetic pointer events and browsers without capture support can skip this.
    }
  };

  carousel.addEventListener("pointerdown", (event) => {
    const target = event.target instanceof Element ? event.target : null;
    if (!event.isPrimary || event.pointerType === "mouse" || target?.closest("[data-rpc-direction]")) {
      return;
    }

    swipeStart = {
      pointerId: event.pointerId,
      x: event.clientX,
      y: event.clientY
    };
    capturePointer("setPointerCapture", event.pointerId);
  });

  carousel.addEventListener("pointerup", (event) => {
    if (!swipeStart || event.pointerId !== swipeStart.pointerId) {
      return;
    }

    const deltaX = event.clientX - swipeStart.x;
    const deltaY = event.clientY - swipeStart.y;
    clearSwipe();
    capturePointer("releasePointerCapture", event.pointerId);

    if (Math.abs(deltaX) < swipeThreshold || Math.abs(deltaX) <= Math.abs(deltaY) * 1.15) {
      return;
    }

    moveTo(deltaX < 0 ? 1 : -1);
  });

  carousel.addEventListener("pointercancel", clearSwipe);
  carousel.addEventListener("lostpointercapture", clearSwipe);

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
