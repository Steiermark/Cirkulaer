// App serves /config with the Api base URL and the shared X-Api-Key. Fetched once, lazily,
// and falling back to same-origin with no key so the frontend-only dev server still works.
let apiConfig = null;
let apiConfigPromise = null;

async function ensureApiConfig() {
  if (apiConfig) return apiConfig;
  if (!apiConfigPromise) {
    apiConfigPromise = fetch("/config")
      .then((response) => (response.ok ? response.json() : {}))
      .catch(() => ({}));
  }
  const loaded = await apiConfigPromise;
  apiConfig = { apiBaseUrl: loaded.apiBaseUrl || "", apiKey: loaded.apiKey || "" };
  return apiConfig;
}

function apiUrl(path) {
  return new URL(path, apiConfig?.apiBaseUrl || window.location.href);
}

function apiHeaders() {
  const headers = { "Content-Type": "application/json" };
  if (apiConfig?.apiKey) headers["X-Api-Key"] = apiConfig.apiKey;
  return headers;
}

function apiErrorMessage(error) {
  if (error instanceof TypeError && /fetch/i.test(error.message)) {
    return [
      "Kunne ikke få forbindelse til API'en.",
      "Hvis du bruger mobilen, skal hele .NET-stakken køre, og både App- og Api-porten skal være tilgængelige på samme Wi-Fi.",
    ].join(" ");
  }
  return error.message || "Der opstod en uventet fejl.";
}

const state = {
  imageFiles: [],
  previewUrls: [],
  assessment: null,
  recommendation: null,
  saleDraft: null,
  saleRequest: 0,
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
  },  {
    keywords: ["cykel", "bike", "bicycle", "mountainbike", "racercykel", "elcykel"],
    object_name: "Cykel",
    category: "Cykel og fritid",
    materials: ["metal", "gummi", "plast"],
    waste_category: "Jern og metal eller storskrald",
    confidence: 0.58,
  },
];
const wasteSortingItems = [
  {
    key: "electronics",
    keywords: ["elektronik", "telefon", "mobil", "boremaskine", "akku", "apparat", "batteri", "ledning"],
    title: "Elektronik",
    type: "Elektronik / elværktøj",
    recommendation: "Aflever som elektronik",
    fraction: "Elektronik",
    container: "Elektronikområdet på en lokal genbrugsplads",
    placement: "Kontrollér kommunens sorteringsguide eller spørg personalet",
    note: "Må ikke i restaffald. Batterier, lader og kabler afleveres sammen med eller separat efter lokal anvisning.",
    impact: "Korrekt aflevering gør det muligt at genanvende metaller og håndtere batterier sikkert.",
    steps: ["Fjern personlige data hvis relevant", "Tag løse batterier ud hvis det kan gøres sikkert", "Aflever i elektronikområdet"],
    actions: [
      { label: "Aflever", detail: "Elektronik på genbrugspladsen" },
      { label: "Fjern batteri", detail: "Kun hvis det er let og sikkert" },
      { label: "Spørg personalet", detail: "Ved store eller beskadigede batterier" },
    ],
  },
  {
    key: "furniture",
    keywords: ["møbel", "møbler", "stol", "bord", "sofa", "reol", "skab", "træ", "inventar"],
    title: "Møbel eller indbo",
    type: "Møbel / inventar",
    recommendation: "Vælg direkte genbrug hvis den kan bruges, ellers storskrald",
    fraction: "Direkte genbrug eller storskrald",
    container: "Direkte genbrug eller storskrald efter lokal ordning",
    placement: "Kontrollér kommunens sorteringsguide eller spørg personalet",
    note: "Brug direkte genbrug, hvis andre kan bruge genstanden. Vælg storskrald, når den er defekt eller ikke kan genbruges.",
    impact: "Genbrug bevarer mest værdi; korrekt sortering reducerer fejlaflevering.",
    steps: ["Vurder om møblet kan bruges af andre", "Fjern løse dele og glas hvis nødvendigt", "Spørg personalet ved blandede materialer"],
    actions: [
      { label: "Direkte genbrug", detail: "Hvis den stadig kan bruges" },
      { label: "Storskrald", detail: "Hvis den er defekt eller ødelagt" },
      { label: "Adskil dele", detail: "Metal, glas eller træ kan høre til egne fraktioner" },
    ],
  },
  {
    key: "bicycle",
    keywords: ["cykel", "bike", "racercykel", "mountainbike", "elcykel"],
    title: "Cykel",
    type: "Cykel / metal og dele",
    recommendation: "Aflever som metal eller direkte genbrug afhængigt af stand",
    fraction: "Jern og metal eller direkte genbrug",
    container: "Metal eller direkte genbrug efter lokal ordning",
    placement: "Kontrollér kommunens sorteringsguide eller spørg personalet",
    note: "En cykel, der ikke kan repareres eller sælges, afleveres typisk som metal. Elcykler og batterier skal håndteres som elektronik/batteri.",
    impact: "Metal kan genanvendes, og brugbare cykler bør først forsøges givet videre.",
    steps: ["Fjern lås og personlige dele", "Tag batteri af elcykel hvis relevant", "Aflever stel og metaldele i metalområdet"],
    actions: [
      { label: "Metal", detail: "Defekt cykel uden realistisk genbrug" },
      { label: "Direkte genbrug", detail: "Hvis nogen kan bruge eller reparere den" },
      { label: "Batteri separat", detail: "Gælder elcykler" },
    ],
  },
  {
    key: "textile",
    keywords: ["tekstil", "tøj", "jakke", "bukser", "trøje", "sko"],
    title: "Tekstil eller tøj",
    type: "Tekstil",
    recommendation: "Sorter efter om det er brugbart eller ødelagt",
    fraction: "Tekstil",
    container: "Tekstilordning efter lokal anvisning",
    placement: "Kontrollér kommunens sorteringsguide eller spørg personalet",
    note: "Rent brugbart tøj doneres. Ødelagt tekstil afleveres som tekstilaffald efter lokal ordning.",
    impact: "Korrekt tekstilsortering kan give genbrug eller materialegenanvendelse.",
    steps: ["Sørg for at tekstilet er rent og tørt", "Pak det i pose hvis krævet", "Hold vådt eller forurenet tekstil adskilt"],
    actions: [
      { label: "Donér", detail: "Kun rent og brugbart" },
      { label: "Tekstilaffald", detail: "Ødelagt men rent og tørt" },
      { label: "Restaffald", detail: "Kun stærkt forurenet tekstil" },
    ],
  },
  {
    key: "hazardous",
    keywords: ["maling", "kemi", "olie", "spray", "lak", "farligt"],
    title: "Farligt affald",
    type: "Kemi / farligt affald",
    recommendation: "Aflever sikkert",
    fraction: "Farligt affald",
    container: "Bemandet modtagelse for farligt affald",
    placement: "Kontakt kommunen eller spørg personalet før aflevering",
    note: "Farligt affald må ikke hældes i afløb eller lægges i restaffald. Bevar original mærkning, hvis muligt.",
    impact: "Sikker aflevering beskytter jord, vand og restaffaldssystemet.",
    steps: ["Hold emballagen lukket", "Bevar mærkningen", "Aflever ved farligt affald eller spørg personalet"],
    actions: [
      { label: "Aflever", detail: "Farligt affald i lukket emballage" },
      { label: "Spørg personalet", detail: "Hvis indholdet er ukendt" },
      { label: "Bland ikke", detail: "Kemikalier må ikke blandes" },
    ],
  },
];

