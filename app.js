const state = {
  imageFile: null,
  assessment: null,
  answers: {},
};

const categoryHints = [
  {
    keywords: ["boremaskine", "drill", "bosch", "makita", "dewalt", "skruemaskine"],
    object_name: "Akkuboremaskine",
    category: "Elektronik og værktøj",
    materials: ["metal", "plast", "batteri"],
    waste_category: "Småt elektronik",
    confidence: 0.68,
  },
  {
    keywords: ["telefon", "iphone", "samsung", "mobil"],
    object_name: "Mobiltelefon",
    category: "Elektronik",
    materials: ["glas", "metal", "batteri"],
    waste_category: "Småt elektronik",
    confidence: 0.72,
  },
  {
    keywords: ["stol", "chair", "bord", "table", "møbel", "sofa"],
    object_name: "Møbel",
    category: "Møbler og indbo",
    materials: ["træ", "tekstil", "metal"],
    waste_category: "Storskrald eller genbrugsplads",
    confidence: 0.62,
  },
  {
    keywords: ["jakke", "shirt", "trøje", "bukser", "sko", "tekstil", "tøj"],
    object_name: "Tekstil eller tøj",
    category: "Tekstiler",
    materials: ["tekstil"],
    waste_category: "Tekstilaffald",
    confidence: 0.64,
  },
  {
    keywords: ["elkedel", "kaffemaskine", "toaster", "lampe", "kabel"],
    object_name: "Mindre elektrisk apparat",
    category: "Elektronik",
    materials: ["plast", "metal", "elektronik"],
    waste_category: "Småt elektronik",
    confidence: 0.66,
  },
];

const imageInput = document.querySelector("#image-input");
const analyzeButton = document.querySelector("#analyze-button");
const recommendButton = document.querySelector("#recommend-button");
const restartButton = document.querySelector("#restart-button");

imageInput.addEventListener("change", handleImage);
analyzeButton.addEventListener("click", analyzeImage);
recommendButton.addEventListener("click", recommend);
restartButton.addEventListener("click", restart);

function handleImage(event) {
  const [file] = event.target.files;
  if (!file) return;

  state.imageFile = file;
  const preview = document.querySelector("#image-preview");
  preview.src = URL.createObjectURL(file);
  document.querySelector("#preview-wrap").classList.remove("hidden");
  analyzeButton.disabled = false;
}

async function analyzeImage() {
  if (!state.imageFile) return;

  setStatus("Analyserer billedet...");
  analyzeButton.disabled = true;

  try {
    const imageDataUrl = await fileToDataUrl(state.imageFile);
    const response = await fetch(new URL("/api/analyze", window.location.href), {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        filename: state.imageFile.name,
        imageDataUrl,
      }),
    });

    const payload = await parseJsonResponse(response);
    if (!response.ok) {
      throw new Error(payload.error || "Billedanalysen kunne ikke gennemføres.");
    }

    const assessment = normalizeAssessment(payload.assessment);
    state.assessment = assessment;
    if (payload.mode === "test" && payload.message) {
      assessment.uncertainty_notes.unshift(payload.message);
    }

    document.querySelector("#object-name").textContent = assessment.object_name;
    document.querySelector("#object-category").textContent = assessment.category;
    document.querySelector("#object-confidence").textContent = confidenceLabel(
      assessment.confidence,
    );
    document.querySelector("#uncertainty-note").textContent =
      assessment.uncertainty_notes.join(" ");

    renderQuestions(assessment);
    hideStatus();
    show("#identify-screen");
    show("#questions-screen");
    document.querySelector("#identify-screen").scrollIntoView({ behavior: "smooth" });
  } catch (error) {
    setStatus(error.message, true);
  } finally {
    analyzeButton.disabled = false;
  }
}

function inferObjectFromFilename(filename) {
  const normalizedName = filename.toLowerCase();
  const match = categoryHints.find((hint) =>
    hint.keywords.some((keyword) => normalizedName.includes(keyword)),
  );

  const base =
    match ||
    {
      object_name: "Ukendt genstand",
      category: "Blandet genstand",
      materials: [],
      waste_category: "Afhænger af materiale og lokal ordning",
      confidence: 0.34,
    };

  return {
    ...base,
    brand: null,
    model: null,
    condition_estimate: "unknown",
    uncertainty_notes: [
      "Testversion: billedet er uploadet og vist, men vurderingen er lokal testlogik.",
      "Mærke, model, skader og materialer skal bekræftes af rigtig billedanalyse i næste fase.",
    ],
  };
}

