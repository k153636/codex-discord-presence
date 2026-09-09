(() => {
  const root = document.documentElement;
  const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");
  const header = document.querySelector(".site-header");
  const targets = [
    ...new Set([
      ...document.querySelectorAll("[data-motion]"),
      ...document.querySelectorAll(".prose > *")
    ])
  ];

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
    if (motionStarted || reducedMotion.matches) {
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

  const stopMotion = () => {
    root.classList.remove("motion-enhanced");
    targets.forEach((target) => target.classList.add("is-visible"));
    observer?.disconnect();
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

  const handleMotionPreferenceChange = () => {
    if (reducedMotion.matches) {
      stopMotion();
      return;
    }

    startMotion();
  };

  startMotion();
  updateHeader();
  window.addEventListener("scroll", scheduleHeaderUpdate, { passive: true });

  if (typeof reducedMotion.addEventListener === "function") {
    reducedMotion.addEventListener("change", handleMotionPreferenceChange);
  } else {
    reducedMotion.addListener(handleMotionPreferenceChange);
  }

  window.addEventListener("pagehide", () => {
    observer?.disconnect();
    window.removeEventListener("scroll", scheduleHeaderUpdate);
  }, { once: true });
})();