const cameraInput = document.querySelector("#camera-input");
const galleryInput = document.querySelector("#gallery-input");
const analyzeButton = document.querySelector("#analyze-button");
const recommendButton = document.querySelector("#recommend-button");
const restartButton = document.querySelector("#restart-button");
const openSaleButton = document.querySelector("#open-sale-button");
const openActionButton = document.querySelector("#open-action-button");
const openWasteButton = document.querySelector("#open-waste-button");
const copyAdButton = document.querySelector("#copy-ad-button");
const backToResultButton = document.querySelector("#back-to-result-button");
const recommendationPanel = document.querySelector(".recommendation");
const openSitePanelButton = document.querySelector("#open-site-panel-button");
const openSortingSearchButton = document.querySelector("#open-sorting-search-button");
const backFromWasteButton = document.querySelector("#back-from-waste-button");
const actionPrimaryButton = document.querySelector("#action-primary-button");
const actionSecondaryButton = document.querySelector("#action-secondary-button");
const backFromActionButton = document.querySelector("#back-from-action-button");
const closeSitePanelButton = document.querySelector("#close-site-panel-button");
const closeSortingSearchButton = document.querySelector("#close-sorting-search-button");
const sortingSearchInput = document.querySelector("#sorting-search-input");
const splashScreen = document.querySelector("#splash-screen");

if (splashScreen) {
  window.setTimeout(() => {
    splashScreen.classList.add("is-hidden");
    window.setTimeout(() => splashScreen.remove(), 400);
  }, 1000);
}

cameraInput.addEventListener("change", handleSelectedImages);
galleryInput.addEventListener("change", handleSelectedImages);
analyzeButton.addEventListener("click", analyzeImage);
recommendButton.addEventListener("click", recommend);
restartButton.addEventListener("click", restart);
openSaleButton.addEventListener("click", openSalePage);
openActionButton.addEventListener("click", openRecommendedAction);
openWasteButton.addEventListener("click", openWasteSortingPage);
copyAdButton.addEventListener("click", copyAdText);
openSitePanelButton.addEventListener("click", () => toggleSortingPanel("site", true));
openSortingSearchButton.addEventListener("click", () => toggleSortingPanel("search", true));
backFromWasteButton.addEventListener("click", () => {
  document.querySelector("#waste-sorting-screen").classList.add("hidden");
  document.querySelector("#result-screen").scrollIntoView({ behavior: "smooth" });
});
backFromActionButton.addEventListener("click", () => {
  document.querySelector("#action-screen").classList.add("hidden");
  document.querySelector("#result-screen").scrollIntoView({ behavior: "smooth" });
});
closeSitePanelButton.addEventListener("click", () => toggleSortingPanel("site", false));
closeSortingSearchButton.addEventListener("click", () => toggleSortingPanel("search", false));
sortingSearchInput.addEventListener("input", () => renderSortingSuggestions(sortingSearchInput.value));
recommendationPanel.addEventListener("click", () => {
  if (state.recommendation?.recommended_action === "sell") {
    openSalePage();
  }
});
backToResultButton.addEventListener("click", () => {
  document.querySelector("#sale-screen").classList.add("hidden");
  document.querySelector("#result-screen").scrollIntoView({ behavior: "smooth" });
});

function handleSelectedImages(event) {
  const selectedFiles = Array.from(event.target.files || []).filter((file) =>
    file.type.startsWith("image/"),
  );
  const roomLeft = Math.max(0, 4 - state.imageFiles.length);
  const acceptedFiles = selectedFiles.slice(0, roomLeft);

  state.imageFiles = state.imageFiles.concat(acceptedFiles);
  renderImagePreviews(state.imageFiles);
  updateImageControls();

  if (selectedFiles.length > roomLeft) {
    setStatus("Der bruges højst 4 billeder i analysen.");
  } else if (selectedFiles.length && acceptedFiles.length === 0) {
    setStatus("Fjern et billede for at tilføje et nyt.");
  } else {
    hideStatus();
  }

  event.target.value = "";
}

