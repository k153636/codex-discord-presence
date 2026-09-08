(() => {
  const preview = document.querySelector("[data-rpc-preview]");
  if (!preview) {
    return;
  }

  const card = preview.querySelector("[data-rpc-card]");
  const elapsed = preview.querySelector("[data-rpc-elapsed]");
  const cardImage = preview.querySelector("[data-rpc-card-image]");
  const cardDetails = preview.querySelector("[data-rpc-card-details]");
  const cardState = preview.querySelector("[data-rpc-card-state]");
  const outputDetails = document.querySelector("[data-rpc-output-details]");
  const outputState = document.querySelector("[data-rpc-output-state]");
  const outputParty = document.querySelector("[data-rpc-output-party]");
  const outputImage = document.querySelector("[data-rpc-output-image]");
  const slideNote = preview.querySelector("[data-rpc-slide-note]");
  const slideStatus = preview.querySelector("[data-rpc-slide-status]");
  const buttons = [...preview.querySelectorAll("[data-rpc-direction]")];

  if (!card || !elapsed || !cardImage || !cardDetails || !cardState || !slideNote || !slideStatus || buttons.length === 0) {
    return;
  }

  const slides = [
    {
      details: "gpt 5.6 luna max 1.5x • 125M Token",
      state: "MCP chrome-devtools",
      party: "5 / 5 from session evidence",
      image: "assets/rpc_reading.gif",
      imageKey: "rpc_reading",
      initialElapsedSeconds: 120
    },
    {
      details: "gpt 5.6 luna max 1.5x • 125M Token",
      state: "Considering frontend design usage",
      party: "5 / 5 from session evidence",
      image: "assets/rpc_thinking.gif",
      imageKey: "rpc_thinking",
      initialElapsedSeconds: 84
    },
    {
      details: "gpt 5.6 luna max 1.5x • 125M Token",
      state: "Editing docs/preview.js",
      party: "5 / 5 from session evidence",
      image: "assets/rpc_coding.gif",
      imageKey: "rpc_coding",
      initialElapsedSeconds: 192
    }
  ];

  const safeSeconds = (value) => Number.isFinite(value) && value >= 0 ? value : 0;

  const formatElapsed = (totalSeconds) => {
    const minutes = Math.floor(totalSeconds / 60);
    const seconds = totalSeconds % 60;
    return `${minutes}:${seconds.toString().padStart(2, "0")}`;
  };

  let activeIndex = 0;
  let startedAt = performance.now();
  let isTransitioning = false;

  const updateElapsed = () => {
    const activeSlide = slides[activeIndex];
    const elapsedSeconds = safeSeconds(activeSlide.initialElapsedSeconds)
      + Math.floor((performance.now() - startedAt) / 1000);
    elapsed.textContent = formatElapsed(elapsedSeconds);
  };

  const updateButtons = () => {
    buttons.forEach((button) => {
      button.disabled = isTransitioning;
    });
  };

  const renderSlide = (slide, index) => {
    cardDetails.textContent = slide.details;
    cardState.textContent = slide.state;
    cardImage.src = slide.image;
    outputDetails?.replaceChildren(document.createTextNode(slide.details));
    outputState?.replaceChildren(document.createTextNode(slide.state));
    outputParty?.replaceChildren(document.createTextNode(slide.party));
    outputImage?.replaceChildren(document.createTextNode(slide.imageKey));
    card.setAttribute("aria-label", `RPC preview state ${index + 1} of ${slides.length}: ${slide.state}`);
    slideNote.textContent = `State ${index + 1} / ${slides.length} · animated GIF · timer starts at ${formatElapsed(safeSeconds(slide.initialElapsedSeconds))}`;
    slideStatus.textContent = `RPC preview state ${index + 1} of ${slides.length}: ${slide.state}`;
    startedAt = performance.now();
    updateElapsed();
  };

  const moveTo = (direction) => {
    if (isTransitioning || direction === 0) {
      return;
    }

    const nextIndex = (activeIndex + direction + slides.length) % slides.length;
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
      activeIndex = nextIndex;
      renderSlide(slides[activeIndex], activeIndex);
      return;
    }

    isTransitioning = true;
    updateButtons();
    const exitClass = direction > 0 ? "is-exiting-left" : "is-exiting-right";
    const enterClass = direction > 0 ? "is-entering-from-right" : "is-entering-from-left";
    card.classList.add(exitClass);

    window.setTimeout(() => {
      activeIndex = nextIndex;
      renderSlide(slides[activeIndex], activeIndex);
      card.classList.remove(exitClass);
      card.classList.add(enterClass);
      void card.offsetWidth;
      card.classList.add("is-settled");

      window.setTimeout(() => {
        card.classList.remove(enterClass, "is-settled");
        isTransitioning = false;
        updateButtons();
      }, 280);
    }, 170);
  };

  buttons.forEach((button) => {
    button.addEventListener("click", () => {
      const direction = Number.parseInt(button.dataset.rpcDirection ?? "0", 10);
      moveTo(Number.isFinite(direction) ? direction : 0);
    });
  });

  renderSlide(slides[activeIndex], activeIndex);
  updateElapsed();
  window.setInterval(updateElapsed, 1000);
})();
