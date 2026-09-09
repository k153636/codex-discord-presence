(() => {
  const preview = document.querySelector("[data-rpc-preview]");
  if (!preview) {
    return;
  }

  const track = preview.querySelector("[data-rpc-preview-track]");
  const initialCard = preview.querySelector("[data-rpc-card]");
  const slideNote = preview.querySelector("[data-rpc-slide-note]");
  const slideStatus = preview.querySelector("[data-rpc-slide-status]");
  const outputDetails = document.querySelector("[data-rpc-output-details]");
  const outputState = document.querySelector("[data-rpc-output-state]");
  const outputVisibility = document.querySelector("[data-rpc-output-visibility]");
  const outputArtwork = document.querySelector("[data-rpc-output-artwork]");
  const buttons = [...preview.querySelectorAll("[data-rpc-direction]")];

  if (!track || !initialCard || !slideNote || !slideStatus || buttons.length === 0) {
    return;
  }

  const slides = [
    {
      model: "GPT-5.6 Luna · Active session",
      activity: "Reviewing a connected tool",
      visibility: "Shown on your Discord profile",
      image: "assets/rpc_reading.gif",
      artwork: "Reading",
      initialElapsedSeconds: 120
    },
    {
      model: "GPT-5.6 Luna · Active session",
      activity: "Thinking through a task\nbefore writing code",
      visibility: "Shown on your Discord profile",
      image: "assets/rpc_thinking.gif",
      artwork: "Thinking",
      initialElapsedSeconds: 84
    },
    {
      model: "GPT-5.6 Luna · Active session",
      activity: "Editing a project file",
      visibility: "Shown on your Discord profile",
      image: "assets/rpc_coding.gif",
      artwork: "Coding",
      initialElapsedSeconds: 192
    }
  ];

  const trackMotionClasses = [
    "is-preparing",
    "is-positioned-left",
    "is-positioned-right",
    "is-moving-left",
    "is-moving-right"
  ];

  const getCardParts = (card) => ({
    image: card.querySelector("[data-rpc-card-image]"),
    details: card.querySelector("[data-rpc-card-details]"),
    state: card.querySelector("[data-rpc-card-state]"),
    elapsed: card.querySelector("[data-rpc-elapsed]")
  });

  const initialParts = getCardParts(initialCard);
  if (Object.values(initialParts).some((part) => !part)) {
    return;
  }

  const cloneCard = initialCard.cloneNode(true);
  const cloneParts = getCardParts(cloneCard);
  if (Object.values(cloneParts).some((part) => !part)) {
    return;
  }

  initialCard.setAttribute("aria-hidden", "false");
  cloneCard.setAttribute("aria-hidden", "true");
  track.append(cloneCard);

  const safeSeconds = (value) => Number.isFinite(value) && value >= 0 ? value : 0;

  const formatElapsed = (totalSeconds) => {
    const minutes = Math.floor(totalSeconds / 60);
    const seconds = totalSeconds % 60;
    return `${minutes}:${seconds.toString().padStart(2, "0")}`;
  };

  let activeCard = initialCard;
  let activeParts = initialParts;
  let inactiveCard = cloneCard;
  let inactiveParts = cloneParts;
  let activeIndex = 0;
  let startedAt = performance.now();
  let isTransitioning = false;

  const updateElapsed = () => {
    const activeSlide = slides[activeIndex];
    const elapsedSeconds = safeSeconds(activeSlide.initialElapsedSeconds)
      + Math.floor((performance.now() - startedAt) / 1000);
    activeParts.elapsed.textContent = formatElapsed(elapsedSeconds);
  };

  const updateButtons = () => {
    buttons.forEach((button) => {
      button.disabled = isTransitioning;
    });
  };

  const renderCard = (card, parts, slide, index) => {
    parts.details.textContent = slide.model;
    parts.state.textContent = slide.activity;
    parts.state.classList.toggle("rpc-preview-card-state--wrapped", slide.activity.includes("\n"));
    parts.image.src = slide.image;
    parts.elapsed.textContent = formatElapsed(safeSeconds(slide.initialElapsedSeconds));
    card.setAttribute("aria-label", `Discord activity example ${index + 1} of ${slides.length}: ${slide.activity.replace(/\s+/g, " ").trim()}`);
  };

  const renderOutputs = (slide, index) => {
    outputDetails?.replaceChildren(document.createTextNode(slide.model));
    outputState?.replaceChildren(document.createTextNode(slide.activity));
    outputVisibility?.replaceChildren(document.createTextNode(slide.visibility));
    outputArtwork?.replaceChildren(document.createTextNode(slide.artwork));
    slideNote.textContent = "Live preview · use the arrows to explore.";
    slideStatus.textContent = `Discord activity example ${index + 1} of ${slides.length}: ${slide.activity.replace(/\s+/g, " ").trim()}`;
  };

  const resetTrack = (firstCard, secondCard) => {
    track.style.transition = "none";
    track.classList.remove(...trackMotionClasses);
    track.insertBefore(firstCard, secondCard);
    track.style.transform = "translate3d(0, 0, 0)";
    void track.offsetWidth;
    track.style.transition = "";
    track.style.transform = "";
  };

  const swapCardReferences = () => {
    const previousActiveCard = activeCard;
    const previousActiveParts = activeParts;
    activeCard = inactiveCard;
    activeParts = inactiveParts;
    inactiveCard = previousActiveCard;
    inactiveParts = previousActiveParts;
  };

  const animateTrack = (outgoingCard, incomingCard, direction, onFinish) => {
    const isNext = direction > 0;
    const firstCard = isNext ? outgoingCard : incomingCard;
    const secondCard = isNext ? incomingCard : outgoingCard;
    const prepareClass = isNext ? "is-positioned-right" : "is-positioned-left";
    const moveClass = isNext ? "is-moving-left" : "is-moving-right";

    track.classList.remove(...trackMotionClasses);
    track.insertBefore(firstCard, secondCard);

    track.classList.add("is-preparing", prepareClass);
    incomingCard.setAttribute("aria-hidden", "false");
    outgoingCard.setAttribute("aria-hidden", "true");
    void track.offsetWidth;

    let fallbackTimer = 0;
    let hasFinished = false;

    const finishTransition = () => {
      if (hasFinished) {
        return;
      }

      hasFinished = true;
      track.removeEventListener("transitionend", finishOnTransitionEnd);
      window.clearTimeout(fallbackTimer);
      onFinish();
    };

    const finishOnTransitionEnd = (event) => {
      if (event.propertyName === "transform") {
        finishTransition();
      }
    };

    fallbackTimer = window.setTimeout(finishTransition, 600);

    window.requestAnimationFrame(() => {
      if (hasFinished) {
        return;
      }

      track.classList.remove("is-preparing", prepareClass);
      track.classList.add(moveClass);
      track.addEventListener("transitionend", finishOnTransitionEnd);
    });
  };

  const moveTo = (direction) => {
    if (isTransitioning || direction === 0) {
      return;
    }

    const nextIndex = (activeIndex + direction + slides.length) % slides.length;
    const outgoingCard = activeCard;
    const incomingCard = inactiveCard;
    const incomingParts = inactiveParts;
    const nextSlide = slides[nextIndex];

    isTransitioning = true;
    updateButtons();
    renderCard(incomingCard, incomingParts, nextSlide, nextIndex);
    renderOutputs(nextSlide, nextIndex);
    animateTrack(outgoingCard, incomingCard, direction, () => {
      resetTrack(incomingCard, outgoingCard);
      swapCardReferences();
      activeIndex = nextIndex;
      startedAt = performance.now();
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

  renderCard(activeCard, activeParts, slides[activeIndex], activeIndex);
  renderOutputs(slides[activeIndex], activeIndex);
  updateElapsed();
  window.setInterval(updateElapsed, 1000);
})();