function renderQuestions(assessment) {
  state.answers = {};
  const questions = buildQuestions(assessment);
  const form = document.querySelector("#question-form");
  form.innerHTML = "";

  questions.forEach((question) => {
    const fieldset = document.createElement("fieldset");
    fieldset.className = "question";

    const legend = document.createElement("legend");
    legend.textContent = question.label;
    fieldset.appendChild(legend);

    const choices = document.createElement("div");
    choices.className = "choice-grid";

    question.options.forEach((option, index) => {
      const id = `${question.id}-${option.value}`;
      const label = document.createElement("label");
      label.setAttribute("for", id);

      const input = document.createElement("input");
      input.type = "radio";
      input.id = id;
      input.name = question.id;
      input.value = option.value;
      input.checked = index === 0;
      state.answers[question.id] = state.answers[question.id] || option.value;
      input.addEventListener("change", () => {
        state.answers[question.id] = option.value;
      });

      const span = document.createElement("span");
      span.textContent = option.label;

      label.append(input, span);
      choices.appendChild(label);
    });

    fieldset.appendChild(choices);
    form.appendChild(fieldset);
  });
}

function buildQuestions(assessment) {
  const isElectronics = assessment.category.includes("Elektronik");
  const producerOptions = assessment.brand?.toLowerCase().includes("ikea")
    ? [{ value: "ikea", label: "IKEA" }, { value: "unknown", label: "Ved ikke" }]
    : [
        { value: "unknown", label: "Ved ikke" },
        { value: "ikea", label: "IKEA" },
        { value: "other", label: "Anden" },
      ];

  const questions = [
    {
      id: "reason",
      label: "Hvorfor vil du af med genstanden?",
      options: [
        { value: "defect", label: "Defekt" },
        { value: "no_need", label: "Bruger den ikke" },
        { value: "replace", label: "Vil erstatte" },
        { value: "no_space", label: "Ikke plads" },
        { value: "give_away", label: "Give videre" },
        { value: "waste_assumption", label: "Tror det er affald" },
      ],
    },
    {
      id: "producer",
      label: "Kender du producenten?",
      options: producerOptions,
    },
    {
      id: "works",
      label: "Virker genstanden?",
      options: [
        { value: "yes", label: "Ja" },
        { value: "partly", label: "Delvist" },
        { value: "no", label: "Nej" },
        { value: "unknown", label: "Ved ikke" },
      ],
    },
    {
      id: "age",
      label: "Hvor gammel virker den?",
      options: [
        { value: "newer", label: "Nyere" },
        { value: "mid", label: "Mellem" },
        { value: "old", label: "Gammel" },
        { value: "unknown", label: "Ved ikke" },
      ],
    },
    {
      id: "damage",
      label: "Kendte fejl eller skader?",
      options: [
        { value: "no", label: "Ingen" },
        { value: "minor", label: "Mindre" },
        { value: "major", label: "Store" },
        { value: "unknown", label: "Ved ikke" },
      ],
    },
    {
      id: "accessories",
      label: "Følger vigtigt tilbehør med?",
      options: [
        { value: "complete", label: "Komplet" },
        { value: "partial", label: "Noget" },
        { value: "missing", label: "Mangler" },
        { value: "irrelevant", label: "Ikke relevant" },
      ],
    },
  ];

  if (isElectronics) {
    questions.push({
      id: "battery",
      label: "Har den batteri eller ledning?",
      options: [
        { value: "battery", label: "Batteri" },
        { value: "cord", label: "Ledning" },
        { value: "none", label: "Nej" },
        { value: "unknown", label: "Ved ikke" },
      ],
    });
  }

  return questions.slice(0, isElectronics ? 7 : 6);
}

