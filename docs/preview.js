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

  const normalizeIndex = (index) => (index + slides.length) % slides.length;

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

  let activeIndex = 0;
  let cardsByOffset = new Map(cards.map((card, index) => [index - 2, card]));
  let activeCard = cardsByOffset.get(0);
  let activeParts = partsByCard.get(activeCard);
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
      const index = normalizeIndex(activeIndex + offset);
      renderCard(card, partsByCard.get(card), slides[index], index);
      setCardPosition(card, offset);
    }
  };

  const moveTo = (direction) => {
    if (isTransitioning || direction === 0) {
      return;
    }

    const step = direction > 0 ? 1 : -1;
    const nextIndex = normalizeIndex(activeIndex + step);
    const incomingOffset = step > 0 ? 1 : -1;
    const incomingCard = cardsByOffset.get(incomingOffset);
    const incomingParts = partsByCard.get(incomingCard);

    isTransitioning = true;
    updateButtons();
    renderCard(incomingCard, incomingParts, slides[nextIndex], nextIndex);
    renderOutputs(slides[nextIndex], nextIndex);

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
      const farIndex = normalizeIndex(nextIndex + resetOffset);

      farCard.style.transition = "none";
      renderCard(farCard, farParts, slides[farIndex], farIndex);
      setCardPosition(farCard, resetOffset);
      void farCard.offsetWidth;
      farCard.style.transition = "";

      nextCardsByOffset.delete(transitionFarOffset);
      nextCardsByOffset.set(resetOffset, farCard);
      cardsByOffset = nextCardsByOffset;
      activeIndex = nextIndex;
      activeCard = cardsByOffset.get(0);
      activeParts = partsByCard.get(activeCard);
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

  renderInitialCards();
  renderOutputs(slides[activeIndex], activeIndex);
  updateElapsed();
  updateButtons();
  window.setInterval(updateElapsed, 1000);
})();
