(() => {
  const root = document.documentElement;
  const header = document.querySelector(".site-header");
  const targets = [
    ...new Set([
      ...document.querySelectorAll("[data-motion]"),
      ...document.querySelectorAll(".prose > *")
    ])
  ];

  const setupPreviewCenterLight = () => {
    const previewShell = document.querySelector("[data-rpc-preview]");
    if (!previewShell) {
      return () => {};
    }

    const activationDistance = 220;
    const fullBrightnessDistance = 32;
    const responseExponent = 0.56;
    const smoothing = 0.16;
    let animationFrame = 0;
    let currentProximity = 0;
    let trackingUntil = 0;

    const clamp = (value, minimum, maximum) => Math.min(Math.max(value, minimum), maximum);

    const getTargetProximity = () => {
      const shellRect = previewShell.getBoundingClientRect();
      const activeCard = previewShell.querySelector(".rpc-preview-card--active");
      if (!activeCard) {
        return 0;
      }

      const cardRect = activeCard.getBoundingClientRect();
      const lightCenterX = shellRect.left + shellRect.width / 2;
      const lightCenterY = shellRect.top + shellRect.height / 2;
      const cardCenterX = cardRect.left + cardRect.width / 2;
      const cardCenterY = cardRect.top + cardRect.height / 2;
      const distance = Math.hypot(cardCenterX - lightCenterX, cardCenterY - lightCenterY);
      const normalizedDistance = clamp(
        (activationDistance - distance) / (activationDistance - fullBrightnessDistance),
        0,
        1
      );

      // A sub-linear curve makes the glow arrive decisively at the stage center.
      return Math.pow(normalizedDistance, responseExponent);
    };

    const renderCenterLight = () => {
      animationFrame = 0;
      const targetProximity = getTargetProximity();
      currentProximity += (targetProximity - currentProximity) * smoothing;

      if (Math.abs(targetProximity - currentProximity) < 0.003) {
        currentProximity = targetProximity;
      }

      previewShell.style.setProperty("--rpc-light-proximity", currentProximity.toFixed(3));
      if (performance.now() < trackingUntil || currentProximity !== targetProximity) {
        animationFrame = window.requestAnimationFrame(renderCenterLight);
      }
    };

    const scheduleCenterLight = (trackTransition = false) => {
      if (trackTransition) {
        trackingUntil = performance.now() + 720;
      }
      if (animationFrame === 0) {
        animationFrame = window.requestAnimationFrame(renderCenterLight);
      }
    };

    const handleTransitionRun = (event) => {
      if (event.propertyName === "transform") {
        scheduleCenterLight(true);
      }
    };

    const handleTransitionEnd = (event) => {
      if (event.propertyName === "transform") {
        trackingUntil = 0;
        scheduleCenterLight();
      }
    };

    const handleLayoutChange = () => {
      scheduleCenterLight();
    };

    const cardObserver = typeof MutationObserver === "function"
      ? new MutationObserver(() => scheduleCenterLight(true))
      : null;
    cardObserver?.observe(previewShell, {
      attributes: true,
      attributeFilter: ["class"],
      subtree: true
    });

    previewShell.addEventListener("transitionrun", handleTransitionRun);
    previewShell.addEventListener("transitionend", handleTransitionEnd);
    window.addEventListener("resize", handleLayoutChange, { passive: true });
    window.addEventListener("scroll", handleLayoutChange, { passive: true });
    scheduleCenterLight();

    return () => {
      if (animationFrame !== 0) {
        window.cancelAnimationFrame(animationFrame);
      }
      previewShell.removeEventListener("transitionrun", handleTransitionRun);
      previewShell.removeEventListener("transitionend", handleTransitionEnd);
      window.removeEventListener("resize", handleLayoutChange);
      window.removeEventListener("scroll", handleLayoutChange);
      cardObserver?.disconnect();
      previewShell.style.removeProperty("--rpc-light-proximity");
    };
  };

  const cleanupPreviewCenterLight = setupPreviewCenterLight();
  window.addEventListener("pagehide", cleanupPreviewCenterLight, { once: true });

  if (targets.length === 0 || !("IntersectionObserver" in window)) {
    return;
  }

  let observer;
  let motionStarted = false;
  let scrollFrame = 0;

  const reveal = (target) => {
    target.classList.add("is-visible");
    observer?.unobserve(target);
  };

  const setStagger = () => {
    const groupIndexes = new Map();

    targets.forEach((target) => {
      const group = target.closest(".prose") ?? target.parentElement;
      const index = groupIndexes.get(group) ?? 0;
      const delay = Math.min(index * 58, 350);

      groupIndexes.set(group, index + 1);
      target.style.setProperty("--motion-delay", `${delay}ms`);
    });
  };

  const startMotion = () => {
    if (motionStarted) {
      return;
    }

    setStagger();
    root.classList.add("motion-enhanced");
    observer = new IntersectionObserver(
      (entries) => {
        entries.forEach((entry) => {
          if (entry.isIntersecting) {
            reveal(entry.target);
          }
        });
      },
      {
        threshold: 0.08,
        rootMargin: "0px 0px 12% 0px"
      }
    );

    targets.forEach((target) => observer.observe(target));
    motionStarted = true;
  };

  const updateHeader = () => {
    scrollFrame = 0;
    header?.classList.toggle("is-scrolled", window.scrollY > 12);
  };

  const scheduleHeaderUpdate = () => {
    if (scrollFrame === 0) {
      scrollFrame = window.requestAnimationFrame(updateHeader);
    }
  };

  startMotion();
  updateHeader();
  window.addEventListener("scroll", scheduleHeaderUpdate, { passive: true });

  window.addEventListener("pagehide", () => {
    observer?.disconnect();
    window.removeEventListener("scroll", scheduleHeaderUpdate);
  }, { once: true });
})();