async function analyzeImage() {
  if (!state.imageFiles.length) return;

  setStatus(`Analyserer ${state.imageFiles.length} billede${state.imageFiles.length === 1 ? "" : "r"}...`);
  analyzeButton.disabled = true;

  try {
    const images = await Promise.all(
      state.imageFiles.map(async (file) => ({
        filename: file.name,
        imageDataUrl: await fileToDataUrl(file),
      })),
    );
    await ensureApiConfig();
    const response = await fetch(apiUrl("/api/analyze"), {
      method: "POST",
      headers: apiHeaders(),
      body: JSON.stringify({
        filename: state.imageFiles.map((file) => file.name).join(", "),
        images,
        imageDataUrls: images.map((image) => image.imageDataUrl),
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
    document.querySelector("#object-brand").textContent = assessment.brand || "Ikke fundet";
    document.querySelector("#object-model").textContent = assessment.model || "Ikke fundet";
    document.querySelector("#object-findings").textContent = assessment.visible_damage.length
      ? assessment.visible_damage.join(", ")
      : assessment.condition_estimate && assessment.condition_estimate !== "unknown"
        ? conditionLabel(assessment.condition_estimate)
        : "Ingen sikre fund";
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
    setStatus(apiErrorMessage(error), true);
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
    fieldset.dataset.questionId = question.id;

    const legend = document.createElement("legend");
    legend.textContent = question.label;
    fieldset.appendChild(legend);

    if (question.type === "text") {
      const input = document.createElement("input");
      input.type = "text";
      input.name = question.id;
      input.id = `question-${question.id}`;
      input.placeholder = question.placeholder || "";
      input.className = "question-text-input";
      input.addEventListener("input", () => {
        state.answers[question.id] = input.value.trim();
        fieldset.classList.remove("question-missing");
      });
      fieldset.appendChild(input);
      form.appendChild(fieldset);
      return;
    }

    const choices = document.createElement("div");
    choices.className = "choice-grid";

    question.options.forEach((option, index) => {
      const id = `${question.id}-${option.value}`;
      const label = document.createElement("label");
      label.setAttribute("for", id);
      label.dataset.question = question.id;
      label.dataset.value = option.value;

      const input = document.createElement("input");
      input.type = "radio";
      input.id = id;
      input.name = question.id;
      input.value = option.value;
      input.checked = Boolean(option.selected);
      if (input.checked) {
        state.answers[question.id] = option.value;
      }
      input.addEventListener("change", () => {
        state.answers[question.id] = option.value;
        fieldset.classList.remove("question-missing");
        if (question.id === "reason") {
          syncWorksOptions();
        }
        if (question.id === "producer") {
          syncProducerDetails();
        }
      });

      const span = document.createElement("span");
      span.textContent = option.label;

      label.append(input, span);
      choices.appendChild(label);
    });

    fieldset.appendChild(choices);

    if (question.id === "producer") {
      fieldset.appendChild(buildProducerDetailsFields(assessment));
    }

    form.appendChild(fieldset);
  });

  syncWorksOptions();
  syncProducerDetails();
}

function buildProducerDetailsFields(assessment) {
  const container = document.createElement("div");
  container.className = "producer-details hidden";
  container.id = "producer-details";
  container.dataset.hasAiModel = String(Boolean(String(assessment?.model || "").trim()));

  const producerLabel = document.createElement("label");
  producerLabel.id = "producer-name-label";
  producerLabel.textContent = "Producent";
  const producerInput = document.createElement("input");
  producerInput.type = "text";
  producerInput.name = "producer_name";
  producerInput.id = "producer-name";
  producerInput.autocomplete = "organization";
  producerInput.placeholder = "Fx Kildemoes, Trek eller andet mærke";
  producerInput.addEventListener("input", () => {
    state.answers.producer_name = producerInput.value.trim();
  });
  producerLabel.appendChild(producerInput);

  const modelLabel = document.createElement("label");
  modelLabel.id = "model-name-label";
  modelLabel.textContent = "Model";
  const modelInput = document.createElement("input");
  modelInput.type = "text";
  modelInput.name = "model_name";
  modelInput.id = "model-name";
  modelInput.autocomplete = "off";
  modelInput.placeholder = "Model, serie eller typenavn hvis du kender det";
  modelInput.addEventListener("input", () => {
    state.answers.model_name = modelInput.value.trim();
  });
  modelLabel.appendChild(modelInput);

  container.append(producerLabel, modelLabel);
  return container;
}

function syncProducerDetails() {
  const container = document.querySelector("#producer-details");
  if (!container) return;

  const producerLabel = container.querySelector("#producer-name-label");
  const modelLabel = container.querySelector("#model-name-label");
  const showProducer = state.answers.producer === "other";
  const showModel = showProducer || (
    ["detected", "ikea"].includes(state.answers.producer) &&
    container.dataset.hasAiModel !== "true"
  );
  const showDetails = showProducer || showModel;

  container.classList.toggle("hidden", !showDetails);
  container.hidden = !showDetails;
  producerLabel.classList.toggle("hidden", !showProducer);
  modelLabel.classList.toggle("hidden", !showModel);

  container.querySelectorAll("input").forEach((input) => {
    const enabled = input.name === "producer_name" ? showProducer : showModel;
    input.disabled = !enabled;
    if (!enabled) {
      input.value = "";
      state.answers[input.name] = "";
    }
  });
}
function syncWorksOptions() {
  const reason = state.answers.reason;
  const yesChoice = document.querySelector('label[data-question="works"][data-value="yes"]');
  const yesInput = document.querySelector('input[name="works"][value="yes"]');
  const noInput = document.querySelector('input[name="works"][value="no"]');
  const hideWorkingYes = reason === "defect";

  if (!yesChoice || !yesInput) return;

  yesChoice.classList.toggle("hidden", hideWorkingYes);
  yesChoice.hidden = hideWorkingYes;
  yesInput.disabled = hideWorkingYes;

  if (hideWorkingYes && yesInput.checked) {
    yesInput.checked = false;
    if (noInput) {
      noInput.checked = true;
      state.answers.works = "no";
    }
  }
}

function questionObjectLabel(assessment) {
  const name = String(assessment?.object_name || "").trim();
  if (!name || /^ukendt/i.test(name)) return "genstanden";

  const normalized = name.toLowerCase();
  const knownDefiniteForms = {
    "akkuboremaskine": "akkuboremaskinen",
    "boremaskine": "boremaskinen",
    "cykel": "cyklen",
    "kaffemaskine": "kaffemaskinen",
    "mobiltelefon": "mobiltelefonen",
    "mindre elektrisk apparat": "det mindre elektriske apparat",
    "møbel": "møblet",
    "sofa": "sofaen",
    "tekstil eller tøj": "tekstilet eller tøjet",
  };

  if (knownDefiniteForms[normalized]) return knownDefiniteForms[normalized];
  if (normalized.includes(" eller ")) return "genstanden";
  if (normalized.endsWith("el")) return `${normalized.slice(0, -2)}len`;
  if (normalized.endsWith("e")) return `${normalized}n`;
  if (normalized.endsWith("a")) return `${normalized}en`;
  return `${normalized}en`;
}
function isIkeaResaleCandidate(assessment) {
  const brand = String(assessment?.brand || "").toLowerCase();
  const candidates = Array.isArray(assessment?.producer_program_candidates)
    ? assessment.producer_program_candidates
    : [];
  return brand.includes("ikea") && candidates.includes("ikea_gensalg");
}
function buildQuestions(assessment) {
  const objectLabel = questionObjectLabel(assessment);
  const isElectronics = assessment.category_id === "electronics" || /Elektronik|Værktøj|Vaerktoej/i.test(assessment.category);
  const isFurniture = assessment.category_id === "furniture" || /Møbler|Møbel|Moebler|Mobler/i.test(assessment.category);
  const ikeaCandidate = isIkeaResaleCandidate(assessment);
  const detectedBrand = String(assessment?.brand || "").trim();
  const producerOptions = ikeaCandidate
    ? [
        { value: "ikea", label: "IKEA (identificeret)", selected: true },
        { value: "other", label: "Anden" },
        { value: "unknown", label: "Ved ikke" },
      ]
    : detectedBrand
      ? [
          { value: "detected", label: `${detectedBrand} (AI-forslag)` },
          { value: "other", label: "Anden" },
          { value: "unknown", label: "Ved ikke" },
        ]
      : [
          { value: "other", label: "Anden" },
          { value: "unknown", label: "Ved ikke" },
        ];

  const aiDamage = assessment.visible_damage?.length
    ? ` AI fandt: ${assessment.visible_damage.join(", ")}.`
    : "";

  const questions = [
    {
      id: "reason",
      label: `Hvorfor vil du af med ${objectLabel}?`,
      options: [
        { value: "replace", label: "Vil erstatte" },
        { value: "no_need", label: "Bruger den ikke" },
        { value: "defect", label: "Defekt" },
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
      label: `Virker ${objectLabel}?`,
      options: [
        { value: "yes", label: "Ja" },
        { value: "partly", label: "Delvist" },
        { value: "no", label: "Nej" },
        { value: "unknown", label: "Ved ikke" },
      ],
    },
    {
      id: "safety",
      label: "Er der tegn på akut fare, f.eks. varme, lækage, brandmærker eller beskadiget batteri?",
      options: [
        { value: "safe", label: "Ingen tegn på fare" },
        { value: "risk", label: "Ja, mulig fare" },
        { value: "unknown", label: "Ved ikke" },
      ],
    },
    {
      id: "age",
      label: `Hvor gammel er ${objectLabel}?`,
      options: [
        { value: "newer", label: "0-2 år" },
        { value: "mid", label: "3-7 år" },
        { value: "old", label: "8+ år" },
        { value: "unknown", label: "Ved ikke" },
      ],
    },
    {
      id: "damage",
      label: `Kendte fejl eller skader?${aiDamage}`,
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

  if (isFurniture) {
    questions.push({
      id: "cleaning",
      label: "Kan rensning eller klargøring løfte standen?",
      options: [
        { value: "light", label: "Let rens" },
        { value: "deep", label: "Grundig rens" },
        { value: "no", label: "Nej" },
        { value: "unknown", label: "Ved ikke" },
      ],
    });
  }

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

  if (ikeaCandidate) {
    questions.push(
      {
        id: "original_product",
        label: "Kan du bekræfte, at det er et originalt IKEA-produkt?",
        options: yesNoUnknownOptions(),
      },
      {
        id: "clean_state",
        label: "Er produktet rent?",
        options: yesNoUnknownOptions(),
      },
      {
        id: "unmodified",
        label: "Er produktet uændret?",
        options: yesNoUnknownOptions(),
      },
      {
        id: "assembled",
        label: "Er produktet korrekt samlet?",
        options: yesNoUnknownOptions(),
      },
    );
  }

  return questions.map(applyDefaultAnswer);
}

// Every choice question starts answered so a test run is two taps, not fifteen.
const defaultAnswers = {
  reason: "no_need",
  producer: ["ikea", "detected", "unknown"],
  works: "yes",
  safety: "safe",
  age: "mid",
  damage: "no",
  accessories: "irrelevant",
  cleaning: "no",
  battery: "none",
};

function applyDefaultAnswer(question) {
  if (question.type === "text" || question.options.some((option) => option.selected)) return question;
  const wanted = [].concat(defaultAnswers[question.id] || "yes");
  const pick = wanted.map((value) => question.options.find((option) => option.value === value)).find(Boolean)
    || question.options[0];
  return { ...question, options: question.options.map((option) => option === pick ? { ...option, selected: true } : option) };
}

function yesNoUnknownOptions() {
  return [
    { value: "yes", label: "Ja" },
    { value: "no", label: "Nej" },
    { value: "unknown", label: "Ved ikke" },
  ];
}

function conditionLabel(condition) {
  return {
    new: "Ser ny ud",
    good: "Ser ud til at være i god stand",
    worn: "Synlig slitage",
    damaged: "Mulige skader",
  }[condition] || "Ingen sikre fund";
}

async function recommend() {
  const assessment = state.assessment;
  collectAnswersFromForm();
  if (!validateQuestionAnswers(assessment)) return;

  recommendButton.disabled = true;
  recommendButton.textContent = "Finder anbefaling...";

  try {
    await ensureApiConfig();
    const response = await fetch(apiUrl("/api/recommend"), {
      method: "POST",
      headers: apiHeaders(),
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
    setStatus(apiErrorMessage(error), true);
  } finally {
    recommendButton.disabled = false;
    recommendButton.textContent = "Få anbefaling";
  }
}

function validateQuestionAnswers(assessment) {
  const questions = buildQuestions(assessment);
  const missing = questions.filter(
    (question) => question.type !== "text" && !state.answers[question.id],
  );
  const form = document.querySelector("#question-form");
  form.querySelectorAll(".question-missing").forEach((item) => item.classList.remove("question-missing"));
  missing.forEach((question) => {
    form.querySelector(`[data-question-id="${question.id}"]`)?.classList.add("question-missing");
  });

  const missingProducer = state.answers.producer === "other" && !String(state.answers.producer_name || "").trim();
  document.querySelector("#producer-name-label")?.classList.toggle("field-missing", missingProducer);

  const status = document.querySelector("#question-status");
  if (missing.length || missingProducer) {
    status.textContent = missingProducer
      ? "Udfyld producenten og besvar de markerede spørgsmål."
      : "Besvar de markerede spørgsmål, før anbefalingen beregnes.";
    status.classList.remove("hidden");
    (form.querySelector(".question-missing") || document.querySelector("#producer-name-label"))?.scrollIntoView({
      behavior: "smooth",
      block: "center",
    });
    return false;
  }

  status.classList.add("hidden");
  status.textContent = "";
  return true;
}

function renderResult(result) {
  state.recommendation = result;
  const badge = document.querySelector("#action-badge");
  badge.textContent = result.options.find(
    (option) => option.key === result.recommended_action,
  ).label;
  badge.className = `action-badge ${result.recommended_action}`;

  document.querySelector("#result-object").textContent = result.object_name;
  document.querySelector("#result-reasoning").textContent = result.reasoning;

  renderDecisionPath(result.decision_path || []);
  renderChecks(result.checks || [], result.recommended_action);
  renderImpact(result.impact, result.confidence);
  renderProducerProgram(result.producer_program);

  openSaleButton.classList.toggle("hidden", result.recommended_action !== "sell");
  openActionButton.classList.toggle(
    "hidden",
    !["repair", "clean", "donate"].includes(result.recommended_action),
  );
  openActionButton.textContent = {
    repair: "Fortsæt med reparation",
    clean: "Fortsæt med rensning og klargøring",
    donate: "Se muligheder for bortgivelse",
  }[result.recommended_action] || "Fortsæt med anbefalingen";
  openWasteButton.classList.toggle("hidden", !["sell", "donate"].includes(result.recommended_action));
  recommendationPanel.classList.toggle("clickable", result.recommended_action === "sell");
  recommendationPanel.title = result.recommended_action === "sell" ? "Åbn salgsforslag" : "";

  document.querySelector("#waste-category").textContent = result.waste.general_fraction;
  document.querySelector("#waste-note").textContent = result.waste.note;
  document.querySelector("#waste-box").classList.add("hidden");
  if (result.recommended_action === "waste") {
    openWasteSortingPage();
  } else {
    document.querySelector("#waste-sorting-screen").classList.add("hidden");
  }
}

function openRecommendedAction() {
  const action = state.recommendation?.recommended_action;
  if (!["repair", "clean", "donate"].includes(action)) return;
  renderActionFlow(action);
  show("#action-screen");
  document.querySelector("#action-screen").scrollIntoView({ behavior: "smooth" });
}

function renderActionFlow(action) {
  const title = document.querySelector("#action-title");
  const intro = document.querySelector("#action-intro");
  const steps = document.querySelector("#action-steps");
  const configs = {
    repair: {
      title: "Undersøg reparation",
      intro: "Start med en sikker vurdering af fejl, reservedel, pris og forventet levetid.",
      steps: [
        "Stop, hvis der er varme, lækage, brandmærker eller beskadiget batteri",
        "Få fejl og pris bekræftet af en fagperson eller Repair Café",
        "Sammenlign reparationspris med genstandens forventede brugsværdi",
      ],
      primary: "Reparation lykkedes – lav salgsforslag",
      secondary: "Reparation er ikke realistisk – sortér sikkert",
      onPrimary: () => {
        state.answers.works = "yes";
        if (state.answers.damage === "major") state.answers.damage = "minor";
        openSalePage();
      },
      onSecondary: () => openWasteSortingPage(),
    },
    clean: {
      title: "Rens og klargør",
      intro: "Klargøring skal løfte stand og præsentation uden at skjule fejl eller ændre produktet.",
      steps: [
        "Brug en metode, der passer til materialet",
        "Efterspænd og saml løse dele sikkert",
        "Tag nye billeder og beskriv resterende brugsspor ærligt",
      ],
      primary: "Klargøring færdig – lav salgsforslag",
      secondary: "Bortgiv i stedet",
      onPrimary: () => openSalePage(),
      onSecondary: () => renderActionFlow("donate"),
    },
    donate: {
      title: "Bortgiv genstanden",
      intro: "Vælg en kanal, hvor genstanden sandsynligvis kan blive brugt videre.",
      steps: [
        "Beskriv funktion, fejl og manglende dele tydeligt",
        "Prøv en genbrugsbutik, lokal gratisgruppe eller et verificeret byttepunkt",
        "Aftal sikker afhentning eller aflevering",
      ],
      primary: "Find genbrugsbutik på kort",
      secondary: "Kan ikke bortgives – gå til affaldssortering",
      onPrimary: () => window.open("https://www.google.com/maps/search/genbrugsbutik", "_blank", "noopener"),
      onSecondary: () => openWasteSortingPage(),
    },
  };
  const config = configs[action];
  title.textContent = config.title;
  intro.textContent = config.intro;
  steps.innerHTML = "";
  config.steps.forEach((step) => {
    const row = document.createElement("div");
    const mark = document.createElement("span");
    mark.setAttribute("aria-hidden", "true");
    mark.textContent = "✓";
    const copy = document.createElement("span");
    copy.textContent = step;
    row.append(mark, copy);
    steps.appendChild(row);
  });
  actionPrimaryButton.textContent = config.primary;
  actionSecondaryButton.textContent = config.secondary;
  actionPrimaryButton.onclick = config.onPrimary;
  actionSecondaryButton.onclick = config.onSecondary;
}

function openWasteSortingPage(item = null) {
  const sortingItem = item || buildWasteSortingItem(state.assessment, state.recommendation);
  renderWasteSortingPage(sortingItem);
  renderSortingSuggestions("");
  show("#waste-sorting-screen");
  document.querySelector("#waste-sorting-screen").scrollIntoView({ behavior: "smooth" });
}

function buildWasteSortingItem(assessment, recommendation) {
  const sourceText = [
    assessment?.object_name,
    assessment?.category,
    assessment?.subcategory,
    assessment?.waste_category,
    ...(Array.isArray(assessment?.materials) ? assessment.materials : []),
  ]
    .join(" ")
    .toLowerCase();

  const match = wasteSortingItems.find((item) =>
    item.keywords.some((keyword) => sourceText.includes(keyword)),
  );
  const base = match || {
    key: "general",
    title: assessment?.object_name || recommendation?.object_name || "Genstand",
    type: assessment?.category || "Ukendt kategori",
    recommendation: "Sortér efter materiale og lokal anvisning",
    fraction: assessment?.waste_category || "Afhænger af materiale og lokal ordning",
    container: "Spørg personalet eller brug kommunens sorteringsguide",
    placement: "Bemandet modtagelse eller relevant container",
    note: "Dette er generel vejledning. Kommunespecifik sortering skal verificeres lokalt.",
    impact: "Korrekt sortering reducerer fejlaflevering og øger mulighed for genanvendelse.",
    steps: ["Tjek materialer", "Fjern batterier eller farlige dele", "Spørg personalet ved tvivl"],
    actions: [
      { label: "Spørg personalet", detail: "Ved uklar fraktion" },
      { label: "Tjek lokal guide", detail: "Kommuneregler kan variere" },
      { label: "Hold farligt affald separat", detail: "Batterier, kemi og elektronik" },
    ],
  };

  return {
    ...base,
    title: assessment?.object_name && !/^ukendt/i.test(assessment.object_name) ? assessment.object_name : base.title,
    type: assessment?.category || base.type,
    confidence: Math.round((assessment?.confidence || 0.45) * 100),
    note: state.answers.safety === "risk"
      ? "Mulig sikkerhedsrisiko: Undgå videre brug og aflever genstanden ved bemandet modtagelse. Fortæl personalet om risikoen."
      : base.note,
    container: state.answers.safety === "risk"
      ? "Bemandet modtagelse på en lokal genbrugsplads"
      : base.container,
  };
}

function renderWasteSortingPage(item) {
  document.querySelector("#sorting-object-name").textContent = item.title;
  document.querySelector("#sorting-object-type").textContent = `${item.type}. ${item.recommendation}. ${item.impact}`;
  const confidence = Number.isFinite(item.confidence) ? item.confidence : null;
  document.querySelector("#sorting-confidence-meter").value = confidence || 0;
  document.querySelector("#sorting-confidence-label").textContent = confidence === null
    ? "Manuelt valgt"
    : `${confidence}%`;
  document.querySelector("#sorting-container").textContent = item.container;
  document.querySelector("#sorting-placement").textContent = item.placement;
  document.querySelector("#sorting-fraction").textContent = item.fraction;
  document.querySelector("#sorting-note").textContent = item.note;

  const actions = document.querySelector("#sorting-actions");
  actions.innerHTML = "";
  item.actions.forEach((action) => {
    const button = document.createElement("button");
    button.type = "button";
    button.innerHTML = `
      <span class="sorting-action-icon" aria-hidden="true">${sortingActionIcon(action.label)}</span>
      <span>
        <strong>${action.label}</strong>
        <small>${action.detail}</small>
      </span>
    `;
    actions.appendChild(button);
  });

  const steps = document.querySelector("#sorting-steps");
  steps.innerHTML = "";
  item.steps.forEach((step) => {
    const row = document.createElement("div");
    row.innerHTML = `<span aria-hidden="true">✓</span><span>${step}</span>`;
    steps.appendChild(row);
  });
}

function sortingActionIcon(label) {
  const normalized = label.toLowerCase();
  if (normalized.includes("genbrug") || normalized.includes("don")) return "↻";
  if (normalized.includes("spørg")) return "?";
  if (normalized.includes("batteri")) return "⚡";
  if (normalized.includes("metal")) return "▣";
  return "♻";
}

function toggleSortingPanel(panel, visible) {
  const sitePanel = document.querySelector("#sorting-site-panel");
  const searchPanel = document.querySelector("#sorting-search-panel");
  if (panel === "site") sitePanel.classList.toggle("hidden", !visible);
  if (panel === "search") searchPanel.classList.toggle("hidden", !visible);
  if (visible && panel === "site") searchPanel.classList.add("hidden");
  if (visible && panel === "search") sitePanel.classList.add("hidden");
}

function renderSortingSuggestions(query) {
  const normalizedQuery = String(query || "").trim().toLowerCase();
  const suggestions = document.querySelector("#sorting-suggestions");
  suggestions.innerHTML = "";
  wasteSortingItems
    .filter((item) => !normalizedQuery || item.keywords.some((keyword) => keyword.includes(normalizedQuery)) || item.title.toLowerCase().includes(normalizedQuery))
    .forEach((item) => {
      const button = document.createElement("button");
      button.type = "button";
      button.innerHTML = `
        <span class="sorting-icon" aria-hidden="true">♻</span>
        <span>
          <strong>${item.title}</strong>
          <small>${item.fraction}</small>
        </span>
      `;
      button.addEventListener("click", () => {
        renderWasteSortingPage({ ...item, confidence: null });
        toggleSortingPanel("search", false);
      });
      suggestions.appendChild(button);
    });
}

async function openSalePage() {
  if (!state.assessment || !state.recommendation) return;
  collectAnswersFromForm();

  show("#sale-screen");
  document.querySelector("#sale-screen").scrollIntoView({ behavior: "smooth" });
  setSaleLoading();

  const request = ++state.saleRequest;
  state.saleDraft = null;

  try {
    await ensureApiConfig();
    const sale = await fetchSaleAssist([]);
    if (request !== state.saleRequest) return;
    state.saleDraft = sale;
    renderSalePage(sale);

    // Grading against the photo takes a few seconds; the rows show meanwhile, the
    // price waits so it does not jump when the graded set replaces them.
    if (state.imageFiles[0] && (sale.comparables || []).length > 0) {
      renderSaleComparables(sale.comparables, "Sorterer efter lighed med dit foto …", true);
      document.querySelector("#sale-price").textContent = "Finder pris...";
      document.querySelector("#sale-price-note").textContent = "Sammenligner annoncerne med dit foto.";
      const photo = await fileToDataUrl(state.imageFiles[0]);
      const graded = await fetchSaleAssist([{ imageDataUrl: photo }]);
      if (request !== state.saleRequest) return;
      state.saleDraft = graded;
      renderSalePage(graded);
    }
  } catch (error) {
    if (request !== state.saleRequest) return;
    if (state.saleDraft) {
      renderSalePage(state.saleDraft);
      return;
    }
    renderSalePage({
      object_name: state.recommendation.object_name,
      details: "Prisforslaget kunne ikke hentes lige nu.",
      price: "Ukendt pris",
      price_note: error.message,
      search_note: "Prøv igen, eller søg manuelt efter lignende genstande.",
      search_url: buildFallbackSearchUrl(state.assessment, state.answers),
      marketplace_search_url: buildMarketplaceSearchUrl(state.assessment, state.answers),
      reshopper_relevant: isReshopperRelevant(state.assessment),
      reshopper_url: buildReshopperUrl(),
      reshopper_note: reshopperNote(state.assessment),
      ad_text: buildFallbackAdText(state.assessment, state.answers),
      marketplace_note: marketplaceApiNote(),
      comparables: [],
      price_confidence: "lav",
    });
  }
}

async function fetchSaleAssist(images) {
  const response = await fetch(apiUrl("/api/sale-assist"), {
    method: "POST",
    headers: apiHeaders(),
    body: JSON.stringify({
      assessment: state.assessment,
      answers: state.answers,
      recommendation: state.recommendation,
      images,
    }),
  });
  const payload = await parseJsonResponse(response);
  if (!response.ok) {
    throw new Error(payload.error || "Salgsforslaget kunne ikke beregnes.");
  }
  return payload.sale;
}

function setSaleLoading() {
  document.querySelector("#sale-object-name").textContent = state.recommendation?.object_name || "Genstand";
  document.querySelector("#sale-object-details").textContent = "Henter søgegrundlag og laver annoncekladde...";
  document.querySelector("#sale-price").textContent = "Finder pris...";
  document.querySelector("#sale-price-note").textContent =
    "Søger efter aktuelle annoncer på DBA.";
  document.querySelector("#sale-search-note").textContent = "";
  document.querySelector("#sale-search-link").href = "#";
  document.querySelector("#marketplace-search-link").href = "#";
  setReshopperVisibility(isReshopperRelevant(state.assessment), buildReshopperUrl(), reshopperNote(state.assessment));
  document.querySelector("#sale-ad-text").value = "";
  document.querySelector("#marketplace-note").textContent = marketplaceApiNote();
  renderSaleComparables([], "Søger efter sammenlignelige annoncer …");
}

function renderSalePage(sale) {
  document.querySelector("#sale-object-name").textContent = sale.object_name || "Genstand";
  document.querySelector("#sale-object-details").textContent = sale.details || "";
  document.querySelector("#sale-price").textContent = sale.price || "Ukendt pris";
  document.querySelector("#sale-price-note").textContent = [
    sale.price_note || "",
    sale.price_confidence ? `Datagrundlag: ${sale.price_confidence} sikkerhed.` : "",
  ].filter(Boolean).join(" ");
  document.querySelector("#sale-search-note").textContent = sale.search_note || "";
  document.querySelector("#sale-search-link").href = sale.search_url || buildFallbackSearchUrl(state.assessment, state.answers);
  document.querySelector("#marketplace-search-link").href = sale.marketplace_search_url || buildMarketplaceSearchUrl(state.assessment, state.answers);
  setReshopperVisibility(Boolean(sale.reshopper_relevant), sale.reshopper_url || buildReshopperUrl(), sale.reshopper_note || reshopperNote(state.assessment));
  document.querySelector("#sale-ad-text").value = sale.ad_text || buildFallbackAdText(state.assessment, state.answers);
  document.querySelector("#marketplace-note").textContent = sale.marketplace_note || marketplaceApiNote();
  renderSaleComparables(sale.comparables || []);
}

function renderSaleComparables(comparables, pendingText, keepRows) {
  const container = document.querySelector("#sale-comparables");
  container.innerHTML = "";
  if (!comparables.length || (pendingText && keepRows)) {
    const note = document.createElement("p");
    note.className = "muted";
    note.textContent = pendingText || "Ingen tilstrækkeligt modelrelevante prisfund blev fundet. Brug søgelinkene til manuel kontrol.";
    container.appendChild(note);
    if (!comparables.length) return;
  }

  comparables.forEach((item) => {
    const row = document.createElement(item.url ? "a" : "div");
    row.className = "sale-comparable";
    if (item.url) {
      row.href = item.url;
      row.target = "_blank";
      row.rel = "noopener";
    }
    const title = document.createElement("span");
    title.textContent = item.url ? `${item.title} ↗` : item.title;
    const price = document.createElement("strong");
    price.textContent = `${Number(item.price).toLocaleString("da-DK")} kr.`;
    row.append(title, price);
    container.appendChild(row);
  });
}

async function copyAdText() {
  const text = document.querySelector("#sale-ad-text").value;
  if (!text) return;

  try {
    await navigator.clipboard.writeText(text);
    copyAdButton.textContent = "Annoncetekst kopieret";
  } catch {
    copyAdButton.textContent = "Markér teksten og kopiér";
  }

  setTimeout(() => {
    copyAdButton.textContent = "Kopiér annoncetekst";
  }, 2200);
}

function collectAnswersFromForm() {
  const form = document.querySelector("#question-form");
  if (!form) return;

  const answers = new FormData(form);
  answers.forEach((value, key) => {
    state.answers[key] = String(value).trim();
  });
}

function buildSaleSearchQuery(assessment, answers) {
  return uniqueTextParts([
    producerSearchName(answers),
    answers?.model_name,
    assessment?.brand,
    assessment?.model,
    assessment?.object_name,
  ]).join(" ");
}

function producerSearchName(answers) {
  if (answers?.producer_name) return answers.producer_name;
  if (answers?.producer === "ikea") return "IKEA";
  if (answers?.producer && !["unknown", "other", "detected"].includes(answers.producer)) {
    return answers.producer;
  }
  return "";
}

function uniqueTextParts(parts) {
  const seen = new Set();
  const unique = parts
    .map((part) => String(part || "").trim())
    .filter((part) => {
      const key = part.toLowerCase();
      if (!part || seen.has(key)) return false;
      seen.add(key);
      return true;
    });
  return unique.filter((part, index) => {
    const pattern = new RegExp(`(^|[^a-z0-9])${escapeRegExp(part.toLowerCase())}([^a-z0-9]|$)`);
    return !unique.some(
      (other, otherIndex) =>
        index !== otherIndex &&
        other.length > part.length &&
        pattern.test(other.toLowerCase()),
    );
  });
}

function escapeRegExp(value) {
  return value.replace(/[^a-z0-9]/g, "\\$&");
}
function buildFallbackSearchUrl(assessment, answers) {
  const query = buildSaleSearchQuery(assessment, answers);
  return `https://www.google.com/search?q=${encodeURIComponent(`${query} brugt pris Danmark`)}`;
}
function buildMarketplaceSearchUrl(assessment, answers) {
  const query = buildSaleSearchQuery(assessment, answers);
  return `https://www.facebook.com/marketplace/search/?query=${encodeURIComponent(`${query} brugt pris Danmark`)}`;
}

function buildFallbackAdText(assessment, answers) {
  const name = uniqueTextParts([
    producerSearchName(answers),
    answers?.model_name,
    assessment?.model,
    assessment?.object_name,
  ]).join(" ") || "Genstand";
  const lines = [
    `${name} sælges`,
    "",
    "Sælges på baggrund af billeder og oplysninger om stand, funktion, producent og model.",
    "",
    "Oplysninger:",
    `- Genstand: ${assessment?.object_name || "ikke angivet"}`,
  ];

  if (producerSearchName(answers)) lines.push(`- Producent/mærke: ${producerSearchName(answers)}`);
  if (answers?.model_name || assessment?.model) lines.push(`- Model/serie: ${answers?.model_name || assessment?.model}`);
  if (assessment?.category) lines.push(`- Kategori: ${assessment.category}`);
  if (answers?.works) lines.push(`- Funktion: ${answers.works === "yes" ? "virker" : answers.works === "partly" ? "virker delvist" : "skal kontrolleres"}`);
  if (answers?.damage) lines.push(`- Stand/skader: ${answers.damage}`);

  lines.push(
    "",
    "Pris bør fastsættes efter lignende annoncer, aktuel stand, alder, dokumentation og efterspørgsel.",
    "",
    "Kan afhentes efter aftale. Skriv gerne ved spørgsmål eller hvis du vil se flere billeder.",
  );
  return lines.join("\n");
}

function marketplaceApiNote() {
  return "Facebook Marketplace bruges som manuel priskontrol via søgelink. Der er ikke tilgængelig direkte oprettelse via en almindelig offentlig Facebook Marketplace API i denne prototype, så annoncen skal oprettes manuelt.";
}
function buildReshopperUrl() {
  return "https://reshopper.com/da";
}

function isReshopperRelevant(assessment) {
  const text = `${assessment?.category || ""} ${assessment?.object_name || ""} ${assessment?.subcategory || ""}`.toLowerCase();
  const relevantTerms = ["barn", "børn", "boern", "baby", "legetøj", "legetoej", "barnevogn", "klapvogn", "autostol", "børnetøj", "boernetoej", "ventetøj", "ventetoej", "tøj", "toej", "tekstil", "møbel", "moebel", "møbler", "moebler", "bolig"];
  const excludedTerms = ["cykel", "elektronik", "værktøj", "vaerktoej", "batteri", "maling", "farligt"];
  return relevantTerms.some((term) => text.includes(term)) && !excludedTerms.some((term) => text.includes(term));
}

function reshopperNote(assessment) {
  if (isReshopperRelevant(assessment)) {
    return "Reshopper vises, fordi genstanden ser ud til at passe til børn, mor eller bolig. Søg manuelt med producent, model og genstandens navn.";
  }
  return "Reshopper er skjult, fordi platformen primært er relevant for børn, mor og bolig.";
}

function setReshopperVisibility(isRelevant, url, note) {
  const quickLink = document.querySelector("#reshopper-link");
  const card = document.querySelector("#reshopper-card");
  const createLink = document.querySelector("#reshopper-create-link");
  const noteElement = document.querySelector("#reshopper-note");
  if (!quickLink || !card || !createLink || !noteElement) return;

  quickLink.classList.toggle("hidden", !isRelevant);
  card.classList.toggle("hidden", !isRelevant);
  quickLink.href = url || buildReshopperUrl();
  createLink.href = url || buildReshopperUrl();
  noteElement.textContent = note || reshopperNote(state.assessment);
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

function renderChecks(checks, recommendedAction) {
  const container = document.querySelector("#check-grid");
  const visibleChecks = ["sell", "donate"].includes(recommendedAction)
    ? checks.filter((check) => check.status === "realistisk")
    : checks;
  container.innerHTML = "";
  visibleChecks.forEach((check) => {
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
          <span class="program-check ${check.ok === true ? "yes" : check.ok === false ? "no" : ""}">
            ${check.ok === true ? "✓" : check.ok === false ? "×" : "?"}
          </span>
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
  if (confidence >= 0.85) return "Høj";
  if (confidence >= 0.7) return "Mellem-høj";
  if (confidence >= 0.5) return "Lav-mellem";
  return "Lav";
}

function show(selector) {
  document.querySelector(selector).classList.remove("hidden");
}

function restart() {
  clearPreviewUrls();
  state.imageFiles = [];
  state.assessment = null;
  state.recommendation = null;
  state.saleDraft = null;
  state.saleRequest++;
  state.answers = {};
  cameraInput.value = "";
  galleryInput.value = "";
  analyzeButton.disabled = true;
  cameraInput.disabled = false;
  galleryInput.disabled = false;
  const previewWrap = document.querySelector("#preview-wrap");
  previewWrap.innerHTML = "";
  previewWrap.classList.add("hidden");
  ["#identify-screen", "#questions-screen", "#result-screen", "#action-screen", "#sale-screen", "#waste-sorting-screen"].forEach((selector) => {
    document.querySelector(selector).classList.add("hidden");
  });
  window.scrollTo({ top: 0, behavior: "smooth" });
}

function renderImagePreviews(files) {
  const previewWrap = document.querySelector("#preview-wrap");
  clearPreviewUrls();
  previewWrap.innerHTML = "";

  files.forEach((file, index) => {
    const url = URL.createObjectURL(file);
    state.previewUrls.push(url);

    const figure = document.createElement("figure");
    figure.className = "preview-item";

    const image = document.createElement("img");
    image.src = url;
    image.alt = `Billede ${index + 1} af genstanden`;

    const caption = document.createElement("figcaption");
    caption.textContent = index === 0 ? "Hovedbillede" : `Ekstra vinkel ${index + 1}`;

    const removeButton = document.createElement("button");
    removeButton.type = "button";
    removeButton.className = "preview-remove";
    removeButton.setAttribute("aria-label", `Fjern billede ${index + 1}`);
    removeButton.textContent = "×";
    removeButton.addEventListener("click", () => removeImage(index));

    figure.append(image, removeButton, caption);
    previewWrap.appendChild(figure);
  });

  previewWrap.classList.toggle("hidden", files.length === 0);
}

function removeImage(index) {
  state.imageFiles.splice(index, 1);
  renderImagePreviews(state.imageFiles);
  updateImageControls();
  if (state.imageFiles.length < 4) {
    hideStatus();
  }
}

function updateImageControls() {
  const hasImages = state.imageFiles.length > 0;
  const isFull = state.imageFiles.length >= 4;
  analyzeButton.disabled = !hasImages;
  cameraInput.disabled = isFull;
  galleryInput.disabled = isFull;
}

function clearPreviewUrls() {
  state.previewUrls.forEach((url) => URL.revokeObjectURL(url));
  state.previewUrls = [];
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
  const fallback = inferObjectFromFilename(state.imageFiles.map((file) => file.name).join(" "));
  return {
    object_name: assessment?.object_name || fallback.object_name,
    category: assessment?.category || fallback.category,
    category_id: assessment?.category_id || canonicalCategoryId(assessment?.category || fallback.category),
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
    search_terms: Array.isArray(assessment?.search_terms) ? assessment.search_terms : [],
    producer_program_candidates: Array.isArray(assessment?.producer_program_candidates)
      ? assessment.producer_program_candidates
      : [],
  };
}

function canonicalCategoryId(category) {
  const value = String(category || "").toLowerCase();
  if (/elektronik|værktøj|vaerktoej/.test(value)) return "electronics";
  if (/møbl|moebl|mobl/.test(value)) return "furniture";
  if (/cykel|bike/.test(value)) return "bicycle";
  if (/tekstil|tøj|toej/.test(value)) return "textile";
  if (/farligt|kemi/.test(value)) return "hazardous";
  return "other";
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




























