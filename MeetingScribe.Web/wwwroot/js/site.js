const stateClassMap = {
  pending: [],
  running: ["is-active"],
  completed: ["is-complete"],
  failed: ["is-failed"]
};

const clamp01 = (value) => Math.min(1, Math.max(0, value ?? 0));

const formatBytes = (bytes) => {
  if (!bytes && bytes !== 0) {
    return "0 MB";
  }
  const mb = bytes / (1024 * 1024);
  if (mb >= 1024) {
    return `${(mb / 1024).toFixed(2)} GB`;
  }
  return `${mb.toFixed(2)} MB`;
};

const formatMegabits = (bytes) => ((bytes * 8) / 1_000_000).toFixed(2);

const initUploadWidget = () => {
  const fileInput = document.getElementById("meetingVideo");
  const fileLabel = document.getElementById("file-name");
  const selectButton = document.getElementById("select-file");
  const dropZone = document.getElementById("file-drop");

  if (!fileInput || !fileLabel || !dropZone) {
    return;
  }

  const defaultLabel = fileLabel.dataset.default || "";

  const updateLabel = () => {
    const [file] = fileInput.files || [];
    fileLabel.textContent = file ? file.name : defaultLabel;
  };

  selectButton?.addEventListener("click", () => fileInput.click());
  fileInput.addEventListener("change", updateLabel);

  const preventDefaults = (event) => {
    event.preventDefault();
    event.stopPropagation();
  };

  ["dragenter", "dragover", "dragleave", "drop"].forEach((eventName) => {
    dropZone.addEventListener(eventName, preventDefaults, false);
  });

  ["dragenter", "dragover"].forEach((eventName) => {
    dropZone.addEventListener(eventName, () => dropZone.classList.add("is-dragging"), false);
  });

  ["dragleave", "drop"].forEach((eventName) => {
    dropZone.addEventListener(eventName, () => dropZone.classList.remove("is-dragging"), false);
  });

  dropZone.addEventListener("drop", (event) => {
    const { files } = event.dataTransfer || {};
    if (!files || !files.length) {
      return;
    }

    const dataTransfer = new DataTransfer();
    Array.from(files).forEach((file) => dataTransfer.items.add(file));
    fileInput.files = dataTransfer.files;
    fileInput.dispatchEvent(new Event("change"));
  });
};

