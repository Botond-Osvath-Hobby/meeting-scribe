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

// Simple markdown to HTML converter (for client-side updates)
const convertMarkdownToHtml = (markdown) => {
  if (!markdown) return "";
  
  // This is a simple implementation - the server-side Markdig library does the full conversion
  // This is just for client-side updates after regeneration
  let html = markdown
    .replace(/### (.*?)(\n|$)/g, "<h3>$1</h3>")
    .replace(/## (.*?)(\n|$)/g, "<h2>$1</h2>")
    .replace(/# (.*?)(\n|$)/g, "<h1>$1</h1>")
    .replace(/\*\*(.*?)\*\*/g, "<strong>$1</strong>")
    .replace(/\*(.*?)\*/g, "<em>$1</em>")
    .replace(/\n\n/g, "</p><p>")
    .replace(/^(.+)$/gm, (match) => match.startsWith("<") ? match : `<p>${match}</p>`);
  
  return html;
};

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

// Shared render functions for transcript and summary
const renderTranscript = (transcript) => {
  console.log("renderTranscript called with:", transcript);
  
  if (!transcript) {
    console.error("No transcript data provided");
    return;
  }
  
  const resultsSection = document.getElementById("results");
  if (!resultsSection) {
    console.error("Results section not found");
    return;
  }

  // Show results section
  resultsSection.hidden = false;
  resultsSection.dataset.hasResult = "false";
  console.log("Results section shown");
  
  // Show transcript card
  const transcriptCard = document.getElementById("transcriptCard");
  if (transcriptCard) {
    transcriptCard.style.display = "block";
    console.log("Transcript card shown");
  }
  
  // Show agent card
  const agentCard = document.getElementById("agentCard");
  if (agentCard) {
    agentCard.style.display = "block";
    console.log("Agent card shown");
    
    // Smooth scroll to agent selector after a brief delay
    setTimeout(() => {
      agentCard.scrollIntoView({ 
        behavior: "smooth", 
        block: "center",
        inline: "nearest"
      });
    }, 300);
  }
  
  // Update notes list
  const notesList = document.querySelector('[data-result="notes"]');
  if (notesList) {
    notesList.innerHTML = "";
    const notes = transcript.notes || transcript.Notes || [];
    console.log("Rendering notes:", notes.length, "notes found");
    
    notes.forEach((note) => {
      const li = document.createElement("li");
      li.textContent = note;
      notesList.appendChild(li);
    });
  } else {
    console.error("Notes list not found");
  }

  // Update transcript path
  const transcriptPathInput = document.getElementById("transcriptPath");
  const transcriptPath = transcript.transcriptPath || transcript.TranscriptPath;
  console.log("Transcript path:", transcriptPath);
  
  if (transcriptPathInput && transcriptPath) {
    transcriptPathInput.value = transcriptPath;
  }

  console.log("Transcript rendering complete");
};

const renderSummary = (result) => {
  if (!result) {
    return;
  }

  const summaryBlock = document.querySelector('[data-result="summary"]');
  if (!summaryBlock) {
    console.error("Summary block not found");
    return;
  }

  const resultsSection = document.getElementById("results");
  if (resultsSection) {
    resultsSection.dataset.hasResult = "true";
  }
  
  // Update summary - always convert markdown to HTML for AJAX responses
  if (result.businessSummary) {
    summaryBlock.innerHTML = convertMarkdownToHtml(result.businessSummary);
  }

  // Show the summary card
  const summaryCard = document.getElementById("summaryCard");
  if (summaryCard) {
    summaryCard.style.display = "block";
    
    // Smooth scroll to summary card after a brief delay (so the card is fully rendered)
    setTimeout(() => {
      summaryCard.scrollIntoView({ 
        behavior: "smooth", 
        block: "start",
        inline: "nearest"
      });
    }, 300);
  }

  // Hide the agent selector (or show regenerate button instead)
  const generateButton = document.getElementById("generateSummary");
  if (generateButton) {
    generateButton.textContent = "🔄 Regenerate with Different Agent";
    generateButton.dataset.initialState = "false";
  }
};

const initProcessingFlow = () => {
  const form = document.querySelector('form[data-progress="meeting"]');
  const overlay = document.getElementById("progress-overlay");
  const operationInput = document.getElementById("operationId");
  const errorBanner = document.getElementById("error-banner");
  const resultsSection = document.getElementById("results");
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
    
    // Hide all result cards
    const transcriptCard = document.getElementById("transcriptCard");
    if (transcriptCard) {
      transcriptCard.style.display = "none";
    }
    const agentCard = document.getElementById("agentCard");
    if (agentCard) {
      agentCard.style.display = "none";
    }
    const summaryCard = document.getElementById("summaryCard");
    if (summaryCard) {
      summaryCard.style.display = "none";
    }
    
    // Clear content
    const summaryBlock = document.querySelector('[data-result="summary"]');
    if (summaryBlock) {
      summaryBlock.textContent = "";
    }
    const notesList = document.querySelector('[data-result="notes"]');
    if (notesList) {
      notesList.innerHTML = "";
    }
    if (operationInput) {
      operationInput.value = crypto.randomUUID();
    }
    setSubmitButtonState();
    
    // Show the main submit button again
    if (actionButton) {
      actionButton.style.display = "block";
    }
    
    resetSteps();
    hideOverlay();
  };

  const applyProgress = (snapshot, includeStep3 = false) => {
    if (!snapshot) {
      return;
    }

    setStepState("upload", snapshot.upload);
    setStepState("transcribe", snapshot.transcribe);
    
    // Only show step 3 progress when explicitly requested (during summary generation)
    if (includeStep3) {
      setStepState("summarize", snapshot.summarize);
    }

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
          applyProgress(snapshot, false); // Don't show step 3 during initial upload

          // After transcription is complete, stop polling (we'll wait for user to generate summary)
          if (snapshot.transcribe?.state === "completed") {
            isActive = false;
            return;
          }

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
      console.log("Upload complete, received payload:", payload);

      if (!payload.success) {
        const message = payload.errorMessage || (payload.errors || []).join(" ") || "Processing failed.";
        showError(message);
        hideOverlay();
        return;
      }

      // After upload, we only have transcript, not summary yet
      if (payload.transcript) {
        console.log("Transcript found in payload, rendering...");
        
        // Hide overlay first, then show results
        hideOverlay();
        stopPolling?.();
        
        // Render the transcript and agent selector
        renderTranscript(payload.transcript);
        
        // Hide the main submit button (we'll use the Generate Summary button in the agent selector)
        if (actionButton) {
          actionButton.style.display = "none";
        }
      } else {
        console.warn("No transcript in payload");
      }
      
      showError("");
    } catch (error) {
      console.error("Upload error:", error);
      showError("We could not process the video. Please try again.");
      hideOverlay();
      stopPolling?.();
    }
  });

  actionButton?.addEventListener("click", (event) => {
    if (actionButton.dataset.state === "reset") {
      event.preventDefault();
      resetUI();
    }
  });
};