async function recommend() {
  const assessment = state.assessment;
  const answers = new FormData(document.querySelector("#question-form"));
  answers.forEach((value, key) => {
    state.answers[key] = value;
  });

  recommendButton.disabled = true;
  recommendButton.textContent = "Finder anbefaling...";

  try {
    const response = await fetch(new URL("/api/recommend", window.location.href), {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ assessment, answers: state.answers }),
    });
    const payload = await parseJsonResponse(response);
    if (!response.ok) {
      throw new Error(payload.error || "Anbefalingen kunne ikke beregnes.");
    }

    renderResult(payload.recommendation);
    show("#result-screen");
    document.querySelector("#result-screen").scrollIntoView({ behavior: "smooth" });
  } catch (error) {
    setStatus(error.message, true);
  } finally {
    recommendButton.disabled = false;
    recommendButton.textContent = "Få anbefaling";
  }
}

function renderResult(result) {
  const badge = document.querySelector("#action-badge");
  badge.textContent = result.options.find(
    (option) => option.key === result.recommended_action,
  ).label;
  badge.className = `action-badge ${result.recommended_action}`;

  document.querySelector("#result-object").textContent = result.object_name;
  document.querySelector("#result-reasoning").textContent = result.reasoning;

  renderDecisionPath(result.decision_path || []);
  renderChecks(result.checks || []);
  renderImpact(result.impact, result.confidence);
  renderProducerProgram(result.producer_program);

  const options = document.querySelector("#options-list");
  options.innerHTML = "";
  result.options.forEach((option, index) => {
    const item = document.createElement("div");
    item.className = "option";
    item.innerHTML = `
      <span class="option-number">${index + 1}</span>
      <div>
        <strong>${option.label}</strong>
        <p>${option.description}</p>
      </div>
    `;
    options.appendChild(item);
  });

  document.querySelector("#waste-category").textContent = result.waste.general_fraction;
  document.querySelector("#waste-note").textContent = result.waste.note;
  document.querySelector("#waste-box").classList.remove("hidden");
}

function renderDecisionPath(path) {
  const container = document.querySelector("#decision-path");
  container.innerHTML = "";
  path.forEach((step, index) => {
    const pill = document.createElement("span");
    pill.className = "path-pill";
    pill.textContent = step;
    container.appendChild(pill);

    if (index < path.length - 1) {
      const arrow = document.createElement("span");
      arrow.className = "path-arrow";
      arrow.textContent = "→";
      container.appendChild(arrow);
    }
  });
}

function renderChecks(checks) {
  const container = document.querySelector("#check-grid");
  container.innerHTML = "";
  checks.forEach((check) => {
    const item = document.createElement("div");
    const isRealistic = check.status === "realistisk";
    item.className = "check-item";
    item.innerHTML = `
      <strong>${check.label}</strong>
      <span class="check-status ${isRealistic ? "yes" : ""}">${check.status}</span>
      <p>${check.description}</p>
    `;
    container.appendChild(item);
  });
}

function renderImpact(impact, confidence) {
  const container = document.querySelector("#impact-box");
  if (!impact) {
    container.innerHTML = "";
    return;
  }

  container.innerHTML = `
    <div class="impact-metrics">
      <div>
        <span>Økonomi</span>
        <strong>${impact.economy}</strong>
      </div>
      <div>
        <span>CO2</span>
        <strong>${impact.co2_saving}</strong>
      </div>
    </div>
    <p>${impact.note} Vurderingssikkerhed: ${confidence || "ukendt"}.</p>
  `;
}

function renderProducerProgram(program) {
  const container = document.querySelector("#producer-program");
  if (!container || !program || program.status === "none") {
    container?.classList.add("hidden");
    return;
  }

  const primary = program.programs?.[0];
  if (!primary) {
    container.classList.add("hidden");
    return;
  }

  const checks = primary.checks
    .map(
      (check) => `
        <li>
          <span class="program-check ${check.ok ? "yes" : ""}">${check.ok ? "✓" : "?"}</span>
          ${check.label}
        </li>
      `,
    )
    .join("");

  const comparison = primary.comparison
    .map(
      (item) => `
        <div>
          <span>${item.label}</span>
          <strong>${item.value}</strong>
        </div>
      `,
    )
    .join("");

  const link = primary.url
    ? `<a class="program-link" href="${primary.url}" target="_blank" rel="noopener">Gå til officiel vurdering</a>`
    : "";

  container.innerHTML = `
    <span class="field-label">Producentordning</span>
    <h3>${primary.title}</h3>
    <p>${primary.message}</p>
    <ul class="program-checks">${checks}</ul>
    <div class="program-comparison">${comparison}</div>
    ${link}
    <p class="program-source">Kilde: ${primary.source_label}. Endelig godkendelse og pris afgøres af producenten.</p>
  `;
  container.classList.remove("hidden");
}
function confidenceLabel(confidence) {
  if (confidence >= 0.7) return "Mellem";
  if (confidence >= 0.5) return "Lav-mellem";
  return "Lav";
}

