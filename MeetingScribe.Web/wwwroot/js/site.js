document.addEventListener("DOMContentLoaded", () => {
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
});