const initAgentSelector = () => {
  const agentPresetSelect = document.getElementById("agentPreset");
  const systemPromptInput = document.getElementById("systemPromptInput");
  const userPromptInput = document.getElementById("userPromptInput");
  const generateButton = document.getElementById("generateSummary");
  const transcriptPathInput = document.getElementById("transcriptPath");
  const operationInput = document.getElementById("operationId");
  const summaryBlock = document.querySelector('[data-result="summary"]');
  const overlay = document.getElementById("progress-overlay");

  if (!agentPresetSelect || !systemPromptInput || !userPromptInput) {
    return;
  }

  // Handle preset selection
  agentPresetSelect.addEventListener("change", () => {
    const selectedOption = agentPresetSelect.options[agentPresetSelect.selectedIndex];
    const systemPrompt = selectedOption.dataset.systemPrompt;
    const userPrompt = selectedOption.dataset.userPrompt;

    if (systemPrompt) {
      systemPromptInput.value = systemPrompt;
    }
    if (userPrompt) {
      userPromptInput.value = userPrompt;
    }
  });

  // Handle generate button click (in agent selector)
  if (generateButton) {
    generateButton.addEventListener("click", async () => {
      await handleSummaryGeneration(generateButton);
    });
  }

  // Handle regenerate button click (in summary card)
  const regenerateButton = document.getElementById("regenerateSummary");
  if (regenerateButton) {
    regenerateButton.addEventListener("click", async () => {
      // Scroll back to agent selector
      const agentSelector = document.getElementById("agentSelector");
      if (agentSelector) {
        agentSelector.scrollIntoView({ behavior: "smooth", block: "center" });
      }
      
      // Focus on the preset dropdown
      if (agentPresetSelect) {
        setTimeout(() => agentPresetSelect.focus(), 300);
      }
      
      // Optionally trigger generation automatically or let user modify first
      // await handleSummaryGeneration(generateButton);
    });
  }

  const handleSummaryGeneration = async (buttonElement) => {
    if (!buttonElement) return;
    const transcriptPath = transcriptPathInput?.value;
    const systemPrompt = systemPromptInput.value.trim();
    const userPromptTemplate = userPromptInput.value.trim();

    if (!transcriptPath) {
      alert("Transcript not found. Please upload a video first.");
      return;
    }

    if (!systemPrompt || !userPromptTemplate) {
      alert("Both system prompt and user prompt template are required.");
      return;
    }

    // Always generate a new operation ID for each summary generation
    const operationId = crypto.randomUUID();
    if (operationInput) {
      operationInput.value = operationId;
    }

    const isInitialGeneration = buttonElement.dataset.initialState === "true";
    const originalButtonText = buttonElement.textContent;

    buttonElement.disabled = true;
    buttonElement.textContent = isInitialGeneration ? "✨ Generating..." : "🔄 Regenerating...";

    // Show overlay with steps 1-2 completed, step 3 active
    const showSummarizeStep = () => {
      if (overlay) {
        overlay.hidden = false;
        overlay.setAttribute("aria-hidden", "false");
        
        // Show upload step as completed
        const uploadStep = overlay.querySelector('[data-progress-step="upload"]');
        if (uploadStep) {
          uploadStep.classList.add("is-visible", "is-complete");
          uploadStep.classList.remove("is-active", "is-failed");
          const status = uploadStep.querySelector(".progress-step__status");
          if (status) {
            status.textContent = "Done";
          }
          const barWrapper = uploadStep.querySelector(".progress-step__bar");
          const bar = barWrapper?.querySelector("span");
          if (barWrapper) {
            barWrapper.classList.add("is-visible");
          }
          if (bar) {
            bar.style.width = "100%";
          }
        }
        
        // Show transcribe step as completed
        const transcribeStep = overlay.querySelector('[data-progress-step="transcribe"]');
        if (transcribeStep) {
          transcribeStep.classList.add("is-visible", "is-complete");
          transcribeStep.classList.remove("is-active", "is-failed");
          const status = transcribeStep.querySelector(".progress-step__status");
          if (status) {
            status.textContent = "Done";
          }
          const barWrapper = transcribeStep.querySelector(".progress-step__bar");
          const bar = barWrapper?.querySelector("span");
          if (barWrapper) {
            barWrapper.classList.add("is-visible");
          }
          if (bar) {
            bar.style.width = "100%";
          }
        }
        
        // Show summarize step as active (properly reset from any previous state)
        const summarizeStep = overlay.querySelector('[data-progress-step="summarize"]');
        if (summarizeStep) {
          // First remove all state classes
          summarizeStep.classList.remove("is-complete", "is-failed");
          // Then add the active state
          summarizeStep.classList.add("is-visible", "is-active");
          
          const status = summarizeStep.querySelector(".progress-step__status");
          if (status) {
            status.textContent = isInitialGeneration ? "Generating summary with selected agent" : "Regenerating with new prompts";
          }
          
          // Make sure the spinner is visible (in case it was hidden)
          const spinner = summarizeStep.querySelector(".progress-step__spinner");
          if (spinner) {
            spinner.dataset.state = "running";
          }
          
          // Reset and hide token counter
          const tokenCounter = document.getElementById("tokenCounter");
          const tokenCount = document.getElementById("tokenCount");
          const tokenDetails = document.getElementById("tokenDetails");
          if (tokenCounter && tokenCount && tokenDetails) {
            tokenCounter.style.display = "none";
            tokenCount.textContent = "0";
            tokenDetails.textContent = "";
          }
        }
      }
    };

    showSummarizeStep();

    // Poll progress for summary generation only
    const pollSummaryProgress = (operationId) => {
      let isActive = true;

      const tick = async () => {
        if (!isActive) {
          return;
        }

        try {
          const response = await fetch(`/api/progress/${operationId}`, { cache: "no-store" });
          if (response.ok) {
            const snapshot = await response.json();
            
            const summarizeStep = overlay?.querySelector('[data-progress-step="summarize"]');
            if (summarizeStep && snapshot.summarize) {
              const status = summarizeStep.querySelector(".progress-step__status");
              if (status) {
                status.textContent = snapshot.summarize.message || "In progress";
              }
              
              // Update token counter if we have token data (from summarizeGenerate stage)
              const tokenCounter = document.getElementById("tokenCounter");
              const tokenCount = document.getElementById("tokenCount");
              const tokenDetails = document.getElementById("tokenDetails");
              
              // Check summarizeGenerate stage for detailed token data (camelCase from JSON)
              if (snapshot.summarizeGenerate && tokenCounter && tokenCount && tokenDetails) {
                const genData = snapshot.summarizeGenerate;
                const message = genData.message || "";
                
                console.log("SummarizeGenerate data:", genData);
                console.log("Token message:", message);
                
                // Parse message format: "Generating chunk 1/2: 100/1024 tokens (10%)"
                const tokenMatch = message.match(/:\s*(\d+)\/(\d+)\s+tokens/);
                const chunkMatch = message.match(/chunk\s+(\d+)\/(\d+)/i);
                
                if (tokenMatch) {
                  const currentTokens = parseInt(tokenMatch[1]);
                  const maxTokens = parseInt(tokenMatch[2]);
                  
                  console.log(`Showing token counter: ${currentTokens}/${maxTokens}`);
                  tokenCounter.style.display = "block";
                  tokenCount.textContent = currentTokens.toLocaleString();
                  
                  if (chunkMatch) {
                    const currentChunk = parseInt(chunkMatch[1]);
                    const totalChunks = parseInt(chunkMatch[2]);
                    tokenDetails.textContent = `Chunk ${currentChunk}/${totalChunks} • Max ${maxTokens.toLocaleString()}`;
                  } else {
                    tokenDetails.textContent = `Max ${maxTokens.toLocaleString()}`;
                  }
                } else {
                  console.log("No token match in message:", message);
                }
              } else {
                if (!snapshot.summarizeGenerate) {
                  console.log("No summarizeGenerate in snapshot");
                }
              }
              
              // Update state classes
              if (snapshot.summarize.state === "completed") {
                summarizeStep.classList.remove("is-active");
                summarizeStep.classList.add("is-complete");
                
                // Hide token counter when complete
                if (tokenCounter) {
                  tokenCounter.style.display = "none";
                }
              } else if (snapshot.summarize.state === "failed") {
                summarizeStep.classList.remove("is-active");
                summarizeStep.classList.add("is-failed");
                
                // Hide token counter on failure
                if (tokenCounter) {
                  tokenCounter.style.display = "none";
                }
              }
            }

            if (snapshot.summarize?.state === "completed" || snapshot.summarize?.state === "failed") {
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

    const stopPolling = pollSummaryProgress(operationId);

    try {
      const formData = new FormData();
      formData.append("TranscriptPath", transcriptPath);
      formData.append("SystemPrompt", systemPrompt);
      formData.append("UserPromptTemplate", userPromptTemplate);
      formData.append("OperationId", operationId);

      const response = await fetch("?handler=GenerateSummary", {
        method: "POST",
        headers: {
          "X-Requested-With": "XMLHttpRequest",
          "RequestVerificationToken": document.querySelector('input[name="__RequestVerificationToken"]')?.value || ""
        },
        body: formData
      });

      const payload = await response.json();

      if (!payload.success) {
        const message = payload.errorMessage || (payload.errors || []).join(" ") || "Regeneration failed.";
        alert(message);
        return;
      }

      // Update the summary display
      if (payload.result?.businessSummary) {
        renderSummary(payload.result);
        
        // Now that we have the summary, show "Upload another meeting" button
        const actionButton = document.getElementById("action-button");
        if (actionButton) {
          actionButton.style.display = "block";
          actionButton.dataset.state = "reset";
          actionButton.textContent = "Upload another meeting";
        }
      }

      buttonElement.textContent = "✓ Summary Generated!";
      setTimeout(() => {
        buttonElement.textContent = "🔄 Regenerate with Different Agent";
        buttonElement.dataset.initialState = "false";
      }, 2000);
    } catch (error) {
      console.error(error);
      alert("Failed to generate summary. Please try again.");
      buttonElement.textContent = originalButtonText;
    } finally {
      stopPolling?.();
      buttonElement.disabled = false;
      
      if (overlay) {
        overlay.hidden = true;
        overlay.setAttribute("aria-hidden", "true");
        
        // Reset all steps
        const uploadStep = overlay.querySelector('[data-progress-step="upload"]');
        if (uploadStep) {
          uploadStep.classList.remove("is-visible", "is-active", "is-complete", "is-failed");
          const barWrapper = uploadStep.querySelector(".progress-step__bar");
          const bar = barWrapper?.querySelector("span");
          if (barWrapper) {
            barWrapper.classList.remove("is-visible");
          }
          if (bar) {
            bar.style.width = "0%";
          }
        }
        
        const transcribeStep = overlay.querySelector('[data-progress-step="transcribe"]');
        if (transcribeStep) {
          transcribeStep.classList.remove("is-visible", "is-active", "is-complete", "is-failed");
          const barWrapper = transcribeStep.querySelector(".progress-step__bar");
          const bar = barWrapper?.querySelector("span");
          if (barWrapper) {
            barWrapper.classList.remove("is-visible");
          }
          if (bar) {
            bar.style.width = "0%";
          }
        }
        
        const summarizeStep = overlay.querySelector('[data-progress-step="summarize"]');
        if (summarizeStep) {
          summarizeStep.classList.remove("is-visible", "is-active", "is-complete", "is-failed");
        }
        
        // Reset token counter
        const tokenCounter = document.getElementById("tokenCounter");
        const tokenCount = document.getElementById("tokenCount");
        const tokenDetails = document.getElementById("tokenDetails");
        if (tokenCounter && tokenCount && tokenDetails) {
          tokenCounter.style.display = "none";
          tokenCount.textContent = "0";
          tokenDetails.textContent = "";
        }
      }
    }
  };
};

document.addEventListener("DOMContentLoaded", () => {
  initUploadWidget();
  initProcessingFlow();
  initAgentSelector();
});