function show(selector) {
  document.querySelector(selector).classList.remove("hidden");
}

function restart() {
  state.imageFile = null;
  state.assessment = null;
  state.answers = {};
  imageInput.value = "";
  analyzeButton.disabled = true;
  document.querySelector("#preview-wrap").classList.add("hidden");
  ["#identify-screen", "#questions-screen", "#result-screen"].forEach((selector) => {
    document.querySelector(selector).classList.add("hidden");
  });
  window.scrollTo({ top: 0, behavior: "smooth" });
}

async function fileToDataUrl(file) {
  const compressed = await compressImage(file);
  if (compressed) return compressed;

  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(reader.result);
    reader.onerror = () => reject(new Error("Billedet kunne ikke læses."));
    reader.readAsDataURL(file);
  });
}

function compressImage(file) {
  return new Promise((resolve) => {
    if (!file.type.startsWith("image/")) {
      resolve(null);
      return;
    }

    const img = new Image();
    const objectUrl = URL.createObjectURL(file);

    img.onload = () => {
      URL.revokeObjectURL(objectUrl);
      const maxSide = 1600;
      const ratio = Math.min(1, maxSide / Math.max(img.width, img.height));
      const width = Math.max(1, Math.round(img.width * ratio));
      const height = Math.max(1, Math.round(img.height * ratio));

      const canvas = document.createElement("canvas");
      canvas.width = width;
      canvas.height = height;
      const context = canvas.getContext("2d");
      context.drawImage(img, 0, 0, width, height);
      resolve(canvas.toDataURL("image/jpeg", 0.82));
    };

    img.onerror = () => {
      URL.revokeObjectURL(objectUrl);
      resolve(null);
    };

    img.src = objectUrl;
  });
}

async function parseJsonResponse(response) {
  const text = await response.text();
  if (!text) return {};

  try {
    return JSON.parse(text);
  } catch {
    return {
      error: response.ok
        ? "Serveren returnerede et uventet svar."
        : `Serverfejl ${response.status}: ${text.slice(0, 160)}`,
    };
  }
}

function normalizeAssessment(assessment) {
  const fallback = inferObjectFromFilename(state.imageFile?.name || "");
  return {
    object_name: assessment?.object_name || fallback.object_name,
    category: assessment?.category || fallback.category,
    subcategory: assessment?.subcategory || null,
    brand: assessment?.brand || null,
    model: assessment?.model || null,
    materials: Array.isArray(assessment?.materials) ? assessment.materials : [],
    visible_damage: Array.isArray(assessment?.visible_damage)
      ? assessment.visible_damage
      : [],
    condition_estimate: assessment?.condition_estimate || "unknown",
    waste_category: assessment?.waste_category || fallback.waste_category,
    confidence:
      typeof assessment?.confidence === "number"
        ? Math.max(0, Math.min(1, assessment.confidence))
        : fallback.confidence,
    uncertainty_notes:
      Array.isArray(assessment?.uncertainty_notes) &&
      assessment.uncertainty_notes.length
        ? assessment.uncertainty_notes
        : ["AI kunne ikke angive væsentlige usikkerheder."],
  };
}

function setStatus(message, isError = false) {
  const status = document.querySelector("#analysis-status");
  status.textContent = message;
  status.classList.toggle("error", isError);
  status.classList.remove("hidden");
}

function hideStatus() {
  const status = document.querySelector("#analysis-status");
  status.textContent = "";
  status.classList.remove("error");
  status.classList.add("hidden");
}