const initProcessingFlow = () => {
  const form = document.querySelector('form[data-progress="meeting"]');
  const overlay = document.getElementById("progress-overlay");
  const operationInput = document.getElementById("operationId");
  const errorBanner = document.getElementById("error-banner");
  const resultsSection = document.getElementById("results");
  const summaryBlock = resultsSection?.querySelector('[data-result="summary"]');
  const notesList = resultsSection?.querySelector('[data-result="notes"]');
  const videoInput = document.getElementById("meetingVideo");
  const fileLabel = document.getElementById("file-name");
  const actionButton = document.getElementById("action-button");

  if (!form || !overlay) {
    return;
  }

  const getStepElement = (key) => overlay.querySelector(`[data-progress-step="${key}"]`);

  const stateLabel = (state) => {
    switch (state) {
      case "running":
        return "In progress";
      case "completed":
        return "Done";
      case "failed":
        return "Failed";
      default:
        return "Pending";
    }
  };

  const setStepState = (key, snapshot) => {
    const el = getStepElement(key);
    if (!el) {
      return;
    }

    const state = snapshot?.state || "pending";
    el.classList.remove("is-active", "is-complete", "is-failed", "is-visible");
    stateClassMap[state]?.forEach((cls) => el.classList.add(cls));

    const status = el.querySelector(".progress-step__status");
    if (status) {
      status.textContent = snapshot?.message || stateLabel(state);
    }

    const barWrapper = el.querySelector(".progress-step__bar");
    const bar = barWrapper?.querySelector("span");
    const spinner = el.querySelector(".progress-step__spinner");
    const percentValue =
      typeof snapshot?.percent === "number" ? clamp01(snapshot.percent) : null;

    if (percentValue !== null || state !== "pending") {
      el.classList.add("is-visible");
    }

    if (barWrapper) {
      barWrapper.classList.toggle("is-visible", percentValue !== null);
    }

    if (bar) {
      bar.style.width = percentValue === null ? "0%" : `${percentValue * 100}%`;
    }

    if (spinner) {
      spinner.dataset.state = state;
    }
  };

  const resetSteps = () => {
    ["upload", "transcribe", "summarize"].forEach((key) => {
      const el = getStepElement(key);
      if (el) {
        el.classList.remove("is-visible", "is-active", "is-complete", "is-failed");
        const barWrapper = el.querySelector(".progress-step__bar");
        const bar = barWrapper?.querySelector("span");
        barWrapper?.classList.remove("is-visible");
        if (bar) {
          bar.style.width = "0%";
        }
        const spinner = el.querySelector(".progress-step__spinner");
        if (spinner) {
          spinner.dataset.state = "pending";
        }
        const status = el.querySelector(".progress-step__status");
        if (status) {
          status.textContent = "Pending";
        }
      }
    });
  };

  const showOverlay = () => {
    overlay.hidden = false;
    overlay.setAttribute("aria-hidden", "false");
  };

  const hideOverlay = () => {
    overlay.hidden = true;
    overlay.setAttribute("aria-hidden", "true");
    resetSteps();
  };

  const showError = (message) => {
    if (!errorBanner) {
      return;
    }
    if (!message) {
      errorBanner.hidden = true;
      errorBanner.textContent = "";
      return;
    }
    errorBanner.hidden = false;
    errorBanner.textContent = message;
  };

  const renderResults = (result) => {
    if (!result || !resultsSection || !summaryBlock || !notesList) {
      return;
    }

    resultsSection.hidden = false;
    resultsSection.dataset.hasResult = "true";
    summaryBlock.textContent = result.businessSummary || "";

    notesList.innerHTML = "";
    (result.notes || []).forEach((note) => {
      const li = document.createElement("li");
      li.textContent = note;
      notesList.appendChild(li);
    });
  };

  const setSubmitButtonState = () => {
    if (!actionButton) {
      return;
    }
    actionButton.dataset.state = "submit";
    actionButton.textContent = "Generate decision summary";
  };

  const prepareForNewUpload = () => {
    if (!actionButton) {
      return;
    }
    actionButton.dataset.state = "reset";
    actionButton.textContent = "Upload another meeting";
  };

  const resetUI = () => {
    form.reset();
    if (videoInput) {
      videoInput.value = "";
    }
    if (fileLabel) {
      fileLabel.textContent = fileLabel.dataset.default || "No file selected";
    }
    showError("");
    if (resultsSection) {
      resultsSection.hidden = true;
      resultsSection.dataset.hasResult = "false";
    }
    if (summaryBlock) {
      summaryBlock.textContent = "";
    }
    if (notesList) {
      notesList.innerHTML = "";
    }
    if (operationInput) {
      operationInput.value = crypto.randomUUID();
    }
    setSubmitButtonState();
    resetSteps();
    hideOverlay();
  };

  const applyProgress = (snapshot) => {
    if (!snapshot) {
      return;
    }

    setStepState("upload", snapshot.upload);
    setStepState("transcribe", snapshot.transcribe);
    setStepState("summarize", snapshot.summarize);

    if (snapshot.overallState === "failed" && snapshot.message) {
      showError(snapshot.message);
    }
  };

  const pollProgress = (operationId) => {
    let isActive = true;

    const tick = async () => {
      if (!isActive) {
        return;
      }

      try {
        const response = await fetch(`/api/progress/${operationId}`, { cache: "no-store" });
        if (response.ok) {
          const snapshot = await response.json();
          applyProgress(snapshot);

          if (["completed", "failed"].includes(snapshot.overallState)) {
            isActive = false;
            return;
          }
        }
      } catch {
        // Ignore network hiccups during polling.
      }

      if (isActive) {
        setTimeout(tick, 1500);
      }
    };

    tick();
    return () => {
      isActive = false;
    };
  };

  const sendWithProgress = (formData, onUploadProgress) =>
    new Promise((resolve, reject) => {
      const xhr = new XMLHttpRequest();
      xhr.open("POST", form.action || window.location.pathname);
      xhr.responseType = "json";
      xhr.setRequestHeader("X-Requested-With", "XMLHttpRequest");

      xhr.upload.onprogress = (event) => {
        const total = event.lengthComputable ? event.total : undefined;
        onUploadProgress(event.loaded, total);
      };

      xhr.upload.onload = () => {
        const fileSize = videoInput?.files?.[0]?.size ?? 0;
        onUploadProgress(fileSize, fileSize);
      };

      xhr.onerror = () => reject(new Error("Network error"));
      xhr.onload = () => {
        const payload = xhr.response ?? {};
        if (xhr.status >= 200 && xhr.status < 300) {
          resolve(payload);
        } else {
          reject(new Error(payload?.errorMessage || "Server error"));
        }
      };

      xhr.send(formData);
    });

  const updateUploadProgress = (loaded, total) => {
    const fallbackTotal = videoInput?.files?.[0]?.size ?? total ?? 0;
    const percent = fallbackTotal ? clamp01(loaded / fallbackTotal) : 0;
    const megabits = formatMegabits(loaded || 0);
    const message = fallbackTotal
      ? `Uploading ${formatBytes(loaded)} / ${formatBytes(fallbackTotal)} · ${(percent * 100).toFixed(0)}% · ${megabits} Mbit sent`
      : `Uploading · ${megabits} Mbit sent`;

    setStepState("upload", {
      state: percent >= 1 ? "completed" : "running",
      message,
      percent
    });
  };

  setSubmitButtonState();

  form.addEventListener("submit", async (event) => {
    event.preventDefault();

    if (!form.checkValidity()) {
      form.reportValidity();
      return;
    }

    const operationId = operationInput?.value || crypto.randomUUID();
    if (operationInput) {
      operationInput.value = operationId;
    }

    const stopPolling = pollProgress(operationId);
    showError("");
    resetSteps();
    showOverlay();

    const formData = new FormData(form);
    formData.set("OperationId", operationId);

    try {
      const payload = await sendWithProgress(formData, updateUploadProgress);

      if (!payload.success) {
        const message = payload.errorMessage || (payload.errors || []).join(" ") || "Processing failed.";
        showError(message);
        return;
      }

      renderResults(payload.result);
      showError("");
      prepareForNewUpload();
    } catch (error) {
      console.error(error);
      showError("We could not process the video. Please try again.");
    } finally {
      stopPolling?.();
      hideOverlay();
    }
  });

  actionButton?.addEventListener("click", (event) => {
    if (actionButton.dataset.state === "reset") {
      event.preventDefault();
      resetUI();
    }
  });
};

document.addEventListener("DOMContentLoaded", () => {
  initUploadWidget();
  initProcessingFlow();
});
